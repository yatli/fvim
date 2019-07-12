module FVim.neovim.proc

open def
open FVim.getopt
open FVim.log

open MessagePack

open System
open System.Diagnostics
open System.Net.Sockets
open System.Text
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading.Tasks
open System.Threading
open FSharp.Control.Reactive
open FSharp.Control.Tasks.V2.ContextSensitive

let private trace fmt = trace "neovim.process" fmt

let private default_notify (e: Request) =
    failwithf "%A" e

let private default_call (e: Request) =
    failwithf "%A" e

type NvimIO =
    | Disconnected
    | StartProcess of Process
    | ConnectTcp of NetworkStream

type Nvim() = 
    let m_id = Guid.NewGuid()
    let mutable m_notify = default_notify
    let mutable m_call   = default_call
    let mutable m_events = None
    let mutable m_io     = Disconnected

    member private this.events =
        match m_events with
        | Some events -> events
        | None -> failwith "events"

    member __.start { server = serveropts; program = prog; preArgs = preargs; stderrenc = enc } =
        match m_io, m_events with
        | Disconnected, None -> ()
        | _ -> failwith "neovim: already started"

        let io = 
            match serveropts with
            | StartNew args ->
                let args = "--embed" :: (List.map (fun (x: string) -> if x.Contains(' ') then "\"" + x + "\"" else x) args)
                let psi  = ProcessStartInfo(prog, String.Join(" ", preargs @ args))
                psi.CreateNoWindow          <- true
                psi.ErrorDialog             <- false
                psi.RedirectStandardError   <- true
                psi.RedirectStandardInput   <- true
                psi.RedirectStandardOutput  <- true
                psi.StandardErrorEncoding   <- enc
                psi.UseShellExecute         <- false
                psi.WindowStyle             <- ProcessWindowStyle.Hidden
                psi.WorkingDirectory        <- Environment.CurrentDirectory

                StartProcess <| Process.Start(psi)
            | Tcp ipe ->
                let sock = new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                sock.Connect(ipe)
                ConnectTcp <| new NetworkStream(sock, true)

        let serverExitCode() =
            match io with
            | StartProcess proc -> if proc.HasExited then Some proc.ExitCode else None
            | ConnectTcp _ -> None
            | Disconnected -> Some -1

        let stdin, stdout, stderr = 
            match io with
            | StartProcess proc ->
                let stdout = proc.StandardOutput.BaseStream
                let stdin  = proc.StandardInput.BaseStream
                let stderr = 
                    proc.ErrorDataReceived 
                    |> Observable.map (fun data -> Error data.Data )
                proc.BeginErrorReadLine()
                stdin, stdout, stderr
            | ConnectTcp stream ->
                stream :> System.IO.Stream, stream :> System.IO.Stream, Observable.empty
            | _ -> failwith ""

        let read (ob: IObserver<obj>) (cancel: CancellationToken) = 
            Task.Factory.StartNew(fun () -> 
                trace "begin read loop"
                while serverExitCode().IsNone && not cancel.IsCancellationRequested do
                   try
                       let data = MessagePackSerializer.Deserialize<obj>(stdout, true)
                       ob.OnNext(data)
                   with 
                   | :? InvalidOperationException 
                   | :? System.IO.IOException
                   | :? System.Net.Sockets.SocketException
                   | :? ObjectDisposedException
                       -> ()

                let ec = serverExitCode()
                if ec.IsSome then
                    let code = ec.Value
                    trace "end read loop: process exited, code = %d" code
                    if code <> 0 then
                        ob.OnNext([|box(Crash code)|])
                        Thread.Sleep 2000
                else
                    trace "end read loop: server process still running"
                    Thread.Sleep 2000
                ob.OnCompleted()
            , cancel, TaskCreationOptions.LongRunning, TaskScheduler.Current)

        let reply (id: int) (rsp: Response) = async {
            let result, error = 
                match rsp.result with
                | Choice1Of2 r -> r, null
                | Choice2Of2 e -> null, e
            do! Async.AwaitTask(MessagePackSerializer.SerializeAsync(stdin, mkparams4 1 id result error))
            do! Async.AwaitTask(stdin.FlushAsync())
        }

        let pending = ConcurrentDictionary<int, TaskCompletionSource<Response>>()
        let parse (data: obj) : Event =
            match data :?> obj[] with
            // request
            | [| (Integer32 0); (Integer32 msg_id) ; (String method); :? (obj[]) as parameters |] 
                -> Request(msg_id, { method = method; parameters = parameters }, reply)
            // response
            | [| (Integer32 1); (Integer32 msg_id) ; err; result |]
                -> Response(msg_id, { result = if err = null then Choice1Of2 result else Choice2Of2 err })
            // redraw
            | [| (Integer32 2); (String "redraw"); :? (obj[]) as cmds |] 
                -> Redraw (Array.map parse_redrawcmd cmds)
            // notification
            | [| (Integer32 2); (String method); :? (obj[]) as parameters |]
                -> Notification { method = method; parameters = parameters }
            // event forwarding
            | [| :? Event as e |] -> e
            | _ -> raise <| EventParseException(data)

        let intercept (ev: Event) =
            match ev with
            | Response(msgid, rsp) ->
                // intercept response message, if it can be completed successfully
                match pending.TryRemove msgid with
                | true, src -> src.TrySetResult rsp |> not
                | _ -> false
            | _ -> true

        let rec _startRead() =
            System.Reactive.Linq.Observable.Create(read)
            |> Observable.map       parse
            |> Observable.catchWith exhandler

        and exhandler (ex: exn) = 
            trace "exhandler: %A" ex
            _startRead()

        let stdout = 
            _startRead()
            |> Observable.filter    intercept
            |> Observable.concat    (Observable.single Exit)

        let events = Observable.merge stdout stderr
        

        let notify (ev: Request) = task {
            let payload = mkparams3 2 ev.method ev.parameters
            // MessagePackSerializer.ToJson(payload) |> trace "notify: %s"
            do! MessagePackSerializer.SerializeAsync(stdin, payload)
            do! stdin.FlushAsync()
        }

        let mutable call_id = 0
        let call (ev: Request) = task {
            let myid = call_id
            call_id <- call_id + 1
            let src = TaskCompletionSource<Response>()
            if not <| pending.TryAdd(myid, src)
            then failwith "call: cannot create call request"

            let payload = mkparams4 0 myid ev.method ev.parameters
            MessagePackSerializer.ToJson(payload) |> trace "call: %s"
            do! MessagePackSerializer.SerializeAsync(stdin, payload)
            do! stdin.FlushAsync()
            let! response = src.Task

            match response.result with
            | Choice2Of2 err -> trace "call #%d(%s) failed with: %A" myid ev.method err
            | _ -> ()

            return response
        }

        m_io <- io
        m_events <- Some events
        m_notify <- notify
        m_call   <- call

    member __.stop (timeout: int) =
        match m_io with
        | StartProcess proc ->
            proc.CancelErrorRead()
            if not <| proc.WaitForExit(timeout) then
                proc.Kill()
            proc.Close()
        | ConnectTcp stream -> 
            stream.Close()
            stream.Dispose()
        | Disconnected -> ()

        m_io <- Disconnected
        m_events <- None
        m_notify <- default_notify
        m_call <- default_call

    member this.subscribe (ctx: SynchronizationContext) (fn: Event -> unit) =
        this.events
        |> Observable.observeOnContext ctx
        |> Observable.subscribe        fn

    member __.input (keys: string[]) =
        let keys = Array.map (fun x -> box x) keys
        m_call { method = "nvim_input"; parameters = keys }

    member __.grid_resize (id: int) (w: int) (h: int) =
        m_call { method = "nvim_ui_try_resize"; parameters = mkparams2 w h }

    member __.ui_attach (w:int) (h:int) =
        let opts = Dictionary<string, bool>()
        opts.[uiopt_rgb]          <- true
        (*opts.[uiopt_ext_multigrid] <- true*)
        opts.[uiopt_ext_linegrid] <- true

        m_call { method = "nvim_ui_attach"; parameters = mkparams3 w h opts }

    member __.set_var (var: string) (value: obj) =
        m_call { method = "nvim_set_var"; parameters = mkparams2 var value }

    member __.command (cmd: string) =
        m_call { method = "nvim_command"; parameters = mkparams1 cmd }


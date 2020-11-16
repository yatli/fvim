module FVim.neovim

open def
open getopt
open log
open common
open States

open MessagePack

open System
open System.Diagnostics
open System.Net.Sockets
open System.Text
open System.Collections.Concurrent
open System.Collections.Generic
open System.Security.Principal
open System.Threading.Tasks
open System.Threading
open FSharp.Control.Reactive
open FSharp.Control.Tasks.V2.ContextSensitive
open System.Runtime.InteropServices

let inline private trace fmt = trace "neovim.process" fmt

let private default_notify (e: Request) =
    failwithf "%A" e

let private default_call (e: Request) =
    failwithf "%A" e

type NvimIO =
    | Disconnected
    | Standalone of Process
    | RemoteSession of System.IO.Stream

type Nvim() = 
    let mutable m_notify      = default_notify
    let mutable m_call        = default_call
    let mutable m_events      = None
    let mutable m_io          = Disconnected
    let mutable m_disposables = []
    let m_cancelSrc           = new CancellationTokenSource()

    member __.Id = Guid.NewGuid()

    member private this.events =
        match m_events with
        | Some events -> events
        | None -> failwith "events"

    member private this.createIO serveropts = 
        match serveropts with
        | Embedded(prog, args, enc) ->
            let args = args |> escapeArgs |> join
            let psi  = ProcessStartInfo(prog, args)
            psi.CreateNoWindow          <- true
            psi.ErrorDialog             <- false
            psi.RedirectStandardError   <- true
            psi.RedirectStandardInput   <- true
            psi.RedirectStandardOutput  <- true
            psi.StandardErrorEncoding   <- enc
            psi.UseShellExecute         <- false
            psi.WindowStyle             <- ProcessWindowStyle.Hidden
            psi.WorkingDirectory        <- Environment.CurrentDirectory

            trace "Starting process. Program: %s; Arguments: %s" prog args
            Standalone <| Process.Start(psi)
        | NeovimRemote(Tcp ipe, _) ->
            let sock = new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            sock.Connect(ipe)
            RemoteSession <| (new NetworkStream(sock, true) :> System.IO.Stream)
        | NeovimRemote(NamedPipe addr, _) ->
            let pipe = new System.IO.Pipes.NamedPipeClientStream(".", addr, IO.Pipes.PipeDirection.InOut, IO.Pipes.PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation)
            pipe.Connect()
            RemoteSession pipe
        | _ ->
            failwith ""

    member this.start opts =
        match m_io, m_events with
        | Disconnected, None -> ()
        | _ -> failwith "neovim: already started"

        let io = this.createIO opts

        let serverExitCode() =
            match io with
            | Standalone proc -> 
              // note: on *Nix, when the nvim child process exits,
              // we don't get an exit code immediately. have to explicitly wait.
              if not <| RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
              then proc.WaitForExit(1000) |> ignore
              try Some proc.ExitCode with _ -> None
            | RemoteSession _ -> None
            | Disconnected -> Some -1

        let stdin, stdout, stderr = 
            match io with
            | Standalone proc ->
                let stdout = proc.StandardOutput.BaseStream
                let stdin  = proc.StandardInput.BaseStream
                let stderr = 
                    proc.ErrorDataReceived 
                    |> Observable.map (fun data -> Error data.Data )
                proc.BeginErrorReadLine()
                stdin, stdout, stderr
            | RemoteSession stream ->
                stream, stream, Observable.empty
            | _ -> failwith ""

        let read (ob: IObserver<obj>) (cancel: CancellationToken) = 
            Task.Factory.StartNew(fun () ->
                trace "%s" "begin read loop"
                let mutable ex = false
                while not ex && not cancel.IsCancellationRequested do
                   try
                       let data = MessagePackSerializer.Deserialize<obj>(stdout, true)
                       ob.OnNext(data)
                   with 
                   | :? InvalidOperationException 
                   | :? System.IO.IOException
                   | :? System.Net.Sockets.SocketException
                   | :? ObjectDisposedException
                       as _ex -> ex <- true

                let ec = serverExitCode()
                if ec.IsSome then
                  let code = ec.Value
                  trace "end read loop: process exited, code = %d" code
                  if code <> 0 then
                    m_cancelSrc.Cancel()
                    ob.OnNext([|box (Crash code)|])
                  else
                    ob.OnNext([|box Exit|])
                else
                  trace "%s" "end read loop."
                  ob.OnNext([|box Exit|])
                ob.OnCompleted()

            , cancel, TaskCreationOptions.LongRunning, TaskScheduler.Current)

        let reply (id: int) (rsp: Response) = task {
            let result, error = 
                match rsp.result with
                | Ok r -> null, r
                | Result.Error e -> e, null
            do! MessagePackSerializer.SerializeAsync(stdin, mkparams4 1 id result error)
            do! stdin.FlushAsync()
        }

        let pending = ConcurrentDictionary<int, TaskCompletionSource<Response>>()
        let parse (data: obj) : Event =
            match data :?> obj[] with
            // request
            | [| (Integer32 0); (Integer32 msg_id) ; (String method); :? (obj[]) as parameters |] 
                -> Request(msg_id, { method = method; parameters = parameters }, reply)
            // response
            | [| (Integer32 1); (Integer32 msg_id) ; err; result |]
                -> Response(msg_id, { result = if err = null then Ok result else Result.Error err })
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
                match rsp.result with
                | Result.Error err -> trace "call %d: error response %A" msgid err
                | _ -> ()

                match pending.TryRemove msgid with
                | true, src -> src.SetResult rsp; false
                | _ -> false
            | _ -> true

        let rec _startRead() =
            System.Reactive.Linq.Observable.Create(read)
            |> Observable.map       parse
            (*|> Observable.catchWith exhandler*)

        and exhandler (ex: exn) = 
            trace "exhandler: %A" ex
            _startRead()

        let stdout = 
            _startRead()
            |> Observable.filter    intercept

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

            use cancel_reg = m_cancelSrc.Token.Register(fun () -> src.TrySetCanceled() |> ignore)

            let payload = mkparams4 0 myid ev.method ev.parameters
#if DEBUG
            MessagePackSerializer.ToJson(payload) |> trace "call: %d -> %s" myid
#endif
            do! MessagePackSerializer.SerializeAsync(stdin, payload)
            do! stdin.FlushAsync()
            return! src.Task
        }

        m_io <- io
        m_events <- Some events
        m_notify <- notify
        m_call   <- call

    member __.isRemote 
        with get() =
            match m_io with
            | Standalone _ -> false
            | _ -> true

    member __.stop (timeout: int) =

        // Send cancellation signal
        m_cancelSrc.Cancel()
        m_cancelSrc.Dispose()

        // Disconnect all the subscriptions
        for (d: IDisposable) in m_disposables do
            d.Dispose()

        // Close the IO channel
        match m_io with
        | Standalone proc ->
            proc.CancelErrorRead()
            if not <| proc.WaitForExit(timeout) then
                proc.Kill()
            proc.Close()
        | RemoteSession stream -> 
            stream.Close()
            stream.Dispose()
        | Disconnected -> ()

        m_io <- Disconnected
        m_events <- None
        m_notify <- default_notify
        m_call <- default_call
        m_disposables <- []

    member __.pushSubscription(x: IDisposable) =
        m_disposables <- x :: m_disposables

    member this.subscribe (ctx: SynchronizationContext) (fn: Event -> unit) =
        this.events
        |> Observable.observeOnContext ctx
        (*|> Observable.synchronize*)
        |> Observable.subscribe fn
        |> this.pushSubscription

    //  ========================== NeoVim API ===============================

    member __.input (key: string) =
        m_call { method = "nvim_input"; parameters = [| box key |] }

    member __.input_mouse (button: string) (action: string) (mods: string) (grid: int) (row: int) (col: int)  =
        m_call { method = "nvim_input_mouse"; parameters = [| box button; box action; box mods; box grid; box row; box col |] }

    member __.grid_resize (id: int) (w: int) (h: int) =
        if ui_multigrid then
            m_call { method = "nvim_ui_try_resize_grid"; parameters = mkparams3 id w h }
        else
            m_call { method = "nvim_ui_try_resize"; parameters = mkparams2 w h }

    member __.ui_attach (w:int) (h:int) =
        let opts = hashmap [
            uiopt_rgb, true
            uiopt_ext_linegrid, true
        ]
        PopulateUIOptions opts
        m_call { method = "nvim_ui_attach"; parameters = mkparams3 w h opts }

    member __.exists (var: string) =
        task {
            let! response = m_call { method = "nvim_call_function"; parameters = mkparams2 "exists" (mkparams1 var) }
            //return response
            trace "exists: response = %A" response
            match response.result with
            | Ok(Integer32 1) -> return true
            | _ -> return false
        }

    member __.set_var (var: string) (value: obj) =
        m_call { method = "nvim_set_var"; parameters = mkparams2 var value }

    member __.command (cmd: string) =
        m_call { method = "nvim_command"; parameters = mkparams1 cmd }

    member nvim.``command!`` (name: string) (nargs: int) (cmd: string) =
        let nargs = 
            match nargs with
            | 0 -> "0"
            | 1 -> "1"
            | _ -> "+"

        nvim.command(sprintf "command! -nargs=%s %s %s" nargs name cmd)

    member nvim.edit (file: string) =
        nvim.command ("edit " + file)

    member nvim.call = m_call

    member nvim.quitall () =
        nvim.command "confirm quitall"

    member __.set_client_info (name: string) (version: hashmap<string,string>) (_type: string) (methods: hashmap<string,string>) (attributes: hashmap<string,string>) =
        m_call { 
            method = "nvim_set_client_info"
            parameters = mkparams5 name version _type methods attributes
        }

    member __.list_chans() =
        task {
            let! rsp = m_call { method = "nvim_list_chans"; parameters = [||] }

            match rsp.result with
            | Ok(ObjArray arr) -> return arr
            | _ -> return [||]
        }

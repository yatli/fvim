module FVim.neovim

open def
open getopt
open log
open common
open states
open daemon
open msgpack

open MessagePack

open System
open System.Diagnostics
open System.Net.Sockets
open System.Collections.Concurrent
open System.Security.Principal
open System.Threading.Tasks
open System.Threading
open FSharp.Control.Reactive
open System.Runtime.InteropServices

let inline private trace fmt = trace "neovim.process" fmt

let private default_notify (e: Request) =
  task { return () }

let private default_call (e: Request) =
  task { return Result.Error(box "not connected") }

type NvimIO =
    | Disconnected
    // Either local or tunneled. running NeoVim in embedded mode
    | Standalone of Process
    // NeoVim RPC remoting, or FVR local session
    | RemoteSession of System.IO.Stream
    // FVR tunneled session
    | TunneledSession of Process

type Nvim() as nvim = 
    let mutable m_notify      = default_notify
    let mutable m_call        = default_call
    let mutable m_events      = None
    let mutable m_io          = Disconnected
    let mutable m_disposables = []
    let mutable m_cancelSrc   = Unchecked.defaultof<CancellationTokenSource>

    //  ========================== Init/Deinit ===============================

    member __.Id = Guid.NewGuid()

    member private __.events =
        match m_events with
        | Some events -> events
        | None -> failwith "events"

    member private __.createIO serveropts = 
        match serveropts with
        | Embedded(prog, args, enc) ->
            trace "Starting process. Program: %s; Arguments: %A" prog args
            let proc = newProcess prog args enc
            proc.Start() |> ignore
            Standalone proc
        | NeovimRemote(Tcp ipe, _) ->
            let sock = new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            sock.Connect(ipe)
            RemoteSession <| (new NetworkStream(sock, true) :> System.IO.Stream)
        | NeovimRemote(NamedPipe addr, _) ->
            let pipe = new System.IO.Pipes.NamedPipeClientStream(".", addr, IO.Pipes.PipeDirection.InOut, IO.Pipes.PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation)
            pipe.Connect()
            RemoteSession pipe
        | FVimRemote(name, Local, verb, _) ->
            let name = Option.defaultValue defaultDaemonName name
            trace "Connecting to local fvr session '%s'" name
            let pipe = new System.IO.Pipes.NamedPipeClientStream(".", name, IO.Pipes.PipeDirection.InOut, IO.Pipes.PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation)
            pipe.Connect()
            trace "Connected, sending session request..."
            let id = fvrConnect pipe pipe verb
            if id < 0 then
              pipe.Dispose()
              getErrorMsg id |> failwith 
            RemoteSession pipe
        | FVimRemote(pipe, Remote(prog, args), verb, _) ->
            let pname = Option.defaultValue defaultDaemonName pipe
            let paddr = pipeaddrUnix pname
            trace "Connecting to remote fvr session '%s'" paddr
            let proc = newProcess prog (args @ [paddr]) Text.Encoding.UTF8
            proc.Start() |> ignore
            let id = fvrConnect proc.StandardInput.BaseStream proc.StandardOutput.BaseStream verb
            if id < 0 then
              proc.Kill()
              getErrorMsg id |> failwith 
            else
              trace "Connected to session %d" id
            TunneledSession proc

    member nvim.start opts =
        match m_io, m_events with
        | Disconnected, None -> ()
        | _ -> failwith "neovim: already started"

        m_cancelSrc <- new CancellationTokenSource()
        let io = nvim.createIO opts

        let serverExitCode() =
            match io with
            | Standalone proc
            | TunneledSession proc ->
              // note: on *Nix, when the nvim child process exits,
              // we don't get an exit code immediately. have to explicitly wait.
              if not <| RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
              then proc.WaitForExit(1000) |> ignore
              try Some proc.ExitCode with _ -> None
            | RemoteSession _ -> None
            | Disconnected -> Some -1

        let stdin, stdout, stderr = 
            match io with
            | Standalone proc
            | TunneledSession proc ->
                let stdout = proc.StandardOutput.BaseStream
                let stdin  = proc.StandardInput.BaseStream
                let stderr = 
                    proc.ErrorDataReceived 
                    |> Observable.map (fun data -> StdError data.Data )
                proc.BeginErrorReadLine()
                stdin, stdout, stderr
            | RemoteSession stream ->
                stream, stream, Observable.empty
            | Disconnected -> failwith "not connected."

        let read (ob: IObserver<obj>) (cancel: CancellationToken) = 
          task {
            trace "begin read loop"
            let mutable channelClosed = false
            let mutable unhandledException = None
            use reader = new MessagePackStreamReader(stdout)
            while not channelClosed && not cancel.IsCancellationRequested && unhandledException.IsNone do
              try
                let! data = reader.ReadAsync(cancel) 
                if data.HasValue then
                    let mutable v = data.Value
                    let data = MessagePackSerializer.Deserialize<obj>(&v, options=msgpackOpts)
                    ob.OnNext(data)
                else
                    channelClosed <- true
              with 
              | :? InvalidOperationException as _ex when _ex.Message = "Invalid MessagePack code was detected, code:-1"
                   -> channelClosed <- true
              | :? System.IO.IOException
              | :? System.Net.Sockets.SocketException
              | :? ObjectDisposedException
                   -> channelClosed <- true
              | ex -> unhandledException <- Some ex

            match serverExitCode(), unhandledException with
            | Some code, _ ->
              trace "end read loop: process exited, code = %d" code
              // 0 = exit normally
              // 1 = exit with error message but it's not a crash
              if code <> 0 && code <> 1 then
                m_cancelSrc.Cancel()
                ob.OnNext([|box (Crash code)|])
              else
                ob.OnNext([|box (Exit code)|])
            | _, Some ex ->
              trace "end read loop: unhandled exception."
              ob.OnNext([|box (UnhandledException ex)|])
            | _ ->
              trace "end read loop."
              ob.OnNext([|box (Exit 0)|])
            ob.OnCompleted()
          } :> Task

        let reply (id: int) (rsp: Response) = task {
            let result, error = 
                match rsp with
                | Ok r -> null, r
                | Error e -> e, null
            do! MessagePackSerializer.SerializeAsync(stdin, mkparams4 1 id result error)
            do! stdin.FlushAsync()
        }

        let pending = ConcurrentDictionary<int, TaskCompletionSource<Response>>()
        let parse (data: obj) : Event =
            match data with
            | :? (obj[]) as data ->
              match data with
              // request
              | [| (Integer32 0); (Integer32 msg_id) ; (String method); :? (obj[]) as parameters |] 
                  -> RpcRequest(msg_id, { method = method; parameters = parameters }, reply)
              // response
              | [| (Integer32 1); (Integer32 msg_id) ; err; result |]
                  -> RpcResponse(msg_id, if err = null then Ok result else Error err)
              // notification
              | [| (Integer32 2); (String method); :? (obj[]) as parameters |]
                  -> Notification { method = method; parameters = parameters }
              // event forwarding
              | [| :? Event as e |] -> e
              | _ -> raise <| EventParseException(data)
            | :? byte as b -> ByteMessage b
            | _ -> raise <| EventParseException(data)

        let intercept (ev: Event) =
            match ev with
            | RpcResponse(msgid, rsp) ->
                // intercept response message, if it can be completed successfully
                match rsp with
                | Error err -> trace "call %d: error response %A" msgid err
                | _ -> ()

                match pending.TryRemove msgid with
                | true, src -> src.SetResult rsp
                | _ -> ()
                false
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
#if DEBUG
            //MessagePackSerializer.SerializeToJson(payload) |> trace "notify: %s"
#endif
            do! MessagePackSerializer.SerializeAsync(stdin, payload)
            do! stdin.FlushAsync()
        }

        let mutable call_id = 0
        let call (ev: Request) = task {
            let myid = Interlocked.Increment(&call_id)
            let src = TaskCompletionSource<Response>()
            if not <| pending.TryAdd(myid, src)
            then failwith "call: cannot create call request"

            use cancel_reg = m_cancelSrc.Token.Register(fun () -> src.TrySetCanceled() |> ignore)

            let payload = mkparams4 0 myid ev.method ev.parameters
#if DEBUG
            MessagePackSerializer.SerializeToJson(payload) |> trace "call: %d -> %s" myid
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

        match m_io with
        | Disconnected -> ()
        | _ ->

        trace "stopping"

        // Send cancellation signal
        m_cancelSrc.Cancel()
        m_cancelSrc.Dispose()

        // Disconnect all the subscriptions
        for (d: IDisposable) in m_disposables do
            d.Dispose()

        // Close the IO channel
        match m_io with
        | Standalone proc
        | TunneledSession proc ->
            proc.CancelErrorRead()
            if not <| proc.WaitForExit(timeout) then
                proc.Kill()
            proc.Close()
        | RemoteSession stream -> 
            stream.Close()
            stream.Dispose()
        | Disconnected -> ()

        m_cancelSrc <- null
        m_io <- Disconnected
        m_events <- None
        m_notify <- default_notify
        m_call <- default_call
        m_disposables <- []

    member __.pushSubscription(x: IDisposable) =
        m_disposables <- x :: m_disposables

    member __.subscribe (ctx: SynchronizationContext) (fn: Event -> unit) =
        nvim.events
        |> Observable.observeOnContext ctx
        (*|> Observable.synchronize*)
        |> Observable.subscribe fn
        |> nvim.pushSubscription

    //  ========================== NeoVim API ===============================

    member __.input (key: string) =
        nvim.call { method = "nvim_input"; parameters = [| box key |] }

    member __.input_mouse (button: string) (action: string) (mods: string) (grid: int) (row: int) (col: int)  =
        nvim.call { method = "nvim_input_mouse"; parameters = [| box button; box action; box mods; box grid; box row; box col |] }

    member __.grid_resize (id: int) (w: int) (h: int) =
        if ui_multigrid then
            nvim.call { method = "nvim_ui_try_resize_grid"; parameters = mkparams3 id w h }
        else
            nvim.call { method = "nvim_ui_try_resize"; parameters = mkparams2 w h }

    member __.ui_attach (w:int) (h:int) =
        let opts = hashmap [
            uiopt_rgb, true
            uiopt_ext_linegrid, true
        ]
        PopulateUIOptions opts
        nvim.call { method = "nvim_ui_attach"; parameters = mkparams3 w h opts }

    member __.exists (var: string) =
        task {
            let! response = nvim.call { method = "nvim_call_function"; parameters = mkparams2 "exists" (mkparams1 var) }
            //return response
            trace "exists: response = %A" response
            match response with
            | Ok(Integer32 1) -> return true
            | _ -> return false
        }

    member __.set_var (var: string) (value: obj) =
        nvim.call { method = "nvim_set_var"; parameters = mkparams2 var value }

    member __.command (cmd: string) =
        nvim.call { method = "nvim_command"; parameters = mkparams1 cmd }

    member __.``command!`` (name: string) (nargs: int) (cmd: string) =
        let nargs = 
            match nargs with
            | 0 -> "0"
            | 1 -> "1"
            | _ -> "+"

        nvim.command $"command! -nargs={nargs} {name} {cmd}"

    member __.exec (src: string) (output: bool) =
        nvim.call { method = "nvim_exec"; parameters = mkparams2 src output }

    member __.exec_lua (src: string) (args: obj[]) =
        nvim.call { method = "nvim_exec_lua"; parameters = mkparams2 src args }

    member __.edit (file: string) =
        nvim.command ("edit " + file)

    member __.call req = m_call req

    member __.quitall () =
        nvim.command "confirm quitall"

    member __.set_client_info (name: string) (version: hashmap<string,string>) (_type: string) (methods: hashmap<string,string>) (attributes: hashmap<string,string>) =
        nvim.call { 
            method = "nvim_set_client_info"
            parameters = mkparams5 name version _type methods attributes
        }

    member __.list_chans() =
        task {
            match! nvim.call { method = "nvim_list_chans"; parameters = [||] } with
            | Ok(ObjArray arr) -> return arr
            | _ -> return [||]
        }

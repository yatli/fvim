module FVim.neovim

open System
open System.Diagnostics
open System.Text
open System.Collections.Concurrent
open System.Diagnostics.Tracing
open MessagePack
open System.Collections.Generic
open System.Threading.Tasks
open System.Threading
open FSharp.Control.Reactive

type NeovimRequest = 
    {
        method:     string
        parameters: obj[]
    }

type NeovimResponse = 
    {
        result: Choice<obj, obj>
    }

type NeovimEvent =
| Request      of int * NeovimRequest
| Response     of int * NeovimResponse
| Notification of NeovimRequest
| Error        of string

[<Struct>]
[<MessagePackObject(keyAsPropertyName=true)>]
type UiOptions =
    {
        rgb            : bool
        //ext_popupmenu  : bool
        //ext_tabline    : bool
        //ext_cmdline    : bool
        //ext_wildmenu   : bool
        //ext_messages   : bool
        //ext_linegrid   : bool
        //ext_multigrid  : bool
        //ext_hlstate    : bool
        //ext_termcolors : bool
    }

type NeovimEventParseException(data: obj) =
    inherit exn()
    member __.Input = data
    override __.Message = sprintf "Could not parse the neovim message: %A" data

let private default_notify (e: NeovimRequest) =
    failwith ""

let private default_call (e: NeovimRequest) =
    failwith ""

let mkparams1 (t1: 'T1)                                              = [| box t1 |]
let mkparams2 (t1: 'T1) (t2: 'T2)                                    = [| box t1; box t2 |]
let mkparams3 (t1: 'T1) (t2: 'T2) (t3: 'T3)                          = [| box t1; box t2; box t3 |]
let mkparams4 (t1: 'T1) (t2: 'T2) (t3: 'T3) (t4: 'T4)                = [| box t1; box t2; box t3; box t4|]


type NeovimProcess() = 
    let m_id = Guid.NewGuid()
    let mutable m_notify = default_notify
    let mutable m_call   = default_call
    let mutable m_proc   = None
    let mutable m_events = None

    member private this.events =
        match m_events with
        | Some events -> events
        | None -> failwith "events"

    member private this.proc =
        match m_proc with
        | Some proc -> proc
        | None -> failwith "process"


    member __.start () =
        match m_proc, m_events with
        | Some(_) , _
        | _, Some(_) -> failwith "neovim: already started"
        | _ -> ()

        let psi  = ProcessStartInfo("nvim", "--embed")
        psi.CreateNoWindow          <- true
        psi.ErrorDialog             <- false
        psi.RedirectStandardError   <- true
        psi.RedirectStandardInput   <- true
        psi.RedirectStandardOutput  <- true
        psi.StandardErrorEncoding   <- Encoding.UTF8
        psi.UseShellExecute         <- false
        psi.WindowStyle             <- ProcessWindowStyle.Hidden
        psi.WorkingDirectory        <- Environment.CurrentDirectory

        let proc   = Process.Start(psi)
        let stdout = proc.StandardOutput.BaseStream
        let stdin  = proc.StandardInput.BaseStream

        let read (ob: IObserver<obj>) (cancel: CancellationToken) = 
            let read_async = async {
                 while not proc.HasExited && not cancel.IsCancellationRequested do
                     printfn "polling"
                     let! data = Async.AwaitTask(MessagePackSerializer.DeserializeAsync<obj>(stdout))
                     printfn "data = %A" data
                     ob.OnNext(data)
            }
            Async.StartAsTask(read_async, cancellationToken=cancel) :> Task

        let pending = ConcurrentDictionary<int, TaskCompletionSource<NeovimResponse>>()
        let parse (data: obj) : NeovimEvent =
            match data :?> obj[] with
            // request
            | [| :? int as msg_type; :? int as msg_id ; :? string as method; :? (obj[]) as parameters |] when msg_type = 0 
                -> Request(msg_id, { method = method; parameters = parameters })
            // response
            | [| :? int as msg_type; :? int as msg_id ; err; result |] when msg_type = 1
                -> Response(msg_id, { result = if err = null then Choice1Of2 result else Choice2Of2 err })
            // notification
            | [| :? int as msg_type; :? string as method ; :? (obj[]) as parameters |] when msg_type = 2 
                -> Notification { method = method; parameters = parameters }
            | _ -> raise <| NeovimEventParseException(data)

        let intercept (ev: NeovimEvent) =
            match ev with
            | Response(msgid, rsp) ->
                // intercept response message, if it can be completed successfully
                match pending.TryRemove msgid with
                | true, src -> src.TrySetResult rsp |> not
                | _ -> false
            | _ -> true

        let rec exhandler (ex) = 
            // TODO log the ex
            System.Reactive.Linq.Observable.Create(read)
            |> Observable.map       parse
            |> Observable.catchWith exhandler

        let stdout = 
            System.Reactive.Linq.Observable.Create(read)
            |> Observable.map       parse
            |> Observable.catchWith exhandler
            |> Observable.filter    intercept
        let stderr = 
            proc.ErrorDataReceived 
            |> Observable.map (fun data -> Error data.Data )
        let events = Observable.merge stdout stderr
        

        let notify (ev: NeovimRequest) = async {
            let payload = mkparams3 2 ev.method ev.parameters
            MessagePackSerializer.ToJson(payload) |> printfn "notify: %s"
            let task = MessagePackSerializer.SerializeAsync(stdin, payload)
            do! Async.AwaitTask(task)
            //do! Async.AwaitTask(stdin.FlushAsync())
            //printfn "notify: flushed."
        }

        let mutable call_id = 0
        let call (ev: NeovimRequest) = async {
            let myid = call_id
            call_id <- call_id + 1
            let src = TaskCompletionSource<NeovimResponse>()
            if not <| pending.TryAdd(myid, src)
            then failwith "call: cannot create call request"

            let payload = mkparams4 0 myid ev.method ev.parameters
            MessagePackSerializer.ToJson(payload) |> printfn "call: %s"
            do! MessagePackSerializer.SerializeAsync(stdin, payload) |> Async.AwaitTask
            do! stdin.FlushAsync() |> Async.AwaitTask
            printfn "call: flushed. waiting for response"
            let! result = src.Task |> Async.AwaitTask
            printfn "call: result = %A" result
            return result
        }

        proc.BeginErrorReadLine()

        m_proc   <- Some proc
        m_events <- Some events
        m_notify <- notify
        m_call   <- call

    member __.stop (timeout: int) =
        match m_proc with
        | Some proc ->
            proc.CancelErrorRead()
            if not <| proc.WaitForExit(timeout) then
                proc.Kill()
            proc.Close()
            m_proc <- None
            m_events <- None
            m_notify <- default_notify
            m_call <- default_call
        | _ -> ()

    member this.subscribe (ctx: SynchronizationContext) (fn: NeovimEvent -> unit) =
        this.events
        |> Observable.observeOnContext ctx
        |> Observable.subscribe        fn

    member __.ui_attach (w:int, h:int) =
        let opts = 
            { 
                rgb            = true 
                //ext_cmdline    = false
                //ext_hlstate    = false
                //ext_linegrid   = false
                //ext_messages   = false
                //ext_multigrid  = false
                //ext_popupmenu  = false
                //ext_tabline    = false
                //ext_termcolors = false
                //ext_wildmenu   = false
            }

        m_call { method = "nvim_ui_attach"; parameters = mkparams3 100 50 opts }
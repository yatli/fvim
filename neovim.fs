module neovim

open System
open System.Diagnostics
open System.Text
open System.Collections.Concurrent
open System.Reactive
open System.Reactive.Linq
open System.Diagnostics.Tracing
open MessagePack
open System.Collections.Generic
open System.Threading.Tasks

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

type NeovimProcess = 
    {
        id:          Guid
        proc:        Process option
        events:      IObservable<NeovimEvent> option
        notify:      NeovimRequest -> unit Async
        call:        NeovimRequest -> NeovimResponse Async
    }

type NeovimEventParseException(data: obj) =
    inherit exn()
    member __.Input = data
    override __.Message = sprintf "Could not parse the neovim message: %A" data

let private default_notify (e: NeovimRequest) =
    failwith ""

let private default_call (e: NeovimRequest) =
    failwith ""

let create () =
    {
        id          = Guid.NewGuid()
        proc        = None
        events      = None
        notify      = default_notify
        call        = default_call
    }

let start (nvim: NeovimProcess) =
    match nvim with
    | { proc = Some _ }
    | { events = Some _ } -> failwith "neovim: already started"
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

    let proc = Process.Start(psi)
    let stdout = proc.StandardOutput.BaseStream
    let stdin = proc.StandardInput.BaseStream
    let read() = Observable.While(
                    (fun () -> not proc.HasExited), 
                    Observable.FromAsync(fun () -> MessagePackSerializer.DeserializeAsync<obj>(stdout)))
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
        read().Select(parse).Catch(exhandler)
    let stdout = read().Select(parse).Catch(exhandler).Where(intercept)
    let stderr = 
        proc.ErrorDataReceived 
        |> Observable.map (fun data -> Error data.Data )
    let events = stdout.Merge(stderr)
    

    let notify (ev: NeovimRequest) = async {
        let task = MessagePackSerializer.SerializeAsync(stdin, [| box 2; box ev.method; box ev.parameters |])
        return! Async.AwaitTask(task)
    }

    let mutable call_id = 1
    let call (ev: NeovimRequest) = async {
        call_id <- call_id + 1
        let src = TaskCompletionSource<NeovimResponse>()
        if not <| pending.TryAdd(call_id, src)
        then failwith "cannot create call request"
        do! MessagePackSerializer.SerializeAsync(stdin, [| box 0; box call_id; box ev.method; box ev.parameters |]) |> Async.AwaitTask
        return! src.Task |> Async.AwaitTask
    }

    proc.BeginErrorReadLine()

    {nvim with proc = Some proc; events = Some events; notify = notify; call = call}

let stop (nvim: NeovimProcess) (timeout: int) =
    match nvim.proc with
    | Some proc ->
        proc.CancelErrorRead()
        if not <| proc.WaitForExit(timeout) then
            proc.Kill()
        proc.Close()
        {nvim with proc = None; events = None; notify = default_notify; call = default_call }
    | _ -> nvim

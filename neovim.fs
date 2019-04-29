module neovim

open System
open System.Diagnostics
open System.Text
open System.Collections.Concurrent
open System.Reactive
open System.Reactive.Linq
open System.Diagnostics.Tracing

type NeovimEvent =
    {
        timestamp: DateTimeOffset
    }

type NeovimError =
    {
        timestamp: DateTimeOffset
        message:   string
    }

type NeovimProcess = 
    {
        id:          Guid
        proc:        Process option
        events:      IObservable<NeovimEvent> option
        stderr:      IObservable<NeovimError> option
        notify:      NeovimEvent -> unit
    }

let private default_notify (e: NeovimEvent) =
    failwith ""

let create () =
    {
        id          = Guid.NewGuid()
        proc        = None
        events      = None
        stderr      = None
        notify      = default_notify
    }

let started (nvim: NeovimProcess) = 
    match nvim.proc with
    | Some proc -> true
    | _ -> false

let start (nvim: NeovimProcess) =
    match nvim with
    | { proc = Some _ }
    | { events = Some _ } 
    | { stderr = Some _ } -> failwith "neovim: already started"
    | _ -> ()

    let psi  = ProcessStartInfo("nvim", "--embedded")
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
    //let events = Observable.Timestamp proc.OutputDataReceived 

    let stdout = proc.StandardOutput.BaseStream

    //Observable.Crea

    let stderr = 
        proc.ErrorDataReceived 
        |> Observable.Timestamp
        |> Observable.map (fun data -> { timestamp = data.Timestamp; message = data.Value.Data })
    
    proc.BeginErrorReadLine()

    {nvim with proc = Some proc; stderr = Some stderr}

let get_active_proc (nvim: NeovimProcess) =
    if not(started nvim) then
        failwith "neovim: process not started"
    nvim.proc.Value

let stop (nvim: NeovimProcess) (timeout: int) =
    nvim |>
    get_active_proc |>
    fun proc ->
    proc.CancelErrorRead()
    proc.CancelOutputRead()
    if not <| proc.WaitForExit(timeout) then
        proc.Kill()
    proc.Close()
    {nvim with proc = None; events = None; stderr = None; notify = default_notify }

module FVim.log

open getopt
open System.Diagnostics
open FSharp.Control.Reactive

let private _logsSource  = Event<string*string>()
let private _logsPub     = _logsSource.Publish |> Observable.map (fun (a,b) -> sprintf "%s: %s" a b)

let private _logsESource = Event<string*string>()
let private _logsEPub    = _logsESource.Publish |> Observable.map (fun (a,b) -> sprintf "error: %s: %s" a b)

let private _logsSink    = Observable.merge _logsPub _logsEPub |> Observable.synchronize
let private _addLogger   = _logsSink.Add 

let trace cat fmt =
    Printf.kprintf (fun s -> _logsSource.Trigger(cat, s)) fmt

let error cat fmt =
    Printf.kprintf (fun s -> _logsESource.Trigger(cat, s)) fmt

let init { logToStdout = logToStdout; logToFile = logToFile } =
    #if DEBUG
    let logToStdout = true
    #endif
    if logToStdout then _addLogger(fun str -> printfn "%s" str)
    if logToFile.IsSome then _addLogger(fun str -> System.IO.File.AppendAllText(logToFile.Value, str + "\n"))

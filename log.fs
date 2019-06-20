module FVim.log

open getopt
open System.Diagnostics
open FSharp.Control.Reactive

let mutable private _filter = fun _ -> true

let private _logsSource  = Event<string*string>()
let private _logsPub     = _logsSource.Publish |> Observable.map (fun (a,b) -> sprintf "%s: %s" a b)

let private _logsESource = Event<string*string>()
let private _logsEPub    = _logsESource.Publish |> Observable.map (fun (a,b) -> sprintf "error: %s: %s" a b)

let private _logsSink    = Observable.merge _logsPub _logsEPub |> Observable.filter(fun x -> _filter x) 

let trace cat fmt =
    Printf.kprintf (fun s -> _logsSource.Trigger(cat, s)) fmt

let error cat fmt =
    Printf.kprintf (fun s -> _logsESource.Trigger(cat, s)) fmt

let flush() =
    async {
        do! Async.Sleep 2000
    }

let init { logToStdout = logToStdout; logToFile = logToFile; logPatterns = logPatterns } =
    #if DEBUG
    let logToStdout = true
    #endif
    if logToStdout then 
        _logsSink.Add(fun str -> printfn "%s" str)
    let logToFile = Option.defaultValue (System.IO.Path.Combine(config.configdir, "fvim.log")) logToFile
    try System.IO.File.Delete logToFile
    with _ -> ()
    _logsSink
    |> Observable.bufferSpan (System.TimeSpan.FromMilliseconds 1000.0)
    |> Observable.add (fun strs -> 
            let strs = strs |> Array.ofSeq
            if strs.Length > 0 then
                try System.IO.File.AppendAllLines(logToFile, strs)
                with _ -> ()
        )

    if logPatterns.IsSome then
        let patterns = logPatterns.Value.Split(",")
        trace "log" "trace patterns: %A" patterns
        _filter <- fun s -> Array.exists (fun (x: string) -> s.Contains x) patterns

    trace "log" "fvim started. time = %A" System.DateTime.Now

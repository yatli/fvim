module FVim.log

open getopt
open System.Diagnostics

let mutable private _writelog = (fun _ -> ())

let _addLogger logger =
    let _current = _writelog
    _writelog <- (fun str -> _current str; logger str)

let trace cat fmt =
    Printf.kprintf (fun s -> _writelog(sprintf "%s: %s" cat s)) fmt

let error cat fmt =
    Printf.kprintf (fun s -> _writelog(sprintf "error: %s: %s" cat s)) fmt

let init { logToStdout = logToStdout; logToFile = logToFile } =
    #if DEBUG
    let logToStdout = true
    #endif
    if logToStdout then _addLogger(fun str -> printfn "%s" str)
    if logToFile.IsSome then _addLogger(fun str -> System.IO.File.AppendAllText(logToFile.Value, str + "\n"))

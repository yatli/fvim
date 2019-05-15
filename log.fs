module FVim.log

open System.Diagnostics

let trace cat fmt =
    Printf.kprintf (fun s -> Trace.TraceInformation(sprintf "%s: %s" cat s)) fmt

let error cat fmt =
    Printf.kprintf (fun s -> Trace.TraceError(sprintf "%s: %s" cat s)) fmt

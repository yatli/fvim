module FVim.log

open System.Diagnostics

let trace cat fmt =
    #if DEBUG
    Printf.kprintf (fun s -> Trace.WriteLine(s, cat)) fmt
    #else
    Printf.kprintf (fun s -> ()) fmt
    #endif

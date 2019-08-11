module FVim.log

open getopt
open System.Diagnostics
open FSharp.Control.Reactive
open System.Collections.Generic

let mutable private _filter = fun _ -> true

let private _logsSource  = Event<string*string>()
let private _logsPub     = _logsSource.Publish |> Observable.map (fun (a,b) -> sprintf "%s: %s" a b)

let private _logsESource = Event<string*string>()
let private _logsEPub    = _logsESource.Publish |> Observable.map (fun (a,b) -> sprintf "error: %s: %s" a b)

let private _logsSink    = Observable.merge _logsPub _logsEPub |> Observable.filter(fun x -> _filter x) 

let mutable private _n_logsSink = 0

type FormatIgnoreBuilder<'T>() = 
    static let m_TThis = typeof<FormatIgnoreBuilder<'T>>
    static let m_Ignore =
        let resultT = typeof<'T>
        let ret = 
            if resultT = typeof<unit> then box ()
            else
            // some kind of FSharpFunc`2
            let fargs = resultT.GetGenericArguments() 
            let tail = 
                m_TThis
                    .GetGenericTypeDefinition()
                    .MakeGenericType(fargs.[1])
                    .GetProperty("Ignore", System.Reflection.BindingFlags.Static ||| System.Reflection.BindingFlags.Public)
                    .GetValue(null)

            let F = 
                m_TThis
                    .GetMethod("F", System.Reflection.BindingFlags.Static ||| System.Reflection.BindingFlags.Public) 
                    .MakeGenericMethod([|fargs.[0]; fargs.[1]|])

            F.Invoke(null, [|tail|])
        ret :?> 'T

    static member F<'a, 'b> (x: 'b) = (fun (a: 'a) -> x)
    static member Ignore = m_Ignore

let trace cat (fmt: Printf.StringFormat< 'a , unit >) =
    if _n_logsSink > 0 then
        Printf.kprintf (fun s -> _logsSource.Trigger(cat, s)) fmt
    else
        FormatIgnoreBuilder<'a>.Ignore

let error cat fmt =
    if _n_logsSink > 0 then
        Printf.kprintf (fun s -> _logsESource.Trigger(cat, s)) fmt
    else
        FormatIgnoreBuilder<'a>.Ignore

// XXX seriously?
let flush() =
    async {
        do! Async.Sleep 2000
    }

let init { logToStdout = logToStdout; logToFile = logToFile; logPatterns = logPatterns; intent = intent } =
    #if DEBUG
    let logToStdout = true
    #endif
    if logToStdout then 
        _n_logsSink <- _n_logsSink + 1
        _logsSink.Add(fun str -> printfn "%s" str)
    let time = System.DateTime.Now
    let ftime = time.ToString "yyyy-MM-dd-hh-mm-ss"
    let fprefix = 
        match intent with
        | Daemon _ -> "fvim-daemon"
        | _ -> "fvim"
    if logToFile then
        _n_logsSink <- _n_logsSink + 1
        let logname = sprintf "%s-%s.log" fprefix ftime
        let logToFile = System.IO.Path.Combine(config.configdir, logname)
        try System.IO.File.Delete logToFile
        with _ -> ()
        _logsSink
        |> Observable.bufferSpan (System.TimeSpan.FromMilliseconds 1000.0)
        |> Observable.add (fun strs -> 
                let strs = strs |> Array.ofSeq
                if strs.Length > 0 then
                    try System.IO.File.AppendAllLines(logToFile, strs)
                    with _ -> ())

    if logPatterns.IsSome then
        let patterns = logPatterns.Value.Split(",")
        trace "log" "trace patterns: %A" patterns
        _filter <- fun s -> Array.exists (fun (x: string) -> s.Contains x) patterns

    trace "log" "fvim started. time = %A" time

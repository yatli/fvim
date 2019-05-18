module FVim.getopt

        //[<Option("trace-to-stdout", HelpText="Write trace messages to stdout.", Required=false, Default=false)>]
        //TraceToStdout: bool
        //[<Option("trace-to-file", HelpText="Write trace messages to a file.", Required=false)>]
        //TraceToFile: string option
        //[<Value(0, MetaName="args...", HelpText="The rest of the arguments will be forwarded to NeoVim.")>] 
        //args: string seq

let parseOptions (args: string[]) =
    let args = ResizeArray(args)
    let mutable trace_to_stdout = false
    let mutable trace_to_file = None
    if args.Contains("--trace-to-stdout") 
    then 
        trace_to_stdout <- true
        ignore <| args.Remove("--trace-to-stdout")
    let idx = args.IndexOf("--trace-to-file")
    if idx >= 0 then
        trace_to_file <- Some args.[idx+1]
        args.RemoveRange(idx, 2)

    args.ToArray(), trace_to_stdout, trace_to_file


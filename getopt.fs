module FVim.getopt

type Options =
    {
        logToStdout: bool
        logToFile: string option
        logPatterns: string option
        program: string
        stderrenc: System.Text.Encoding
        preArgs: string list
        args: string list
    }

let parseOptions (args: string[]) =
    let args = ResizeArray(args)

    let eat1 opt =
        if args.Contains(opt) 
        then args.Remove(opt)
        else false

    let eat2 opt =
        let idx = args.IndexOf(opt)
        if idx >= 0 then
            let res = Some args.[idx+1]
            args.RemoveRange(idx, 2)
            res
        else None

    let trace_to_stdout     = eat1 "--trace-to-stdout"
    let trace_to_file       = eat2 "--trace-to-file"
    let trace_patterns      = eat2 "--trace-patterns"
    let ssh                 = eat2 "--ssh"
    let wsl                 = eat1 "--wsl"
    let nvim                = eat2 "--nvim" |> Option.defaultValue "nvim"

    if wsl && ssh.IsSome then
        failwith "--wsl and --ssh cannot be used together."

    let prog                = if wsl then "wsl" elif ssh.IsSome then "ssh" else nvim
    let preargs             = if wsl then [nvim] elif ssh.IsSome then [ssh.Value; nvim] else []
    let enc                 = if wsl then System.Text.Encoding.Unicode else System.Text.Encoding.UTF8
    let args                = List.ofSeq args

    { 
        logToStdout     = trace_to_stdout
        logToFile       = trace_to_file
        logPatterns     = trace_patterns
        program         = prog
        args            = args
        preArgs         = preargs
        stderrenc       = enc
    }


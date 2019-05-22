module FVim.getopt

type Options =
    {
        logToStdout: bool
        logToFile: string option
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
    let wsl                 = eat1 "--wsl"
    let prog                = "wsl" // if wsl then "wsl" else "nvim"
    let preargs             = ["nvim"] // if wsl then ["nvim"] else []
    let enc                 = System.Text.Encoding.Unicode // if wsl then System.Text.Encoding.Unicode else System.Text.Encoding.UTF8
    let args                = List.ofSeq args

    { 
        logToStdout     = trace_to_stdout
        logToFile       = trace_to_file
        program         = prog
        args            = args
        preArgs         = preargs
        stderrenc       = enc
    }


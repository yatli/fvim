module FVim.getopt

open common
open System.Net

type ServerOptions =
    | StartNew
    | Tcp of endpoint: IPEndPoint
    | NamedPipe of address: string
    | TryDaemon

type Intent =
    | Start
    | Setup
    | Daemon of port: uint16 option * pipe: string option

type Options =
    {
        intent: Intent
        logToStdout: bool
        logToFile: bool
        logPatterns: string option
        args: string list
        program: string
        stderrenc: System.Text.Encoding
        preArgs: string list
        server: ServerOptions
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
    let trace_to_file       = eat1 "--trace-to-file"
    let trace_patterns      = eat2 "--trace-patterns"
    let ssh                 = eat2 "--ssh"
    let wsl                 = eat1 "--wsl"
    let nvim                = eat2 "--nvim" |> Option.defaultValue "nvim"
    let connect             = eat2 "--connect"
    let setup               = eat1 "--setup"
    let tryDaemon           = eat1 "--tryDaemon"
    let runDaemon           = eat1 "--daemon"
    let port                = eat2 "--daemonPort" >>= ParseUInt16
    let pipe                = eat2 "--daemonPipe"
    let terminal            = eat1 "--terminal"
    let termcmd             = eat2 "--terminal-cmd"

    if wsl && ssh.IsSome then
        failwith "--wsl and --ssh cannot be used together."

    let prog                = if wsl then "wsl" elif ssh.IsSome then "ssh" else nvim
    let preargs             = if wsl then [nvim] elif ssh.IsSome then [ssh.Value; nvim] else []
    let enc                 = if wsl then System.Text.Encoding.Unicode else System.Text.Encoding.UTF8

    let intent = 
        if setup then Setup
        elif runDaemon then Daemon(port, pipe)
        else Start

    if terminal then
        let set x = "+\"set " + x + "\""
        // fvim --wsl -u NORC +terminal +"set noshowmode" +"set laststatus=0" +"set noruler" +"set noshowcmd"
        args.AddRange([
            "-u"; "NORC"
            set "noshowmode"
            set "laststatus=0"
            set "noruler"
            set "noshowcmd"
        ])
        match termcmd with
        | Some cmd -> "+\"terminal " + cmd + "\""
        | None -> "+terminal"
        |> args.Add

    let serveropts = 
        if tryDaemon then
            TryDaemon
        elif connect.IsNone then 
            StartNew
        else
            match connect.Value.Split(':') with
            | [| ParseIp ipaddr; ParseUInt16 port |] -> Tcp(IPEndPoint(ipaddr, int port))
            | _ -> NamedPipe connect.Value

    { 
        logToStdout     = trace_to_stdout
        logToFile       = trace_to_file
        logPatterns     = trace_patterns
        program         = prog
        args            = List.ofSeq args
        server          = serveropts
        preArgs         = preargs
        stderrenc       = enc
        intent          = intent
    }


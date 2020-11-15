module FVim.getopt

open common
open System.Net

type NeovimRemoteEndpoint =
    | Tcp of endpoint: IPEndPoint
    | NamedPipe of address: string

type FVimRemoteVerb =
    | AttachTo of id: int * files: string list
    | NewSession of args: string list
    | AttachFirst of files: string list
    // | Interactive

type FVimRemoteTransport =
    | Local
    | Remote of prog: string * args: string list

type ServerOptions =
    | Embedded of prog: string * args: string list * stderrenc: System.Text.Encoding
    | NeovimRemote of addr: NeovimRemoteEndpoint * files: string list
    | FVimRemote of transport: FVimRemoteTransport * verb: FVimRemoteVerb

type Intent =
    | Start of serveropts: ServerOptions * norc: bool * debugMultigrid: bool
    | Setup
    | Uninstall
    | Daemon of pipe: string option * nvim: string

type Options =
    {
        intent: Intent
        logToStdout: bool
        logToFile: bool
        logPatterns: string option
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

    //  startup options
    let nvim                = eat2 "--nvim" |> Option.defaultValue "nvim"
    let terminal            = eat1 "--terminal"
    let termcmd             = eat2 "--terminal-cmd"
    //  remoting
    let ssh                 = eat2 "--ssh"
    let wsl                 = eat1 "--wsl"
    let nvr                 = eat2 "--nvr"
    let fvr                 = eat2 "--fvr"
    //  shell
    let setup               = eat1 "--setup"
    let uninstall           = eat1 "--uninstall"
    let daemon              = eat1 "--daemon"
    let pipe                = eat2 "--pipe"
    //  debug & tracing
    let debug_multigrid     = eat1 "--debug-multigrid"
    let trace_to_stdout     = eat1 "--trace-to-stdout"
    let trace_to_file       = eat1 "--trace-to-file"
    let trace_patterns      = eat2 "--trace-patterns"

    if wsl && ssh.IsSome then
        failwith "--wsl and --ssh cannot be used together."
    if nvr.IsSome && (wsl || ssh.IsSome || fvr.IsSome) then
        failwith "--nvr cannot be used with --fvr, --wsl or --ssh."

    let norc = 
        args 
        |> Seq.pairwise
        |> Seq.exists( (=) ("-u", "NORC") )

    if terminal then
        let set x = "+\"set " + x + "\""
        // fvim --wsl -u NORC +terminal +"set noshowmode" +"set laststatus=0" +"set noruler" +"set noshowcmd"
        args.AddRange([
            "-u"; "NORC"
            set "noshowmode"
            set "laststatus=0"
            set "noruler"
            set "noshowcmd"
            set "mouse=a"
        ])
        match termcmd with
        | Some cmd -> "+\"terminal " + cmd + "\""
        | None -> "+terminal"
        |> args.Add

    // stop altering the args list now
    let argsL = List.ofSeq args

    let intent = 
        if setup then Setup
        elif uninstall then Uninstall
        elif daemon then Daemon(pipe, nvim)
        else 
        let serveropts = 
            match fvr, nvr with
            | Some fvrVerb, _ -> 
              let transport = 
                // TODO
                if wsl then Remote("wsl", ["bash"; "-l"; "-c"; "nc"])
                elif ssh.IsSome then Remote("ssh", [ssh.Value; "nc"])
                else Local
              let verb = 
                match fvrVerb.ToLowerInvariant() with
                | "attach" | "a" -> AttachFirst(argsL)
                | "new" | "n" -> NewSession(argsL)
                | v -> AttachTo(int v, argsL)
              FVimRemote(transport, verb)
            | _, Some nvrAddr ->
              match nvrAddr.Split(":") with
              | [| ParseIp ipaddr; ParseUInt16 port |] -> NeovimRemote(Tcp <| IPEndPoint(ipaddr, int port), argsL)
              | _ -> NeovimRemote(NamedPipe nvrAddr, argsL)
            | None, None -> 
              let prog, args = 
                if wsl then 
                    "wsl", ["bash"; "-l"; "-c"; sprintf "nvim --embed %s" (args |> escapeArgs |> join)] 
                elif ssh.IsSome then 
                    "ssh", [ssh.Value; nvim; "--embed"] @ argsL
                else 
                    nvim, ["--embed"] @ argsL
              let enc = 
                if wsl then 
                    System.Text.Encoding.Unicode 
                else 
                    System.Text.Encoding.UTF8
              in
                Embedded(prog, args, enc)
        in
          Start(serveropts, norc, debug_multigrid)

    { 
        logToStdout     = trace_to_stdout
        logToFile       = trace_to_file
        logPatterns     = trace_patterns
        intent          = intent
    }


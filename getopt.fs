module FVim.getopt

open common
open System.Net

type NeovimRemoteEndpoint =
    | Tcp of endpoint: IPEndPoint
    | NamedPipe of address: string

type FVimRemoteVerb =
    | AttachTo of id: int
    | NewSession of args: string list
    | AttachFirst 
    // | Interactive

type FVimRemoteTransport =
    | Local
    | Remote of prog: string * args: string list

type ServerOptions =
    | Embedded of prog: string * args: string list * stderrenc: System.Text.Encoding
    | NeovimRemote of addr: NeovimRemoteEndpoint * files: string list
    | FVimRemote of serverName: string option * transport: FVimRemoteTransport * verb: FVimRemoteVerb * files: string list

type Intent =
    | Start of serveropts: ServerOptions * norc: bool * remoteinit: bool
    | Setup
    | Uninstall
    | Daemon of pipe: string option * nvim: string * stderrenc: System.Text.Encoding

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

    let eat2d opt defaultValue = 
        match eat2 opt with
        | None -> if eat1 opt then Some defaultValue else None
        | x -> x

    //  startup options
    let nvim                = eat2 "--nvim" |> Option.defaultValue "nvim"
    let terminal            = eat1 "--terminal"
    let termcmd             = eat2 "--terminal-cmd"
    //  remoting
    let ssh                 = eat2 "--ssh"
    let wsl                 = eat1 "--wsl"
    let nvr                 = eat2 "--nvr"
    let fvr                 = eat2d "--fvr" "new"
    //  shell
    let setup               = eat1 "--setup"
    let uninstall           = eat1 "--uninstall"
    let daemon              = eat1 "--daemon"
    let pipe                = eat2 "--pipe"
    //  debug & tracing
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
        // fvim --wsl -u NORC +"set noshowmode" +"set laststatus=0" +"set noruler" +"set noshowcmd" +"set mouse=a" +terminal 
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

    let enc = 
      if wsl then System.Text.Encoding.Unicode 
      else System.Text.Encoding.UTF8

    // stop altering the args list now
    let argsL = List.ofSeq args

    let intent = 
        if setup then Setup
        elif uninstall then Uninstall
        elif daemon then Daemon(pipe, nvim, enc)
        else 
        let serveropts, remoteinit = 
            match fvr, nvr with
            | Some fvrVerb, _ -> 
              let transport = 
                // TODO
                if wsl then Remote("wsl", ["bash"; "-l"; "-c"; "nc"; "-U"])
                elif ssh.IsSome then Remote("ssh", [ssh.Value; "nc"; "-U"])
                else Local
              let verb = 
                match fvrVerb.ToLowerInvariant() with
                | "attach" | "a" -> AttachFirst
                | "new" | "n" -> NewSession(argsL)
                | v -> AttachTo(int v)
              let files = 
                match verb with
                | NewSession _ -> []
                | _ -> argsL
              FVimRemote(pipe, transport, verb, files),true
            | _, Some nvrAddr ->
              match nvrAddr.Split(":") with
              | [| ParseIp ipaddr; ParseUInt16 port |] -> NeovimRemote(Tcp <| IPEndPoint(ipaddr, int port), argsL),true
              | _ -> NeovimRemote(NamedPipe nvrAddr, argsL),true
            | None, None -> 
              let prog, args, r = 
                if wsl then 
                    "wsl", ["bash"; "-l"; "-c"; sprintf "nvim --embed %s" (args |> escapeArgs |> join)], true
                elif ssh.IsSome then 
                    "ssh", [ssh.Value; nvim; "--embed"] @ argsL, true
                else 
                    nvim, ["--embed"] @ argsL, false
              in
                Embedded(prog, args, enc),r
        in
          Start(serveropts, norc, remoteinit)

    { 
        logToStdout     = trace_to_stdout
        logToFile       = trace_to_file
        logPatterns     = trace_patterns
        intent          = intent
    }


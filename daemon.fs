module FVim.Daemon

open log
open System.Runtime.InteropServices
open System.Diagnostics
open FVim.common
open System

let inline private trace x = trace "daemon" x

let FVimPipeAddress = sprintf "fvr-%s"

let daemon (pipe: string option) (nvim: string) = 
    trace "%s" "Running as daemon."
    let pipe = pipe |> Option.defaultValue (FVimPipeAddress "server")
    trace "FVimServerName = %s" pipe
    let pipeArgs = 
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then ["--listen"; @"\\.\pipe\" + pipe]
        else ["--listen"; pipe]
    while true do
        (*
        try 
            let pipe = new System.IO.Pipes.NamedPipeClientStream(".", FVim.Shell.FVimServerAddress, IO.Pipes.PipeDirection.InOut, IO.Pipes.PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation)
            pipe.Connect(timeout=50)
            RemoteSession pipe
        with :? TimeoutException ->
            //  transition from TryDamon to StartNew, add "--embed"
            this.createIO {opts with serveropts = StartNew; args = ["--embed"] @ args}
        *)
        let psi = ProcessStartInfo(nvim, join("--headless" :: pipeArgs))
        psi.CreateNoWindow          <- true
        psi.ErrorDialog             <- false
        psi.RedirectStandardError   <- true
        psi.RedirectStandardInput   <- true
        psi.RedirectStandardOutput  <- true
        psi.UseShellExecute         <- false
        psi.WindowStyle             <- ProcessWindowStyle.Hidden
        psi.WorkingDirectory        <- Environment.CurrentDirectory

        use proc = Process.Start(psi)
        use __sub = AppDomain.CurrentDomain.ProcessExit.Subscribe (fun _ -> proc.Kill(true))
        trace "Neovim process started. Pid = %d" proc.Id
        proc.WaitForExit()
        trace "Neovim process terminated. ExitCode = %d" proc.ExitCode
    0

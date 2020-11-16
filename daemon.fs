module FVim.Daemon

open log
open System.Runtime.InteropServices
open System.Diagnostics
open FVim.common
open System
open System.IO.Pipes
open FSharp.Control.Tasks.V2.ContextSensitive

let inline private trace x = trace "daemon" x

let pipeaddr x =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    then @"\\.\pipe\" + x
    else "/tmp/" + x

let pipename = sprintf "fvr-%s"

let daemon (pname: string option) (nvim: string) =
    trace "Running as daemon."
    let pname = pname |> Option.defaultValue (pipename "main")
    let paddr = pipeaddr pname
    trace "FVR server address is '%s'" paddr

    while true do
      (task {
        let svrpipe =
            new NamedPipeServerStream(pname, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                                      PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
        do! svrpipe.WaitForConnectionAsync()
      }).Wait()
        (*
        try
            let pipe = new System.IO.Pipes.NamedPipeClientStream(".", FVim.Shell.FVimServerAddress, IO.Pipes.PipeDirection.InOut, IO.Pipes.PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation)
            pipe.Connect(timeout=50)
            RemoteSession pipe
        with :? TimeoutException ->
            //  transition from TryDamon to StartNew, add "--embed"
            this.createIO {opts with serveropts = StartNew; args = ["--embed"] @ args}
        *)
        (*let psi = ProcessStartInfo(nvim, join ("--headless" :: pipeArgs))*)
        (*psi.CreateNoWindow <- true*)
        (*psi.ErrorDialog <- false*)
        (*psi.RedirectStandardError <- true*)
        (*psi.RedirectStandardInput <- true*)
        (*psi.RedirectStandardOutput <- true*)
        (*psi.UseShellExecute <- false*)
        (*psi.WindowStyle <- ProcessWindowStyle.Hidden*)
        (*psi.WorkingDirectory <- Environment.CurrentDirectory*)

        (*use proc = Process.Start(psi)*)
        (*use __sub = AppDomain.CurrentDomain.ProcessExit.Subscribe(fun _ -> proc.Kill(true))*)
        (*trace "Neovim process started. Pid = %d" proc.Id*)
        (*proc.WaitForExit()*)
        (*trace "Neovim process terminated. ExitCode = %d" proc.ExitCode*)
    0

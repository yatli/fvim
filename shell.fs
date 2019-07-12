module FVim.Shell
//  Provides shell integration for supported platforms
//

open log
open getopt

open System
open System.IO
open System.Reflection
open System.Diagnostics
open System.Runtime.InteropServices
open Microsoft.Win32
open UACHelper

let private trace x = trace "shell" x

let FVimServerAddress =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "FVimServer"
    else "/tmp/FVimServer"

let FVimDir = 
    Assembly.GetExecutingAssembly().Location 
    |> Path.GetDirectoryName
let FVimIcons = 
    FVimDir
    |> fun x -> Path.Combine(x, "icons")
    |> Directory.GetFiles
    |> Array.filter (fun x -> x.EndsWith ".ico" && Path.GetFileName(x).StartsWith("."))
    |> Array.map (fun x -> (Path.GetFullPath(x), let x = Path.GetFileName(x) in x.Substring(0, x.Length - 4)))

trace "FVimServerName = %s" FVimServerAddress
trace "FVim directory = %s" FVimDir
trace "Discovered %d file icons" FVimIcons.Length

let private win32CheckUAC() =
    if not(UACHelper.IsElevated) then
        trace "Starting setup in elevated environment..."
        let exe = Path.Combine(FVimDir, "FVim.exe")
        let psi = ProcessStartInfo(exe, String.Join(" ", Environment.GetCommandLineArgs() |> Array.skip 1))

        psi.CreateNoWindow          <- true
        psi.ErrorDialog             <- false
        psi.UseShellExecute         <- true
        psi.WindowStyle             <- ProcessWindowStyle.Hidden
        psi.WorkingDirectory        <- Environment.CurrentDirectory

        let proc = UACHelper.StartElevated(psi)
        proc.WaitForExit()
        false
    else
        trace "FVim is elevated"
        true

let private win32RegisterFileAssociation() =

    trace "registering file associations..."

    let HKCR = Registry.ClassesRoot
    let HKLM = Registry.LocalMachine
    let exe = Path.Combine(FVimDir, "FVim.exe")
    let fvicon = sprintf "%s,0" exe

    let setupShell(key: RegistryKey) (ico: string) =

        trace "setupShell: registering %s..." key.Name

        for subkey in key.GetSubKeyNames() do
            key.DeleteSubKeyTree(subkey)

        use defaultIcon = key.CreateSubKey("DefaultIcon")
        defaultIcon.SetValue("", ico)

        use shell = key.CreateSubKey("shell")

        shell.SetValue("", "open")

        let () =
            use _open = shell.CreateSubKey("open")
            _open.SetValue("", "Open with FVim")
            _open.SetValue("Icon", fvicon)
            use command = _open.CreateSubKey("command")
            command.SetValue("", sprintf "\"%s\" --tryDaemon \"%%1\"" exe)
        in ()

        let () =
            use _open = shell.CreateSubKey("new")
            _open.SetValue("", "Open with new FVim")
            _open.SetValue("Icon", fvicon)
            use command = _open.CreateSubKey("command")
            command.SetValue("", sprintf "\"%s\" \"%%1\"" exe)
        in ()
    
    // https://docs.microsoft.com/en-us/windows/desktop/shell/app-registration
    let () =
        use appPathKey = HKLM.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\FVim.exe")
        appPathKey.SetValue("", exe)
        appPathKey.SetValue("UseUrl", 0, RegistryValueKind.DWord)
        use appsKey = HKCR.CreateSubKey(@"Applications\FVim.exe")
        setupShell appsKey fvicon
        use appsKey = HKLM.CreateSubKey(@"SOFTWARE\Classes\Applications\FVim.exe")
        setupShell appsKey fvicon

    // https://docs.microsoft.com/en-us/windows/desktop/shell/fa-file-types
    // https://docs.microsoft.com/en-us/windows/desktop/shell/fa-how-work
    // https://docs.microsoft.com/en-us/windows/desktop/shell/fa-verbs
    // https://docs.microsoft.com/en-us/windows/desktop/shell/fa-progids
    for (ico,ext) in FVimIcons do
        // register ProgId
        // https://docs.microsoft.com/en-us/windows/desktop/shell/how-to-register-a-file-type-for-a-new-application
        let progId = "FVim" + ext
        use progIdKey = HKCR.CreateSubKey(progId)

        setupShell progIdKey ico
        use extKey = HKCR.CreateSubKey(ext)
        extKey.SetValue("", progId)

let setup() =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
        if win32CheckUAC() then
            win32RegisterFileAssociation()

    // setup finished.
    0

let daemon {args=args; program=program; stderrenc = enc} = 
    trace "Running as daemon."
    let pipeaddr = 
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then @"\\.\pipe\" + FVimServerAddress
        else FVimServerAddress
    while true do
        let psi = ProcessStartInfo(program, String.Join(" ", "--headless" :: "--listen" :: pipeaddr :: args))
        psi.CreateNoWindow          <- true
        psi.ErrorDialog             <- false
        psi.RedirectStandardError   <- true
        psi.RedirectStandardInput   <- true
        psi.RedirectStandardOutput  <- true
        psi.StandardErrorEncoding   <- enc
        psi.UseShellExecute         <- false
        psi.WindowStyle             <- ProcessWindowStyle.Hidden
        psi.WorkingDirectory        <- Environment.CurrentDirectory

        use proc = Process.Start(psi)
        use __sub = AppDomain.CurrentDomain.ProcessExit.Subscribe (fun _ -> proc.Kill(true))
        trace "Neovim process started. Pid = %d" proc.Id
        proc.WaitForExit()
        trace "Neovim process terminated. ExitCode = %d" proc.ExitCode
    0

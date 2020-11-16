module FVim.Shell
//  Provides shell integration for supported platforms
//

open log
open getopt
open common

open System
open System.IO
open System.Reflection
open System.Diagnostics
open System.Runtime.InteropServices
open Microsoft.Win32
open UACHelper

let inline private trace x = trace "shell" x

let FVimDir = 
    Assembly.GetExecutingAssembly().Location 
    |> Path.GetDirectoryName
let FVimIcons = 
    FVimDir
    |> fun x -> Path.Combine(x, "icons")
    |> Directory.GetFiles
    |> Array.filter (fun x -> x.EndsWith ".ico" && Path.GetFileName(x).StartsWith("."))
    |> Array.map (fun x -> (Path.GetFullPath(x), let x = Path.GetFileName(x) in x.Substring(0, x.Length - 4)))

trace "FVim directory = %s" FVimDir
trace "Discovered %d file icons" FVimIcons.Length

let private win32CheckUAC() =
    if not(UACHelper.IsElevated) then
        trace "%s" "Starting setup in elevated environment..."
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
        trace "%s" "FVim is elevated"
        true

let private noextkey ext =
    let noextkey = [".bat"; ".cmd"; ".ps1"; ".reg"; ".sln" ]
    List.contains ext noextkey

let private win32RegisterFileAssociation() =

    trace "%s" "registering file associations..."

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

        shell.SetValue("", "edit")

        do
            use _edit = shell.CreateSubKey("edit")
            _edit.SetValue("", "Open with FVim")
            _edit.SetValue("Icon", fvicon)
            use command = _edit.CreateSubKey("command")
            command.SetValue("", sprintf "\"%s\" \"%%1\"" exe)
    
    // https://docs.microsoft.com/en-us/windows/desktop/shell/app-registration
    do
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

        if not (noextkey ext) then
            use extKey = HKCR.CreateSubKey(ext)
            extKey.SetValue("", progId)

let private win32UnregisterFileAssociation() =
  trace "%s" "unregistering file associations..."
  let HKCR = Registry.ClassesRoot
  let HKLM = Registry.LocalMachine

  HKLM.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\FVim.exe", false)
  HKCR.DeleteSubKeyTree(@"Applications\FVim.exe", false)
  HKLM.DeleteSubKeyTree(@"SOFTWARE\Classes\Applications\FVim.exe", false)

  for (ico,ext) in FVimIcons do
      let progId = "FVim" + ext
      HKCR.DeleteSubKeyTree(progId, false)

let setup() =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
        if win32CheckUAC() then
            win32RegisterFileAssociation()
    0
    // setup finished.

let uninstall() =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
        if win32CheckUAC() then
            win32UnregisterFileAssociation()
    0
    // uninstall finished.


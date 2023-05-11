module FVim.shell
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
open System.Linq
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

let private noextkey ext =
    let noextkey = [".bat"; ".cmd"; ".ps1"; ".reg"; ".sln" ]
    List.contains ext noextkey

let private win32RegisterFileAssociation() =

    trace "registering file associations..."

    let HKCR = Registry.ClassesRoot
    let HKLM = Registry.LocalMachine
    let exe = Path.Combine(FVimDir, "FVim.exe")
    let fvicon = $"{exe},0"

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
            command.SetValue("", $"\"{exe}\" \"%%1\"")
    
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
  trace "unregistering file associations..."
  use HKCR = Registry.ClassesRoot
  use HKLM = Registry.LocalMachine
  use HKCU = Registry.CurrentUser

  HKLM.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\FVim.exe", false)
  HKCR.DeleteSubKeyTree(@"Applications\FVim.exe", false)
  HKLM.DeleteSubKeyTree(@"SOFTWARE\Classes\Applications\FVim.exe", false)

  for (_,ext) in FVimIcons do
      let progId = "FVim" + ext
      trace "Deleting %s" progId
      HKCR.DeleteSubKeyTree(progId, false)

  let tryCreateSubKey subKeyName (rootKey: RegistryKey) =
    try Some(rootKey.CreateSubKey(subKeyName))
    with _ -> None

  let tryOpenSubKey subKeyName (rootKey: RegistryKey) =
    if rootKey.GetSubKeyNames().Contains(subKeyName) then Some(rootKey.CreateSubKey subKeyName)
    else None

  let tryDeleteFVimShellKey (rootKey: RegistryKey) (subkey: RegistryKey) = 
      let name = subkey.Name
      match subkey.GetValue("") with
      | :? string as v when v.Contains("FVim") ->
        trace "Deleting %O" subkey
        let path = name.Substring(rootKey.Name.Length + 1)
        subkey.Dispose()
        rootKey.DeleteSubKeyTree path
        Some()
      | _ -> None

  let removeExtShellCommands (rootKey: RegistryKey) subKeyName =
    let shell = rootKey 
                |>  tryCreateSubKey subKeyName
                >>= tryOpenSubKey "shell"
    shell >>= (fun shell -> tryOpenSubKey "new" shell >>= tryDeleteFVimShellKey shell) |> ignore
    shell >>= (fun shell -> tryOpenSubKey "open" shell >>= tryDeleteFVimShellKey shell) |> ignore
    shell >>= (fun shell -> Some <| shell.Dispose()) |> ignore

  let removeFVimShellCommands (rootKey: RegistryKey) =
    rootKey.GetSubKeyNames()
    |> Seq.where(fun x -> x.StartsWith("."))
    |> Seq.iter(removeExtShellCommands rootKey)

  use hkcu_classes = HKCU.CreateSubKey(@"SOFTWARE\Classes")
  removeFVimShellCommands HKCR
  removeFVimShellCommands hkcu_classes


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


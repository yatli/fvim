namespace FVim

open Avalonia
open Avalonia.Logging.Serilog
open System.Reflection
open FSharp.Data
open Avalonia.ReactiveUI
open System.Runtime.InteropServices
open Microsoft.Win32

module Program =

    open System
    open System.IO
    open System.Diagnostics
    open getopt

    // Avalonia configuration, don't remove; also used by visual designer.
    [<CompiledName "BuildAvaloniaApp">]
    let buildAvaloniaApp() =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI()
            .With(new Win32PlatformOptions(UseDeferredRendering=false, AllowEglInitialization=true))
            .With(new AvaloniaNativePlatformOptions(UseDeferredRendering=false, UseGpu=true))
            .With(new X11PlatformOptions(UseEGL=true, UseGpu=true))
            .With(new MacOSPlatformOptions(ShowInDock=true))
            .LogToDebug()

    let registerFileAssoc = async {
        let asmDir = 
            Assembly.GetExecutingAssembly().Location 
            |> Path.GetDirectoryName
        let icons = 
            asmDir
            |> fun x -> Path.Combine(x, "icons")
            |> Directory.GetFiles
            |> Array.filter (fun x -> x.EndsWith ".ico" && Path.GetFileName(x).StartsWith("."))
            |> Array.map (fun x -> {| Icon = Path.GetFullPath(x); Ext = let x = Path.GetFileName(x) in x.Substring(0, x.Length - 4)|})
        FVim.log.trace "FVim" "registering file associations..."
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            // Win32 fileassoc registration
            let HKCR = Registry.ClassesRoot
            let HKLM = Registry.LocalMachine
            let exe = Path.Combine(asmDir, "FVim.exe")

            let setupShell(key: RegistryKey) =
                use shell = key.CreateSubKey("shell")
                shell.SetValue("", "open")

                let () =
                    use _open = shell.CreateSubKey("open")
                    _open.SetValue("", "Open with FVim")
                    use command = _open.CreateSubKey("command")
                    command.SetValue("", sprintf "\"%s\" \"%%1\"" exe)
                let () =
                    use _open = shell.CreateSubKey("nvr")
                    _open.SetValue("", "neovim-remote with FVim")
                    use command = _open.CreateSubKey("command")
                    command.SetValue("", "\"nvr.exe -l\" \"%%1\"")
                in ()
            
            // https://docs.microsoft.com/en-us/windows/desktop/shell/app-registration
            let () =
                use appPathKey = HKLM.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\FVim.exe")
                appPathKey.SetValue("", exe)
                appPathKey.SetValue("UseUrl", 0, RegistryValueKind.DWord)
                use appsKey = HKCR.CreateSubKey(@"Applications\FVim.exe")
                setupShell appsKey
                use appsKey = HKLM.CreateSubKey(@"SOFTWARE\Classes\Applications\FVim.exe")
                setupShell appsKey

            // https://docs.microsoft.com/en-us/windows/desktop/shell/fa-file-types
            // https://docs.microsoft.com/en-us/windows/desktop/shell/fa-how-work
            // https://docs.microsoft.com/en-us/windows/desktop/shell/fa-verbs
            // https://docs.microsoft.com/en-us/windows/desktop/shell/fa-progids
            for ico in icons do
                // register ProgId
                // https://docs.microsoft.com/en-us/windows/desktop/shell/how-to-register-a-file-type-for-a-new-application
                let progId = "FVim" + ico.Ext
                use progIdKey = HKCR.CreateSubKey(progId)
                use defaultIcon = progIdKey.CreateSubKey("DefaultIcon")
                defaultIcon.SetValue("", ico.Icon)
                setupShell progIdKey
                use extKey = HKCR.CreateSubKey(ico.Ext)
                extKey.SetValue("", progId)
    }

    // Your application's entry point.
    [<CompiledName "AppMain">]
    let appMain (app: Application) (args: string[]) =
        AppDomain.CurrentDomain.UnhandledException.Add(fun exArgs -> 
            let filename = Path.Combine(config.configdir, sprintf "fvim-crash-%s.txt" (DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")))
            use dumpfile = new StreamWriter(filename)
            dumpfile.WriteLine(sprintf "Unhandled exception: (terminating:%A)" exArgs.IsTerminating)
            dumpfile.WriteLine(exArgs.ExceptionObject.ToString())
        )
        System.Console.OutputEncoding <- System.Text.Encoding.Unicode
        let opts = parseOptions args
        FVim.log.init opts
        if opts.regFileAssoc then
            Async.Start registerFileAssoc

        Model.Start opts
        let cfg = config.load()
        let cwd = Environment.CurrentDirectory |> Path.GetFullPath
        let workspace = cfg.Workspace |> Array.tryFind(fun w -> w.Path = cwd)
        let mainwin = new MainWindowViewModel(workspace)
        app.Run(MainWindow(DataContext = mainwin)) |> ignore
        config.save cfg mainwin.WindowX mainwin.WindowY mainwin.WindowWidth mainwin.WindowHeight mainwin.WindowState



    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [<EntryPoint>]
    [<CompiledName "Main">]
    let main(args: string[]) =
        buildAvaloniaApp().Start(appMain, args)
        0

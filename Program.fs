namespace FVim

open Avalonia
open Avalonia.Logging.Serilog
open FSharp.Data
open Avalonia.ReactiveUI
open System.Threading
open Avalonia.Controls.ApplicationLifetimes

module Program =

    open System
    open System.IO
    open getopt
    open Shell

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

    [<EntryPoint>]
    [<CompiledName "Main">]
    let main(args: string[]) =
        let _ = Thread.CurrentThread.TrySetApartmentState(ApartmentState.STA)
        let builder = buildAvaloniaApp()
        let lifetime = new ClassicDesktopStyleApplicationLifetime(builder.Instance)
        lifetime.ShutdownMode <- Controls.ShutdownMode.OnMainWindowClose
        builder.Instance.ApplicationLifetime <- lifetime
        let _ = builder.SetupWithoutStarting()

        // Avalonia is initialized. SynchronizationContext-reliant code should be working by now;

        AppDomain.CurrentDomain.UnhandledException.Add(fun exArgs -> 
            let filename = Path.Combine(config.configdir, sprintf "fvim-crash-%s.txt" (DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")))
            use dumpfile = new StreamWriter(filename)
            dumpfile.WriteLine(sprintf "Unhandled exception: (terminating:%A)" exArgs.IsTerminating)
            dumpfile.WriteLine(exArgs.ExceptionObject.ToString())
        )

        System.Console.OutputEncoding <- System.Text.Encoding.Unicode
        let opts = parseOptions args
        FVim.log.init opts
        match opts.intent with
        | Setup -> setup()
        | Daemon -> daemon opts
        | Start -> 

        Async.RunSynchronously(Model.Start opts)
        let cfg = config.load()
        let cwd = Environment.CurrentDirectory |> Path.GetFullPath
        let workspace = cfg.Workspace |> Array.tryFind(fun w -> w.Path = cwd)
        let mainwin = new MainWindowViewModel(workspace)
        lifetime.MainWindow <- MainWindow(DataContext = mainwin)
        let ret = lifetime.Start(args)

        config.save cfg mainwin.WindowX mainwin.WindowY mainwin.WindowWidth mainwin.WindowHeight mainwin.WindowState
        ret
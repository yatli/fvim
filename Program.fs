namespace FVim

open Avalonia
open Avalonia.Logging.Serilog
open System.Reflection
open FSharp.Data

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
            .UseSkia()
            .With(new Win32PlatformOptions(UseDeferredRendering=false, AllowEglInitialization=true))
            .With(new AvaloniaNativePlatformOptions(UseDeferredRendering=true, UseGpu=true))
            .With(new X11PlatformOptions(UseEGL=true, UseGpu=true))
            .With(new MacOSPlatformOptions(ShowInDock=true))
            .UseReactiveUI()
            .LogToDebug()

    // Your application's entry point.
    [<CompiledName "AppMain">]
    let appMain (app: Application) (args: string[]) =
        System.Console.OutputEncoding <- System.Text.Encoding.Unicode
        let opts = parseOptions args
        FVim.log.init opts
        Model.Start opts
        let cfg = config.load()
        let cwd = Environment.CurrentDirectory |> Path.GetFullPath
        let workspace = cfg.Workspace |> Array.tryFind(fun w -> w.Path = cwd)
        let mainwin = new MainWindowViewModel(workspace)
        app.Run(MainWindow(DataContext = mainwin))
        config.save cfg mainwin.WindowX mainwin.WindowY mainwin.WindowWidth mainwin.WindowHeight mainwin.WindowState



    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [<EntryPoint>]
    [<CompiledName "Main">]
    let main(args: string[]) =
        buildAvaloniaApp().Start(appMain, args)
        0

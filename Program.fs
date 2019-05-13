namespace FVim

open Avalonia
open Avalonia.Logging.Serilog
open System.Reflection
open FSharp.Data

module Program =
    open System
    open System.IO

    // Avalonia configuration, don't remove; also used by visual designer.
    [<CompiledName "BuildAvaloniaApp">]
    let buildAvaloniaApp() =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions(UseDeferredRendering=false))
            .With(new AvaloniaNativePlatformOptions(UseDeferredRendering=false, UseGpu=false))
            .With(new X11PlatformOptions(UseEGL=false, UseGpu=false))
            .With(new MacOSPlatformOptions(ShowInDock=true))
            .UseReactiveUI()
            .LogToDebug()

    // Your application's entry point.
    [<CompiledName "AppMain">]
    let appMain (app: Application) (args: string[]) =
        Model.Start(args)
        let cfg = config.load()
        let cwd = Environment.CurrentDirectory |> Path.GetFullPath
        let workspace = cfg.Workspace |> Array.tryFind(fun w -> w.Path = cwd)
        let mainwin = MainWindowViewModel(workspace)
        app.Run(MainWindow(DataContext = mainwin))
        config.save cfg mainwin.WindowX mainwin.WindowY mainwin.WindowWidth mainwin.WindowHeight mainwin.WindowState

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [<EntryPoint>]
    [<CompiledName "Main">]
    let main(args: string[]) =
        System.Console.OutputEncoding <- System.Text.Encoding.Unicode
        buildAvaloniaApp().Start(appMain, args)
        0

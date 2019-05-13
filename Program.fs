namespace FVim

open Avalonia
open Avalonia.Logging.Serilog

module Program =
    open System.Reflection

    // Avalonia configuration, don't remove; also used by visual designer.
    [<CompiledName "BuildAvaloniaApp">]
    let buildAvaloniaApp() =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions(UseDeferredRendering=false))
            .UseReactiveUI()
            .LogToDebug()

    // Your application's entry point.
    [<CompiledName "AppMain">]
    let appMain (app: Application) (args: string[]) =
        Model.Start(args)
        app.Run(MainWindow(DataContext = MainWindowViewModel()))

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [<EntryPoint>]
    [<CompiledName "Main">]
    let main(args: string[]) =
        System.Console.OutputEncoding <- System.Text.Encoding.Unicode
        buildAvaloniaApp().Start(appMain, args)
        0

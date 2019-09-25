module FVim.Program

open Avalonia
open Avalonia.Logging.Serilog
open FSharp.Data
open Avalonia.ReactiveUI
open System.Threading
open Avalonia.Controls.ApplicationLifetimes

open MessagePack
open MessagePack.Resolvers
open MessagePack.FSharp
open MessagePack.ImmutableCollection

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

    CompositeResolver.RegisterAndSetAsDefault(
        ImmutableCollectionResolver.Instance,
        FSharpResolver.Instance,
        StandardResolver.Instance
    )
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
    | Daemon(port, pipe) -> daemon port pipe opts
    | Start -> 

    // Avalonia initialization
    let builder = buildAvaloniaApp()
    let lifetime = new ClassicDesktopStyleApplicationLifetime(builder.Instance)
    lifetime.ShutdownMode <- Controls.ShutdownMode.OnMainWindowClose
    builder.Instance.ApplicationLifetime <- lifetime
    let _ = builder.SetupWithoutStarting()
    // Avalonia is initialized. SynchronizationContext-reliant code should be working by now;

    try
        Model.Start opts
    with ex -> ()
    let cfg = config.load()
    let cwd = Environment.CurrentDirectory |> Path.GetFullPath
    let workspace = cfg.Workspace |> Array.tryFind(fun w -> w.Path = cwd)
    let mainwin = new MainWindowViewModel(workspace)
    lifetime.MainWindow <- MainWindow(DataContext = mainwin)
    let ret = lifetime.Start(args)

    config.save cfg (int mainwin.X) (int mainwin.Y) mainwin.Width mainwin.Height mainwin.WindowState
    ret

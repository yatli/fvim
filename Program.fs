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
open common
open MessagePack.Formatters

// Avalonia configuration, don't remove; also used by visual designer.
[<CompiledName "BuildAvaloniaApp">]
let buildAvaloniaApp() =
    AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .UseReactiveUI()
        .With(new Win32PlatformOptions(UseDeferredRendering=false, AllowEglInitialization=true))
        .With(new AvaloniaNativePlatformOptions(UseDeferredRendering=false, UseGpu=true))
        .With(new X11PlatformOptions(UseEGL=true, UseGpu=false))
        .With(new MacOSPlatformOptions(ShowInDock=true))
        .LogToDebug()

type MsgPackFormatter(resolver: IFormatterResolver) = 
    let m_formatter = resolver.GetFormatter<obj>()
    interface IMessagePackFormatter<obj> with
        member this.Serialize(bytes: byref<byte []>, offset: int, value: obj, formatterResolver: IFormatterResolver): int = 
            m_formatter.Serialize(&bytes, offset, value, formatterResolver)
        member x.Deserialize(bytes: byte[] , offset: int, formatterResolver: IFormatterResolver , readSize: byref<int>) =
            if MessagePackBinary.GetMessagePackType(bytes, offset) = MessagePackType.Extension then
                let result = MessagePackBinary.ReadExtensionFormat(bytes, offset, &readSize)
                if result.TypeCode = 1y then 
                    let mutable _size = 0
                    m_formatter.Deserialize(result.Data, 0, formatterResolver, &_size)
                else 
                    m_formatter.Deserialize(bytes, offset, formatterResolver, &readSize)
            else
                m_formatter.Deserialize(bytes, offset, formatterResolver, &readSize)

type MsgPackResolver() =
    static let s_formatter = box(MsgPackFormatter(MessagePack.Resolvers.StandardResolver.Instance))
    static let s_resolver = MessagePack.Resolvers.StandardResolver.Instance
    interface IFormatterResolver with
        member x.GetFormatter<'a>() =
            if typeof<'a> = typeof<obj> then
                s_formatter :?> IMessagePackFormatter<'a>
            else
                s_resolver.GetFormatter<'a>()


[<EntryPoint>]
[<CompiledName "Main">]
let main(args: string[]) =

    let _ = Thread.CurrentThread.TrySetApartmentState(ApartmentState.STA)

    CompositeResolver.RegisterAndSetAsDefault(
        MsgPackResolver()
    //  ImmutableCollectionResolver.Instance,
    //  FSharpResolver.Instance,
    //  StandardResolver.Instance
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
    | Uninstall -> uninstall()
    | Daemon(port, pipe) -> daemon port pipe opts
    | Start -> 

    // Avalonia initialization
    let builder = buildAvaloniaApp()
    let lifetime = new ClassicDesktopStyleApplicationLifetime(builder.Instance)
    lifetime.ShutdownMode <- Controls.ShutdownMode.OnMainWindowClose
    builder.Instance.ApplicationLifetime <- lifetime
    let _ = builder.SetupWithoutStarting()
    // Avalonia is initialized. SynchronizationContext-reliant code should be working by now;

    let model_start = 
        try 
            Model.Start opts
            Ok()
        with ex -> Error ex

    match model_start with
    | Ok() ->
        let cfg = config.load()
        let cwd = Environment.CurrentDirectory |> Path.GetFullPath
        let workspace = cfg.Workspace |> Array.tryFind(fun w -> w.Path = cwd)
        workspace 
        >>= fun workspace -> workspace.Mainwin.BackgroundComposition
        >>= fun comp -> States.parseBackgroundComposition(box comp)
        >>= fun comp -> States.background_composition <- comp; None
        |> ignore

        let mainwin = new MainWindowViewModel(workspace)
        lifetime.MainWindow <- MainWindow(DataContext = mainwin)
        let ret = lifetime.Start(args)

        config.save cfg (int mainwin.X) (int mainwin.Y) (mainwin.Width) (mainwin.Height) (mainwin.WindowState) (States.backgroundCompositionToString States.background_composition) mainwin.CustomTitleBar
        ret
    | Error ex ->
        let crash = new CrashReportViewModel(ex)
        lifetime.MainWindow <- new CrashReport(DataContext = crash)
        ignore <| lifetime.Start(args)
        -1

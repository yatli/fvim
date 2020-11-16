module FVim.Program

open System
open System.IO
open System.Threading

open Avalonia
open Avalonia.ReactiveUI
open Avalonia.Controls.ApplicationLifetimes

open MessagePack
open MessagePack.Resolvers

open getopt
open common
open Shell
open Daemon
open MessagePack.Formatters

// Avalonia configuration, don't remove; also used by visual designer.
[<CompiledName "BuildAvaloniaApp">]
let buildAvaloniaApp() =
  AppBuilder
    .Configure<App>()
    .UsePlatformDetect()
    .UseReactiveUI()
    .With(new Win32PlatformOptions(UseDeferredRendering=false))
    .With(new AvaloniaNativePlatformOptions(UseDeferredRendering=false))
    .With(new X11PlatformOptions(UseDeferredRendering=false))
    .With(new MacOSPlatformOptions(ShowInDock=true))

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

let startMainWindow app serveropts =
    let app = app()
    Model.Start serveropts

    let cfg = config.load()
    let cwd = Environment.CurrentDirectory |> Path.GetFullPath
    let workspace = cfg.Workspace |> Array.tryFind(fun w -> w.Path = cwd)
    workspace 
    >>= fun workspace -> workspace.Mainwin.BackgroundComposition
    >>= fun comp -> States.parseBackgroundComposition(box comp)
    >>= fun comp -> States.background_composition <- comp; None
    |> ignore

    let mainwin = new MainWindowViewModel(workspace)
    app <| MainWindow(DataContext = mainwin)
    let x, y, w, h = 
      let x, y, w, h = (int mainwin.X), (int mainwin.Y), (int mainwin.Width), (int mainwin.Height)
      // sometimes the metrics will just go off...
      // see #136
      let x, y = (max x 0), (max y 0)
      if x + w < 0 || y + h < 0
      then 0, 0, 800, 600
      else x, y, w, h
    config.save cfg x y w h (mainwin.WindowState) (States.backgroundCompositionToString States.background_composition) mainwin.CustomTitleBar
    0

let startCrashReportWindow app ex = 
    let app = app()
    FVim.log.trace "main" "displaying crash dialog"
    FVim.log.trace "main" "exception: %O" ex
    let code, msgs = States.get_crash_info()
    let crash = new CrashReportViewModel(ex, code, msgs)
    let win = new CrashReport(DataContext = crash)
    // there may be messages already posted into the sync context,
    // so we may immediately crash when we start the app and drive
    // the message loop forward... in that case, launch it again.
    try app win
    with _ -> app win
    -1

[<EntryPoint>]
[<STAThread>]
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

  let builder = lazy buildAvaloniaApp()
  let lifetime = lazy new ClassicDesktopStyleApplicationLifetime()
  let app () = 
    // Avalonia initialization
    let lifetime = lifetime.Value
    if not builder.IsValueCreated then
      let _ = builder.Value.SetupWithLifetime(lifetime)
      ()
    // Avalonia is initialized. SynchronizationContext-reliant code should be working by now;
    (fun (win: Avalonia.Controls.Window) ->
        lifetime.ShutdownMode <- Controls.ShutdownMode.OnMainWindowClose
        lifetime.MainWindow <- win
        lifetime.Start(args) |> ignore)

  try 
    let opts = parseOptions args
    FVim.log.init opts
    match opts.intent with
    | Setup -> setup()
    | Uninstall -> uninstall()
    | Daemon(pipe, nvim, enc) -> daemon pipe nvim enc
    | Start(a,b,c) -> startMainWindow app (a,b,c) 
  with 
    | ex -> startCrashReportWindow app ex

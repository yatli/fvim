module FVim.Program

open System
open System.IO
open System.Threading

open Avalonia
open Avalonia.ReactiveUI
open Avalonia.Controls.ApplicationLifetimes

open def
open getopt
open common
open shell
open daemon
open FVim.ui

let inline trace x = FVim.log.trace "main" x

// Avalonia configuration, don't remove; also used by visual designer.
[<CompiledName "BuildAvaloniaApp">]
let buildAvaloniaApp() =
  AppBuilder
    .Configure<App>()
    .UsePlatformDetect()
    .UseReactiveUI()
    //.With(new Win32PlatformOptions(UseDeferredRendering=true, UseWgl=true))
    //.With(new AvaloniaNativePlatformOptions(UseDeferredRendering=false))
    //.With(new X11PlatformOptions(UseDeferredRendering=false))
    //.With(new MacOSPlatformOptions(ShowInDock=true))

let startMainWindow app serveropts =
    let app = app()
    model.Start serveropts

    let cfg = config.load()
    let cwd = Environment.CurrentDirectory |> Path.GetFullPath
    let workspace = cfg.Workspace |> Array.tryFind(fun w -> w.Path = cwd)
    workspace 
    >>= fun workspace -> workspace.Mainwin.BackgroundComposition
    >>= fun comp -> parseBackgroundComposition(box comp)
    >>= fun comp -> states.background_composition <- comp; None
    |> ignore

    model.CreateFrame <- fun _gridui ->
        let gridui = _gridui :?> GridViewModel
        let framevm = new FrameViewModel(workspace, gridui)
        let frame = new Frame(DataContext=framevm)
        frame.Show()

    let mainwinVM = new FrameViewModel(workspace)
    let mainwin = Frame(DataContext = mainwinVM)
    // sometimes the metrics will just go off...
    // see #136
    let screenBounds = 
      mainwin.Screens.All
      |> Seq.fold (fun r (s: Platform.Screen) -> s.Bounds.Union r) (PixelRect()) 
    let boundcheck() = 
      let mutable winBounds = 
        PixelRect(max (int mainwinVM.X) screenBounds.X, 
                  max (int mainwinVM.Y) screenBounds.Y, 
                  min (int mainwinVM.Width) screenBounds.Width, 
                  min (int mainwinVM.Height) screenBounds.Height)
      if winBounds.Right > screenBounds.Right then
        winBounds <- winBounds.WithX(screenBounds.Right - winBounds.Width)
      if winBounds.Bottom > screenBounds.Bottom then
        winBounds <- winBounds.WithY(screenBounds.Bottom - winBounds.Height)
      trace "mainwin bound adjusted to %O" winBounds
      mainwinVM.X <- float winBounds.X
      mainwinVM.Y <- float winBounds.Y
      mainwinVM.Width <- float winBounds.Width
      mainwinVM.Height <- float winBounds.Height
    boundcheck()
    app <| mainwin
    boundcheck()
    let x, y, w, h = (int mainwinVM.X), (int mainwinVM.Y), (int mainwinVM.Width), (int mainwinVM.Height)
    config.save cfg x y w h (mainwinVM.WindowState.ToString()) (backgroundCompositionToString states.background_composition) mainwinVM.CustomTitleBar
    0

let startCrashReportWindow app ex = 
    let app = app()
    trace "displaying crash dialog"
    trace "exception: %O" ex
    let code, msgs = model.get_crash_info()
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
    | Start(a,b) -> startMainWindow app (a,b) 
  with 
    | ex -> startCrashReportWindow app ex

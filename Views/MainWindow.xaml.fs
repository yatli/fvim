namespace FVim

open def
open log
open common
open ui

open ReactiveUI
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia
open Avalonia.Data
open Avalonia.VisualTree
open Avalonia.ReactiveUI
open System.Runtime.InteropServices
open Avalonia.Rendering

#nowarn "0025"

type MainWindow() as this =
    inherit ReactiveWindow<MainWindowViewModel>()

    static let XProp = AvaloniaProperty.Register<MainWindow,int>("PosX")
    static let YProp = AvaloniaProperty.Register<MainWindow,int>("PosY")

    let mutable m_saved_size  = Size(100.0,100.0)
    let mutable m_saved_pos   = PixelPoint(300, 300)
    let mutable m_saved_state = WindowState.Normal

    let toggleFullscreen(v) =
        if not v then
            this.WindowState <- m_saved_state
            this.PlatformImpl.Resize(m_saved_size)
            this.Position <- m_saved_pos
            this.HasSystemDecorations <- not this.ViewModel.CustomTitleBar
        else
            //  The order of actions is very important.
            //  1. Remove decorations
            //  2. Save current states
            //  3. Turn window state to normal
            //  4. Position window to TopLeft, and resize
            let screen                = this.Screens.ScreenFromVisual(this)
            let screenBounds          = screen.Bounds
            let sz                    = screenBounds.Size.ToSizeWithDpi(96.0 * (this:>IRenderRoot).RenderScaling)
            this.HasSystemDecorations <- false
            m_saved_size              <- this.ClientSize
            m_saved_pos               <- this.Position
            m_saved_state             <- this.WindowState
            this.WindowState          <- WindowState.Normal
            this.Position             <- screenBounds.TopLeft
            this.PlatformImpl.Resize(sz)

    let toggleTitleBar(custom) =
        if custom then
            this.HasSystemDecorations <- false
        else 
            this.HasSystemDecorations <- not this.ViewModel.Fullscreen

    do
        #if DEBUG
        this.Renderer.DrawFps <- true
        Avalonia.DevToolsExtensions.AttachDevTools(this)
        #endif

        DragDrop.SetAllowDrop(this, true)

        let flushop = 
            if RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then
                fun () -> 
                    let editor: Avalonia.VisualTree.IVisual = this.GetEditor()
                    editor.InvalidateVisual()
            else this.InvalidateVisual

        this.Watch [
            this.Closing.Subscribe (fun e -> Model.OnTerminating e)
            this.Closed.Subscribe  (fun _ -> Model.OnTerminated())
            this.Bind(XProp, Binding("X", BindingMode.TwoWay))
            this.Bind(YProp, Binding("Y", BindingMode.TwoWay))

            States.Register.Notify "DrawFPS" (fun [| Bool(v) |] -> 
                trace "mainwindow" "DrawFPS: %A" v
                this.Renderer.DrawFps <- v)

            Model.Flush |> Observable.subscribe flushop

            this.AddHandler(DragDrop.DropEvent, (fun _ (e: DragEventArgs) ->
                if e.Data.Contains(DataFormats.FileNames) then
                    Model.EditFiles <| e.Data.GetFileNames()
                elif e.Data.Contains(DataFormats.Text) then
                    Model.InsertText <| e.Data.GetText()
            ))

            this.AddHandler(DragDrop.DragOverEvent, (fun _ (e:DragEventArgs) ->
                e.DragEffects <- DragDropEffects.Move ||| DragDropEffects.Link ||| DragDropEffects.Copy
            ))

        ]
        AvaloniaXamlLoader.Load this

    member this.GetEditor() =
        this.LogicalChildren.[0] :?> Avalonia.VisualTree.IVisual

    override this.OnDataContextChanged _ =
        let ctx = this.DataContext :?> MainWindowViewModel
        let pos = PixelPoint(int ctx.X, int ctx.Y)
        let mutable firstPoschange = true
        let mutable deltaX = 0
        let mutable deltaY = 0
        this.ViewModel <- ctx

        trace "mainwindow" "set position: %d, %d" pos.X pos.Y
        this.Position <- pos
        this.WindowState <- ctx.WindowState
        this.Watch [
            this.PositionChanged.Subscribe (fun p ->
                if firstPoschange then
                    firstPoschange <- false
                    deltaX <- p.Point.X - pos.X
                    deltaY <- p.Point.Y - pos.Y
                    trace "mainwindow" "first PositionChanged event: %d, %d (delta=%d, %d)" p.Point.X p.Point.Y deltaX deltaY
                else
                    this.SetValue(XProp, p.Point.X - deltaX)
                    this.SetValue(YProp, p.Point.Y - deltaY)
                )
            ctx.ObservableForProperty((fun x -> x.Fullscreen), skipInitial=true).Subscribe(fun v -> toggleFullscreen <| v.GetValue())
            ctx.ObservableForProperty((fun x -> x.CustomTitleBar), skipInitial=true).Subscribe(fun v -> toggleTitleBar <| v.GetValue())
        ]

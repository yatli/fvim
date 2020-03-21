namespace FVim

open def
open log
open common
open ui

open ReactiveUI
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia.Input
open Avalonia
open Avalonia.Data
open Avalonia.ReactiveUI
open System.Runtime.InteropServices
open Avalonia.Rendering
open Avalonia.Interactivity
open Avalonia.VisualTree

#nowarn "0025"

open System.Runtime.InteropServices
open System.Runtime
open Avalonia.Media

type MainWindow() as this =
    inherit ReactiveWindow<MainWindowViewModel>()

    static let XProp = AvaloniaProperty.Register<MainWindow,int>("PosX")
    static let YProp = AvaloniaProperty.Register<MainWindow,int>("PosY")

    let mutable m_bgcolor: Color = Color()
    let mutable m_bgopacity: float = 1.0
    let mutable m_bgcomp = States.NoComposition

    let configBackground() =
        m_bgcomp <- States.background_composition
        m_bgopacity <- States.background_opacity
        let comp =
          match m_bgcomp with
          | States.Acrylic     -> ui.AdvancedBlur(m_bgopacity, m_bgcolor)
          | States.Blur        -> ui.GaussianBlur(m_bgopacity, m_bgcolor)
          | States.Transparent -> ui.TransparentBackground(m_bgopacity, m_bgcolor)
          | _                  -> ui.SolidBackground(m_bgopacity, m_bgcolor)
        trace "mainwindow" "configBackground: %A" comp
        ui.SetWindowBackgroundComposition this comp
        
    let mutable m_saved_size          = Size(100.0,100.0)
    let mutable m_saved_pos           = PixelPoint(300, 300)
    let mutable m_saved_state         = WindowState.Normal
    let mutable m_left_border:Panel   = null
    let mutable m_right_border:Panel  = null
    let mutable m_bottom_border:Panel = null

    let m_cursor_ns = Cursor(StandardCursorType.SizeNorthSouth)
    let m_cursor_we = Cursor(StandardCursorType.SizeWestEast)
    let m_cursor_ne = Cursor(StandardCursorType.TopRightCorner)
    let m_cursor_nw = Cursor(StandardCursorType.TopLeftCorner)
    let m_cursor_se = Cursor(StandardCursorType.BottomRightCorner)
    let m_cursor_sw = Cursor(StandardCursorType.BottomLeftCorner)

    let setCursor c =
        this.Cursor <- c

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

    let dragThreshold = 5.0

    let getDragEdge (pos: Point) =
        if this.ViewModel.CustomTitleBar && not this.ViewModel.Fullscreen && this.WindowState <> WindowState.Maximized then
            let l = pos.X <= dragThreshold
            let r = this.Width - pos.X <= dragThreshold
            let b = this.Height - pos.Y <= dragThreshold
            let t = pos.Y <= dragThreshold
            match l, r, b, t with
            | true, _, false, false -> Some WindowEdge.West
            | true, _, true, _      -> Some WindowEdge.SouthWest
            | true, _, _, true      -> Some WindowEdge.NorthWest
            | _, true, false, false -> Some WindowEdge.East
            | _, true, true, _      -> Some WindowEdge.SouthEast
            | _, true, _, true      -> Some WindowEdge.NorthWest
            | _, _, true, _         -> Some WindowEdge.South
            | _, _, _, true         -> Some WindowEdge.North
            | _                     -> None
        else None


    do
        #if DEBUG
        this.Renderer.DrawFps <- true
        Avalonia.DevToolsExtensions.AttachDevTools(this)
        #endif

        DragDrop.SetAllowDrop(this, true)
        configBackground()

        let flushop () = 
            let editor: IControl = this.GetEditor()
            if editor <> null then
              editor.InvalidateVisual()
            else
              this.InvalidateVisual()

        this.Watch [
            this.Closing.Subscribe (fun e -> Model.OnTerminating e)
            this.Closed.Subscribe  (fun _ -> Model.OnTerminated())
            this.Bind(XProp, Binding("X", BindingMode.TwoWay))
            this.Bind(YProp, Binding("Y", BindingMode.TwoWay))
            this.GotFocus.Subscribe (fun _ -> Model.OnFocusGained())
            this.LostFocus.Subscribe (fun _ -> Model.OnFocusLost())

            States.Register.Watch "background.composition" configBackground
            States.Register.Watch "background.opacity" configBackground
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
            this.AddHandler(MainWindow.PointerMovedEvent, (fun _ (ev: PointerEventArgs) ->
                match getDragEdge <| ev.GetPosition(this) with
                | Some (WindowEdge.NorthWest) -> setCursor m_cursor_nw
                | Some (WindowEdge.SouthEast) -> setCursor m_cursor_se
                | Some (WindowEdge.NorthEast) -> setCursor m_cursor_ne
                | Some (WindowEdge.SouthWest) -> setCursor m_cursor_sw
                | Some (WindowEdge.North | WindowEdge.South) -> setCursor m_cursor_ns
                | Some (WindowEdge.East | WindowEdge.West) -> setCursor m_cursor_we
                | _ -> setCursor Cursor.Default)
                , RoutingStrategies.Tunnel)
            this.AddHandler(MainWindow.PointerPressedEvent, (fun _ (ev: PointerPressedEventArgs) ->
                match getDragEdge <| ev.GetPosition(this) with
                | Some edge ->
                    ev.Handled <- true
                    this.BeginResizeDrag(edge, ev)
                | _ -> ())
                , RoutingStrategies.Tunnel)


        ]
        AvaloniaXamlLoader.Load this

    override __.OnPointerReleased ev =
        setCursor Cursor.Default 
        base.OnPointerReleased ev

    override __.OnTemplateApplied _ =
        m_left_border   <- this.FindControl<Panel>("LeftBorder")
        m_right_border  <- this.FindControl<Panel>("RightBorder")
        m_bottom_border <- this.FindControl<Panel>("BottomBorder")

    member this.GetEditor() =
      this.FindControl("RootEditor")

    override this.OnDataContextChanged _ =
        let ctx = this.DataContext :?> MainWindowViewModel
        let pos = PixelPoint(int ctx.X, int ctx.Y)
        let mutable firstPoschange = true
        let mutable deltaX = 0
        let mutable deltaY = 0
        this.ViewModel <- ctx
        toggleTitleBar ctx.CustomTitleBar

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
            ctx.MainGrid.ObservableForProperty(fun x -> x.BackgroundColor) 
            |> Observable.subscribe(fun c -> 
                trace "mainwindow" "update background color: %s" (c.Value.ToString())
                m_bgcolor <- c.Value
                configBackground())
            ctx.ObservableForProperty((fun x -> x.Fullscreen), skipInitial=true).Subscribe(fun v -> toggleFullscreen <| v.GetValue())
            ctx.ObservableForProperty((fun x -> x.CustomTitleBar), skipInitial=true).Subscribe(fun v -> toggleTitleBar <| v.GetValue())
        ]

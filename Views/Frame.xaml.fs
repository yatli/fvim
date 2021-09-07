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

open Avalonia.Diagnostics
open model

#nowarn "0025"

open System.Runtime.InteropServices
open System.Runtime
open Avalonia.Media

type Frame() as this =
    inherit ReactiveWindow<FrameViewModel>()

    static let XProp = AvaloniaProperty.Register<Frame,int>("PosX")
    static let YProp = AvaloniaProperty.Register<Frame,int>("PosY")

    let mutable m_bgcolor: Color = Color()
    let mutable m_bgopacity: float = 1.0
    let mutable m_bgcomp = NoComposition

    let configBackground() =
        m_bgcomp <- states.background_composition
        m_bgopacity <- states.background_opacity
        let comp =
          match m_bgcomp with
          | Acrylic     -> ui.AdvancedBlur(m_bgopacity, m_bgcolor)
          | Blur        -> ui.GaussianBlur(m_bgopacity, m_bgcolor)
          | Transparent -> ui.TransparentBackground(m_bgopacity, m_bgcolor)
          | _                  -> ui.SolidBackground(m_bgopacity, m_bgcolor)
        trace "frame" "configBackground: %A" comp
        ui.SetWindowBackgroundComposition this comp
        
    let mutable m_saved_size          = Size(100.0,100.0)
    let mutable m_saved_pos           = PixelPoint(300, 300)
    let mutable m_saved_state         = WindowState.Normal
    let mutable m_left_border:Panel   = null
    let mutable m_right_border:Panel  = null
    let mutable m_bottom_border:Panel = null

    let m_cursor_ns = new Cursor(StandardCursorType.SizeNorthSouth)
    let m_cursor_we = new Cursor(StandardCursorType.SizeWestEast)
    let m_cursor_ne = new Cursor(StandardCursorType.TopRightCorner)
    let m_cursor_nw = new Cursor(StandardCursorType.TopLeftCorner)
    let m_cursor_se = new Cursor(StandardCursorType.BottomRightCorner)
    let m_cursor_sw = new Cursor(StandardCursorType.BottomLeftCorner)

    let setCursor c =
        this.Cursor <- c

    let toggleTitleBar(custom) =
        this.SystemDecorations <- if custom then SystemDecorations.BorderOnly else SystemDecorations.Full

    let toggleFullscreen(v) =
        if not v then
            this.WindowState <- m_saved_state
            this.PlatformImpl.Resize(m_saved_size)
            this.Position <- m_saved_pos
            toggleTitleBar this.ViewModel.CustomTitleBar
        else
            //  The order of actions is very important.
            //  1. Remove decorations
            //  2. Save current states
            //  3. Turn window state to normal
            //  4. Position window to TopLeft, and resize
            let screen                = this.Screens.ScreenFromVisual(this)
            let screenBounds          = screen.Bounds
            let sz                    = screenBounds.Size.ToSizeWithDpi(96.0 * (this:>IRenderRoot).RenderScaling)
            this.SystemDecorations    <- SystemDecorations.None
            m_saved_size              <- this.ClientSize
            m_saved_pos               <- this.Position
            m_saved_state             <- this.WindowState
            this.WindowState          <- WindowState.Normal
            this.Position             <- screenBounds.TopLeft
            this.PlatformImpl.Resize(sz)

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
        this.AttachDevTools()
        #endif

        DragDrop.SetAllowDrop(this, true)
        configBackground()

        let flushop () = 
            let editor: IControl = this.FindControl("RootGrid")
            if editor <> null then
              editor.InvalidateVisual()
            else
              this.InvalidateVisual()

        this.Watch [
            rpc.register.watch "background.composition" configBackground
            rpc.register.watch "background.opacity" configBackground
            rpc.register.notify "DrawFPS" (fun [| Bool(v) |] -> 
                trace "mainwindow" "DrawFPS: %A" v
                this.Renderer.DrawFps <- v)

            model.Flush |> Observable.subscribe flushop
        ]

        this.AddHandler(DragDrop.DropEvent, (fun _ (e: DragEventArgs) ->
            if e.Data.Contains(DataFormats.FileNames) then
                model.EditFiles <| e.Data.GetFileNames()
            elif e.Data.Contains(DataFormats.Text) then
                model.InsertText <| e.Data.GetText()
        ))

        this.AddHandler(DragDrop.DragOverEvent, (fun _ (e:DragEventArgs) ->
            e.DragEffects <- DragDropEffects.Move ||| DragDropEffects.Link ||| DragDropEffects.Copy
        ))
        this.AddHandler(Frame.PointerMovedEvent, (fun _ (ev: PointerEventArgs) ->
            match getDragEdge <| ev.GetPosition(this) with
            | Some (WindowEdge.NorthWest) -> setCursor m_cursor_nw
            | Some (WindowEdge.SouthEast) -> setCursor m_cursor_se
            | Some (WindowEdge.NorthEast) -> setCursor m_cursor_ne
            | Some (WindowEdge.SouthWest) -> setCursor m_cursor_sw
            | Some (WindowEdge.North | WindowEdge.South) -> setCursor m_cursor_ns
            | Some (WindowEdge.East | WindowEdge.West) -> setCursor m_cursor_we
            | _ -> setCursor Cursor.Default)
            , RoutingStrategies.Tunnel)
        this.AddHandler(Frame.PointerPressedEvent, (fun _ (ev: PointerPressedEventArgs) ->
            match getDragEdge <| ev.GetPosition(this) with
            | Some edge ->
                ev.Handled <- true
                this.BeginResizeDrag(edge, ev)
            | _ -> ())
            , RoutingStrategies.Tunnel)


        AvaloniaXamlLoader.Load this

    override __.OnPointerReleased ev =
        setCursor Cursor.Default 
        base.OnPointerReleased ev

    override __.OnTemplateApplied _ =
        m_left_border   <- this.FindControl<Panel>("LeftBorder")
        m_right_border  <- this.FindControl<Panel>("RightBorder")
        m_bottom_border <- this.FindControl<Panel>("BottomBorder")

    override this.OnDataContextChanged _ =
        let ctx = this.DataContext :?> FrameViewModel
        this.ViewModel <- ctx
        toggleTitleBar ctx.CustomTitleBar

        if ctx.MainGrid.Id = 1 then
            let pos = PixelPoint(int ctx.X, int ctx.Y)
            let mutable firstPoschange = true
            let mutable deltaX = 0
            let mutable deltaY = 0
            trace "mainwindow" "set position: %d, %d" pos.X pos.Y
            this.Position <- pos
            this.WindowState <- ctx.WindowState
            this.Watch [
                this.Closing.Subscribe (fun e -> model.OnTerminating e)
                this.Closed.Subscribe  (fun _ -> model.OnTerminated())
                this.Bind(XProp, Binding("X", BindingMode.TwoWay))
                this.Bind(YProp, Binding("Y", BindingMode.TwoWay))
                this.GotFocus.Subscribe (fun _ -> model.OnFocusGained())
                this.LostFocus.Subscribe (fun _ -> model.OnFocusLost())
                this.PositionChanged.Subscribe (fun p ->
                    if firstPoschange then
                        firstPoschange <- false
                        deltaX <- p.Point.X - pos.X
                        deltaY <- p.Point.Y - pos.Y
                        trace "mainwindow" "first PositionChanged event: %d, %d (delta=%d, %d)" p.Point.X p.Point.Y deltaX deltaY
                    else
                        this.SetValue(XProp, p.Point.X - deltaX) |> ignore
                        this.SetValue(YProp, p.Point.Y - deltaY) |> ignore
                    )
            ]
        else
            m_bgcolor <- theme.default_bg
            configBackground()
            let grid_vm = ctx.MainGrid:?>GridViewModel
            ctx.Width <- grid_vm.BufferWidth
            // XXX bad hack we don't know title bar height atm
            ctx.Height <- grid_vm.BufferHeight + if ctx.CustomTitleBar then 40.0 else 0.0

            this.Watch [
                grid_vm.ExtWinClosed.Subscribe(this.Close)
                this.Closed.Subscribe (fun _ -> model.OnExtClosed grid_vm.WindowHandle)
            ]
        this.Watch [
            (ctx :> IFrame).MainGrid.ObservableForProperty(fun x -> x.BackgroundColor) 
            |> Observable.subscribe(fun c -> 
                trace "mainwindow" "update background color: %s" (c.Value.ToString())
                m_bgcolor <- c.Value
                configBackground())
            ctx.ObservableForProperty((fun x -> x.Fullscreen), skipInitial=true).Subscribe(fun v -> toggleFullscreen <| v.GetValue())
            ctx.ObservableForProperty((fun x -> x.CustomTitleBar), skipInitial=true).Subscribe(fun v -> toggleTitleBar <| v.GetValue())
        ]

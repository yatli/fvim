namespace FVim

open FVim.ui
open FVim.log
open FVim.wcwidth

open ReactiveUI
open SkiaSharp
open SkiaSharp.HarfBuzz
open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open Avalonia.Threading
open Avalonia.Platform
open Avalonia.Skia
open Avalonia.Media.Imaging
open Avalonia.VisualTree
open System
open FSharp.Control.Reactive

type Editor() as this =
    inherit Canvas()

    static let ViewModelProp  = AvaloniaProperty.Register<Editor, EditorViewModel>("ViewModel")
    static let AnchorXProp    = AvaloniaProperty.Register<Editor, float>("AnchorX")
    static let AnchorYProp    = AvaloniaProperty.Register<Editor, float>("AnchorY")

    let mutable m_render_queued = false
    let mutable m_saved_size  = Size(100.0,100.0)
    let mutable m_saved_pos   = PixelPoint(300, 300)
    let mutable m_saved_state = WindowState.Normal
    let mutable grid_fb: RenderTargetBitmap  = null
    let mutable grid_scale: float            = 1.0
    let mutable grid_vm: EditorViewModel     = Unchecked.defaultof<_>
    let mutable (m_visualroot: IVisual) = this :> IVisual

    let mutable m_debug = false

    let trace fmt = 
        let nr =
            if grid_vm <> Unchecked.defaultof<_> then (grid_vm:>IGridUI).Id.ToString()
            else "(no vm attached)"
        trace ("editor #" + nr) fmt


    let image() = this.FindControl<Image>("FrameBuffer")

    let resizeFrameBuffer() =
        grid_scale <- this.GetVisualRoot().RenderScaling
        let image = image()
        image.Source <- null
        if grid_fb <> null then
            grid_fb.Dispose()
            grid_fb <- null
        grid_fb <- AllocateFramebuffer (grid_vm.BufferWidth) (grid_vm.BufferHeight) grid_scale
        image.Source <- grid_fb

    //-------------------------------------------------------------------------
    //           = The rounding error of the rendering system =
    //
    // Suppose our grid is arranged uniformly with the height of the font:
    //
    //   Y_line = row * H_font
    //
    // Here, row is an integer and H_font float. We then have line Y positions
    // as a sequence of incrementing floats: [ 0 * H_font; 1 * H_font; ... ]
    // Suppose the whole grid is rendered in one pass, the lines will be drawn
    // with coordinates:
    //
    //   [ {0Hf, 1Hf}; {1Hf, 2Hf}; {2Hf, 3Hf} ... ]
    //
    // Clearly this is overlapping. In a pixel-based coordinate system we simply
    // reduce the line height by one pixel. However now we are in a float co-
    // ordinate system.. The overlapped rectangles are drawn differently -- not
    // only that they don't overlap, they leave whitespace gaps in between!
    // To compensate, we have to manually do the rounding to snap the pixels...
    //-------------------------------------------------------------------------
    // like this:
    let rounding (pt: Point) =
        let px = pt * grid_scale 
        Point(Math.Ceiling px.X, Math.Ceiling px.Y) / grid_scale 

    let drawBuffer (ctx: IDrawingContextImpl) row col colend hlid (str: string list) (issym: bool) =

        let x = 
            match str with
            | x :: _ -> x
            | _ -> " "

        let font, fontwide, fontsize = grid_vm.GetFontAttrs()
        let fg, bg, sp, attrs = grid_vm.GetDrawAttrs hlid 
        let shaper, typeface = GetTypeface(x, attrs.italic, attrs.bold, font, fontwide)

        use fgpaint = new SKPaint()
        use bgpaint = new SKPaint()
        use sppaint = new SKPaint()
        SetForegroundBrush(fgpaint, fg, typeface, fontsize)

        let nr_col = 
            match wswidth grid_vm.[row, colend - 1].text with
            | CharType.Wide | CharType.Nerd | CharType.Emoji -> colend - col + 1
            | _ -> colend - col

        let topLeft      = grid_vm.GetPoint row col
        let bottomRight  = topLeft + grid_vm.GetPoint 1 nr_col
        let bg_region    = Rect(topLeft , bottomRight)

        bgpaint.Color <- bg.ToSKColor()
        sppaint.Color <- sp.ToSKColor()

        let txt = String.Concat str
        let shaping = 
            if txt.Length > 1 && txt.Length < 5 && issym then
                ValueSome shaper
            else ValueNone

        try
            RenderText(ctx, bg_region, grid_scale, fgpaint, bgpaint, sppaint, attrs.underline, attrs.undercurl, txt, shaping)
        with
        | ex -> trace "drawBuffer: %s" <| ex.ToString()

    // assembles text from grid and draw onto the context.
    let drawBufferLine (ctx: IDrawingContextImpl) y x0 xN =
        let xN = min xN grid_vm.Cols
        let x0 = max x0 0
        let y  = (min y  (grid_vm.Rows - 1) ) |> max 0
        let mutable x': int                  = xN - 1
        let mutable prev: GridBufferCell ref = ref grid_vm.[y, x']
        let mutable str: string list         = []
        let mutable wc: CharType             = wswidth (!prev).text
        let mutable sym: bool                = isProgrammingSymbol (!prev).text
        let mutable bold = 
            let _,_,_,hl_attrs = grid_vm.GetDrawAttrs (!prev).hlid
            hl_attrs.bold
        //  in each line we do backward rendering.
        //  the benefit is that the italic fonts won't be covered by later drawings
        for x = xN - 1 downto x0 do
            let current = ref grid_vm.[y,x]
            let mytext = (!current).text
            //  !NOTE text shaping is slow. We only use shaping for
            //  a symbol-only span (for ligature drawing).
            let mysym = isProgrammingSymbol mytext
            let mywc = wswidth mytext
            //  !NOTE bold glyphs are generally wider than normal.
            //  Therefore, we have to break them into single glyphs
            //  to prevent overflow into later cells.
            let prev_hlid = (!prev).hlid
            let hlidchange = prev_hlid <> (!current).hlid 
            if hlidchange || mywc <> wc || bold || sym <> mysym then
                drawBuffer ctx y (x + 1) (x' + 1) prev_hlid str sym
                wc <- mywc
                sym <- mysym
                x' <- x
                str <- []
                if hlidchange then
                    prev <- current
                    bold <- let _,_,_,hl_attrs = grid_vm.GetDrawAttrs (!current).hlid
                            in hl_attrs.bold
            str <- mytext :: str
        drawBuffer ctx y x0 (x' + 1) (!prev).hlid str sym

    let toggleFullscreen(v) =
        let win = this.GetVisualRoot() :?> Window

        if not v then
            win.WindowState <- m_saved_state
            win.PlatformImpl.Resize(m_saved_size)
            win.Position <- m_saved_pos
            win.HasSystemDecorations <- true
        else
            m_saved_size             <- win.ClientSize
            m_saved_pos              <- win.Position
            m_saved_state            <- win.WindowState
            let screen                = win.Screens.ScreenFromVisual(this)
            let screenBounds          = screen.Bounds
            let sz                    = screenBounds.Size.ToSizeWithDpi(96.0 * this.GetVisualRoot().RenderScaling)
            win.HasSystemDecorations <- false
            win.WindowState          <- WindowState.Normal
            win.Position             <- screenBounds.TopLeft
            win.PlatformImpl.Resize(sz)

    let doWithDataContext fn =
        match this.DataContext with
        | :? EditorViewModel as viewModel ->
            fn viewModel
        | _ -> Unchecked.defaultof<_>

    let redraw tick =
        if not m_render_queued then
            trace "render tick %d" tick
            m_render_queued <- true
            this.InvalidateVisual()

    let onViewModelConnected (vm:EditorViewModel) =
        grid_vm <- vm
        trace "viewmodel connected"
        vm.Watch [
            vm.ObservableForProperty(fun x -> x.RenderTick).Subscribe(fun tick -> redraw <| tick.GetValue())
            vm.ObservableForProperty(fun x -> x.Fullscreen).Subscribe(fun v -> toggleFullscreen <| v.GetValue())
            Observable.merge
                (vm.ObservableForProperty(fun x -> x.BufferWidth))
                (vm.ObservableForProperty(fun x -> x.BufferHeight))
            |> Observable.subscribe(fun _ -> 
                if this.GetVisualRoot() <> null then resizeFrameBuffer())

            Observable.interval(TimeSpan.FromMilliseconds 100.0)
            |> Observable.firstIf(fun _ -> this.IsInitialized)
            |> Observable.subscribe(fun _ -> 
                Model.OnGridReady(vm :> IGridUI)
                ignore <| Dispatcher.UIThread.InvokeAsync(this.Focus)
               )

            vm.ObservableForProperty(fun x -> x.X).Subscribe(fun x -> this.AnchorX <- x.GetValue())
            vm.ObservableForProperty(fun x -> x.Y).Subscribe(fun y -> this.AnchorY <- y.GetValue())
        ]

    let subscribeAndHandleInput fn (ob: IObservable< #Avalonia.Interactivity.RoutedEventArgs>) =
        ob.Subscribe(fun e ->
            if this.IsFocused && not e.Handled then
                e.Handled <- true
                doWithDataContext (fn e))

    let drawDebug (dc: IDrawingContextImpl) =
        let txt = Media.FormattedText()
        txt.Text <- sprintf "Grid #%d, Z=%d" grid_vm.GridId this.ZIndex
        txt.Typeface <- Media.Typeface("Iosevka Slab", 16.0)

        dc.DrawText(Media.Brushes.Tan, Point(10.0, 10.0), txt.PlatformImpl)
        dc.DrawText(Media.Brushes.Tan, Point(this.Bounds.Width - 60.0, 10.0), txt.PlatformImpl)
        dc.DrawText(Media.Brushes.Tan, Point(10.0, this.Bounds.Height - 60.0), txt.PlatformImpl)
        dc.DrawText(Media.Brushes.Tan, Point(this.Bounds.Width - 60.0, this.Bounds.Height - 60.0), txt.PlatformImpl)

        dc.DrawRectangle(Media.Pen(Media.Brushes.Red, 3.0), this.Bounds)
        dc.DrawLine(Media.Pen(Media.Brushes.Red, 1.0), Point(0.0, 0.0), Point(this.Bounds.Width, this.Bounds.Height))
        dc.DrawLine(Media.Pen(Media.Brushes.Red, 1.0), Point(0.0, this.Bounds.Height), Point(this.Bounds.Width, 0.0))


    do
        this.Watch [
            this.AttachedToVisualTree.Subscribe(fun e -> m_visualroot <- e.Root)
            this.GetObservable(Editor.DataContextProperty)
            |> Observable.ofType
            |> Observable.subscribe onViewModelConnected

            States.Register.Watch "font" (fun () -> 
                if grid_vm <> Unchecked.defaultof<_> then
                    grid_vm.MarkAllDirty()
                    this.InvalidateVisual()
                )

            //  Input handling
            this.TextInput |> subscribeAndHandleInput(fun e vm -> vm.OnTextInput e)
            this.KeyDown   |> subscribeAndHandleInput(fun e vm -> vm.OnKey e)
            this.PointerPressed |> subscribeAndHandleInput(fun e vm -> vm.OnMouseDown e m_visualroot)
            this.PointerReleased |> subscribeAndHandleInput(fun e vm -> vm.OnMouseUp e m_visualroot)
            this.PointerMoved |> subscribeAndHandleInput(fun e vm -> vm.OnMouseMove e m_visualroot)
            this.PointerWheelChanged |> subscribeAndHandleInput(fun e vm -> vm.OnMouseWheel e m_visualroot)
        ]
        AvaloniaXamlLoader.Load(this)

    override this.Render ctx =
        (*trace "render begin"*)
        if grid_fb <> null then
            let dirty = grid_vm.Dirty
            if not <| dirty.Empty() then
                let regions = dirty.Regions()
                trace "drawing %d regions"  regions.Count
                let timer = System.Diagnostics.Stopwatch.StartNew()
                use grid_dc = grid_fb.CreateDrawingContext(null)
                grid_dc.PushClip(Rect this.Bounds.Size)
                for r in regions do
                    for row = r.row to r.row_end - 1 do
                        drawBufferLine grid_dc row r.col r.col_end

                if m_debug then
                    drawDebug grid_dc

                grid_dc.PopClip()
                timer.Stop()
                trace "drawing end, time = %dms." timer.ElapsedMilliseconds
                grid_vm.markClean()

            (*trace "image size: %A; fb size: %A" (image().Bounds) (grid_fb.Size)*)
        (*trace "base rendering"*)
        base.Render ctx
        m_render_queued <- false
        (*trace "render end"*)

    override this.MeasureOverride(size) =
        trace "MeasureOverride: %A" size
        doWithDataContext (fun vm ->
            vm.RenderScale <- (this :> IVisual).GetVisualRoot().RenderScaling
            let sz  =
                if vm.TopLevel then size
                // multigrid: size is top-down managed, which means that
                // the measurement of the view should be consistent with
                // the buffer size calculated from the viewmodel.
                else Size(vm.BufferWidth, vm.BufferHeight)
            vm.SetMeasuredSize sz
            sz
        )

    interface IViewFor<EditorViewModel> with
        member this.ViewModel
            with get (): EditorViewModel = this.GetValue(ViewModelProp)
            and set (v: EditorViewModel): unit = this.SetValue(ViewModelProp, v)
        member this.ViewModel
            with get (): obj = this.GetValue(ViewModelProp) :> obj
            and set (v: obj): unit = this.SetValue(ViewModelProp, v)

    member this.AnchorX
        with get(): float = this.GetValue(AnchorXProp)
        and set(v: float) = this.SetValue(AnchorXProp, v)

    member this.AnchorY
        with get(): float = this.GetValue(AnchorYProp)
        and set(v: float) = this.SetValue(AnchorYProp, v)

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
open System.Collections.Generic
open Avalonia.Utilities

type Span = Span of text: string * hlid: int

type SpanCache(w, h, s) =
    let m_bitmap = AllocateFramebuffer w h s
    let mutable m_time = DateTime.Now

    let mutable m_curpos = 0.0

    member __.Alloc(w') =
        if m_curpos + w' <= w then
            let ret = ValueSome(m_curpos, m_bitmap)
            m_curpos <- ceil (m_curpos + w')
            ret
        else ValueNone

    member __.Hit() =
        m_time <- DateTime.Now

    interface IDisposable with
        member __.Dispose() =
            m_bitmap.Dispose()

type RenderedSpan =
    {
        Canvas: RenderTargetBitmap
        Start: float
        Cache: SpanCache
    }

type SpanCacheQueryResult =
| Hit of RenderedSpan
| Allocated of RenderTargetBitmap * float
| Fail

[<AllowNullLiteral>]
type SpanRenderCache(scale, h) =
    let m_width = 4096.0
    let m_caches = List<SpanCache>([ new SpanCache(m_width, h, scale) ])
    let m_lookup = Dictionary<Span, RenderedSpan>()

    member __.Query(txt, hlid, w) =
        let span = Span(txt, hlid)
        match m_lookup.TryGetValue(Span(txt,hlid)) with
        | _ when w > m_width || txt.Length < 2  -> Fail
        | true, result -> 
            result.Cache.Hit()
            Hit result
        | _ ->
        let cache, pos, bitmap = 
            let last_cache = m_caches.[m_caches.Count - 1]
            match last_cache.Alloc(w) with
            | ValueSome(curpos, bitmap) -> last_cache, curpos, bitmap
            | _ ->
            let new_cache = new SpanCache(m_width, h, scale)
            m_caches.Add(new_cache)
            match new_cache.Alloc(w) with
            | ValueSome(curpos, bitmap) -> new_cache, curpos, bitmap
            | _ -> failwith "?"
        m_lookup.[span] <- { Canvas=bitmap; Start=pos; Cache=cache  }
        cache.Hit()
        Allocated(bitmap, pos)

    member __.Height = h

    interface IDisposable with
        member __.Dispose() =
            m_caches.ForEach(fun x -> (x :> IDisposable).Dispose())

type Editor() as this =
    inherit Canvas()

    static let ViewModelProp  = AvaloniaProperty.Register<Editor, EditorViewModel>("ViewModel")
    static let AnchorXProp    = AvaloniaProperty.Register<Editor, float>("AnchorX")
    static let AnchorYProp    = AvaloniaProperty.Register<Editor, float>("AnchorY")

    let mutable m_render_queued = false
    let mutable m_saved_size = Size(100.0,100.0)
    let mutable m_saved_pos = PixelPoint(300, 300)
    let mutable m_saved_state = WindowState.Normal
    let mutable m_fb: RenderTargetBitmap = null
    let mutable m_cache: SpanRenderCache = null
    let mutable m_scale: float = 1.0
    let mutable m_viewmodel: EditorViewModel = Unchecked.defaultof<_>
    let mutable (m_visualroot: IVisual) = this :> IVisual

    let mutable m_debug = false

    let trace fmt = 
        let nr =
            if m_viewmodel <> Unchecked.defaultof<_> then (m_viewmodel:>IGridUI).Id.ToString()
            else "(no vm attached)"
        trace ("editor #" + nr) fmt


    let image() = this.FindControl<Image>("FrameBuffer")

    let ensureCache h =
        if m_cache = null || m_cache.Height < h then
            if m_cache <> null then
                (m_cache:>IDisposable).Dispose()
                m_cache <- null
            m_cache <- new SpanRenderCache(m_scale, h)

    let resizeFrameBuffer() =
        m_scale <- this.GetVisualRoot().RenderScaling
        let image = image()
        image.Source <- null
        if m_fb <> null then
            m_fb.Dispose()
            m_fb <- null

        m_fb <- AllocateFramebuffer (m_viewmodel.BufferWidth) (m_viewmodel.BufferHeight) m_scale
        image.Source <- m_fb

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
        let px = pt * m_scale 
        Point(Math.Ceiling px.X, Math.Ceiling px.Y) / m_scale 

    let drawBufferImpl ctx region hlid issym x (txt: string) =
        let font, fontwide, fontsize = m_viewmodel.GetFontAttrs()
        let fg, bg, sp, attrs = m_viewmodel.GetDrawAttrs hlid

        let shaper, typeface = GetTypeface(x, attrs.italic, attrs.bold, font, fontwide)

        use fgpaint = new SKPaint()
        use bgpaint = new SKPaint()
        use sppaint = new SKPaint()
        SetForegroundBrush(fgpaint, fg, typeface, fontsize)

        bgpaint.Color <- bg.ToSKColor()
        sppaint.Color <- sp.ToSKColor()

        let shaping = 
            if txt.Length > 1 && txt.Length < 5 && issym then
                ValueSome shaper
            else ValueNone

        try
            RenderText(ctx, region, fgpaint, bgpaint, sppaint, attrs.underline, attrs.undercurl, txt, shaping)
        with
        | ex -> trace "drawBufferImpl: %s" <| ex.ToString()

    let drawBuffer (ctx: IDrawingContextImpl) row col colend hlid (xs: string list) (issym: bool) =

        let x = 
            if xs.Length = 0 then " "
            else xs.[0]
        let txt = String.Concat xs

        let nr_col = 
            match wswidth m_viewmodel.[row, colend - 1].text with
            | CharType.Wide | CharType.Nerd | CharType.Emoji -> colend - col + 1
            | _ -> colend - col

        let topLeft      = m_viewmodel.GetPoint row col
        let bottomRight  = topLeft + m_viewmodel.GetPoint 1 nr_col
        let bg_region    = Rect(topLeft , bottomRight)

        match m_cache.Query(txt, hlid, bg_region.Width) with
        | Fail -> drawBufferImpl ctx bg_region hlid issym x txt
        | Allocated(bitmap, pos) ->
            trace "hit cache, txt='%s', pos=%f" txt pos
            let dc = bitmap.CreateDrawingContext(null)
            let region = bg_region.WithX(pos).WithY(0.0)
            dc.PushClip(region)
            drawBufferImpl dc region hlid issym x txt
            dc.Dispose()
            let bitmap' = bitmap.PlatformImpl :?> IRef<IBitmapImpl>
            ctx.DrawImage(bitmap', 1.0, Rect(0.0, 0.0, region.Width * m_scale, region.Height * m_scale), bg_region)
        | Hit { Canvas = bitmap; Start=pos } ->
            let bitmap' = bitmap.PlatformImpl :?> IRef<IBitmapImpl>
            let region = bg_region.WithX(pos).WithY(0.0)
            ctx.DrawImage(bitmap', 1.0, Rect(0.0, 0.0, region.Width * m_scale, region.Height * m_scale), bg_region)

    // assembles text from grid and draw onto the context.
    let drawBufferLine (ctx: IDrawingContextImpl) y x0 xN =
        let xN = min xN m_viewmodel.Cols
        let x0 = max x0 0
        let y  = (min y  (m_viewmodel.Rows - 1) ) |> max 0
        let mutable x': int                  = xN - 1
        let mutable prev: GridBufferCell ref = ref m_viewmodel.[y, x']
        let mutable str: string list         = []
        let mutable wc: CharType             = wswidth (!prev).text
        let mutable sym: bool                = isProgrammingSymbol (!prev).text
        let mutable bold = 
            let _,_,_,hl_attrs = m_viewmodel.GetDrawAttrs (!prev).hlid
            hl_attrs.bold
        //  in each line we do backward rendering.
        //  the benefit is that the italic fonts won't be covered by later drawings
        for x = xN - 1 downto x0 do
            let current = ref m_viewmodel.[y,x]
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
                    bold <- let _,_,_,hl_attrs = m_viewmodel.GetDrawAttrs (!current).hlid
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
        m_viewmodel <- vm
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
        txt.Text <- sprintf "Grid #%d, Z=%d" m_viewmodel.GridId this.ZIndex
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
                if m_viewmodel <> Unchecked.defaultof<_> then
                    m_viewmodel.MarkAllDirty()
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
        if m_fb <> null then
            ensureCache m_viewmodel.GlyphHeight

            let dirty = m_viewmodel.Dirty
            if not <| dirty.Empty() then
                let regions = dirty.Regions()
                trace "drawing %d regions"  regions.Count
                let timer = System.Diagnostics.Stopwatch.StartNew()
                use grid_dc = m_fb.CreateDrawingContext(null)
                grid_dc.PushClip(Rect this.Bounds.Size)
                for r in regions do
                    for row = r.row to r.row_end - 1 do
                        drawBufferLine grid_dc row r.col r.col_end

                if m_debug then
                    drawDebug grid_dc

                grid_dc.PopClip()
                timer.Stop()
                trace "drawing end, time = %dms." timer.ElapsedMilliseconds
                m_viewmodel.markClean()

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

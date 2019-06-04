namespace FVim

open FVim.ui
open FVim.log
open FVim.wcwidth

open SkiaSharp
open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open Avalonia.Threading
open Avalonia.Platform
open Avalonia.Skia
open Avalonia.Media.Imaging
open ReactiveUI
open Avalonia.VisualTree
open System.Reactive.Linq
open System

type EmbeddedEditor() as this =
    inherit UserControl()
    do
        AvaloniaXamlLoader.Load(this)

and Editor() as this =
    inherit Canvas()

    let mutable m_saved_size  = Size(100.0,100.0)
    let mutable m_saved_pos   = PixelPoint(300, 300)
    let mutable m_saved_state = WindowState.Normal
    let mutable grid_fb: RenderTargetBitmap  = null
    let mutable grid_scale: float            = 1.0
    let mutable grid_vm: EditorViewModel     = Unchecked.defaultof<_>

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

    let drawBuffer (ctx: IDrawingContextImpl) row col colend hlid (str: string list) =

        let x = 
            match str with
            | x :: _ -> x
            | _ -> " "

        let font, fontwide, fontsize = grid_vm.GetFontAttrs()
        let fg, bg, sp, attrs = grid_vm.GetDrawAttrs hlid 
        let typeface = GetTypeface(x, attrs.italic, attrs.bold, font, fontwide)

        use fgpaint = new SKPaint()
        use bgpaint = new SKPaint()
        use sppaint = new SKPaint()
        SetForegroundBrush(fgpaint, fg, typeface, fontsize)

        let nr_col = 
            match wswidth grid_vm.[row, colend - 1].text with
            | CharType.Wide | CharType.Nerd | CharType.Emoji -> colend - col + 1
            | _ -> colend - col

        let topLeft      = grid_vm.GetPoint row col
        let bottomRight  = (topLeft + grid_vm.GetPoint 1 nr_col) |> rounding
        let bg_region    = Rect(topLeft , bottomRight)

        bgpaint.Color <- bg.ToSKColor()
        sppaint.Color <- sp.ToSKColor()

        RenderText(ctx, bg_region, fgpaint, bgpaint, sppaint, attrs.underline, attrs.undercurl, String.Concat str)

    // assembles text from grid and draw onto the context.
    let drawBufferLine (ctx: IDrawingContextImpl) y x0 xN =
        let xN = min xN grid_vm.Cols
        let x0 = max x0 0
        let y  = (min y  (grid_vm.Rows - 1) ) |> max 0
        let mutable x': int                  = xN - 1
        let mutable prev: GridBufferCell ref = ref grid_vm.[y, x']
        let mutable str: string list         = []
        let mutable wc: CharType             = wswidth (!prev).text
        let mutable bold = 
            let _,_,_,hl_attrs = grid_vm.GetDrawAttrs (!prev).hlid
            hl_attrs.bold
        //  in each line we do backward rendering.
        //  the benefit is that the italic fonts won't be covered by later drawings
        for x = xN - 1 downto x0 do
            let current = ref grid_vm.[y,x]
            let mytext = (!current).text
            let mywc = wswidth mytext
            //  !NOTE bold glyphs are generally wider than normal.
            //  Therefore, we have to break them into single glyphs
            //  to prevent overflow into later cells.
            let prev_hlid = (!prev).hlid
            let hlidchange = prev_hlid <> (!current).hlid 
            if hlidchange || mywc <> wc || bold then
                drawBuffer ctx y (x + 1) (x' + 1) prev_hlid str
                wc <- mywc
                x' <- x
                str <- []
                if hlidchange then
                    prev <- current
                    bold <- let _,_,_,hl_attrs = grid_vm.GetDrawAttrs (!current).hlid
                            in hl_attrs.bold
            str <- mytext :: str
        drawBuffer ctx y x0 (x' + 1) (!prev).hlid str

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
        | _ -> ()

    let redraw tick =
        trace "render tick %d" tick
        this.InvalidateVisual()

    let onViewModelConnected (vm:EditorViewModel) =
        grid_vm <- vm
        [
            vm.ObservableForProperty(fun x -> x.RenderTick).Subscribe(fun tick -> redraw <| tick.GetValue())
            vm.ObservableForProperty(fun x -> x.Fullscreen).Subscribe(fun v -> toggleFullscreen <| v.GetValue())
            Observable.merge
                <| vm.ObservableForProperty(fun x -> x.BufferWidth)
                <| vm.ObservableForProperty(fun x -> x.BufferHeight)
            |> Observable.subscribe(fun _ -> resizeFrameBuffer())
            Observable.Interval(TimeSpan.FromMilliseconds(100.0))
                      .FirstAsync(fun _ -> this.IsInitialized)
                      .Subscribe(fun _ -> 
                        Model.OnGridReady(vm :> IGridUI)
                        ignore <| Dispatcher.UIThread.InvokeAsync(this.Focus)
                    )
        ] |> vm.Watch 
        
    do
        this.Watch [

            this.TextInput.Subscribe(fun e -> doWithDataContext(fun vm -> vm.OnTextInput e))

            this.GetObservable(Editor.DataContextProperty)
                          .OfType<EditorViewModel>()
                          .Subscribe(onViewModelConnected)

        ]
        AvaloniaXamlLoader.Load(this)

    static member RenderTickProp = AvaloniaProperty.Register<Editor, int>("RenderTick")
    static member FullscreenProp = AvaloniaProperty.Register<Editor, bool>("Fullscreen")
    static member ViewModelProp  = AvaloniaProperty.Register<Editor, EditorViewModel>("ViewModel")

    override this.Render ctx =
        if grid_fb <> null then
            let dirty = grid_vm.Dirty
            if dirty.height > 0 then
                trace "render begin, dirty = %A" dirty
                use grid_dc = grid_fb.CreateDrawingContext(null)
                grid_dc.PushClip(Rect this.Bounds.Size)
                for row = dirty.row to dirty.row_end - 1 do
                    drawBufferLine grid_dc row dirty.col dirty.col_end
                grid_dc.PopClip()
                trace "render end"
                grid_vm.markClean()
        base.Render ctx

    override this.MeasureOverride(size) =
        doWithDataContext (fun vm ->
            if vm.TopLevel then
                vm.MeasuredSize <- size
            vm.RenderScale <- (this :> IVisual).GetVisualRoot().RenderScaling
        )
        size

    (*each event repeats 4 times... use the event instead *)
    (*override this.OnTextInput(e) =*)

    override __.OnKeyDown(e) =
        doWithDataContext(fun vm -> vm.OnKey e)

    override __.OnKeyUp(e) =
        e.Handled <- true

    override this.OnPointerPressed(e) =
        doWithDataContext(fun vm -> vm.OnMouseDown e this)

    override this.OnPointerReleased(e) =
        doWithDataContext(fun vm -> vm.OnMouseUp e this)

    override this.OnPointerMoved(e) =
        doWithDataContext(fun vm -> vm.OnMouseMove e this)

    override this.OnPointerWheelChanged(e) =
        doWithDataContext(fun vm -> vm.OnMouseWheel e this)

    interface IViewFor<EditorViewModel> with
        member this.ViewModel
            with get (): EditorViewModel = this.GetValue(Editor.ViewModelProp)
            and set (v: EditorViewModel): unit = this.SetValue(Editor.ViewModelProp, v)
        member this.ViewModel
            with get (): obj = this.GetValue(Editor.ViewModelProp) :> obj
            and set (v: obj): unit = this.SetValue(Editor.ViewModelProp, v)


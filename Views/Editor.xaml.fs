namespace FVim

open FVim.ui
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
open System.Collections.Specialized
open FSharp.Control.Reactive
open Avalonia.Data
open Avalonia.Visuals.Media.Imaging
open Avalonia.Layout

module private EditorHelper =
  let inline trace vm fmt =
    let nr =
      if vm <> Unchecked.defaultof<_> then (vm :> IGridUI).Id.ToString() else "(no vm attached)"
    FVim.log.trace ("editor #" + nr) fmt

open EditorHelper
open System.Text

type Editor() as this =
  inherit Canvas()

  static let ViewModelProperty = AvaloniaProperty.Register<Editor, EditorViewModel>("ViewModel")
  static let GridIdProperty = AvaloniaProperty.Register<Editor, int>("GridId")
  static let RenderTickProperty = AvaloniaProperty.Register<Editor, int>("RenderTick")

  let mutable grid_fb: RenderTargetBitmap = null
  let mutable grid_dc: IDrawingContextImpl = null
  let mutable grid_scale: float = 1.0
  let mutable grid_vm: EditorViewModel = Unchecked.defaultof<_>

  let mutable m_debug = States.ui_multigrid

  // !Only call this if VisualRoot is attached
  let resizeFrameBuffer() =
    trace grid_vm "resizeFrameBuffer bufw=%A bufh=%A" grid_vm.BufferWidth grid_vm.BufferHeight
    let vroot = this.GetVisualRoot()
    grid_scale <- this.GetVisualRoot().RenderScaling
    if grid_fb <> null then 
      grid_fb.Dispose()
      grid_dc.Dispose()
    grid_fb <- AllocateFramebuffer (grid_vm.BufferWidth) (grid_vm.BufferHeight) grid_scale
    grid_dc <- grid_fb.CreateDrawingContext(null)

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
  let rounding(pt: Point) =
    let px = pt * grid_scale
    Point(Math.Ceiling px.X, Math.Ceiling px.Y) / grid_scale

  let mutable fgpaint = null
  let mutable bgpaint = null
  let mutable sppaint = null

  let drawBuffer (ctx: IDrawingContextImpl) row col colend hlid (issym: bool) =

    let font, fontwide, fontsize = grid_vm.GetFontAttrs()
    let fg, bg, sp, attrs = theme.GetDrawAttrs hlid
    let shaper, typeface = GetTypeface(grid_vm.[row, col].text, attrs.italic, attrs.bold, font, fontwide)

    if fgpaint = null then
      fgpaint <- new SKPaint()
      bgpaint <- new SKPaint()
      sppaint <- new SKPaint()
    SetForegroundBrush(fgpaint, fg, typeface, fontsize)

    let nr_col =
      match wswidth grid_vm.[row, colend - 1].text with
      | CharType.Wide
      | CharType.Nerd
      | CharType.Emoji -> colend - col + 1
      | _ -> colend - col

    let topLeft = grid_vm.GetPoint row col
    let bottomRight = topLeft + grid_vm.GetPoint 1 nr_col
    let bg_region = Rect(topLeft, bottomRight)

    bgpaint.Color <- bg.ToSKColor()
    sppaint.Color <- sp.ToSKColor()

    let txt = 
      let sb = StringBuilder()
      for i = col to colend - 1 do
        match grid_vm.[row, i] with
        | { text = { c1 = c1; c2 = c2; isSurrogatePair = true } } -> sb.Append(c1).Append(c2) |> ignore
        | { text = { c1 = c1 } } -> sb.Append(c1) |> ignore
      sb.ToString()

    let shaping =
      if txt.Length > 1 && txt.Length < 5 && issym && States.font_ligature
      then ValueSome shaper
      else ValueNone

    try
      RenderText(ctx, bg_region, grid_scale, fgpaint, bgpaint, sppaint, attrs.underline, attrs.undercurl, txt, shaping)
    with ex -> trace grid_vm "drawBuffer: %s" (ex.ToString())

  // assembles text from grid and draw onto the context.
  let drawBufferLine (ctx: IDrawingContextImpl) y x0 xN =
    let xN = min xN grid_vm.Cols
    let x0 = max x0 0
    let y = Math.Clamp(y, 0, (grid_vm.Rows - 1))
    let mutable x': int = xN - 1
    let mutable wc: CharType = wswidth grid_vm.[y, x'].text
    let mutable sym: bool = isProgrammingSymbol grid_vm.[y, x'].text
    let mutable prev_hlid = grid_vm.[y, x'].hlid

    let mutable bold =
      let _, _, _, hl_attrs = theme.GetDrawAttrs prev_hlid
      hl_attrs.bold
    //  in each line we do backward rendering.
    //  the benefit is that the italic fonts won't be covered by later drawings
    for x = xN - 1 downto x0 do
      let current = grid_vm.[y, x]
      let mytext = current.text
      //  !NOTE text shaping is slow. We only use shaping for
      //  a symbol-only span (for ligature drawing).
      let mysym = isProgrammingSymbol mytext
      let mywc = wswidth mytext
      //  !NOTE bold glyphs are generally wider than normal.
      //  Therefore, we have to break them into single glyphs
      //  to prevent overflow into later cells.
      let hlidchange = prev_hlid <> current.hlid
      if hlidchange || mywc <> wc || bold || sym <> mysym then
        drawBuffer ctx y (x + 1) (x' + 1) prev_hlid sym
        wc <- mywc
        sym <- mysym
        x' <- x
        if hlidchange then
          prev_hlid <- current.hlid
          bold <-
            let _, _, _, hl_attrs = theme.GetDrawAttrs prev_hlid
            hl_attrs.bold
    drawBuffer ctx y x0 (x' + 1) prev_hlid sym

  let doWithDataContext fn =
    match this.DataContext with
    | :? EditorViewModel as viewModel -> fn viewModel
    | _ -> Unchecked.defaultof<_>

  let findChildEditor(vm: obj) = this.Children |> Seq.tryFind(fun x -> x.DataContext = vm)

  let onViewModelConnected(vm: EditorViewModel) =
    grid_vm <- vm
    trace grid_vm "%s" "viewmodel connected"
    resizeFrameBuffer()
    vm.Watch
      [ Observable.merge (vm.ObservableForProperty(fun x -> x.BufferWidth))
          (vm.ObservableForProperty(fun x -> x.BufferHeight))
        |> Observable.subscribe(fun _ ->
             if this.GetVisualRoot() <> null then resizeFrameBuffer())

        Observable.interval(TimeSpan.FromMilliseconds 100.0)
        |> Observable.firstIf(fun _ -> this.IsInitialized && vm.Height > 0.0 && vm.Width > 0.0)
        |> Observable.subscribe(fun _ ->
             Model.OnGridReady(vm :> IGridUI)
             ignore <| Dispatcher.UIThread.InvokeAsync(this.Focus))

        this.GetObservable(RenderTickProperty).Subscribe(fun id -> 
          trace grid_vm "render tick %d" id
          this.InvalidateVisual())

        vm.ChildGrids.CollectionChanged.Subscribe(fun changes ->
          match changes.Action with
          | NotifyCollectionChangedAction.Add ->
              for e_vm in changes.NewItems do
                let view = Editor()
                view.DataContext <- e_vm
                view.ZIndex <- 3
                view.RenderTransformOrigin <- RelativePoint.TopLeft
                view.VerticalAlignment <- VerticalAlignment.Top
                view.HorizontalAlignment <- HorizontalAlignment.Left
                vm.Watch
                  [ view.Bind(Editor.GetGridIdProp(), Binding("GridId"))
                    // important: bind to BufferHeight/BufferWidth, not
                    // Height/Width.
                    view.Bind(Editor.HeightProperty, Binding("BufferHeight"))
                    view.Bind(Editor.WidthProperty, Binding("BufferWidth")) ]
                this.Children.Add(view)
          | NotifyCollectionChangedAction.Remove ->
              for e_vm in changes.OldItems do
                match findChildEditor e_vm with
                | Some view -> ignore(this.Children.Remove view)
                | _ -> ()
          | _ -> failwith "not supported") ]

  let subscribeAndHandleInput fn (ob: IObservable<#Avalonia.Interactivity.RoutedEventArgs>) =
    ob.Subscribe(fun e ->
      // only root handles events
      if not e.Handled && this.GridId = 1 then
        e.Handled <- true
        doWithDataContext(fn e))

  let drawDebug(dc: IDrawingContextImpl) =
    let txt = Media.FormattedText()
    txt.Text <- sprintf "Grid #%d, Z=%d" grid_vm.GridId this.ZIndex
    txt.Typeface <- Media.Typeface("Iosevka Slab")

    dc.DrawText(Media.Brushes.Tan, Point(10.0, 10.0), txt.PlatformImpl)
    dc.DrawText(Media.Brushes.Tan, Point(this.Bounds.Width - 60.0, 10.0), txt.PlatformImpl)
    dc.DrawText(Media.Brushes.Tan, Point(10.0, this.Bounds.Height - 60.0), txt.PlatformImpl)
    dc.DrawText(Media.Brushes.Tan, Point(this.Bounds.Width - 60.0, this.Bounds.Height - 60.0), txt.PlatformImpl)

    dc.DrawRectangle(null, Media.Pen(Media.Brushes.Red, 3.0), RoundedRect(this.Bounds.Translate(Vector(-this.Bounds.X, -this.Bounds.Y))))
    dc.DrawLine(Media.Pen(Media.Brushes.Red, 1.0), Point(0.0, 0.0), Point(this.Bounds.Width, this.Bounds.Height))
    dc.DrawLine(Media.Pen(Media.Brushes.Red, 1.0), Point(0.0, this.Bounds.Height), Point(this.Bounds.Width, 0.0))


  do

    

    this.Watch
      [ this.GetObservable(Editor.DataContextProperty)
        |> Observable.ofType
        |> Observable.zip this.AttachedToVisualTree
        |> Observable.map snd
        |> Observable.subscribe onViewModelConnected

        this.Bind(Canvas.LeftProperty, Binding("X"))
        this.Bind(Canvas.TopProperty, Binding("Y"))

        States.Register.Watch "font" (fun () ->
          if grid_vm <> Unchecked.defaultof<_> then
            grid_vm.MarkAllDirty()
            this.InvalidateVisual())

        //  Input handling
        this.TextInput |> subscribeAndHandleInput(fun e vm -> vm.OnTextInput e)
        this.KeyDown |> subscribeAndHandleInput(fun e vm -> vm.OnKey e)
        this.PointerPressed |> subscribeAndHandleInput(fun e vm -> vm.OnMouseDown e this)
        this.PointerReleased |> subscribeAndHandleInput(fun e vm -> vm.OnMouseUp e this)
        this.PointerMoved |> subscribeAndHandleInput(fun e vm -> vm.OnMouseMove e this)
        this.PointerWheelChanged |> subscribeAndHandleInput(fun e vm -> vm.OnMouseWheel e this) ]
    AvaloniaXamlLoader.Load(this)

  override this.Render ctx =
    (*trace "render begin"*)
    if grid_fb <> null then
      let dirty = grid_vm.Dirty
      if not <| dirty.Empty() then
        let regions = dirty.Regions()
        trace grid_vm "drawing %d regions" regions.Count
        let timer = System.Diagnostics.Stopwatch.StartNew()
        grid_dc.PushClip(Rect this.Bounds.Size)
        for r in regions do
          for row = r.row to r.row_end - 1 do
            drawBufferLine grid_dc row r.col r.col_end

        (*if m_debug then drawDebug grid_dc*)

        grid_dc.PopClip()
        timer.Stop()
        trace grid_vm "drawing end, time = %dms." timer.ElapsedMilliseconds
        grid_vm.markClean()
      let src_rect = Rect(0.0, 0.0, float grid_fb.PixelSize.Width, float grid_fb.PixelSize.Height)
      let tgt_rect = Rect(0.0, 0.0, grid_fb.Size.Width, grid_fb.Size.Height)

      ctx.DrawImage(grid_fb, src_rect, tgt_rect, BitmapInterpolationMode.Default)
    else
      trace grid_vm "%s" "grid_fb is null"

  (*trace "image size: %A; fb size: %A" (image().Bounds) (grid_fb.Size)*)
  (*trace "base rendering"*)
  (*base.Render ctx*)
  (*trace "render end"*)

  override this.MeasureOverride(size) =
    trace grid_vm "MeasureOverride: %A" size
    doWithDataContext(fun vm ->
      vm.RenderScale <- (this :> IVisual).GetVisualRoot().RenderScaling
      let sz =
        if vm.TopLevel then
          size
        // multigrid: size is top-down managed, which means that
        // the measurement of the view should be consistent with
        // the buffer size calculated from the viewmodel.
        else
          Size(vm.BufferWidth, vm.BufferHeight)
      vm.SetMeasuredSize sz
      sz)

  interface IViewFor<EditorViewModel> with

    member this.ViewModel
      with get (): EditorViewModel = this.GetValue(ViewModelProperty)
      and set (v: EditorViewModel): unit = this.SetValue(ViewModelProperty, v) |> ignore

    member this.ViewModel
      with get (): obj = this.GetValue(ViewModelProperty) :> obj
      and set (v: obj): unit = this.SetValue(ViewModelProperty, v) |> ignore

  member this.GridId
    with get () = this.GetValue(GridIdProperty)
    and set (v: int) = this.SetValue(GridIdProperty, v) |> ignore

  member this.RenderTick
    with get() = this.GetValue(RenderTickProperty)
    and  set(v) = this.SetValue(RenderTickProperty, v) |> ignore

  static member GetGridIdProp() = GridIdProperty

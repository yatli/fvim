namespace FVim

open FVim.ui
open FVim.wcwidth
open FVim.def

open ReactiveUI
open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open Avalonia.Threading
open Avalonia.Platform
open Avalonia.Media.Imaging
open Avalonia.VisualTree
open System
open System.Collections.Specialized
open FSharp.Control.Reactive
open Avalonia.Data
open Avalonia.Visuals.Media.Imaging
open Avalonia.Layout

module private GridHelper =
  let inline trace vm fmt =
    let nr =
      if vm <> Unchecked.defaultof<_> then (vm :> IGridUI).Id.ToString() else "(no vm attached)"
    FVim.log.trace ("editor #" + nr) fmt

open GridHelper
open model
open Avalonia.Input.TextInput
open Avalonia.Input

type Grid() as this =
  inherit Canvas()

  static let ViewModelProperty = AvaloniaProperty.Register<Grid, GridViewModel>("ViewModel")
  static let GridIdProperty = AvaloniaProperty.Register<Grid, int>("GridId")
  static let RenderTickProperty = AvaloniaProperty.Register<Grid, int>("RenderTick")

  let mutable grid_fb: RenderTargetBitmap = null
  let mutable grid_dc: IDrawingContextImpl = null
  (*let mutable grid_fb_scroll: RenderTargetBitmap = null*)
  (*let mutable grid_dc_scroll: IDrawingContextImpl = null*)
  let mutable grid_scale: float = 1.0
  let mutable grid_vm: GridViewModel = Unchecked.defaultof<_>

  //let mutable m_debug = states.ui_multigrid
  let mutable m_debug = false

  let ev_cursor_rect_changed = Event<EventHandler,EventArgs>()
  let ev_text_view_visual_changed = Event<EventHandler,EventArgs>()

  // !Only call this if VisualRoot is attached
  let resizeFrameBuffer() =
    trace grid_vm "resizeFrameBuffer bufw=%A bufh=%A" grid_vm.BufferWidth grid_vm.BufferHeight
    grid_scale <- this.GetVisualRoot().RenderScaling
    if grid_fb <> null then 
      grid_fb.Dispose()
      grid_dc.Dispose()
      (*grid_fb_scroll.Dispose()*)
      (*grid_dc_scroll.Dispose()*)
    grid_fb <- AllocateFramebuffer (grid_vm.BufferWidth) (grid_vm.BufferHeight) grid_scale
    grid_dc <- grid_fb.CreateDrawingContext(null)
    (*grid_fb_scroll <- AllocateFramebuffer (grid_vm.BufferWidth) (grid_vm.BufferHeight) grid_scale*)
    (*grid_dc_scroll <- grid_fb_scroll.CreateDrawingContext(null)*)

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

  let mutable _render_glyph_buf: uint[] = [||]
  let mutable _render_char_buf: char[] = [||]

  let drawBuffer (vm: GridViewModel) (ctx: IDrawingContextImpl) row col colend hlid (issym: bool) =

    if col = colend then () else

    let font, fontwide, fontsize = vm.FontAttrs
    let fg, bg, sp, attrs = theme.GetDrawAttrs hlid
    let typeface = GetTypeface(vm.[row, col].text, attrs.italic, attrs.bold, font, fontwide)

    let glyph_type = wswidth vm.[row, colend - 1].text
    let nr_col =
      match glyph_type with
      | CharType.Wide
      | CharType.Nerd
      | CharType.Emoji -> colend - col + 1
      | _ -> colend - col
    let clip = 
      match glyph_type with
      | CharType.Nerd
      | CharType.Emoji -> true
      | _ -> issym

    let topLeft = grid_vm.GetPoint (row + vm.AnchorRow) (col + vm.AnchorCol)
    let bottomRight = topLeft + grid_vm.GetPoint 1 nr_col
    let bg_region = Rect(topLeft, bottomRight)

    let txt =
      if nr_col > 1 && nr_col < 5 && issym && states.font_ligature then
        if _render_char_buf.Length < (colend - col) * 2 then
          _render_char_buf <- Array.zeroCreate ((colend - col) * 2)
        let mutable _len = 0
        for i = col to colend - 1 do
          Rune.feed(vm.[row, i].text, _render_char_buf, &_len)
        Shaped <| ReadOnlyMemory(_render_char_buf, 0, _len)
      else
        if _render_glyph_buf.Length < (colend - col) * 2 then
          _render_glyph_buf <- Array.zeroCreate ((colend - col)*2)
        let mutable _len = 0
        for i = col to colend - 1 do
          Rune.feed(vm.[row, i].text, _render_glyph_buf, &_len)

        Unshaped <| ReadOnlyMemory(_render_glyph_buf, 0, _len)

    try
        RenderText(ctx, bg_region, grid_scale, fg, bg, sp, attrs.underline, attrs.undercurl, txt, typeface, fontsize, clip)
    with ex -> trace vm "drawBuffer: %s" (ex.ToString())

  // assembles text from grid and draw onto the context.
  let drawBufferLine (vm: GridViewModel) (ctx: IDrawingContextImpl) y x0 xN =
    let xN = min xN vm.Cols
    let x0 = max x0 0
    let y = Math.Clamp(y, 0, (vm.Rows - 1))
    let mutable x': int = xN - 1
    let mutable wc: CharType = wswidth vm.[y, x'].text
    let mutable sym: bool = isProgrammingSymbol vm.[y, x'].text
    let mutable prev_hlid = vm.[y, x'].hlid
    let mutable nr_symbols = if sym then 1 else 0

    let mutable bold =
      let _, _, _, hl_attrs = theme.GetDrawAttrs prev_hlid
      hl_attrs.bold
    //  in each line we do backward rendering.
    //  the benefit is that the italic fonts won't be covered by later drawings
    for x = xN - 2 downto x0 do
      let current = vm.[y, x]
      let mytext = current.text
      //  !NOTE text shaping is slow. We only use shaping for
      //  a symbol-only span (for ligature drawing) with width > 1,
      //  That is, a single symbol will be merged into adjacent spans.
      let mysym = 
        let v = isProgrammingSymbol mytext
        if not v then
          nr_symbols <- 0
          false
        else
          nr_symbols <- nr_symbols + 1
          nr_symbols > 1
      let mywc = wswidth mytext
      //  !NOTE bold glyphs are generally wider than normal.
      //  Therefore, we have to break them into single glyphs
      //  to prevent overflow into later cells.
      let hlidchange = prev_hlid <> current.hlid
      if hlidchange || mywc <> wc || bold || sym <> mysym then
        //  If the span split is caused by symbols, we put [x+1]
        //  into the current span because nr_symbols has latched one rune.
        let prev_span = if mysym && not sym && mywc = wc && not bold && not hlidchange 
                        then x + 2 
                        else x + 1
        drawBuffer vm ctx y prev_span (x' + 1) prev_hlid sym
        x' <- prev_span - 1
        wc <- mywc
        sym <- mysym
        if hlidchange then
          prev_hlid <- current.hlid
          bold <-
            let _, _, _, hl_attrs = theme.GetDrawAttrs prev_hlid
            hl_attrs.bold
    drawBuffer vm ctx y x0 (x' + 1) prev_hlid sym

  let doWithDataContext fn =
    match this.DataContext with
    | :? GridViewModel as viewModel -> fn viewModel
    | _ -> Unchecked.defaultof<_>

  //let findChildEditor(vm: obj) = this.Children |> Seq.tryFind(fun x -> x.DataContext = vm)

  let onViewModelConnected(vm: GridViewModel) =
    grid_vm <- vm
    trace grid_vm "viewmodel connected"
    resizeFrameBuffer()
    vm.Watch
      [ Observable.merge (vm.ObservableForProperty(fun x -> x.BufferWidth))
          (vm.ObservableForProperty(fun x -> x.BufferHeight))
        |> Observable.subscribe(fun _ ->
             if this.GetVisualRoot() <> null then resizeFrameBuffer())

        Observable.interval(TimeSpan.FromMilliseconds 100.0)
        |> Observable.firstIf(fun _ -> this.IsInitialized && vm.Height > 0.0 && vm.Width > 0.0)
        |> Observable.subscribe(fun _ ->
             model.OnGridReady(vm :> IGridUI)
             if vm.Focusable then
               ignore <| Dispatcher.UIThread.InvokeAsync(this.Focus))

        this.GetObservable(RenderTickProperty).Subscribe(fun id -> 
          trace grid_vm "render tick %d" id
          this.InvalidateVisual())

        vm.ObservableForProperty(fun x -> x.IsFocused)
        |> Observable.subscribe(fun focused -> 
          if focused.Value && not this.IsFocused then 
            trace grid_vm "viewmodel ask to focus"
            this.Focus())

        this.GotFocus.Subscribe(fun _ -> vm.IsFocused <- true)
        this.LostFocus.Subscribe(fun _ -> vm.IsFocused <- false)

        //vm.ChildGrids.CollectionChanged.Subscribe(fun changes ->
        //  match changes.Action with
        //  | NotifyCollectionChangedAction.Add ->
        //      for e_vm in changes.NewItems do
        //        let view = Grid()
        //        view.DataContext <- e_vm
        //        view.ZIndex <- 3
        //        view.RenderTransformOrigin <- RelativePoint.TopLeft
        //        view.VerticalAlignment <- VerticalAlignment.Top
        //        view.HorizontalAlignment <- HorizontalAlignment.Left
        //        vm.Watch
        //          [ view.Bind(Grid.GetGridIdProp(), Binding("GridId"))
        //            // important: bind to BufferHeight/BufferWidth, not
        //            // Height/Width.
        //            view.Bind(Grid.HeightProperty, Binding("BufferHeight"))
        //            view.Bind(Grid.WidthProperty, Binding("BufferWidth")) ]
        //        this.Children.Add(view)
        //  | NotifyCollectionChangedAction.Remove ->
        //      for e_vm in changes.OldItems do
        //        match findChildEditor e_vm with
        //        | Some view -> ignore(this.Children.Remove view)
        //        | _ -> ()
        //  | _ -> failwith "not supported") 
          ]

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

  // prevent repetitive drawings
  let _drawnRegions = ResizeArray()

  let rec drawOps (vm: GridViewModel) = 
    if vm.Hidden then false
    else
    if vm.Dirty then
        trace vm "drawing whole grid"
        for row = 0 to vm.Rows - 1 do
            drawBufferLine vm grid_dc row 0 vm.Cols
        for c in vm.ChildGrids do
            c.MarkAllDirty()
            drawOps c |> ignore
        true
    else
    let draw row col colend = 
      let covered = _drawnRegions |> Seq.exists(fun (r, c, ce) -> r = row + vm.AnchorRow && c <= col + vm.AnchorCol && ce >= colend + vm.AnchorCol)
      if not covered then
        drawBufferLine vm grid_dc row col colend
        _drawnRegions.Add(row + vm.AnchorRow, col + vm.AnchorCol, colend + vm.AnchorCol)
      else
        trace vm "region %d %d %d waived" row col colend

    vm.DrawOps |> Seq.iter (
      function 
      | Scroll (top, bot, left, right, row, _col) ->
        let (t, l, b, r) = 
          if row > 0 then (top, left, bot - row, right)
          else (top - row, left, bot, right)
        for row = t to b - 1 do
          draw row l r

        (*Note: the captured bitmap still misaligns with the rendered text*)
        (*Until this is fixed, we have to draw the scrolled region line by line...*)

        (*let s_topLeft = grid_vm.GetPoint top left*)
        (*let s_bottomRight = grid_vm.GetPoint (bot) (right)*)
        (*let vec = grid_vm.GetPoint row 0*)
        (*let src, dst = *)
          (*if row > 0 then*)
            (*Rect(s_topLeft + vec, s_bottomRight),*)
            (*Rect(s_topLeft, s_bottomRight - vec)*)
          (*else*)
            (*Rect(s_topLeft, s_bottomRight + vec),*)
            (*Rect(s_topLeft - vec, s_bottomRight)*)

        (*grid_dc_scroll.PushClip(dst)*)
        (*grid_dc_scroll.Clear(Avalonia.Media.Colors.Transparent)*)
        (*grid_dc_scroll.PopClip()*)

        (*use r1 = grid_fb.PlatformImpl.CloneAs<_>()*)
        (*grid_dc_scroll.DrawBitmap(r1, 1.0, src, dst)*)

        (*grid_dc.PushClip(dst)*)
        (*grid_dc.Clear(Avalonia.Media.Colors.Transparent)*)
        (*grid_dc.PopClip()*)

        (*use r2 = grid_fb_scroll.PlatformImpl.CloneAs<_>()*)
        (*grid_dc.DrawBitmap(r2, 1.0, dst, dst)*)

      | Put r -> 
        for row = r.row to r.row_end - 1 do
          draw row r.col r.col_end
    )
    let mutable drawn = vm.DrawOps.Count <> 0
    for c in vm.ChildGrids do
        let child_drawn = drawOps c
        drawn <- drawn || child_drawn
    drawn

  do
    this.Watch
      [ this.GetObservable(Grid.DataContextProperty)
        |> Observable.ofType
        |> Observable.zip this.AttachedToVisualTree
        |> Observable.map snd
        |> Observable.subscribe onViewModelConnected

        this.Bind(Canvas.LeftProperty, Binding("X"))
        this.Bind(Canvas.TopProperty, Binding("Y"))

        rpc.register.watch "font" (fun () ->
          if grid_vm <> Unchecked.defaultof<_> then
            grid_vm.MarkAllDirty()
            this.InvalidateVisual())

        //  Input handling
        this.TextInput |> subscribeAndHandleInput(fun e vm -> vm.OnTextInput e)
        this.KeyDown |> subscribeAndHandleInput(fun e vm -> vm.OnKey e)
        this.PointerPressed |> subscribeAndHandleInput(fun e vm -> vm.OnMouseDown e this)
        this.PointerReleased |> subscribeAndHandleInput(fun e vm -> vm.OnMouseUp e this)
        this.PointerMoved |> subscribeAndHandleInput(fun e vm -> vm.OnMouseMove e this)
        this.PointerWheelChanged |> subscribeAndHandleInput(fun e vm -> vm.OnMouseWheel e this) 
      ]
    AvaloniaXamlLoader.Load(this)
  static do
    InputElement.TextInputMethodClientRequestedEvent.AddClassHandler<Grid>(fun grid e -> 
        e.Client <- grid) |> ignore

  override this.Render ctx =
    if isNull grid_fb then
      trace grid_vm "grid_fb is null"
    else
    grid_dc.PushClip(Rect this.Bounds.Size)
    let timer = System.Diagnostics.Stopwatch.StartNew()
    _drawnRegions.Clear()
    let drawn = drawOps grid_vm
    if m_debug then drawDebug grid_dc
    grid_dc.PopClip()

    grid_vm.MarkClean()

    let src_rect = Rect(0.0, 0.0, float grid_fb.PixelSize.Width, float grid_fb.PixelSize.Height)
    let tgt_rect = Rect(0.0, 0.0, grid_fb.Size.Width, grid_fb.Size.Height)

    ctx.DrawImage(grid_fb, src_rect, tgt_rect, BitmapInterpolationMode.Default)
    timer.Stop()
    if drawn then trace grid_vm "drawing end, time = %dms." timer.ElapsedMilliseconds

  override this.MeasureOverride(size) =
    trace grid_vm "MeasureOverride: %A" size
    doWithDataContext(fun vm ->
      vm.RenderScale <- (this :> IVisual).GetVisualRoot().RenderScaling
      let sz =
        if vm.IsTopLevel then
          size
        // multigrid: size is top-down managed, which means that
        // the measurement of the view should be consistent with
        // the buffer size calculated from the viewmodel.
        else
          Size(vm.BufferWidth, vm.BufferHeight)
      vm.SetMeasuredSize sz
      sz)

  interface IViewFor<GridViewModel> with

    member this.ViewModel
      with get (): GridViewModel = this.GetValue(ViewModelProperty)
      and set (v: GridViewModel): unit = this.SetValue(ViewModelProperty, v) |> ignore

    member this.ViewModel
      with get (): obj = this.GetValue(ViewModelProperty) :> obj
      and set (v: obj): unit = this.SetValue(ViewModelProperty, v) |> ignore

  interface ITextInputMethodClient with
      member _.SupportsPreedit = false
      member _.SupportsSurroundingText = false
      member _.SetPreeditText(_) = raise (NotSupportedException())
      member _.SurroundingText = raise (NotSupportedException())
      member _.TextAfterCursor: string = raise (NotSupportedException())
      member _.TextBeforeCursor: string = raise (NotSupportedException())
      [<CLIEvent>] member _.SurroundingTextChanged: IEvent<EventHandler,EventArgs> = raise (NotSupportedException())

      member _.CursorRectangle: Rect = 
        Rect()
      member _.TextViewVisual: IVisual = 
        this.FindControl<FVim.Cursor>("cursor") :> IVisual
      [<CLIEvent>] member _.CursorRectangleChanged: IEvent<EventHandler,EventArgs> = ev_cursor_rect_changed.Publish
      [<CLIEvent>] member _.TextViewVisualChanged: IEvent<EventHandler,EventArgs> = ev_text_view_visual_changed.Publish

  member this.GridId
    with get () = this.GetValue(GridIdProperty)
    and set (v: int) = this.SetValue(GridIdProperty, v) |> ignore

  member this.RenderTick
    with get() = this.GetValue(RenderTickProperty)
    and  set(v) = this.SetValue(RenderTickProperty, v) |> ignore

  static member GetGridIdProp() = GridIdProperty

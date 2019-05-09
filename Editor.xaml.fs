namespace FVim

open FVim.neovim.def
open FVim.log
open FVim.ui
open FVim.wcwidth

open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Markup.Xaml
open Avalonia.Media
open System
open MessagePack
open Avalonia.VisualTree
open Avalonia.Media.Imaging
open Avalonia.Threading
open Avalonia.Platform
open System.Text
open Avalonia.Utilities
open Avalonia.Skia
open FSharp.Control.Reactive
open Avalonia.Rendering
open SkiaSharp
open Avalonia.Native.Interop
open System.Reflection


[<Struct>]
type private GridBufferCell =
    {
        mutable text:  string
        mutable hlid:  int32
    } 
    with static member empty = { text  = " "; hlid = 0 }

[<Struct>]
type private GridSize =
    {
        rows: int32
        cols: int32
    }

[<Struct>]
type private GridRect =
    {
        row: int32
        col: int32
        // exclusive
        height: int32
        // exclusive
        width: int32
    }
    with 
    member x.row_end = x.row + x.height
    member x.col_end = x.col + x.width

type Editor() as this =
    inherit Control()

    let mutable default_fg       = Colors.Black
    let mutable default_bg       = Colors.Black
    let mutable default_sp       = Colors.Black

    let mutable hi_defs          = Array.zeroCreate<HighlightAttr>(256)
    let mutable mode_defs        = Array.empty<ModeInfo>

    let mutable _guifont         = "Iosevka Slab"
    let mutable _guifontwide     = "DengXian"

    let mutable font_size        = 16.0
    let mutable glyph_size       = Size(1.0, 1.0)

    let mutable typeface_normal  = null
    let mutable typeface_italic  = null
    let mutable typeface_bold    = null

    let mutable wtypeface_normal = null
    let mutable wtypeface_italic = null
    let mutable wtypeface_bold   = null

    let nerd_typeface = SKTypeface.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream("fvim.nerd.ttf"))

    let mutable grid_size        = { rows = 100; cols=50 }
    let mutable grid_scale       = 1.0
    let mutable grid_linespace   = 0.0
    let mutable grid_fullscreen  = false
#if USE_FRAMEBUFFER
    let mutable grid_fb: RenderTargetBitmap  = null
    let mutable grid_dc: IDrawingContextImpl = null
#endif
    let mutable grid_flushed     = false
    let mutable grid_buffer      = Array2D.create grid_size.rows grid_size.cols GridBufferCell.empty
    let mutable grid_dirty       = { row = 0; col = 0; height = 100; width = 50 }

    let mutable cursor_row       = 0
    let mutable cursor_col       = 0
    let mutable cursor_en        = false
    let mutable cursor_show      = false
    let mutable cursor_blinkoff  = 0
    let mutable cursor_blinkon   = 0
    let mutable cursor_blinkwait = 0
    let mutable cursor_modeidx   = -1
    let mutable cursor_timer: IDisposable = null

    let mutable mouse_en         = true
    let mutable mouse_pressed    = MouseButton.None
    let mutable mouse_pos        = 0,0

    let mutable is_ready         = false
    let mutable measured_size    = Size()

    let resizeEvent = Event<IGridUI>()
    let inputEvent  = Event<InputEvent>()

    //  converts grid position to UI Point
    let getPoint row col =
        Point(double(col) * glyph_size.Width, double(row) * glyph_size.Height)

    let getPos (p: Point) =
        int(p.X / glyph_size.Width), int(p.Y / glyph_size.Height)

    let getDrawAttrs hlid row col = 
        let attrs = hi_defs.[hlid].rgb_attr
        let txt = grid_buffer.[row, col].text
        let w = wswidth txt

        let typeface = 
            match w with
            | CharType.Wide ->
                (*printfn "wide: %A (%A)" txt (txt.ToCharArray() |> Array.map int)*)
                if   attrs.italic then wtypeface_italic
                elif attrs.bold   then wtypeface_bold
                else                   wtypeface_normal
            | CharType.Nerd ->         nerd_typeface
            | _ ->
                if   attrs.italic then typeface_italic
                elif attrs.bold   then typeface_bold
                else                   typeface_normal

        let mutable fg = Option.defaultValue default_fg attrs.foreground
        let mutable bg = Option.defaultValue default_bg attrs.background
        let mutable sp = Option.defaultValue default_sp attrs.special

        let rev (c: Color) =
            let inv = UInt32.MaxValue - c.ToUint32()
            Color.FromUInt32(inv ||| 0xFF000000u)

        if attrs.reverse then
            fg <- rev fg
            bg <- rev bg
            sp <- rev sp

        let bg_brush = new SKPaint(Color = bg.ToSKColor())
        let fg_brush = new SKPaint(Color = fg.ToSKColor())
        let sp_brush = new SKPaint(Color = sp.ToSKColor())

        fg_brush.Typeface <- typeface
        fg_brush.TextSize <- single font_size
        fg_brush.IsAntialias <- true
        fg_brush.IsAutohinted <- true
        fg_brush.IsLinearText <- false
        fg_brush.HintingLevel <- SKPaintHinting.Full
        fg_brush.LcdRenderText <- true
        fg_brush.SubpixelText <- true
        fg_brush.TextAlign <- SKTextAlign.Left
        fg_brush.DeviceKerningEnabled <- false
        fg_brush.TextEncoding <- SKTextEncoding.Utf16

        fg_brush, bg_brush, sp_brush, attrs

    //-------------------------------------------------------------------------
    //           = The rounding error of the rendering system: =
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
        let px = pt * grid_scale * grid_scale
        Point(Math.Ceiling px.X, Math.Ceiling px.Y) / grid_scale / grid_scale

    let drawBuffer (ctx: IDrawingContextImpl) row col colend hlid (str: string list) =

        let _fg, _bg, _sp, attrs = getDrawAttrs hlid row col
        use fg = _fg
        use bg = _bg
        use sp = _sp

        let nr_col = 
            match wswidth grid_buffer.[row, colend - 1].text with
            | CharType.Wide | CharType.Nerd | CharType.Emoji -> colend - col + 1
            | _ -> colend - col

        let topLeft      = getPoint row col
        let bottomRight  = (topLeft + getPoint 1 nr_col) |> rounding
        let bg_region    = Rect(topLeft , bottomRight)

        // DrawText accepts the coordinate of the baseline.
        //  h = [padding space 1] + above baseline | below baseline + [padding space 2]
        let h = bottomRight.Y - topLeft.Y
        //  total_padding = padding space 1 + padding space 2
        let total_padding = h + float fg.FontMetrics.Top - float fg.FontMetrics.Bottom
        let baseline      = topLeft.Y - float fg.FontMetrics.Top + (total_padding / 2.8)
        let fontPos       = Point(topLeft.X, baseline)

        let skia = ctx :?> DrawingContextImpl

        skia.Canvas.DrawRect(bg_region.ToSKRect(), bg)
        skia.Canvas.DrawText(String.Concat str, fontPos.ToSKPoint(), fg)

        // Text bounding box drawing:
        // --------------------------------------------------
        // let bounds = ref <| SKRect()
        // ignore <| fg.MeasureText(String.Concat str, bounds)
        // let mutable bounds = !bounds
        // bounds.Left <- bounds.Left + single (fontPos.X)
        // bounds.Top <- bounds.Top + single (fontPos.Y)
        // bounds.Right <- bounds.Right + single (fontPos.X)
        // bounds.Bottom <- bounds.Bottom + single (fontPos.Y)
        // fg.Style <- SKPaintStyle.Stroke
        // skia.Canvas.DrawRect(bounds, fg)
        // --------------------------------------------------

        if attrs.underline then
            let underline_pos = fg.FontMetrics.UnderlinePosition.GetValueOrDefault()
            let p1 = fontPos + Point(0.0, float <| underline_pos)
            let p2 = p1 + getPoint 0 (colend - col)
            sp.Style <- SKPaintStyle.Stroke
            skia.Canvas.DrawLine(p1.ToSKPoint(), p2.ToSKPoint(), sp)

        if attrs.undercurl then
            let underline_pos = fg.FontMetrics.UnderlinePosition.GetValueOrDefault()
            let p0 = fontPos + Point(0.0, float <| underline_pos)
            let qf = single glyph_size.Width / 4.0F
            let hf = qf * 2.0F
            let q3f = qf * 3.0F
            sp.Style <- SKPaintStyle.Stroke
            use path = new SKPath()
            path.MoveTo(single p0.X, single p0.Y)
            for i = 0 to (colend - col - 1) do
                let p = p0 + getPoint 0 i
                path.LineTo(single p.X,      single p.Y)
                path.LineTo(single p.X + qf, single p.Y - 2.0f)
                path.LineTo(single p.X + hf, single p.Y)
                path.LineTo(single p.X + q3f, single p.Y + 2.0f)
            skia.Canvas.DrawPath(path , sp)


    // assembles text from grid and draw onto the context.
    let drawBufferLine (ctx: IDrawingContextImpl) y x0 xN =
        let xN = min xN grid_size.cols
        let x0 = max x0 0
        let y  = (min y  (grid_size.rows - 1) ) |> max 0
        let mutable x'   = xN - 1
        let mutable hlid = grid_buffer.[y, x'].hlid
        let mutable str  = []
        let mutable wc   = wswidth grid_buffer.[y,x'].text
        //  in each line we do backward rendering.
        //  the benefit is that the italic fonts won't be covered by later drawings
        for x = xN - 1 downto x0 do
            let myhlid = grid_buffer.[y,x].hlid 
            let mywc   = wswidth grid_buffer.[y,x].text
            if myhlid <> hlid || mywc <> wc then
                drawBuffer ctx y (x + 1) (x' + 1) hlid str
                hlid <- myhlid 
                wc <- mywc
                x' <- x
                str <- []
            str <- grid_buffer.[y,x].text :: str
        drawBuffer ctx y x0 (x' + 1) hlid str

    let markDirty (region: GridRect) =
        if grid_dirty.height < 1 || grid_dirty.width < 1 
        then
            // was not dirty
            grid_dirty <- region
        else
            // calculate union
            let top  = min grid_dirty.row region.row
            let left = min grid_dirty.col region.col
            let bottom = max grid_dirty.row_end region.row_end
            let right = max grid_dirty.col_end region.col_end
            grid_dirty <- { row = top; col = left; height = bottom - top; width = right - left }
        #if USE_FRAMEBUFFER
        if grid_dc <> null then
            for y = region.row to region.row_end - 1 do
                drawBufferLine grid_dc y region.col region.col_end
        #endif

    let markAllDirty () =
        grid_dirty   <- { row = 0; col = 0; height = grid_size.rows; width = grid_size.cols }
        #if USE_FRAMEBUFFER
        if grid_dc <> null then
            for y = 0 to grid_size.rows - 1 do
                drawBufferLine grid_dc y 0 grid_size.cols
        #endif

    let flush() = 
        trace "redraw" "flush."
        // FIXME align our position to dirty region
        //let sz     = getPoint grid_dirty.height grid_dirty.width
        //let origin = getPoint grid_dirty.row grid_dirty.col
        //this.Height <- sz.Y
        //this.Width <- sz.X

        if not grid_flushed then
            this.InvalidateVisual()
        grid_flushed <- true

    let measureText (str: string) =
        use paint = new SKPaint()
        paint.Typeface <- typeface_bold
        paint.TextSize <- single font_size
        paint.IsAntialias <- true
        paint.IsAutohinted <- true
        paint.IsLinearText <- false
        paint.HintingLevel <- SKPaintHinting.Full
        paint.LcdRenderText <- true
        paint.SubpixelText <- true
        paint.TextAlign <- SKTextAlign.Left
        paint.DeviceKerningEnabled <- false
        paint.TextEncoding <- SKTextEncoding.Utf16

        let w = paint.MeasureText str
        let h = paint.FontSpacing

        w, h

        
    let fontConfig() =
        font_size <- max font_size 1.0

        typeface_normal <- SKTypeface.FromFamilyName(_guifont, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        typeface_italic <- SKTypeface.FromFamilyName(_guifont, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic)
        typeface_bold   <- SKTypeface.FromFamilyName(_guifont, SKFontStyleWeight.Medium, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)

        if String.IsNullOrEmpty _guifontwide then
            wtypeface_normal <- typeface_normal 
            wtypeface_italic <- typeface_italic 
            wtypeface_bold   <- typeface_bold   
        else
            wtypeface_normal <- SKTypeface.FromFamilyName(_guifontwide, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            wtypeface_italic <- SKTypeface.FromFamilyName(_guifontwide, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic)
            wtypeface_bold   <- SKTypeface.FromFamilyName(_guifontwide, SKFontStyleWeight.Medium, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)


        // It turns out the space " " advances farest...
        // So we measure it as the width.
        let w, h = measureText " "

        glyph_size <- Size(float w, float h)

        trace "fontConfig" "guifont=%s guifontwide=%s size=%A" _guifont _guifontwide glyph_size
        resizeEvent.Trigger(this)

    let setHighlight x =
        if hi_defs.Length < x.id + 1 then
            Array.Resize(&hi_defs, x.id + 100)
        hi_defs.[x.id] <- x
        if x.id = 0 then
            default_fg <- x.rgb_attr.foreground.Value
            default_bg <- x.rgb_attr.background.Value
            default_sp <- x.rgb_attr.special.Value
        markAllDirty()

    let setDefaultColors fg bg sp = 

        setHighlight {
            id = 0
            info = [||]
            cterm_attr = RgbAttr.Empty
            rgb_attr = { 
                foreground = Some fg
                background = Some bg
                special = Some sp
                reverse = false
                italic = false
                bold = false
                underline = false
                undercurl = false
            }
        }
        
    let markClean () =
        grid_flushed <- false
        grid_dirty <- { row = 0; col = 0; height = 0; width = 0}

    let clearBuffer () =
        grid_scale  <- this.GetVisualRoot().RenderScaling
        trace "redraw" "RenderScaling is %f" grid_scale
        #if USE_FRAMEBUFFER
        let size     = grid_scale * grid_scale * getPoint grid_size.rows grid_size.cols
        let pxsize   = PixelSize(int <| Math.Ceiling size.X, int <| Math.Ceiling size.Y)
        this.DestroyFramebuffer()

        grid_fb  <- new RenderTargetBitmap(pxsize, Vector(96.0 * grid_scale, 96.0 * grid_scale))
        grid_dc  <- grid_fb.CreateDrawingContext(null)

        #endif
        grid_buffer  <- Array2D.create grid_size.rows grid_size.cols GridBufferCell.empty
        // paint the whole framebuffer with default background color
        let canvas = (grid_dc :?> DrawingContextImpl).Canvas
        use paint = new SKPaint()
        paint.Color <- default_bg.ToSKColor()
        canvas.DrawRect(0.0f, 0.0f, single size.X, single size.Y, paint)
        markAllDirty()

    let initBuffer nrow ncol =
        grid_size    <- { rows = nrow; cols = ncol }
        clearBuffer()

    let putBuffer (line: GridLine) =
        let         row  = line.row
        let mutable col  = line.col_start
        let mutable hlid = -1
        let mutable rep = 1
        for cell in line.cells do
            hlid <- Option.defaultValue hlid cell.hl_id
            rep  <- Option.defaultValue 1 cell.repeat
            for i = 1 to rep do
                grid_buffer.[row, col].hlid <- hlid
                grid_buffer.[row, col].text <- cell.text
                col <- col + 1
        let dirty = { row = row; col = line.col_start; height = 1; width = col - line.col_start } 
        //trace "redraw" "putBuffer: writing to %A" dirty
        markDirty dirty

    let setModeInfo (cs_en: bool) (info: ModeInfo[]) =
        mode_defs <- info
        cursor_en <- cs_en

    let showCursor(show) =
        cursor_show <- show
        markDirty { row = cursor_row; col = cursor_col; height = 1; width = 1 }
        flush()

    let cursorTimerRun action time =
        if cursor_timer <> null then
            cursor_timer.Dispose()
            cursor_timer <- null
        if time > 0 then
            cursor_timer <- DispatcherTimer.RunOnce(Action(action), TimeSpan.FromMilliseconds(float time))

    let rec blinkon() =
        showCursor true
        cursorTimerRun blinkoff cursor_blinkon
    and blinkoff() = 
        showCursor false
        cursorTimerRun blinkon cursor_blinkoff

    let setCursor row col =
        markDirty { row = cursor_row; col = cursor_col; height = 1; width = 1 }
        cursor_row <- row
        cursor_col <- col
        showCursor true
        cursorTimerRun blinkon cursor_blinkwait

    let setBlink on off wait =

        cursor_blinkon   <- on
        cursor_blinkoff  <- off
        cursor_blinkwait <- wait
        cursor_show      <- true

        trace "blink" "on=%d off=%d wait=%d" on off wait
        setCursor cursor_row cursor_col

    let changeMode (name: string) (index: int) = 
        cursor_modeidx <- index
        match mode_defs.[index] with
        | { blinkon = Some on; blinkoff = Some off; blinkwait = Some wait  }
            when on > 0 && off > 0 && wait > 0 -> setBlink on off wait
        | _ -> setBlink 0 0 0
        markDirty { row = cursor_row; col = cursor_col; height = 1; width = 1 }

    let bell (visual: bool) =
        // TODO
        trace "neovim" "bell: %A" visual
        ()

    let setBusy (v: bool) =
        trace "neovim" "busy: %A" v
        cursor_en <- not v
        //if v then this.Cursor <- Cursor(StandardCursorType.Wait)
        //else this.Cursor <- Cursor(StandardCursorType.Arrow)
        flush()

    let scrollBuffer (top: int) (bot: int) (left: int) (right: int) (rows: int) (cols: int) =
        //  !NOTE top-bot are the bounds of the SCROLL-REGION, not SRC or DST.
        //        scrollBuffer first specifies the SR, and offsets SRC/DST according
        //        to the following rules:
        //
        //    If `rows` is bigger than 0, move a rectangle in the SR up, this can
        //    happen while scrolling down.
        //>
        //    +-------------------------+
        //    | (clipped above SR)      |            ^
        //    |=========================| dst_top    |
        //    | dst (still in SR)       |            |
        //    +-------------------------+ src_top    |
        //    | src (moved up) and dst  |            |
        //    |-------------------------| dst_bot    |
        //    | src (invalid)           |            |
        //    +=========================+ src_bot
        //<
        //    If `rows` is less than zero, move a rectangle in the SR down, this can
        //    happen while scrolling up.
        //>
        //    +=========================+ src_top
        //    | src (invalid)           |            |
        //    |------------------------ | dst_top    |
        //    | src (moved down) and dst|            |
        //    +-------------------------+ src_bot    |
        //    | dst (still in SR)       |            |
        //    |=========================| dst_bot    |
        //    | (clipped below SR)      |            v
        //    +-------------------------+
        //<
        //    `cols` is always zero in this version of Nvim, and reserved for future
        //    use. 

        trace "editor" "scroll: %A %A %A %A %A %A" top bot left right rows cols

        let copy src dst =
            if src >= 0 && src < grid_size.rows && dst >= 0 && dst < grid_size.rows then
                Array.Copy(grid_buffer, src * grid_size.cols + left, grid_buffer, dst * grid_size.cols + left, right - left)

        if rows > 0 then
            for i = top + rows to bot do
                copy i (i-rows)
                markDirty {row = i - rows; height = 1; col = left; width = right - left }
            //markDirty {row = top; height = bot - top - rows + 1; col = left; width = right - left }
        elif rows < 0 then
            for i = bot + rows - 1 downto top do
                copy i (i-rows)
                markDirty {row = i - rows; height = 1; col = left; width = right - left }
            //markDirty {row = top - rows; height = bot - top - rows + 1; col = left; width = right - left }

    let setOption (opt: UiOption) = 
        trace "setOption" "%A" opt

        let (|FN|_|) (x: string) =
            // try to parse with 'font\ name:hNN'
            match x.Split(':', StringSplitOptions.RemoveEmptyEntries) with
            | [|name; size|] when size.Length > 0 && size.[0] = 'h' -> Some(name.Trim('\'', '"'), size.Substring(1).TrimEnd('\'','"') |> float)
            | _ -> None

        let mutable config_font = true

        match opt with
        | Guifont(FN(name, sz))           -> _guifont     <- name; font_size <- sz
        | GuifontWide(FN(name, sz))       -> _guifontwide <- name; font_size <- sz
        | Guifont("+") | GuifontWide("+") -> font_size    <- font_size + 1.0
        | Guifont("-") | GuifontWide("-") -> font_size    <- font_size - 1.0
        | _                               -> config_font  <- false

        if config_font then fontConfig()


    let setMouse (en:bool) =
        mouse_en <- en

    let hiattrDefine (hls: HighlightAttr[]) =
        Array.iter setHighlight hls

    let redraw(cmd: RedrawCommand) =
        match cmd with
        | UnknownCommand x                                                   -> trace "redraw" "unknown command %A" x
        | HighlightAttrDefine hls                                            -> hiattrDefine hls
        | DefaultColorsSet(fg,bg,sp,_,_)                                     -> setDefaultColors fg bg sp
        | ModeInfoSet(cs_en, info)                                           -> setModeInfo cs_en info
        | ModeChange(name, index)                                            -> changeMode name index
        | GridResize(id, w, h) when id = this.GridId                         -> initBuffer h w
        | GridClear id when id = this.GridId                                 -> clearBuffer()
        | GridLine lines                                                     -> Array.iter (fun (line: GridLine) -> if line.grid = this.GridId then putBuffer line) lines
        | GridCursorGoto(id, row, col) when id = this.GridId                 -> setCursor row col
        | GridDestroy id when id = this.GridId                               -> this.DestroyFramebuffer()
        | GridScroll(id, top,bot,left,right,rows,cols) when id = this.GridId -> scrollBuffer top bot left right rows cols
        | Flush                                                              -> flush() 
        | Bell                                                               -> bell false
        | VisualBell                                                         -> bell true
        | Busy is_busy                                                       -> setBusy is_busy
        | SetTitle title                                                     -> Application.Current.MainWindow.Title <- title
        | SetIcon icon                                                       -> trace "neovim" "icon: %s" icon // TODO
        | SetOption opts                                                     -> Array.iter setOption opts
        | Mouse en                                                           -> setMouse en
        | _                                                                  -> ()

    let toggleFullScreen(gridid: int) =
        if gridid = this.GridId then
            let win = this.GetVisualRoot() :?> Window
            if grid_fullscreen then
                win.WindowState <- WindowState.Normal
                win.HasSystemDecorations <- true
                win.Topmost <- false
                grid_fullscreen <- false
            else
                win.HasSystemDecorations <- false
                win.WindowState <- WindowState.Maximized
                win.Topmost <- true
                grid_fullscreen <- true

    do
        fontConfig()
        AvaloniaXamlLoader.Load(this)
        this.TextInput.Add(fun e -> inputEvent.Trigger <| TextInput e.Text)

    interface IGridUI with
        member this.Id = this.GridId
        member this.GridHeight = int( measured_size.Height / glyph_size.Height )
        member this.GridWidth  = int( measured_size.Width  / glyph_size.Width  )
        member this.Connect redraw_ev fullscreen_ev = 
            redraw_ev.Add (Array.iter redraw)
            fullscreen_ev
            |> Observable.observeOnContext (AvaloniaSynchronizationContext.Current)
            |> Observable.add toggleFullScreen
        member this.Resized = resizeEvent.Publish
        member this.Input = inputEvent.Publish

    member this.OnReady _ =
        is_ready <- true
        measured_size <- this.Bounds.Size
        this.Focus()
        match this.DataContext with
        | :? FVimViewModel as ctx ->
            ctx.OnGridReady(this)
        | _ -> failwithf "%O" this.DataContext

    (*override this.OnTextInput(e) =*)
        (*e.Handled <- true*)
        (*inputEvent.Trigger <| InputEvent.TextInput(e.Text)*)

    override this.OnKeyDown(e) =
        e.Handled <- true
        inputEvent.Trigger <| InputEvent.Key(e.Modifiers, e.Key)

    override this.OnKeyUp(e) =
        e.Handled <- true

    override this.OnPointerPressed(e) =
        if mouse_en then
            let x, y = e.GetPosition this |> getPos
            e.Handled <- true
            mouse_pressed <- e.MouseButton
            inputEvent.Trigger <| InputEvent.MousePress(e.InputModifiers, y, x, e.MouseButton, e.ClickCount)

    override this.OnPointerReleased(e) =
        if mouse_en then
            let x, y = e.GetPosition this |> getPos
            e.Handled <- true
            mouse_pressed <- MouseButton.None
            inputEvent.Trigger <| InputEvent.MouseRelease(e.InputModifiers, y, x, e.MouseButton)

    override this.OnPointerMoved(e) =
        if mouse_en && mouse_pressed <> MouseButton.None then
            let x, y = e.GetPosition this |> getPos
            e.Handled <- true
            if (x,y) <> mouse_pos then
                mouse_pos <- x,y
                inputEvent.Trigger <| InputEvent.MouseDrag(e.InputModifiers, y, x, mouse_pressed)

    override this.OnPointerWheelChanged(e) =
        if mouse_en then
            let x, y = e.GetPosition this |> getPos
            let col, row = int(e.Delta.X), int(e.Delta.Y)
            e.Handled <- true
            inputEvent.Trigger <| InputEvent.MouseWheel(e.InputModifiers, y, x, col, row)

    override this.MeasureOverride(size) =
        let gridui = this :> IGridUI
        let gw, gh = gridui.GridWidth, gridui.GridHeight
        measured_size <- size
        markAllDirty()
        flush()
        let gw', gh' = gridui.GridWidth, gridui.GridHeight
        if gw <> gw' || gh <> gh' then 
            resizeEvent.Trigger(this)
        size

    override this.Render(ctx) =
        if (not is_ready) then this.OnReady()
        let ctx = ctx.PlatformImpl

        let doRenderBuffer() =
            #if USE_FRAMEBUFFER
            if grid_fb <> null then
                let screen_size   = getPoint grid_dirty.height grid_dirty.width
                let screen_origin = getPoint grid_dirty.row grid_dirty.col
                let screen_region = Rect(screen_origin, screen_origin + screen_size)
                let fb_size       = Point(screen_size.X * grid_scale, screen_size.Y * grid_scale)
                let fb_origin     = Point(screen_origin.X * grid_scale, screen_origin.Y * grid_scale)
                let fb_region     = Rect(fb_origin, fb_origin + fb_size)
                
                ctx.DrawImage(grid_fb.PlatformImpl :?> IRef<IBitmapImpl>, 1.0, fb_region, screen_region)
            #else
            for y = grid_dirty.row to grid_dirty.row_end-1 do
                drawBufferLine ctx y grid_dirty.col grid_dirty.col_end
            #endif
            // draw the uncovered parts with default background
            let x1, y1 = float grid_size.cols * glyph_size.Width, float grid_size.rows * glyph_size.Height
            let x2, y2 = this.Bounds.Width, this.Bounds.Height
            let bg = SolidColorBrush(default_bg)
            ctx.FillRectangle(bg, Rect(0.0, y1, x2, y2))
            ctx.FillRectangle(bg, Rect(x1, 0.0, x2, y2))

            grid_flushed <- false
            //markClean()

        #if USE_FRAMEBUFFER
        let ctx' = grid_dc :?> DrawingContextImpl
        #else
        let ctx' = ctx
        #endif

        let doRenderCursor() =
            let mode  = mode_defs.[cursor_modeidx]
            let hlid  = grid_buffer.[cursor_row, cursor_col].hlid
            let hlid  = Option.defaultValue hlid mode.attr_id

            let _, bg, _, _ = getDrawAttrs hlid cursor_row cursor_col
            let origin = getPoint cursor_row cursor_col |> rounding

            let cellw p = min (double(p) / 100.0 * glyph_size.Width)  1.0
            let cellh p = min (double(p) / 100.0 * glyph_size.Height) 5.0

            match mode.cursor_shape, mode.cell_percentage with
            | Some(CursorShape.Block), _ ->
                drawBuffer ctx' cursor_row cursor_col (cursor_col+1) hlid [grid_buffer.[cursor_row, cursor_col].text]
            | Some(CursorShape.Horizontal), Some p ->
                let region = Rect(origin + (getPoint 1 0), origin + (getPoint 1 1) - Point(0.0, cellh p))
                ctx'.Canvas.DrawRect(region.ToSKRect(), bg)
            | Some(CursorShape.Vertical), Some p ->
                // FIXME Point(cellw p, -1.0) to avoid spanning to the next row. 
                // rounding should be implemented
                let region = Rect(origin, origin + (getPoint 1 0) + Point(cellw p, -1.0))
                ctx'.Canvas.DrawRect(region.ToSKRect(), bg)
            | _ -> ()

        // do not actually draw the buffer unless there's a pending flush command
        if grid_flushed then doRenderBuffer()
        if cursor_en && cursor_show then doRenderCursor()
    
    member this.DestroyFramebuffer() =
        #if USE_FRAMEBUFFER
        if grid_fb <> null then
            grid_dc.Dispose()
            grid_dc <- null
            grid_fb.Dispose()
            grid_fb <- null
        #else
        ()
        #endif

    member val GridId: int = 0 with get, set

    static member GridIdProperty = 
        AvaloniaProperty.RegisterDirect<Editor, int>(
            "GridId", 
            (fun e -> e.GridId),
            (fun e v -> e.GridId <- v))

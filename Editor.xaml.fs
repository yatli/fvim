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
open Avalonia.Data


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
    inherit Canvas()

    let mutable default_fg       = Colors.Black
    let mutable default_bg       = Colors.Black
    let mutable default_sp       = Colors.Black

    let mutable hi_defs          = Array.zeroCreate<HighlightAttr>(256)
    let mutable mode_defs        = Array.empty<ModeInfo>

    let mutable _guifont         = "Iosevka Slab"
    let mutable _guifontwide     = "DengXian"

    let mutable font_size        = 16.0
    let mutable glyph_size       = Size(1.0, 1.0)

    let mutable grid_size        = { rows = 100; cols=50 }
    let mutable grid_scale       = 1.0
    let mutable grid_fullscreen  = false
#if USE_FRAMEBUFFER
    let mutable grid_fb: RenderTargetBitmap  = null
    let mutable grid_dc: IDrawingContextImpl = null
#endif
    let mutable grid_flushed     = false
    let mutable grid_buffer      = Array2D.create grid_size.rows grid_size.cols GridBufferCell.empty
    let mutable grid_dirty       = { row = 0; col = 0; height = 100; width = 50 }

    let mutable cursor_modeidx   = -1
    let mutable cursor_row       = 0
    let mutable cursor_col       = 0
    let mutable cursor_info = CursorInfo.Default

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

        fg, bg, sp, attrs

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

        let attrs = hi_defs.[hlid].rgb_attr
        let typeface = GetTypeface(List.head str, attrs.italic, attrs.bold, _guifont, _guifontwide)
        let _fg, _bg, _sp, attrs = getDrawAttrs hlid row col

        use fg = GetForegroundBrush(_fg, typeface, font_size)
        use bg = new SKPaint(Color = _bg.ToSKColor())
        use sp = new SKPaint(Color = _sp.ToSKColor())

        let nr_col = 
            match wswidth grid_buffer.[row, colend - 1].text with
            | CharType.Wide | CharType.Nerd | CharType.Emoji -> colend - col + 1
            | _ -> colend - col

        let topLeft      = getPoint row col
        let bottomRight  = (topLeft + getPoint 1 nr_col) |> rounding
        let bg_region    = Rect(topLeft , bottomRight)

        RenderText(ctx, bg_region, fg, bg, sp, attrs.underline, attrs.undercurl, String.Concat str)

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
        paint.Typeface <- GetTypeface(str, false, true, _guifont, _guifontwide)
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
        // It turns out the space " " advances farest...
        // So we measure it as the width.
        let w, h = measureText " "
        glyph_size <- Size(float w, float h)
        trace "fontConfig" "guifont=%s guifontwide=%s size=%A" _guifont _guifontwide glyph_size
        if is_ready then
            this.cursorConfig()
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
        this.setCursorEnabled cs_en

    let cursorGoto row col =
        cursor_row <- row
        cursor_col <- col
        this.cursorConfig()

    let changeMode (name: string) (index: int) = 
        cursor_modeidx <- index
        this.cursorConfig()

    let bell (visual: bool) =
        // TODO
        trace "neovim" "bell: %A" visual
        ()

    let setBusy (v: bool) =
        trace "neovim" "busy: %A" v
        this.setCursorEnabled <| not v
        //if v then this.Cursor <- Cursor(StandardCursorType.Wait)
        //else this.Cursor <- Cursor(StandardCursorType.Arrow)
        //flush()

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
        | GridCursorGoto(id, row, col) when id = this.GridId                 -> cursorGoto row col
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

    //each event repeats 4 times... use the event instead
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
        let size = getPoint grid_size.rows grid_size.cols

        let doRenderBuffer() =
            #if USE_FRAMEBUFFER
            if grid_fb <> null then
                // partial update:
                //let screen_size   = getPoint grid_dirty.height grid_dirty.width
                //let screen_origin = getPoint grid_dirty.row grid_dirty.col
                //let screen_region = Rect(screen_origin, screen_origin + screen_size)
                //let fb_size       = Point(screen_size.X * grid_scale, screen_size.Y * grid_scale)
                //let fb_origin     = Point(screen_origin.X * grid_scale, screen_origin.Y * grid_scale)
                //let fb_region     = Rect(fb_origin, fb_origin + fb_size)

                // full update:
                let screen_region = Rect(0.0, 0.0, size.X, size.Y)
                let fb_region = Rect(grid_fb.Size)
                
                ctx.DrawImage(grid_fb.PlatformImpl :?> IRef<IBitmapImpl>, 1.0, fb_region, screen_region, Avalonia.Visuals.Media.Imaging.BitmapInterpolationMode.LowQuality)
            #else
            for y = grid_dirty.row to grid_dirty.row_end-1 do
                drawBufferLine ctx y grid_dirty.col grid_dirty.col_end
            #endif
            // draw the uncovered parts with default background
            let x1, y1 = size.X, size.Y
            let x2, y2 = this.Bounds.Width, this.Bounds.Height
            let bg = SolidColorBrush(default_bg)
            ctx.FillRectangle(bg, Rect(0.0, y1, x2, y2))
            ctx.FillRectangle(bg, Rect(x1, 0.0, x2, y2))

            grid_flushed <- false
            markClean()

        #if USE_FRAMEBUFFER
        let ctx' = grid_dc :?> DrawingContextImpl
        #else
        let ctx' = ctx
        #endif

        // do not actually draw the buffer unless there's a pending flush command
        if grid_flushed then doRenderBuffer()
    
    member __.DestroyFramebuffer() =
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

    member this.setCursorEnabled v =
        cursor_info <- {cursor_info with enabled = v}
        this.cursorConfig()

    member this.cursorConfig() =
        async {
            if not is_ready || mode_defs.Length = 0 || cursor_modeidx < 0 then return ()
            else
            let mode  = mode_defs.[cursor_modeidx]
            let hlid  = grid_buffer.[cursor_row, cursor_col].hlid
            let hlid  = Option.defaultValue hlid mode.attr_id
            let fg, bg, sp, attrs = getDrawAttrs hlid cursor_row cursor_col
            let origin = getPoint cursor_row cursor_col |> rounding
            let size = getPoint 1 1
            let on, off, wait =
                match mode with
                | { blinkon = Some on; blinkoff = Some off; blinkwait = Some wait  }
                    when on > 0 && off > 0 && wait > 0 -> on, off, wait
                | _ -> 0,0,0

            let updated_ci = 
                { 
                    typeface  = _guifont
                    wtypeface = _guifontwide
                    fontSize  = font_size
                    text      = grid_buffer.[cursor_row, cursor_col].text
                    fg        = fg
                    bg        = bg
                    sp        = sp
                    underline = attrs.underline
                    undercurl = attrs.undercurl
                    bold      = attrs.bold
                    italic    = attrs.italic
                    cellPercentage = Option.defaultValue 100 mode.cell_percentage
                    w         = size.X
                    h         = size.Y
                    x         = origin.X
                    y         = origin.Y
                    blinkon   = on
                    blinkoff  = off
                    blinkwait = wait
                    shape     = cursor_info.shape
                    enabled   = cursor_info.enabled
                }
            ignore <| Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                fun () -> 
                    printfn "set cursor info"
                    this.SetValue(Editor.CursorInfoProperty, updated_ci)
            )
        } |> Async.RunSynchronously

    static member CursorInfoProperty = AvaloniaProperty.Register<Editor, CursorInfo>("CursorInfo", CursorInfo.Default)

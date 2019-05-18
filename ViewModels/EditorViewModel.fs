namespace FVim

open log
open ui
open wcwidth
open neovim.def
open neovim.rpc

open Avalonia
open Avalonia.Input
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform
open Avalonia.Threading
open FSharp.Control.Reactive
open ReactiveUI

open System
open System.Collections.ObjectModel
open Avalonia.Controls
open System.Reactive.Disposables

[<Struct>]
type private GridBufferCell =
    {
        mutable text:  string
        mutable hlid:  int32
    } 
    with static member empty = { text  = " "; hlid = 0 }

[<Struct>]
type GridSize =
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

[<AutoOpen>]
module private helpers =
    let _d x = Option.defaultValue x

type EmbeddedEditorViewModel(child, X, Y, W, H) = 
    inherit ViewModelBase()

    let mutable m_x = X
    let mutable m_y = Y
    let mutable m_w = W
    let mutable m_h = H

    member this.child: EditorViewModel = child
    member this.X with get(): float = m_x and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_x, v)
    member this.Y with get(): float = m_y and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_y, v)
    member this.W with get(): float = m_w and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_w, v)
    member this.H with get(): float = m_h and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_h, v)

and EditorViewModel(GridId: int, ?parent: EditorViewModel, ?_gridsize: GridSize, ?_glyphsize: Size, ?_measuredsize: Size, ?_fontsize: float, ?_gridscale: float,
                    ?_hldefs: HighlightAttr[], ?_modedefs: ModeInfo[], ?_guifont: string, ?_guifontwide: string, ?_cursormode: int) as this =
    inherit ViewModelBase()

    let trace fmt = trace (sprintf "editorvm #%d" GridId) fmt

    let mutable disposed         = false
    let mutable grid_fb: RenderTargetBitmap  = null
    let mutable grid_dc: IDrawingContextImpl = null
    let mutable cursor_info = new CursorViewModel()
    let mutable cursor_modeidx   = _d -1 _cursormode
    let mutable cursor_row       = 0
    let mutable cursor_col       = 0
    let mutable cursor_en        = true
    let mutable cursor_ingrid    = true

    let mutable mouse_en         = true
    let mutable mouse_pressed    = MouseButton.None
    let mutable mouse_pos        = 0,0

    let mutable default_fg       = Colors.White
    let mutable default_bg       = Colors.Black
    let mutable default_sp       = Colors.Red

    let mutable hi_defs          = match _hldefs with 
                                   | None -> Array.create<HighlightAttr> 256 HighlightAttr.Default
                                   | Some arr -> arr.Clone() :?> HighlightAttr[]
    let mutable mode_defs        = match _modedefs with
                                   | None -> Array.empty<ModeInfo>
                                   | Some arr -> arr.Clone() :?> ModeInfo[]

    let mutable _guifont         = _d "Iosevka Slab" _guifont
    let mutable _guifontwide     = _d "DengXian" _guifontwide

    let mutable font_size        = _d 16.0 _fontsize
    let mutable glyph_size       = _d (Size(10.0, 10.0)) _glyphsize

    let mutable grid_size        = _d { rows = 10; cols= 10 } _gridsize
    let mutable grid_scale       = _d 1.0 _gridscale
    let mutable grid_fullscreen  = false
    let mutable grid_rendertick  = 0
    let mutable measured_size    = _d (Size(100.0, 100.0)) _measuredsize
    let mutable grid_buffer      = Array2D.create grid_size.rows grid_size.cols GridBufferCell.empty
    let mutable grid_dirty       = { row = 0; col = 0; height = grid_size.rows; width = grid_size.cols }

    let child_grids = ObservableCollection<EmbeddedEditorViewModel>()
    let resizeEvent = Event<IGridUI>()
    let inputEvent  = Event<InputEvent>()

    let toggleFullScreen(gridid: int) =
        if gridid = GridId then
            trace "ToggleFullScreen"
            this.Fullscreen <- not this.Fullscreen

    //  converts grid position to UI Point
    let getPoint row col =
        Point(double(col) * glyph_size.Width, double(row) * glyph_size.Height)

    let getPos (p: Point) =
        int(p.X / glyph_size.Width), int(p.Y / glyph_size.Height)

    let getDrawAttrs hlid = 
        let attrs = hi_defs.[hlid].rgb_attr

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

        let attrs = hi_defs.[hlid].rgb_attr
        let typeface = GetTypeface(List.head str, attrs.italic, attrs.bold, _guifont, _guifontwide)
        let _fg, bg, sp, attrs = getDrawAttrs hlid 

        use fg = GetForegroundBrush(_fg, typeface, font_size)

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
        if grid_dc <> null then
            for y = region.row to region.row_end - 1 do
                drawBufferLine grid_dc y region.col region.col_end

    let markAllDirty () =
        grid_dirty   <- { row = 0; col = 0; height = grid_size.rows; width = grid_size.cols }
        if grid_dc <> null then
            for y = 0 to grid_size.rows - 1 do
                drawBufferLine grid_dc y 0 grid_size.cols

    let markClean () =
        grid_dirty <- { row = 0; col = 0; height = 0; width = 0}

    let flush() = 
        trace "flush."
        this.RenderTick <- this.RenderTick + 1
        markClean()

    let fontConfig() =
        font_size <- max font_size 1.0
        // It turns out the space " " advances farest...
        // So we measure it as the width.
        let w, h = MeasureText(" ", _guifont, _guifontwide, font_size)
        glyph_size <- Size(float w, float h)
        trace "fontConfig: guifont=%s guifontwide=%s size=%A" _guifont _guifontwide glyph_size
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
            this.RaisePropertyChanged("BackgroundBrush")
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
        
    let clearBuffer () =
        trace "RenderScaling is %f" grid_scale

        this.DestroyFramebuffer()
        let size          = getPoint grid_size.rows grid_size.cols
        grid_fb          <- AllocateFramebuffer size.X size.Y grid_scale
        grid_dc          <- grid_fb.CreateDrawingContext(null)
        grid_buffer      <- Array2D.create grid_size.rows grid_size.cols GridBufferCell.empty
        // notify buffer update and size change
        this.RaisePropertyChanged("FrameBuffer")
        this.RaisePropertyChanged("BufferHeight")
        this.RaisePropertyChanged("BufferWidth")

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
        // if the buffer under cursor is updated, also notify the cursor view model
        if row = cursor_row && line.col_start <= cursor_col && cursor_col < col
        then this.cursorConfig()
        //trace "redraw" "putBuffer: writing to %A" dirty
        // italic font artifacts I: remainders after scrolling and redrawing the dirty part
        // workaround: extend the dirty region one cell further towards the end

        // italic font artifacts II: when inserting on an italic line, later glyphs cover earlier with the background.
        // potential workaround 1: redraw the whole line...
        // potential workaround 2: do not draw the background for the latest glyph

        // italic font artifacts III: block cursor may not have italic style. 
        // how to fix this? curious about how the original GVim handles this situation.

        // apply workaround I:
        let dirty = {dirty with width = min (dirty.width + 1) grid_size.cols}
        markDirty dirty

    let setModeInfo (cs_en: bool) (info: ModeInfo[]) =
        mode_defs <- info
        this.setCursorEnabled cs_en

    let cursorGoto id row col =
        cursor_ingrid <- (id = GridId)
        cursor_row <- row
        cursor_col <- col
        this.cursorConfig()

    let changeMode (name: string) (index: int) = 
        cursor_modeidx <- index
        this.cursorConfig()

    let bell (visual: bool) =
        // TODO
        trace "neovim: bell: %A" visual
        ()

    let setBusy (v: bool) =
        trace "neovim: busy: %A" v
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

        trace "scroll: %A %A %A %A %A %A" top bot left right rows cols

        let copy src dst =
            if src >= 0 && src < grid_size.rows && dst >= 0 && dst < grid_size.rows then
                Array.Copy(grid_buffer, src * grid_size.cols + left, grid_buffer, dst * grid_size.cols + left, right - left)
                markDirty {row = dst; height = 1; col = left; width = right - left }

        if rows > 0 then
            for i = top + rows to bot do
                copy i (i-rows)
        elif rows < 0 then
            for i = bot + rows - 1 downto top do
                copy i (i-rows)

    let setOption (opt: UiOption) = 
        trace "setOption: %A" opt

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

    let setWinPos grid win startrow startcol w h =
        trace "setWinPos: grid = %A, win = %A, startrow = %A, startcol = %A, w = %A, h = %A" grid win startrow startcol w h
        let existing =  child_grids 
                     |> Seq.indexed
                     |> Seq.tryPick (function | (i, a) when (a.child :> IGridUI).Id = grid  -> Some(i, a.child)
                                              | _ -> None)
        let origin = getPoint startrow startcol
        let child_size = getPoint h w
        trace "setWinPos: child will be positioned at %A" origin
        match existing with
        | Some(i, child) -> 
            (* manually resize the child grid as per neovim docs *)
            child.initBuffer h w
            child_grids.[i] <- new EmbeddedEditorViewModel(child,origin.X,origin.Y, child_size.X, child_size.Y)
        | None -> 
            let child = new EditorViewModel(grid, this, {rows=h; cols=w}, glyph_size, Size(child_size.X, child_size.Y), font_size, grid_scale, hi_defs, mode_defs, _guifont, _guifontwide, cursor_modeidx)
            let anchor = new EmbeddedEditorViewModel(child, origin.X,origin.Y, child_size.X, child_size.Y)
            child_grids.Add <| anchor
            //let wnd = Window()
            //wnd.Height  <- child_size.Y
            //wnd.Width   <- child_size.X
            //wnd.Content <- anchor.child
            //wnd.Show()

    let redraw(cmd: RedrawCommand) =
        match cmd with
        | UnknownCommand x                                                   -> trace "unknown command %A" x
        | HighlightAttrDefine hls                                            -> hiattrDefine hls
        | DefaultColorsSet(fg,bg,sp,_,_)                                     -> setDefaultColors fg bg sp
        | ModeInfoSet(cs_en, info)                                           -> setModeInfo cs_en info
        | ModeChange(name, index)                                            -> changeMode name index
        | GridResize(id, w, h) when id = GridId                              -> this.initBuffer h w
        | GridClear id when id = GridId                                      -> clearBuffer()
        | GridLine lines                                                     -> Array.iter (fun (line: GridLine) -> if line.grid = GridId then putBuffer line) lines
        | GridCursorGoto(id, row, col)                                       -> cursorGoto id row col
        | GridDestroy id when id = GridId                                    -> this.DestroyFramebuffer()
        | GridScroll(id, top,bot,left,right,rows,cols) when id = GridId      -> scrollBuffer top bot left right rows cols
        | Flush                                                              -> flush() 
        | Bell                                                               -> bell false
        | VisualBell                                                         -> bell true
        | Busy is_busy                                                       -> setBusy is_busy
        | SetTitle title                                                     -> Application.Current.MainWindow.Title <- title
        | SetIcon icon                                                       -> trace "icon: %s" icon // TODO
        | SetOption opts                                                     -> Array.iter setOption opts
        | Mouse en                                                           -> setMouse en
        | WinPos(grid, win, startrow, startcol, w, h) when GridId = 1        -> setWinPos grid win startrow startcol w h
        | _ -> ()

    do
        let fg,bg,sp,attr = getDrawAttrs 0
        default_bg <- bg
        default_fg <- fg
        default_sp <- sp
        fontConfig()
        this.Watch [
            Model.Redraw (Array.iter redraw)
            Model.Notify "ToggleFullScreen" (fun [| Integer32(gridid) |] -> toggleFullScreen gridid )
        ] 

    member private __.initBuffer nrow ncol =
        grid_size <- { rows = nrow; cols = ncol }
        trace "buffer resize = %A" grid_size
        clearBuffer()

    interface IGridUI with
        member __.Id = GridId
        member __.GridHeight = int( measured_size.Height / glyph_size.Height )
        member __.GridWidth  = int( measured_size.Width  / glyph_size.Width  )
        member __.Resized = resizeEvent.Publish
        member __.Input = inputEvent.Publish

    member __.DestroyFramebuffer() =
        if grid_fb <> null then
            grid_dc.Dispose()
            grid_dc <- null
            grid_fb.Dispose()
            grid_fb <- null

    member __.cursorConfig() =
        async {
            if mode_defs.Length = 0 || cursor_modeidx < 0 then return ()
            elif grid_buffer.GetLength(0) <= cursor_row || grid_buffer.GetLength(1) <= cursor_col then return()
            else
            let mode  = mode_defs.[cursor_modeidx]
            let hlid  = grid_buffer.[cursor_row, cursor_col].hlid
            let hlid  = Option.defaultValue hlid mode.attr_id
            let fg, bg, sp, attrs = getDrawAttrs hlid 
            let origin = getPoint cursor_row cursor_col 
            let text = grid_buffer.[cursor_row, cursor_col].text
            let text_type = wswidth text
            let width = float(CharTypeWidth text_type) * glyph_size.Width

            let on, off, wait =
                match mode with
                | { blinkon = Some on; blinkoff = Some off; blinkwait = Some wait  }
                    when on > 0 && off > 0 && wait > 0 -> on, off, wait
                | _ -> 0,0,0

            cursor_info.typeface       <- _guifont
            cursor_info.wtypeface      <- _guifontwide
            cursor_info.fontSize       <- font_size
            cursor_info.text           <- text
            cursor_info.fg             <- fg
            cursor_info.bg             <- bg
            cursor_info.sp             <- sp
            cursor_info.underline      <- attrs.underline
            cursor_info.undercurl      <- attrs.undercurl
            cursor_info.bold           <- attrs.bold
            cursor_info.italic         <- attrs.italic
            cursor_info.cellPercentage <- Option.defaultValue 100 mode.cell_percentage
            cursor_info.w              <- width
            cursor_info.h              <- glyph_size.Height
            cursor_info.x              <- origin.X
            cursor_info.y              <- origin.Y
            cursor_info.blinkon        <- on
            cursor_info.blinkoff       <- off
            cursor_info.blinkwait      <- wait
            cursor_info.shape          <- Option.defaultValue CursorShape.Block mode.cursor_shape
            cursor_info.enabled        <- cursor_en
            cursor_info.ingrid         <- cursor_ingrid
            cursor_info.RenderTick     <- cursor_info.RenderTick + 1
            trace "set cursor info"
        } |> Async.RunSynchronously

    member this.setCursorEnabled v =
        cursor_en <- v
        this.cursorConfig()

    (*******************   Exposed properties   ***********************)

    member this.FrameBuffer
        with get() : RenderTargetBitmap = grid_fb
        and set(v) =
            ignore <| this.RaiseAndSetIfChanged(&grid_fb, v)

    member this.Fullscreen
        with get() : bool = grid_fullscreen
        and set(v) =
            ignore <| this.RaiseAndSetIfChanged(&grid_fullscreen, v)

    member this.RenderTick
        with get() : int = grid_rendertick
        and set(v) =
            ignore <| this.RaiseAndSetIfChanged(&grid_rendertick, v)

    member this.CursorInfo
        with get() : CursorViewModel = cursor_info
        and set(v) =
            ignore <| this.RaiseAndSetIfChanged(&cursor_info, v)

    member __.RenderScale
        with get() : float = grid_scale
        and set(v) =
            grid_scale <- v

    member __.BackgroundBrush
        with get(): SolidColorBrush = SolidColorBrush(default_bg)

    member __.BufferHeight with get(): float = if grid_fb <> null then grid_fb.Size.Height else 10.0
    member __.BufferWidth  with get(): float = if grid_fb <> null then grid_fb.Size.Width else 10.0
    member __.TopLevel     with get(): bool  = parent.IsNone

    member this.MeasuredSize
        with get() : Size = measured_size
        and set(v) =
            trace "set measured size: %A" v
            let gridui = this :> IGridUI
            let gw, gh = gridui.GridWidth, gridui.GridHeight
            measured_size <- v
            let gw', gh' = gridui.GridWidth, gridui.GridHeight
            if gw <> gw' || gh <> gh' then 
                resizeEvent.Trigger(this)

    member __.ChildGrids = child_grids

    (*******************   Events   ***********************)

    member __.OnKey (e: KeyEventArgs) = 
        e.Handled <- true
        inputEvent.Trigger <| InputEvent.Key(e.Modifiers, e.Key)

    member __.OnMouseDown (e: PointerPressedEventArgs) (view: Visual) = 
        if mouse_en then
            let x, y = e.GetPosition view |> getPos
            e.Handled <- true
            mouse_pressed <- e.MouseButton
            inputEvent.Trigger <| InputEvent.MousePress(e.InputModifiers, y, x, e.MouseButton, e.ClickCount)

    member __.OnMouseUp (e: PointerReleasedEventArgs) (view: Visual) = 
        if mouse_en then
            let x, y = e.GetPosition view |> getPos
            e.Handled <- true
            mouse_pressed <- MouseButton.None
            inputEvent.Trigger <| InputEvent.MouseRelease(e.InputModifiers, y, x, e.MouseButton)

    member __.OnMouseMove (e: PointerEventArgs) (view: Visual) = 
        if mouse_en && mouse_pressed <> MouseButton.None then
            let x, y = e.GetPosition view |> getPos
            e.Handled <- true
            if (x,y) <> mouse_pos then
                mouse_pos <- x,y
                inputEvent.Trigger <| InputEvent.MouseDrag(e.InputModifiers, y, x, mouse_pressed)

    member __.OnMouseWheel (e: PointerWheelEventArgs) (view: Visual) = 
        if mouse_en then
            let x, y = e.GetPosition view |> getPos
            let col, row = int(e.Delta.X), int(e.Delta.Y)
            e.Handled <- true
            inputEvent.Trigger <| InputEvent.MouseWheel(e.InputModifiers, y, x, col, row)

    member __.OnTextInput (e: TextInputEventArgs) = 
        inputEvent.Trigger <| InputEvent.TextInput(e.Text)


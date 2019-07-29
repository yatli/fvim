namespace FVim

open log
open ui
open wcwidth
open neovim.def

open ReactiveUI
open Avalonia
open Avalonia.Input
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform
open Avalonia.Threading
open Avalonia.Skia
open FSharp.Control.Reactive

open System
open System.Collections.ObjectModel
open Avalonia.Controls
open System.Reactive.Disposables
open SkiaSharp

[<AutoOpen>]
module private helpers =
    let _d x = Option.defaultValue x

type EditorViewModel(GridId: int, ?parent: EditorViewModel, ?_gridsize: GridSize, ?_glyphsize: Size, ?_measuredsize: Size, ?_fontsize: float, ?_gridscale: float,
                    ?_hldefs: HighlightAttr[], ?_modedefs: ModeInfo[], ?_guifont: string, ?_guifontwide: string, ?_cursormode: int, ?_anchorX: float, ?_anchorY: float) as this =
    inherit ViewModelBase()

    let trace fmt = trace (sprintf "editorvm #%d" GridId) fmt

    let mutable _busy            = false
    let mutable cursor_info      = new CursorViewModel()
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

    let mutable _guifont         = _d DefaultFont     _guifont
    let mutable _guifontwide     = _d DefaultFontWide _guifontwide

    let mutable font_size        = _d 16.0 _fontsize
    let mutable glyph_size       = _d (Size(10.0, 10.0)) _glyphsize

    let mutable grid_size        = _d { rows = 10; cols= 10 } _gridsize
    let mutable grid_scale       = _d 1.0 _gridscale
    let mutable grid_fullscreen  = false
    let mutable grid_rendertick  = 0
    let mutable measured_size    = _d (Size(100.0, 100.0)) _measuredsize
    let mutable grid_buffer      = Array2D.create grid_size.rows grid_size.cols GridBufferCell.empty
    let mutable grid_dirty       = GridRegion()
    let mutable _fb_h            = 10.0
    let mutable _fb_w            = 10.0
    let mutable anchor_x         = _d 0.0 _anchorX
    let mutable anchor_y         = _d 0.0 _anchorY

    let child_grids = ObservableCollection<EditorViewModel>()
    let resizeEvent = Event<IGridUI>()
    let inputEvent  = Event<int*InputEvent>()
    let hlchangeEvent = Event<unit>()

    let toggleFullScreen(gridid: int) =
        if gridid = GridId then
            trace "ToggleFullScreen"
            this.Fullscreen <- not this.Fullscreen

    let getPos (p: Point) =
        int(p.X / glyph_size.Width), int(p.Y / glyph_size.Height)

    let markDirty = grid_dirty.Union

    let markAllDirty () =
        grid_dirty.Clear()
        grid_dirty.Union{ row = 0; col = 0; height = grid_size.rows; width = grid_size.cols }

    let flush() = 
        trace "flush."
        this.RenderTick <- this.RenderTick + 1

    let fontConfig() =
        font_size <- max font_size 1.0
        // It turns out the space " " advances farest...
        // So we measure it as the width.
        let s, w, h = MeasureText(" ", _guifont, _guifontwide, font_size, grid_scale)
        glyph_size <- Size(w, h)
        font_size <- s

        trace "fontConfig: guifont=%s guifontwide=%s size=%A" _guifont _guifontwide glyph_size
        this.cursorConfig()
        markAllDirty()
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
        hlchangeEvent.Trigger()

    let setDefaultColors fg bg sp = 

        let bg = 
            if fg = bg && bg = sp then GetReverseColor bg
            else bg

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
        trace "setDefaultColors: %A %A %A" fg bg sp

    let clearBuffer () =
        grid_buffer <- Array2D.create grid_size.rows grid_size.cols GridBufferCell.empty
        // notify buffer update and size change
        let size: Point = this.GetPoint grid_size.rows grid_size.cols
        _fb_w <- size.X
        _fb_h <- size.Y
        this.RaisePropertyChanged("BufferHeight")
        this.RaisePropertyChanged("BufferWidth")

    let putBuffer (line: GridLine) =
        let         row  = line.row
        let mutable col  = line.col_start
        let mutable hlid = 0
        let mutable rep = 1
        for cell in line.cells do
            hlid <- Option.defaultValue hlid cell.hl_id
            rep  <- Option.defaultValue 1 cell.repeat
            for _i = 1 to rep do
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
        // workaround: if italic, extend the dirty region towards the beginning, until not italic

        // italic font artifacts III: block cursor may not have italic style. 
        // how to fix this? curious about how the original GVim handles this situation.

        // ligature artifacts I: ligatures do not build as characters are laid down.
        // workaround: like italic, case II.

        // apply workaround I:
        let dirty = {dirty with width = min (dirty.width + 1) grid_size.cols }
        // apply workaround II:
        col  <- dirty.col - 1
        let mutable italic = true
        let mutable ligature = true
        while col > 0 && (italic || ligature) do
            hlid <- grid_buffer.[row, col].hlid
            col <- col - 1
            ligature <- isProgrammingSymbol grid_buffer.[row, col].text
            italic <- hi_defs.[hlid].rgb_attr.italic 
        let dirty = {dirty with width = dirty.width + (dirty.col - col); col = col }
        

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
        _busy <- v
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

        if top <= cursor_row 
           && cursor_row <= bot 
           && left <= cursor_col 
           && cursor_col <= right
        then
            this.cursorConfig()

    let setOption (opt: UiOption) = 
        trace "setOption: %A" opt

        let (|FN|_|) (x: string) =
            // try to parse with 'font\ name:hNN'
            match x.Split(':') with
            | [|name; size|] when size.Length > 0 && size.[0] = 'h' -> Some(name.Trim('\'', '"'), size.Substring(1).TrimEnd('\'','"') |> float)
            | _ -> None

        let mutable config_font = true

        match opt with
        | Guifont(FN(name, sz))             -> _guifont     <- name; font_size <- sz
        | GuifontWide(FN(name, sz))         -> _guifontwide <- name; font_size <- sz
        | Guifont("+") | GuifontWide("+")   -> font_size    <- font_size + 1.0
        | Guifont("-") | GuifontWide("-")   -> font_size    <- font_size - 1.0
        | Guifont(".+") | GuifontWide(".+") -> font_size    <- font_size + 0.1
        | Guifont(".-") | GuifontWide(".-") -> font_size    <- font_size - 0.1
        | _                                 -> config_font  <- false

        if config_font then fontConfig()

    let setMouse (en:bool) =
        mouse_en <- en

    let hiattrDefine (hls: HighlightAttr[]) =
        Array.iter setHighlight hls

    let setWinPos grid win startrow startcol w h =
        trace "setWinPos: grid = %A, win = %A, startrow = %A, startcol = %A, w = %A, h = %A" grid win startrow startcol w h
        let existing =  child_grids 
                     |> Seq.indexed
                     |> Seq.tryPick (function | (i, a) when (a :> IGridUI).Id = grid  -> Some(i, a)
                                              | _ -> None)
        let origin: Point = this.GetPoint startrow startcol
        let child_size    = this.GetPoint h w
        trace "setWinPos: child will be positioned at %A" origin
        match existing with
        | Some(i, child) -> 
            (* manually resize and position the child grid as per neovim docs *)
            child.initBuffer h w
            child.AnchorX <- origin.X
            child.AnchorY <- origin.Y
        | None -> 
            let child = new EditorViewModel(grid, this, {rows=h; cols=w}, glyph_size, Size(child_size.X, child_size.Y), font_size, grid_scale, hi_defs, mode_defs, _guifont, _guifontwide, cursor_modeidx, origin.X, origin.Y)
            child_grids.Add child
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
        | GridDestroy id when id = GridId                                    -> ()
        | GridScroll(id, top,bot,left,right,rows,cols) when id = GridId      -> scrollBuffer top bot left right rows cols
        | Flush                                                              -> flush() 
        | Bell                                                               -> bell false
        | VisualBell                                                         -> bell true
        | Busy is_busy                                                       -> setBusy is_busy
        | SetTitle title                                                     -> Model.appLifetime.MainWindow.Title <- title
        | SetIcon icon                                                       -> trace "icon: %s" icon // TODO
        | SetOption opts                                                     -> Array.iter setOption opts
        | Mouse en                                                           -> setMouse en
        | WinPos(grid, win, startrow, startcol, w, h) when GridId = 1        -> setWinPos grid win startrow startcol w h
        | _ -> ()

    let raiseInputEvent e =
        inputEvent.Trigger(GridId, e)

    do
        let fg,bg,sp,_ = this.GetDrawAttrs 0
        default_bg <- bg
        default_fg <- fg
        default_sp <- sp
        fontConfig()
        this.Watch [
            Model.Redraw (Array.iter redraw)

            Model.Notify "ToggleFullScreen" (fun [| Integer32(gridid) |] -> toggleFullScreen gridid )

            hlchangeEvent.Publish 
            |> Observable.throttle(TimeSpan.FromMilliseconds 100.0) 
            |> Observable.subscribe (fun () -> markAllDirty())
        ] 

    member __.Item with get(row, col) = grid_buffer.[row, col]

    member __.Cols with get() = grid_size.cols

    member __.Rows with get() = grid_size.rows

    member __.Dirty with get() = grid_dirty

    member __.GetFontAttrs() =
        _guifont, _guifontwide, font_size

    member __.GetDrawAttrs hlid = 
        let attrs = hi_defs.[hlid].rgb_attr

        let mutable fg = Option.defaultValue default_fg attrs.foreground
        let mutable bg = Option.defaultValue default_bg attrs.background
        let mutable sp = Option.defaultValue default_sp attrs.special

        if attrs.reverse then
            fg <- GetReverseColor fg
            bg <- GetReverseColor bg
            sp <- GetReverseColor sp

        fg, bg, sp, attrs


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
        member __.HasChildren = child_grids.Count <> 0

    member __.markClean = grid_dirty.Clear

    //  converts grid position to UI Point
    member __.GetPoint row col =
        Point(double(col) * glyph_size.Width, double(row) * glyph_size.Height)

    member __.cursorConfig() =
        if mode_defs.Length = 0 || cursor_modeidx < 0 then ()
        elif grid_buffer.GetLength(0) <= cursor_row || grid_buffer.GetLength(1) <= cursor_col then ()
        else
        let mode              = mode_defs.[cursor_modeidx]
        let hlid              = grid_buffer.[cursor_row, cursor_col].hlid
        let hlid              = Option.defaultValue hlid mode.attr_id
        let fg, bg, sp, attrs = this.GetDrawAttrs hlid
        let origin            = this.GetPoint cursor_row cursor_col
        let text              = grid_buffer.[cursor_row, cursor_col].text
        let text_type         = wswidth text
        let width             = float(max <| 1 <| CharTypeWidth text_type) * glyph_size.Width

        let on, off, wait =
            match mode with
            | { blinkon = Some on; blinkoff = Some off; blinkwait = Some wait  }
                when on > 0 && off > 0 && wait > 0 -> on, off, wait
            | _ -> 0,0,0

        // do not use the default colors for cursor
        let colorf = if hlid = 0 then GetReverseColor else id
        let fg, bg, sp = colorf fg, colorf bg, colorf sp
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
        trace "set cursor info, color = %A %A %A" fg bg sp

    member this.setCursorEnabled v =
        cursor_en <- v
        this.cursorConfig()

    (*******************   Exposed properties   ***********************)

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

    member __.BufferHeight with get(): float = _fb_h
    member __.BufferWidth  with get(): float = _fb_w
    member __.GlyphHeight with get(): float = glyph_size.Height
    member __.GlyphWidth with get(): float = glyph_size.Width
    member __.TopLevel     with get(): bool  = parent.IsNone

    member __.AnchorX
        with get() : float = anchor_x
        and set(v) = anchor_x <- v

    member __.AnchorY
        with get() : float = anchor_y
        and set(v) = anchor_y <- v

    member this.MeasuredSize
        with get() : Size = measured_size
        and set(v) =
            trace "set measured size: %A" v
            let gridui = this :> IGridUI
            let gw, gh = gridui.GridWidth, gridui.GridHeight
            measured_size <- v
            let gw', gh' = gridui.GridWidth, gridui.GridHeight
            if gw <> gw' || gh <> gh' then 
                if this.TopLevel then
                    resizeEvent.Trigger(this)

    member __.ChildGrids = child_grids

    (*******************   Events   ***********************)

    member __.OnKey (e: KeyEventArgs) = 
        e.Handled <- true
        raiseInputEvent <| InputEvent.Key(e.Modifiers, e.Key)

    member __.OnMouseDown (e: PointerPressedEventArgs) (root: Avalonia.VisualTree.IVisual) = 
        if mouse_en then
            let x, y = e.GetPosition root |> getPos
            e.Handled <- true
            mouse_pressed <- e.MouseButton
            raiseInputEvent <| InputEvent.MousePress(e.InputModifiers, y, x, e.MouseButton, e.ClickCount)

    member __.OnMouseUp (e: PointerReleasedEventArgs) (root: Avalonia.VisualTree.IVisual) = 
        if mouse_en then
            let x, y = e.GetPosition root |> getPos
            e.Handled <- true
            mouse_pressed <- MouseButton.None
            raiseInputEvent <| InputEvent.MouseRelease(e.InputModifiers, y, x, e.MouseButton)

    member __.OnMouseMove (e: PointerEventArgs) (root: Avalonia.VisualTree.IVisual) = 
        if mouse_en && mouse_pressed <> MouseButton.None then
            let x, y = e.GetPosition root |> getPos
            e.Handled <- true
            if (x,y) <> mouse_pos then
                mouse_pos <- x,y
                raiseInputEvent <| InputEvent.MouseDrag(e.InputModifiers, y, x, mouse_pressed)

    member __.OnMouseWheel (e: PointerWheelEventArgs) (root: Avalonia.VisualTree.IVisual) = 
        if mouse_en then
            let x, y = e.GetPosition root |> getPos
            let dx, dy = e.Delta.X, e.Delta.Y
            e.Handled <- true
            raiseInputEvent <| InputEvent.MouseWheel(e.InputModifiers, y, x, dx, dy)

    member __.OnTextInput (e: TextInputEventArgs) = 
        raiseInputEvent <| InputEvent.TextInput(e.Text)


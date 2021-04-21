namespace FVim

open common
open ui
open wcwidth
open def

open ReactiveUI
open Avalonia
open Avalonia.Input
open Avalonia.Media
open FSharp.Control.Reactive

open System
open System.Collections.ObjectModel

#nowarn "0025"

module private GridViewModelHelper =
  let inline trace id fmt =
    FVim.log.trace (sprintf "editorvm #%d" id) fmt

open GridViewModelHelper

[<Struct>]
type GridDrawOperation = 
  | Scroll of int * int * int * int * int * int
  | Put of GridRect

/// <summary>
/// A Grid is a 2D surface for characters, and central to
/// the Frame-Grid-Window hierarchy.
/// </summary>
type GridViewModel(_gridid: int, ?parent: GridViewModel, ?_gridsize: GridSize, ?_measuredsize: Size, ?_gridscale: float,
                     ?_cursormode: int, ?_anchorX: float, ?_anchorY: float) as this =
    inherit ViewModelBase(_anchorX, _anchorY, _measuredsize)

    let m_cursor_vm              = new CursorViewModel(_cursormode, parent.IsNone)
    let m_popupmenu_vm           = new PopupMenuViewModel()
    let m_child_grids            = ObservableCollection<IGridUI>()
    let m_resize_ev              = Event<IGridUI>()
    let m_input_ev               = Event<int * InputEvent>()
    let m_drawops                = ResizeArray() // keeps the scroll and putBuffer operations

    let mutable m_busy           = false
    let mutable m_mouse_en       = true
    let mutable m_mouse_pressed  = MouseButton.None
    let mutable m_mouse_pos      = 0,0

    let mutable m_gridsize       = _d { rows = 10; cols= 10 } _gridsize
    let mutable m_gridscale      = _d 1.0 _gridscale
    let mutable m_gridbuffer     = Array2D.create m_gridsize.rows m_gridsize.cols GridBufferCell.empty
    let mutable m_griddirty      = false // if true, the whole grid needs to be redrawn.
    let mutable m_fontsize       = theme.fontsize
    let mutable m_glyphsize      = Size(10.0, 10.0)
    let mutable m_gridfocused    = false
    let mutable m_gridfocusable  = true

    let mutable m_fb_h           = 10.0
    let mutable m_fb_w           = 10.0


    let raiseInputEvent e = m_input_ev.Trigger(_gridid, e)

    let getPos (p: Point) =
        int(p.X / m_glyphsize.Width), int(p.Y / m_glyphsize.Height)

    let markAllDirty () =
        m_griddirty <- true

    let cursorConfig() =
        if theme.mode_defs.Length = 0 || m_cursor_vm.modeidx < 0 then ()
        elif m_gridbuffer.GetLength(0) <= m_cursor_vm.row || m_gridbuffer.GetLength(1) <= m_cursor_vm.col then ()
        else
        let mode              = theme.mode_defs.[m_cursor_vm.modeidx]
        let hlid              = m_gridbuffer.[m_cursor_vm.row, m_cursor_vm.col].hlid
        let hlid              = Option.defaultValue hlid mode.attr_id
        let fg, bg, sp, attrs = theme.GetDrawAttrs hlid
        let origin : Point    = this.GetPoint m_cursor_vm.row m_cursor_vm.col
        let text              = m_gridbuffer.[m_cursor_vm.row, m_cursor_vm.col].text
        let text_type         = wswidth text
        let width             = float(max <| 1 <| CharTypeWidth text_type) * m_glyphsize.Width

        let on, off, wait =
            match mode with
            | { blinkon = Some on; blinkoff = Some off; blinkwait = Some wait  }
                when on > 0 && off > 0 && wait > 0 -> on, off, wait
            | _ -> 0,0,0

        // do not use the default colors for cursor
        let colorf = if hlid = 0 then GetReverseColor else id
        let fg, bg, sp = colorf fg, colorf bg, colorf sp

        if _gridid = 1 && states.ui_multigrid then () else

        this.DoWithRootCursorVM((fun cursor_vm x y ->
          let chksum = m_cursor_vm.VisualChecksum()
          cursor_vm.typeface       <- theme.guifont
          cursor_vm.wtypeface      <- theme.guifontwide
          cursor_vm.fontSize       <- m_fontsize
          cursor_vm.text           <- text
          cursor_vm.fg             <- fg
          cursor_vm.bg             <- bg
          cursor_vm.sp             <- sp
          cursor_vm.underline      <- attrs.underline
          cursor_vm.undercurl      <- attrs.undercurl
          cursor_vm.bold           <- attrs.bold
          cursor_vm.italic         <- attrs.italic
          cursor_vm.cellPercentage <- Option.defaultValue 100 mode.cell_percentage
          cursor_vm.blinkon        <- on
          cursor_vm.blinkoff       <- off
          cursor_vm.blinkwait      <- wait
          cursor_vm.shape          <- Option.defaultValue CursorShape.Block mode.cursor_shape
          cursor_vm.X              <- x
          cursor_vm.Y              <- y
          cursor_vm.Width          <- width
          cursor_vm.Height         <- m_glyphsize.Height
          if chksum <> m_cursor_vm.VisualChecksum() then
            cursor_vm.RenderTick     <- cursor_vm.RenderTick + 1
            trace _gridid "set cursor info, color = %A %A %A" fg bg sp
        ), origin.X, origin.Y)

    let clearBuffer preserveContent =
        let oldgrid = m_gridbuffer
        m_gridbuffer <- Array2D.create m_gridsize.rows m_gridsize.cols GridBufferCell.empty
        if preserveContent then
            let crow = 
                Array2D.length1 oldgrid
                |> min m_gridsize.rows
            let ccol = 
                Array2D.length2 oldgrid
                |> min m_gridsize.cols
            for r = 0 to crow-1 do
                for c = 0 to ccol-1 do
                    m_gridbuffer.[r,c] <- oldgrid.[r,c]
        markAllDirty()
        // notify buffer update and size change
        let size: Point = this.GetPoint m_gridsize.rows m_gridsize.cols
        m_fb_w <- size.X
        m_fb_h <- size.Y
        this.RaisePropertyChanged("BufferHeight")
        this.RaisePropertyChanged("BufferWidth")

    let initBuffer nrow ncol preserveContent =
        let new_gridsize = { rows = nrow; cols = ncol }
        if m_gridsize <> new_gridsize then
          m_gridsize <- new_gridsize
          trace _gridid "buffer resize = %A" m_gridsize
          clearBuffer preserveContent

    let putBuffer (line: GridLine) =
        let         row  = line.row
        let mutable col  = line.col_start
        let mutable hlid = 0
        let mutable rep = 1
        for cell in line.cells do
            hlid <- ValueOption.defaultValue hlid cell.hl_id
            rep  <- ValueOption.defaultValue 1 cell.repeat
            for _i = 1 to rep do
                m_gridbuffer.[row, col].hlid <- hlid
                m_gridbuffer.[row, col].text <- cell.text
                col <- col + 1
        let dirty = { row = row; col = line.col_start; height = 1; width = col - line.col_start } 
        // if the buffer under cursor is updated, also notify the cursor view model
        if row = m_cursor_vm.row && line.col_start <= m_cursor_vm.col && m_cursor_vm.col < col
        then cursorConfig()
        // trace _gridid "putBuffer: writing to %A" dirty
        // italic font artifacts I: remainders after scrolling and redrawing the dirty part
        // workaround: extend the dirty region one cell further towards the end

        // italic font artifacts II: when inserting on an italic line, later glyphs cover earlier with the background.
        // workaround: if italic, extend the dirty region towards the beginning, until not italic

        // italic font artifacts III: block cursor may not have italic style. 
        // how to fix this? curious about how the original GVim handles this situation.

        // ligature artifacts I: ligatures do not build as characters are laid down.
        // workaround: like italic, case II.

        // apply workaround I:
        let dirty = {dirty with width = min (dirty.width + 1) m_gridsize.cols }
        // apply workaround II:
        col  <- dirty.col - 1
        let mutable italic = true
        let mutable ligature = true
        while col > 0 && (italic || ligature) do
            hlid <- m_gridbuffer.[row, col].hlid
            col <- col - 1
            ligature <- isProgrammingSymbol m_gridbuffer.[row, col].text
            italic <- theme.hi_defs.[hlid].rgb_attr.italic 
        let dirty = {dirty with width = dirty.width + (dirty.col - col); col = col }
        m_drawops.Add(Put dirty)

    let putBufferM (M: ReadOnlyMemory<_>) =
      for line in M.Span do
        putBuffer line

    let cursorGoto id row col =
        if id = _gridid then
            m_cursor_vm.row <- row
            m_cursor_vm.col <- col
            cursorConfig()

    let changeMode (name: string) (index: int) = 
        m_cursor_vm.modeidx <- index
        cursorConfig()

    let setCursorEnabled v =
        m_cursor_vm.enabled <- v
        m_cursor_vm.RenderTick <- m_cursor_vm.RenderTick + 1

    let setBusy (v: bool) =
        trace _gridid "neovim: busy: %A" v
        m_busy <- v
        setCursorEnabled <| not v
        //if v then this.Cursor <- Cursor(StandardCursorType.Wait)
        //else this.Cursor <- Cursor(StandardCursorType.Arrow)

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

        trace _gridid "scroll: %A %A %A %A %A %A" top bot left right rows cols

        let copy src dst =
            if src >= 0 && src < m_gridsize.rows && dst >= 0 && dst < m_gridsize.rows then
                Array.Copy(m_gridbuffer, src * m_gridsize.cols + left, m_gridbuffer, dst * m_gridsize.cols + left, right - left)

        if rows > 0 then
            for i = top + rows to bot do
                copy i (i-rows)
        elif rows < 0 then
            for i = bot + rows - 1 downto top do
                copy i (i-rows)

        if top <= m_cursor_vm.row 
           && m_cursor_vm.row <= bot 
           && left <= m_cursor_vm.col 
           && m_cursor_vm.col <= right
        then
            cursorConfig()

        m_drawops.Add(Scroll(top, bot, left, right, rows, cols))

    let setMouse (en:bool) =
        m_mouse_en <- en

    let closeGrid() =
        trace _gridid "closeGrid"
        if this.IsFocused then
          this.IsFocused <- false
          match parent with
          | Some p -> 
            trace _gridid "try focus parent, id = %d" p.GridId
            if p.Focusable then p.IsFocused <- true
          | _ -> ()

    let setWinPos startrow startcol r c f =
        let parent = 
            match parent with
            | Some p -> p
            | None -> failwith "setWinPos: no parent"
        let grid = _gridid
        trace _gridid "setWinPos: grid = %A, parent = %A, startrow = %A, startcol = %A, c = %A, r = %A" grid parent.GridId startrow startcol c r
        (* manually resize and position the child grid as per neovim docs *)
        let origin: Point = parent.GetPoint startrow startcol
        trace _gridid "setWinPos: update parameters: c = %d r = %d X = %f Y = %f" c r origin.X origin.Y
        initBuffer r c true
        this.X <- origin.X
        this.Y <- origin.Y
        this.Focusable <- f

    let hidePopupMenu() =
        m_popupmenu_vm.Show <- false

    let selectPopupMenuPassive i =
        m_popupmenu_vm.Selection <- i

    let selectPopupMenuActive i =
        model.SelectPopupMenuItem i true false

    let commitPopupMenu i =
        model.SelectPopupMenuItem i true true

    let showPopupMenu grid (items: CompleteItem[]) selected row col =
        if grid <> _gridid then
            hidePopupMenu()
        else
        let startPos  = this.GetPoint row col
        let cursorPos = this.GetPoint (m_cursor_vm.row + 1) m_cursor_vm.col

        trace _gridid "show popup menu at [%O, %O]" startPos cursorPos

        //  Decide the maximum size of the popup menu based on grid dimensions
        let menuLines = min items.Length 15
        let menuCols = 
            items
            |> Array.map CompleteItem.GetLength
            |> Array.max

        let bounds = this.GetPoint menuLines menuCols
        let editorSize = this.GetPoint m_gridsize.rows m_gridsize.cols

        m_popupmenu_vm.Selection <- selected
        m_popupmenu_vm.SetItems(items, startPos, cursorPos, m_glyphsize.Height, bounds, editorSize)
        m_popupmenu_vm.Show <- true

        let w = m_popupmenu_vm.Width / m_glyphsize.Width
        let h = m_popupmenu_vm.Height / m_glyphsize.Height
        let r = m_popupmenu_vm.Y / m_glyphsize.Height
        let c = m_popupmenu_vm.X / m_glyphsize.Width
        model.SetPopupMenuPos w h r c

    let redraw(cmd: RedrawCommand) =
        //trace "%A" cmd
        match cmd with
        | GridResize(_, c, r)                                                -> initBuffer r c true
        | GridClear _                                                        -> clearBuffer false
        | GridLine lines                                                     -> putBufferM lines
        | GridCursorGoto(id, row, col)                                       -> cursorGoto id row col
        | GridScroll(_, top,bot,left,right,rows,cols)                        -> scrollBuffer top bot left right rows cols
        | ModeChange(name, index)                                            -> changeMode name index
        | Busy is_busy                                                       -> setBusy is_busy
        | Mouse en                                                           -> setMouse en
        | WinClose(_)                                                        -> closeGrid()
        | WinPos(_, _, startrow, startcol, c, r)                             -> setWinPos startrow startcol r c true
        | MsgSetPos(_, row, scrolled, sep_char)                              -> setWinPos row 0 1 m_gridsize.cols true
        | WinFloatPos (_, _, anchor, anchor_grid, r, c, f)                   -> setWinPos (int r + 1) (int c) m_gridsize.rows m_gridsize.cols f // XXX assume attaching to grid #1, assume NW
        | PopupMenuShow(items, selected, row, col, grid)                     -> showPopupMenu grid items selected row col
        | PopupMenuSelect(selected)                                          -> selectPopupMenuPassive selected
        | PopupMenuHide                                                      -> hidePopupMenu ()
        | x -> trace _gridid "unimplemented command: %A" x

    let fontConfig() =
        // It turns out the space " " advances farest...
        // So we measure it as the width.
        let s, w, h = MeasureText(Rune.empty, theme.guifont, theme.guifontwide, theme.fontsize, m_gridscale)
        m_glyphsize <- Size(w, h)
        m_fontsize <- s
        trace _gridid "fontConfig: glyphsize=%A, measured font size=%A" m_glyphsize m_fontsize

        // sync font to cursor vm
        cursorConfig()
        // sync font to popupmenu vm
        m_popupmenu_vm.SetFont(theme.guifont, theme.fontsize)
        markAllDirty()
        m_resize_ev.Trigger(this)

    let hlConfig(id) =
        if id = 0 then
            this.RaisePropertyChanged("BackgroundColor")
        markAllDirty()

    let updateMouseButton (pp: PointerPoint) =
        let k = pp.Properties.PointerUpdateKind
        match k with
        | PointerUpdateKind.LeftButtonPressed -> 
            m_mouse_pressed <- MouseButton.Left
            m_mouse_pressed
        | PointerUpdateKind.RightButtonPressed -> 
            m_mouse_pressed <- MouseButton.Right
            m_mouse_pressed
        | PointerUpdateKind.MiddleButtonPressed -> 
            m_mouse_pressed <- MouseButton.Middle
            m_mouse_pressed
        | PointerUpdateKind.LeftButtonReleased -> 
            m_mouse_pressed <- MouseButton.None
            MouseButton.Left
        | PointerUpdateKind.RightButtonReleased -> 
            m_mouse_pressed <- MouseButton.None
            MouseButton.Right
        | PointerUpdateKind.MiddleButtonReleased -> 
            m_mouse_pressed <- MouseButton.None
            MouseButton.Middle
        | _ -> 
            // unrecognized event, do not update our state
            MouseButton.None

    do
        trace _gridid "ctor"
        fontConfig()
        setCursorEnabled theme.cursor_enabled
        clearBuffer false

        this.Watch [

            m_popupmenu_vm.ObservableForProperty(fun x -> x.Selection)
            |> Observable.subscribe (fun x -> selectPopupMenuActive <| x.GetValue())

            m_popupmenu_vm.Commit
            |> Observable.subscribe commitPopupMenu

            theme.hlchange_ev.Publish 
            |> Observable.subscribe hlConfig 

            theme.fontconfig_ev.Publish
            |> Observable.subscribe fontConfig

            theme.cursoren_ev.Publish
            |> Observable.subscribe (fun en ->
                if m_cursor_vm.IsRootCursor then 
                    setCursorEnabled en)

            states.register.watch "font" fontConfig

            this.ObservableForProperty(fun x -> x.IsFocused)
            |> Observable.subscribe (fun x ->
              trace _gridid "focus state changed: %A" x.Value
              if x.Value then cursorConfig()
            )
        ] 

    interface IGridUI with
        member __.Id = _gridid
        member __.GridHeight = int( this.Height / m_glyphsize.Height )
        member __.GridWidth  = int( this.Width  / m_glyphsize.Width  )
        member __.Resized = m_resize_ev.Publish
        member __.Input = m_input_ev.Publish
        member __.HasChildren = m_child_grids.Count <> 0
        member __.Redraw cmd = redraw cmd
        member __.CreateChild id r c =
            trace _gridid "CreateChild: #%d" id
            let child_size = this.GetPoint r c
            let child = GridViewModel(id, this, {rows=r; cols=c}, Size(child_size.X, child_size.Y), m_gridscale, m_cursor_vm.modeidx)
            m_child_grids.Add child
            child :> IGridUI
        member __.RemoveChild c =
            ignore <| m_child_grids.Remove c
        member __.Detach() =
          match parent with
          | None -> ()
          | Some p -> (p:>IGridUI).RemoveChild this

    member __.MarkClean () = 
      m_griddirty <- false
      m_drawops.Clear()

    member __.MarkAllDirty = markAllDirty

    //  converts grid position to UI Point
    member __.GetPoint row col =
        Point(double(col) * m_glyphsize.Width, double(row) * m_glyphsize.Height)

    member __.SetMeasuredSize (v: Size) =
        trace _gridid "set measured size: %A" v
        let gridui = this :> IGridUI
        let gw, gh = gridui.GridWidth, gridui.GridHeight
        this.Width <- v.Width
        this.Height <- v.Height
        let gw', gh' = gridui.GridWidth, gridui.GridHeight
        if gw <> gw' || gh <> gh' then 
            if this.IsTopLevel then
                m_resize_ev.Trigger(this)

    member __.DoWithRootCursorVM (fn: CursorViewModel -> float -> float -> unit, x: float, y: float) =
        match parent with
        | Some p -> p.DoWithRootCursorVM(fn, x + this.X, y + this.Y)
        | None -> fn m_cursor_vm x y

    (*******************   Exposed properties   ***********************)

    member __.Item with get(row, col) = m_gridbuffer.[row, col]
    member __.Cols with get() = m_gridsize.cols
    member __.Rows with get() = m_gridsize.rows
    member __.Dirty with get() = m_griddirty
    member __.DrawOps with get() = m_drawops
    member __.CursorInfo with get() : CursorViewModel = m_cursor_vm
    member __.PopupMenu with get(): PopupMenuViewModel = m_popupmenu_vm
    member __.RenderScale
        with get() : float = m_gridscale
        and set(v) = m_gridscale <- v
    member __.FontAttrs with get() = theme.guifont, theme.guifontwide, m_fontsize
    member __.BackgroundColor with get(): Color = theme.default_bg
    member __.BufferHeight with get(): float = m_fb_h
    member __.BufferWidth  with get(): float = m_fb_w
    member __.GlyphHeight with get(): float = m_glyphsize.Height
    member __.GlyphWidth with get(): float = m_glyphsize.Width
    member __.IsTopLevel with get(): bool  = parent.IsNone
    member __.GridId with get() = _gridid
    member __.ChildGrids = m_child_grids
    member __.IsFocused with get() = m_gridfocused and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_gridfocused, v)
    member __.Focusable with get() = m_gridfocusable and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_gridfocusable, v)

    (*******************   Events   ***********************)

    member __.OnKey (e: KeyEventArgs) = 
        raiseInputEvent <| InputEvent.Key(e.KeyModifiers, e.Key)

    member __.OnMouseDown (e: PointerPressedEventArgs) (root: Avalonia.VisualTree.IVisual) = 
        if m_mouse_en then
            let x, y = e.GetPosition root |> getPos
            let button = updateMouseButton(e.GetCurrentPoint null)
            raiseInputEvent <| InputEvent.MousePress(e.KeyModifiers, y, x, button)

    member __.OnMouseUp (e: PointerReleasedEventArgs) (root: Avalonia.VisualTree.IVisual) = 
        if m_mouse_en then
            let x, y = e.GetPosition root |> getPos
            let button = updateMouseButton(e.GetCurrentPoint null)
            raiseInputEvent <| InputEvent.MouseRelease(e.KeyModifiers, y, x, button)

    member __.OnMouseMove (e: PointerEventArgs) (root: Avalonia.VisualTree.IVisual) = 
        if m_mouse_en && m_mouse_pressed <> MouseButton.None then
            let x, y = e.GetPosition root |> getPos
            if (x,y) <> m_mouse_pos then
                m_mouse_pos <- x,y
                raiseInputEvent <| InputEvent.MouseDrag(e.KeyModifiers, y, x, m_mouse_pressed)

    member __.OnMouseWheel (e: PointerWheelEventArgs) (root: Avalonia.VisualTree.IVisual) = 
        if m_mouse_en then
            let x, y = e.GetPosition root |> getPos
            let dx, dy = e.Delta.X, e.Delta.Y
            raiseInputEvent <| InputEvent.MouseWheel(e.KeyModifiers, y, x, dx, dy)

    member __.OnTextInput (e: TextInputEventArgs) = 
        raiseInputEvent <| InputEvent.TextInput(e.Text)

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
open model
open widgets
open System.Collections.Generic

#nowarn "0025"

module private GridViewModelHelper =
  let inline trace id fmt =
    FVim.log.trace (sprintf "editorvm #%d" id) fmt

open GridViewModelHelper

type TrackedGridPosition =
  {
    mutable trow: int
    mutable tcol: int
  }

[<Struct>]
type GridDrawOperation = 
  | Scroll of int * int * int * int * int * int
  | Put of GridRect

/// <summary>
/// A Grid is a 2D surface for characters, and central to
/// the Frame-Grid-Window hierarchy.
/// </summary>
and GridViewModel(_gridid: int, ?_parent: GridViewModel, ?_gridsize: GridSize) as this =
    inherit ViewModelBase()

    let m_cursor_vm              = new CursorViewModel(None)
    let m_popupmenu_vm           = new PopupMenuViewModel()
    let m_child_grids            = ResizeArray<GridViewModel>()
    let m_resize_ev              = Event<IGridUI>()
    let m_input_ev               = Event<int * InputEvent>()
    let m_ext_winclose_ev        = Event<unit>()
    let m_drawops                = ResizeArray() // keeps the scroll and putBuffer operations

    let mutable m_parent           = _parent
    let mutable m_busy           = false
    let mutable m_mouse_en       = true
    let mutable m_mouse_pressed  = MouseButton.None
    let mutable m_mouse_pressed_vm = this
    let mutable m_mouse_pressed_widget = -1
    let mutable m_mouse_pos      = 0,0

    let mutable m_gridsize       = _d { rows = 10; cols= 10 } _gridsize
    let mutable m_gridscale      = 1.0
    let mutable m_gridbuffer     = GridBufferCell.CreateGrid m_gridsize.rows m_gridsize.cols
    let mutable m_griddirty      = false // if true, the whole grid needs to be redrawn.
    let mutable m_fontsize       = theme.fontsize
    let mutable m_glyphsize      = Size(10.0, 10.0)
    let mutable m_gridfocused    = false
    let mutable m_gridfocusable  = true

    let mutable m_fb_h           = 10.0
    let mutable m_fb_w           = 10.0
    let mutable m_anchor_row     = 0
    let mutable m_anchor_col     = 0
    let mutable m_is_hidden      = false
    let mutable m_is_external    = false
    let mutable m_is_float       = false
    let mutable m_is_msg         = false
    let mutable m_msg_scrolled   = false
    let mutable m_msg_sepchar    = ""
    let mutable m_z              = -100
    let mutable m_winhnd         = 0 // the window handle (winid), not winnr
    let mutable m_bufnr          = 0
    let mutable m_scrollbar_show = false
    let mutable m_scrollbar_top  = 0
    let mutable m_scrollbar_bot  = 0
    let mutable m_scrollbar_row  = 0
    let mutable m_scrollbar_col  = 0
    let mutable m_scrollbar_linecount = 0
    let mutable m_extmarks       = hashmap[] // tracks existing extmarks and last known position -- some may be scrolled out of viewport
    let m_gridComparer = GridViewModel.MakeGridComparer() :> IComparer<GridViewModel>

    let raiseInputEvent id e = m_input_ev.Trigger(id, e)

    let getPos (p: Point) =
        int(p.X / m_glyphsize.Width), int(p.Y / m_glyphsize.Height)

    let cursorConfig() =
        if theme.mode_defs.Length = 0 || m_cursor_vm.modeidx < 0 then ()
        elif m_gridbuffer.GetLength(0) <= m_cursor_vm.row || m_gridbuffer.GetLength(1) <= m_cursor_vm.col then ()
        else
        let _,_,target_vm,target_row,target_col,_ : int*int*GridViewModel*int*int*int = this.FindTargetVm m_cursor_vm.row m_cursor_vm.col
        let mode              = theme.mode_defs.[m_cursor_vm.modeidx]
        let hlid              = target_vm.[target_row, target_col].hlid
        let hlid              = Option.defaultValue hlid mode.attr_id
        let fg, bg, sp, attrs = theme.GetDrawAttrs hlid
        let origin : Point    = this.GetPoint m_cursor_vm.row m_cursor_vm.col
        let text              = target_vm.[target_row, target_col].text
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

        m_cursor_vm.typeface       <- theme.guifont
        m_cursor_vm.wtypeface      <- theme.guifontwide
        m_cursor_vm.fontSize       <- m_fontsize
        m_cursor_vm.text           <- text
        m_cursor_vm.fg             <- fg
        m_cursor_vm.bg             <- bg
        m_cursor_vm.sp             <- sp
        m_cursor_vm.underline      <- attrs.underline
        m_cursor_vm.undercurl      <- attrs.undercurl
        m_cursor_vm.bold           <- attrs.bold
        m_cursor_vm.italic         <- attrs.italic
        m_cursor_vm.cellPercentage <- Option.defaultValue 100 mode.cell_percentage
        m_cursor_vm.blinkon        <- on
        m_cursor_vm.blinkoff       <- off
        m_cursor_vm.blinkwait      <- wait
        m_cursor_vm.shape          <- Option.defaultValue CursorShape.Block mode.cursor_shape
        m_cursor_vm.X              <- origin.X
        m_cursor_vm.Y              <- origin.Y
        m_cursor_vm.Width          <- width
        m_cursor_vm.Height         <- m_glyphsize.Height
        m_cursor_vm.RenderTick <- m_cursor_vm.RenderTick + 1
        //trace _gridid "set cursor info, color = %A %A %A" fg bg sp

    let markAllDirty () =
        m_griddirty <- true
        for c in m_child_grids do
            c.MarkDirty()

    let rec markDirty ({ row = row; col = col; height = h; width = w } as dirty) =
        if h > 1 then
            for i = 0 to h-1 do
                markDirty {row=row+i;col=col;height=1;width=w}
        else

        // if the buffer under cursor is updated, also notify the cursor view model
        if row = m_cursor_vm.row && col <= m_cursor_vm.col && m_cursor_vm.col < col + w
        then cursorConfig()

        // the workarounds below will extend the dirty region -- if we are drawing
        // a base grid displaying the grid boundaries, do not apply them.
        if states.ui_multigrid && this.GridId = 1 then
            m_drawops.Add(Put dirty)
        else

        // trace _gridid "markDirty: writing to %A" dirty
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
        let mutable col = dirty.col - 1
        let mutable italic = true
        let mutable ligature = true
        let mutable hlid = 0
        while col > 0 && (italic || ligature) do
            hlid <- m_gridbuffer.[row, col].hlid
            col <- col - 1
            ligature <- isProgrammingSymbol m_gridbuffer.[row, col].text
            italic <- theme.hi_defs.[hlid].rgb_attr.italic 
        let dirty = {dirty with width = dirty.width + (dirty.col - col); col = col }
        m_drawops.Add(Put dirty)

    let clearBuffer preserveContent =
        let oldgrid = m_gridbuffer
        m_gridbuffer <- GridBufferCell.CreateGrid m_gridsize.rows m_gridsize.cols
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
          #if DEBUG
          trace _gridid "buffer resize = %A" m_gridsize
          #endif
          clearBuffer preserveContent

    let putBuffer (M: ReadOnlyMemory<GridLine>) =
      //if _gridid = 1 then
      //    trace _gridid "putBuffer"
      for line in M.Span do
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
                for m in m_gridbuffer.[row, col].marks do
                  m_extmarks.Remove m.mark |> ignore
                m_gridbuffer.[row, col].marks <- []
                col <- col + 1
        markDirty { row = row; col = line.col_start; height = 1; width = col - line.col_start } 

    let clearMarks() =
      for _,cell,_ in m_extmarks.Values do
        cell.marks <- []
      m_extmarks.Clear()
    #if DEBUG
      trace _gridid $"clearMarks"
    #endif

    let putExtmarks (M: Extmark[]) =
    #if DEBUG
      trace _gridid $"putExtmarks:\n%A{M}"
    #endif
      let rec rm (l: Extmark list) (id: int) =
        match l with
        | [] -> []
        | {mark = mark} :: rest when mark = id -> rm rest id
        | x :: rest -> x :: (rm rest id)
      for mark in M do
        let { mark = id; row = row; col = col } = mark
        match m_extmarks.TryGetValue id with
        | true, (_, cell, _) -> 
          cell.marks <- rm cell.marks id
        | _ -> ()
        m_extmarks.[id] <- (mark, m_gridbuffer.[row, col], { trow = row; tcol = col })
        m_gridbuffer.[row, col].marks <- mark :: m_gridbuffer.[row, col].marks

    let changeMode (name: string) (index: int) = 
        m_cursor_vm.modeidx <- index
        cursorConfig()

    let setCursorEnabled v =
        m_cursor_vm.enabled <- v
        m_cursor_vm.RenderTick <- m_cursor_vm.RenderTick + 1

    let setBusy (v: bool) =
      #if DEBUG
        trace _gridid "neovim: busy: %A" v
      #endif
        m_busy <- v
        setCursorEnabled <| not v

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

        #if DEBUG
        trace _gridid "scroll: %A %A %A %A %A %A" top bot left right rows cols
        #endif

        // copies the line from src to dst, and then refill the dst with "new cells"
        let copy src dst =
            if src >= 0 && src < m_gridsize.rows && dst >= 0 && dst < m_gridsize.rows then
              for c = left to right - 1 do
                // since the "dst" line will be overwritten, we may say it's really new cells...
                let dst_cell = m_gridbuffer.[dst,c]
                let src_cell = m_gridbuffer.[src,c]
                m_gridbuffer.[dst,c] <- src_cell
                m_gridbuffer.[src,c] <- dst_cell
                // update src row marks -> dst row
                for m in src_cell.marks do
                  let _,_,pos = m_extmarks.[m.mark]
                  pos.trow <- dst
                // remove dst row marks
                for m in dst_cell.marks do
                  m_extmarks.Remove m.mark |> ignore
                dst_cell.marks <- []

        if rows > 0 then
            for i = top + rows to bot do
                copy i (i-rows)
        elif rows < 0 then
            for i = bot + rows - 1 downto top do
                copy i (i-rows)

        if m_cursor_vm.enabled 
           && m_cursor_vm.focused
           && top <= m_cursor_vm.row 
           && m_cursor_vm.row <= bot 
           && left <= m_cursor_vm.col 
           && m_cursor_vm.col <= right
        then
            cursorConfig()

        m_drawops.Add(Scroll(top, bot, left, right, rows, cols))

    let setMouse (en:bool) =
        m_mouse_en <- en

    let getRootGrid() =
        let mutable p = m_parent
        let mutable q = this
        while p.IsSome do
            q <- p.Value
            p <- p.Value.Parent
        q

    let closeGrid() =
        trace _gridid "closeGrid"
        if m_is_external then
            m_ext_winclose_ev.Trigger()
        elif m_is_float then
            m_is_hidden <- true
            getRootGrid().MarkDirty()

    let setWinPos startrow startcol r c f =
        let oldRegion = { row = m_anchor_row; col = m_anchor_col; height = m_gridsize.rows; width = m_gridsize.cols}
        let newRegion = { row = startrow; col = startcol; height = r; width = c}
        m_is_hidden <- false
        let parent = 
            match m_parent with
            | Some p -> p
            | None -> failwith "setWinPos: no parent"
        let grid = _gridid
        #if DEBUG
        trace _gridid "setWinPos: grid = %A, parent = %A, startrow = %A, startcol = %A, c = %A, r = %A" grid parent.GridId startrow startcol c r
        #endif
        (* manually resize and position the child grid as per neovim docs *)
        initBuffer r c true
        m_anchor_col <- startcol
        m_anchor_row <- startrow
        this.Focusable <- f
        parent.OnChildChanged oldRegion newRegion

    let setMsgWinPos startrow scrolled sep_char =
        setWinPos startrow 0 m_gridsize.rows m_gridsize.cols true
        m_is_msg <- true
        m_msg_scrolled <- scrolled
        m_msg_sepchar <- sep_char
        m_z <- 9999 // always put msg window on the top

    let setWinFloatPos win anchor anchor_grid r c f z =
        m_winhnd <- win
        m_is_float <- true
        m_z <- z
        #if DEBUG
        trace _gridid "setWinFloatPos: z = %d" z
        #endif
        setWinPos (int r) (int c) m_gridsize.rows m_gridsize.cols f // XXX assume assume NW
        m_parent.Value.SortChildren()

    let setWinExternalPos win =
        m_winhnd <- win
        if not m_is_external then
            m_is_external <- true
            m_anchor_col <- 0
            m_anchor_row <- 0
            (this:>IGridUI).Detach()
            CreateFrame this

    let setWinViewport win top bot row col lc =
        m_winhnd <- win
        m_scrollbar_top <- top
        m_scrollbar_bot <- bot
        m_scrollbar_row <- row
        m_scrollbar_col <- col
        m_scrollbar_linecount <- lc

    let hidePopupMenu() =
        m_popupmenu_vm.Show <- false

    let selectPopupMenuPassive i =
        m_popupmenu_vm.Selection <- i

    let selectPopupMenuActive i =
        model.SelectPopupMenuItem i true false

    let commitPopupMenu i =
        model.SelectPopupMenuItem i true true

    let redraw(cmd: RedrawCommand) =
        match cmd with
        | GridResize(_, c, r)                                                -> initBuffer r c true
        | GridClear _                                                        -> clearBuffer false
        | GridLine lines                                                     -> putBuffer lines
        | GridCursorGoto(id, row, col)                                       -> this.CursorGoto id row col
        | GridScroll(_, top,bot,left,right,rows,cols)                        -> scrollBuffer top bot left right rows cols
        | ModeChange(name, index)                                            -> changeMode name index
        | Busy is_busy                                                       -> setBusy is_busy
        | Mouse en                                                           -> setMouse en
        | WinClose(_)                                                        -> closeGrid()
        | WinPos(_, _, startrow, startcol, c, r)                             -> setWinPos startrow startcol r c true
        | WinHide(_)                                                         -> m_is_hidden <- true
        | MsgSetPos(_, row, scrolled, sep_char)                              -> setMsgWinPos row scrolled sep_char
        | WinFloatPos (_, win, anchor, anchor_grid, r, c, f, z)              -> setWinFloatPos win anchor anchor_grid r c f z
        | PopupMenuShow(items, selected, row, col, grid)                     -> this.ShowPopupMenu grid items selected row col
        | PopupMenuSelect(selected)                                          -> selectPopupMenuPassive selected
        | PopupMenuHide                                                      -> hidePopupMenu ()
        | WinExternalPos(_,win)                                              -> setWinExternalPos win
        | WinViewport(id, win, top, bot, row, col, lc)                       -> setWinViewport win top bot row col lc
        | WinExtmarks(win, marks)                                            -> if win = m_winhnd then putExtmarks marks
        | WinExtmarksClear(win)                                              -> if win = m_winhnd then clearMarks()
        | x -> trace _gridid "unimplemented command: %A" x

    let fontConfig() =
        // It turns out the space " " advances farest...
        // So we measure it as the width.
        let s, w, h = MeasureText(Rune.empty, theme.guifont, theme.guifontwide, theme.fontsize, m_gridscale)
        m_glyphsize <- Size(w, h)
        m_fontsize <- s
        //trace _gridid "fontConfig: glyphsize=%A, measured font size=%A" m_glyphsize m_fontsize

        // sync font to cursor vm
        cursorConfig()
        // sync font to popupmenu vm
        m_popupmenu_vm.SetFont(theme.guifont, theme.fontsize)
        markAllDirty()
        if this.IsTopLevel then
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
            |> Observable.subscribe setCursorEnabled

            rpc.register.watch "font" fontConfig

            this.ObservableForProperty(fun x -> x.IsFocused)
            |> Observable.subscribe (fun x ->
              trace _gridid "focus state changed: %A" x.Value
              cursorConfig()
            )

            rpc.register.notify "OnBufWinEnter" this.OnBufWinEnter
        ] 

    interface IGridUI with
        member __.Id = _gridid
        member __.GridHeight = int( this.Height / m_glyphsize.Height )
        member __.GridWidth  = int( this.Width  / m_glyphsize.Width  )
        member __.Resized = m_resize_ev.Publish
        member __.Input = m_input_ev.Publish
        member __.BackgroundColor with get(): Color = theme.default_bg
        member __.HasChildren = m_child_grids.Count <> 0
        member __.Redraw cmd = redraw cmd
        member _igrid.RenderScale = this.RenderScale
        member __.CreateChild id r c =
            trace _gridid "CreateChild: #%d" id
            let child = GridViewModel(id, this, {rows=r; cols=c})
            m_child_grids.Add child |> ignore
            child.ZIndex <- this.ZIndex + 1
            this.SortChildren()
            child :> IGridUI
        member __.AddChild c =
            let c = c :?> GridViewModel
            trace _gridid "AddChild: #%d" c.GridId
            m_child_grids.Add c |> ignore
            c.Parent <- (Some this)
            c.ZIndex <- this.ZIndex + 1
            this.SortChildren()
            markAllDirty()
        member __.RemoveChild c =
            ignore <| m_child_grids.Remove (c:?>GridViewModel)
            markAllDirty()
        member __.Detach() =
          match m_parent with
          | None -> ()
          | Some p -> 
            (p:>IGridUI).RemoveChild this
            m_parent <- None
            m_z <- -100
          markAllDirty()

    member __.CursorGoto id row col =
        // translation back to parent
        if m_parent.IsSome && id = _gridid then
        #if DEBUG
            trace _gridid "CursorGoto parent"
        #endif
            m_parent.Value.CursorGoto m_parent.Value.GridId (row + m_anchor_row) (col + m_anchor_col)
        // goto me
        elif id = _gridid then
        #if DEBUG
            trace _gridid "CursorGoto me"
        #endif
            m_cursor_vm.focused <- true
            m_cursor_vm.row <- row
            m_cursor_vm.col <- col
            this.IsFocused <- true
            cursorConfig()
        // goto my child
        elif m_child_grids.FindIndex(fun x -> x.GridId = id) > -1 then
            ()
        // was me, but not anymore
        elif m_cursor_vm.focused then
        #if DEBUG
            trace _gridid "CursorGoto notme"
        #endif
            m_cursor_vm.focused <- false
            m_cursor_vm.RenderTick <- m_cursor_vm.RenderTick + 1

    member __.ShowPopupMenu grid (items: CompleteItem[]) selected row col =
        if m_parent.IsSome && grid = _gridid then
            m_parent.Value.ShowPopupMenu m_parent.Value.GridId items selected (row + m_anchor_row) (col + m_anchor_col)
        elif grid <> _gridid then
            hidePopupMenu()
        else
        let startPos  = this.GetPoint row col
        let cursorPos = this.GetPoint (m_cursor_vm.row + 1) m_cursor_vm.col

        #if DEBUG
        trace _gridid "show popup menu at [%O, %O]" startPos cursorPos
        #endif

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



    member __.MarkClean () = 
      m_griddirty <- false
      m_drawops.Clear()
      for c in m_child_grids do
        c.MarkClean()

    member __.MarkDirty = markAllDirty

    //  converts grid position to UI Point
    member __.GetPoint row col =
        Point(double(col) * m_glyphsize.Width, double(row) * m_glyphsize.Height)

    member __.SetMeasuredSize (v: Size) =
        trace _gridid "set measured size: %A" v
        let gridui = this :> IGridUI
        this.Width <- v.Width
        this.Height <- v.Height
        let gw, gh = gridui.GridWidth, gridui.GridHeight
        let gw', gh' = int(this.BufferWidth / this.GlyphWidth), int(this.BufferHeight / this.GlyphHeight)
        if gw <> gw' || gh <> gh' then 
            if this.IsTopLevel then
                m_resize_ev.Trigger(this)

    /// The reason that some grid boundary updates are missing is that, NeoVim uses lazy base grid update.
    /// When the grid boundary is already drawn in the base grid previously and now we are going back to it,
    /// NeoVim will not send the update command.
    /// So on child region change, we need to compute the regions that were previously covered by the child,
    /// but now "revealed" so that the base grid must draw over it. Naturally, this implies that the content
    /// of the base grid should never be overwritten by child grids because it may be re-used later.
    member __.OnChildChanged oldRegion newRegion =
        if newRegion.Contains oldRegion then 
            // child is growing, so do nothing.
            ()
        elif oldRegion.Contains newRegion then
            // child is shrinking, find out which way.
            // top
            if oldRegion.row < newRegion.row then
                markDirty {oldRegion with height = newRegion.row - oldRegion.row}
            // bottom
            if oldRegion.row_end > newRegion.row_end then
                markDirty {oldRegion with row = newRegion.row_end; height = oldRegion.row_end - newRegion.row_end }
            // left
            if oldRegion.col < newRegion.col then
                markDirty {oldRegion with width = newRegion.col - oldRegion.col}
            // right
            if oldRegion.col_end > newRegion.col_end then
                markDirty {oldRegion with col = newRegion.col_end; width = oldRegion.col_end - newRegion.col_end}
        elif oldRegion.Disjoint newRegion then
            // child completely changed geometry. oldRegion completely "revealed"
            markDirty oldRegion
        else
            // child intersects with old
            // TODO mark more precisely?
            markDirty oldRegion

    member __.OnBufWinEnter [| bufnr; ObjArray(wins) |] =
        let bufnr = 
            match bufnr with
            | String bufnr -> int bufnr
            | Integer32 bufnr -> bufnr
        if Array.exists (function | Integer32(w) when w = m_winhnd -> true | _ -> false) wins then
        #if DEBUG
            trace _gridid "bufnr updated to %d" bufnr
        #endif
            m_bufnr <- bufnr
        elif bufnr = m_bufnr then // but current win is not found in the array
        #if DEBUG
            trace _gridid "bufnr %d detached" bufnr
        #endif
            m_bufnr <- 0

    (*******************   Exposed properties   ***********************)

    member __.Item with get(row, col) = m_gridbuffer.[row, col]
    member __.Cols with get() = m_gridsize.cols
    member __.Rows with get() = m_gridsize.rows
    member __.AnchorRow with get() = m_anchor_row
    member __.AnchorCol with get() = m_anchor_col
    member __.AbsAnchor with get() =
        match m_parent with
        | Some p ->
            let pr,pc = p.AbsAnchor
            pr + m_anchor_row, pc + m_anchor_col
        | _ -> m_anchor_row, m_anchor_col
    member __.Dirty with get() = m_griddirty
    member __.DrawOps with get() = m_drawops
    member __.Hidden with get():bool = m_is_hidden
                     and  set(v) = m_is_hidden <- v
    member __.CursorInfo with get() : CursorViewModel = m_cursor_vm
    member __.PopupMenu with get(): PopupMenuViewModel = m_popupmenu_vm
    member __.RenderScale
        with get() : float = m_gridscale
        and set(v) = m_gridscale <- v
    member __.FontAttrs with get() = theme.guifont, theme.guifontwide, m_fontsize
    member __.BufferHeight with get(): float = m_fb_h
    member __.BufferWidth  with get(): float = m_fb_w
    member __.GlyphHeight with get(): float = m_glyphsize.Height
    member __.GlyphWidth with get(): float = m_glyphsize.Width
    member __.IsTopLevel with get(): bool  = m_parent.IsNone
    member __.GridId with get() = _gridid
    member __.ChildGrids = m_child_grids
    member __.IsFocused with get() = m_gridfocused and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_gridfocused, v)
    member __.Focusable with get() = m_gridfocusable and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_gridfocusable, v)
    member __.WindowHandle = m_winhnd
    member __.ExtWinClosed = m_ext_winclose_ev.Publish
    member __.Parent with get() = m_parent and set(v) = m_parent <- v
    member __.ZIndex with get() = m_z and set(v) = m_z <- v
    member __.SortChildren() =
        m_child_grids.Sort(m_gridComparer)
    member __.FindTargetVm r c =
        let mutable target_vm = this
        let mutable target_row = r
        let mutable target_col = c
        let mutable target_z = m_z
        let mutable target_ar = this.AnchorRow
        let mutable target_ac = this.AnchorCol
        for cg in m_child_grids do
            if cg.Hidden then ()
            else
            let ar,ac,vm,row,col,z = cg.FindTargetVm (r-cg.AnchorRow) (c-cg.AnchorCol)
            if ar <= r && r < ar + vm.Rows &&
               ac <= c && c < ac + vm.Cols &&
               z >= target_z
            then
                target_vm <- vm
                target_row <- row
                target_col <- col
                target_z <- z
                target_ar <- ar + this.AnchorRow
                target_ac <- ac + this.AnchorCol
        target_ar,target_ac,target_vm,target_row,target_col,target_z
    member __.DrawMsgSeparator = m_msg_scrolled
    member __.ScrollbarData = m_scrollbar_top,m_scrollbar_bot,m_scrollbar_row,m_scrollbar_col,m_scrollbar_linecount
    member __.IsFloat = m_is_float
    member __.IsMsg = m_is_msg
    member __.BufNr = m_bufnr
    member __.Extmarks = m_extmarks
    member __.FindTargetWidget (r: int) (c: int) =
      let placements = getGuiWidgetPlacements m_bufnr
      m_extmarks.Values
      |> Seq.tryPick(fun ({ns = ns; mark = mark},cell,{trow=cr;tcol=cc}) -> 
        if ns <> guiwidgetNamespace || not(cell.ContainsMark mark) then None else
        match placements.TryGetValue mark with
        | false, _ -> None
        | true, {w=w;h=h} -> 
        if cr <= r && r < cr + h && cc <= c && c < cc + w then Some mark
        else None)
      |> Option.defaultValue -1

    static member MakeGridComparer() =
          { new IComparer<GridViewModel> with
                member __.Compare(x: GridViewModel, y: GridViewModel): int = 
                  let dz = x.ZIndex - y.ZIndex
                  if dz = 0 then
                    y.GridId - x.GridId
                  else dz
          }


    (*******************   Events   ***********************)

    member __.OnKey (e: KeyEventArgs) = 
        raiseInputEvent _gridid <| InputEvent.Key(e.KeyModifiers, e.Key)

    member __.OnMouseDown (e: PointerPressedEventArgs) (root: Avalonia.VisualTree.IVisual) (gadgetsEnable: bool) = 
        if m_mouse_en then
            let x, y = e.GetPosition root |> getPos
            let button = updateMouseButton(e.GetCurrentPoint null)
            //raiseInputEvent _gridid <| InputEvent.MousePress(e.KeyModifiers, y, x, button)
            let _, _, vm, r, c, _ = this.FindTargetVm y x
            m_mouse_pressed_vm <- vm
            let wid = 
              if not gadgetsEnable then -1
              else vm.FindTargetWidget r c 
            if wid > 0 then
              m_mouse_pressed_widget <- wid
              model.GuiWidgetMouseDown vm.BufNr wid
            else
              raiseInputEvent vm.GridId <| InputEvent.MousePress(e.KeyModifiers, r, c, button)

    member __.OnMouseUp (e: PointerReleasedEventArgs) (root: Avalonia.VisualTree.IVisual) (gadgetsEnable: bool) = 
        if m_mouse_en then
            let x, y = e.GetPosition root |> getPos
            let button = updateMouseButton(e.GetCurrentPoint null)
            //raiseInputEvent _gridid <| InputEvent.MouseRelease(e.KeyModifiers, y, x, button)
            let ar,ac = m_mouse_pressed_vm.AbsAnchor
            let r, c = (y-ar),(x-ac)
            if m_mouse_pressed_widget > 0 then
              model.GuiWidgetMouseUp m_mouse_pressed_vm.BufNr m_mouse_pressed_widget
              m_mouse_pressed_widget <- -1
            else
              raiseInputEvent m_mouse_pressed_vm.GridId <| InputEvent.MouseRelease(e.KeyModifiers, r, c, button)

    member __.OnMouseMove (e: PointerEventArgs) (root: Avalonia.VisualTree.IVisual) (gadgetsEnable: bool) = 
        if m_mouse_en && m_mouse_pressed <> MouseButton.None then
            let x, y = e.GetPosition root |> getPos
            if (x,y) <> m_mouse_pos then
                m_mouse_pos <- x,y
                //trace m_mouse_pressed_vm.GridId "mousemove: %d %d" y x
                //raiseInputEvent _gridid <| InputEvent.MouseDrag(e.KeyModifiers, y, x, m_mouse_pressed)
                let ar,ac = m_mouse_pressed_vm.AbsAnchor
                let r,c = (y-ar),(x-ac)
                if gadgetsEnable && m_mouse_pressed_widget > 0 then 
                  ()
                else
                  #if DEBUG
                  trace m_mouse_pressed_vm.GridId "mousemove: %d %d" r c
                  #endif
                  raiseInputEvent m_mouse_pressed_vm.GridId <| InputEvent.MouseDrag(e.KeyModifiers, r, c, m_mouse_pressed)

    member __.OnMouseWheel (e: PointerWheelEventArgs) (root: Avalonia.VisualTree.IVisual) (gadgetsEnable: bool) = 
        if m_mouse_en then
            let x, y = e.GetPosition root |> getPos
            let _, _, vm, r, c, _ = this.FindTargetVm y x
            let dx, dy = e.Delta.X, e.Delta.Y
            raiseInputEvent vm.GridId <| InputEvent.MouseWheel(e.KeyModifiers, r, c, dx, dy)

    member __.OnTextInput (e: TextInputEventArgs) = 
        raiseInputEvent _gridid <| InputEvent.TextInput(e.Text)

namespace FVim

open FVim.neovim.def
open FVim.log

open Avalonia
open Avalonia.Controls
open Avalonia.Data
open Avalonia.Input
open Avalonia.Markup.Xaml
open Avalonia.Media
open System
open MessagePack

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
    let mutable font_family      = "Iosevka Slab"
    let mutable font_size        = 16.0
    let mutable glyph_size       = Size(1., 1.)

    let mutable typeface_normal  = null
    let mutable typeface_italic  = null
    let mutable typeface_bold    = null

    let mutable grid_size        = { rows = 100; cols=50 }
    let mutable grid_buffer: GridBufferCell[,]  = Array2D.create grid_size.rows grid_size.cols GridBufferCell.empty
    let mutable grid_dirty       = { row = 0; col = 0; height = 100; width = 50 }

    let mutable cursor_row       = 0
    let mutable cursor_col       = 0
    let mutable cursor_en        = false
    let mutable cursor_blinkshow = true
    let mutable cursor_modeidx   = -1

    let mutable mouse_en         = true

    let mutable is_ready         = false
    let mutable is_flushed       = false
    let mutable measured_size    = Size()

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

    let markAllDirty () =
        grid_dirty   <- { row = 0; col = 0; height = grid_size.rows; width = grid_size.cols }

    let setFont name size =
        font_family     <- name
        font_size       <- size
        typeface_normal <- Typeface(font_family, font_size, FontStyle.Normal, FontWeight.Regular)
        typeface_italic <- Typeface(font_family, font_size, FontStyle.Italic, FontWeight.Regular)
        typeface_bold   <- Typeface(font_family, font_size, FontStyle.Normal, FontWeight.Bold)

        let txt = FormattedText()
        txt.Text        <- "X"
        txt.Typeface    <- typeface_normal
        glyph_size      <- txt.Bounds.Size
        trace "setFont" "%A %A" glyph_size glyph_size
        this.InvalidateMeasure()

    let setCursor row col =
        markDirty { row = cursor_row; col = cursor_col; height = 1; width = 1 }
        cursor_row <- row
        cursor_col <- col
        markDirty { row = cursor_row; col = cursor_col; height = 1; width = 1 }

    let setHighlight x =
        if hi_defs.Length < x.id + 1 then
            Array.Resize(&hi_defs, x.id + 1)
            trace "setHighlight" "set hl attr size %d" hi_defs.Length
        hi_defs.[x.id] <- x
        markAllDirty()

    let setDefaultColors fg bg sp = 
        default_fg <- fg
        default_bg <- bg
        default_sp <- sp

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
        
    let flush() = 
        is_flushed <- true
        this.InvalidateVisual()

    let markClean () =
        is_flushed <- false
        grid_dirty <- { row = 0; col = 0; height = 0; width = 0}

    let clearBuffer () =
        trace "clearBuffer" "."
        grid_buffer  <- Array2D.create grid_size.rows grid_size.cols GridBufferCell.empty
        markAllDirty()

    let initBuffer nrow ncol =
        grid_size    <- { rows = nrow; cols = ncol }
        clearBuffer()

        trace "createBuffer" "glyph size %A" glyph_size
        trace "createBuffer" "grid buffer size %A" grid_size

    let putBuffer (line: GridLine) =
        let         row  = line.row
        let mutable col  = line.col_start
        let mutable hlid = -1
        let mutable rep = 1
        for cell in line.cells do
            hlid <- Option.defaultValue hlid cell.hl_id
            rep  <- Option.defaultValue 1 cell.repeat
            for i = 0 to rep-1 do
                grid_buffer.[row, col].hlid <- hlid
                grid_buffer.[row, col].text <- cell.text
                col <- col + 1
        let dirty = { row = row; col = line.col_start; height = 1; width = col - line.col_start } 
        //trace "putBuffer: writing to %A" dirty
        markDirty dirty

    let setModeInfo (cs_en: bool) (info: ModeInfo[]) =
        mode_defs <- info
        cursor_en <- cs_en

    let changeMode (name: string) (index: int) = 
        cursor_modeidx <- index
        markDirty { row = cursor_row; col = cursor_col; height = 1; width = 1 }

    let bell (visual: bool) =
        // TODO
        trace "neovim" "bell: %A" visual
        ()

    let setBusy (v: bool) =
        // TODO
        trace "neovim" "busy: %A" v
        ()

    let scrollBuffer (top: int) (bot: int) (left: int) (right: int) (rows: int) (cols: int) =
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

        // editor: scroll: 2 34 0 103 -1 0
        // height = 36;
        // width  = 103;
        trace "editor" "scroll: %A %A %A %A %A %A" top bot left right rows cols

        let copy src dst =
            if src >= 0 && src < grid_size.rows && dst >= 0 && dst < grid_size.rows then
                Array.Copy(grid_buffer, src * grid_size.cols + left, grid_buffer, dst * grid_size.cols + left, right - left)

        if rows > 0 then
            for i = top + 1 to bot - 1 do
                copy i (i-rows)
            markAllDirty()
        elif rows < 0 then
            for i = bot - 2 downto top do
                copy i (i-rows)
            markAllDirty()
        ()

    let setOptions (opts: UiOption[]) = 
        trace "setOptions" "%A" opts
        ()

    let setMouse (en:bool) =
        mouse_en <- en

    let redraw(cmd: RedrawCommand) =
        match cmd with
        | UnknownCommand x -> trace "redraw" "unknown command %A" x
        | HighlightAttrDefine hls -> Array.iter setHighlight hls
        | DefaultColorsSet(fg,bg,sp,_,_) -> setDefaultColors fg bg sp
        | ModeInfoSet(cs_en, info) -> setModeInfo cs_en info
        | ModeChange(name, index) -> changeMode name index
        | GridResize(id, w, h) -> initBuffer h w
        | GridClear id -> clearBuffer()
        | GridLine lines -> Array.iter putBuffer lines
        | GridCursorGoto(id, row, col) -> setCursor row col
        | GridDestroy id when id = this.GridId -> () // TODO
        | GridScroll(grid, top,bot,left,right,rows,cols) -> scrollBuffer top bot left right rows cols
        | Flush -> flush() 
        | Bell -> bell false
        | VisualBell -> bell true
        | Busy is_busy -> setBusy is_busy
        | SetOption opts -> setOptions opts
        | SetTitle title -> Application.Current.MainWindow.Title <- title
        | SetIcon icon -> trace "neovim" "icon: %s" icon // TODO
        | SetOption opts -> setOptions opts
        | Mouse en -> setMouse en
        //| _ -> ()

    let resizeEvent = Event<IGridUI>()
    let keyEvent = Event<KeyEventArgs>()

    let getDrawAttrs hlid = 
        let attrs = hi_defs.[hlid].rgb_attr
        let typeface = 
            if   attrs.italic then typeface_italic
            elif attrs.bold   then typeface_bold
            else                   typeface_normal

        let fg = Option.defaultValue default_fg attrs.foreground
        let bg = Option.defaultValue default_bg attrs.background
        let sp = Option.defaultValue default_sp attrs.special

        let bg_brush = SolidColorBrush(bg)
        let fg_brush = SolidColorBrush(fg)
        let sp_brush = SolidColorBrush(sp)

        fg_brush, bg_brush, sp_brush, typeface, attrs

    let getPoint row col =
        Point(double(col) * glyph_size.Width, double(row) * glyph_size.Height)

    do
        setFont font_family font_size
        AvaloniaXamlLoader.Load(this)

    interface IGridUI with
        member this.Id = this.GridId
        member this.GridHeight = int( measured_size.Height / glyph_size.Height )
        member this.GridWidth  = int( measured_size.Width  / glyph_size.Width  )
        member this.Connect cmds = cmds.Add (Array.iter redraw)
        member this.Resized = resizeEvent.Publish
        member this.KeyInput = keyEvent.Publish

    member this.OnReady _ =
        is_ready <- true
        measured_size <- this.Bounds.Size
        this.Focus()
        match this.DataContext with
        | :? FVimViewModel as ctx ->
            ctx.OnGridReady(this)
        | _ -> failwithf "%O" this.DataContext

    override this.OnKeyDown(e) =
        e.Handled <- true
        keyEvent.Trigger e

    override this.OnKeyUp(e) =
        e.Handled <- true

    override this.MeasureOverride(size) =
        trace "MeasureOverride" "%A" size
        let gridui = this :> IGridUI
        let gw, gh = gridui.GridWidth, gridui.GridHeight
        measured_size <- size
        flush()
        markAllDirty()
        let gw', gh' = gridui.GridWidth, gridui.GridHeight
        if gw <> gw' || gh <> gh' then 
            resizeEvent.Trigger(this)
        Size(double(gw') * glyph_size.Width, double(gh') * glyph_size.Height)

    override this.Render(ctx) =

        if (not is_ready) then this.OnReady()

        use transform = ctx.PushPreTransform(Matrix.CreateScale(1.0, 1.0))

        let drawText row col colend hlid invert =
            let topLeft                 = getPoint row col
            let bottomRight             = topLeft + getPoint 1 (colend - col) + Point(0., 0.5)
            let region                  = Rect(topLeft, bottomRight)
            let fg, bg, sp, typeface, _ = getDrawAttrs hlid

            let text = FormattedText()
            text.Text <- grid_buffer.[row, col..colend-1] |> Array.map (fun x -> x.text) |> String.concat ""
            text.Typeface <- typeface

            //trace "drawText: %d %d-%d hlid=%A" row col colend hlid
            if invert then
                ctx.FillRectangle(fg, region)
                ctx.DrawText(bg, topLeft, text)
            else
                ctx.FillRectangle(bg, region)
                ctx.DrawText(fg, topLeft, text)

        let doRenderBuffer() =
            trace "RENDER!" "dirty = %s" (grid_dirty.ToString())
            for y = grid_dirty.row to grid_dirty.row_end-1 do
                let mutable x0   = grid_dirty.col
                let mutable hlid = grid_buffer.[y, x0].hlid
                for x = grid_dirty.col to grid_dirty.col_end-1 do
                    let myhlid = grid_buffer.[y,x].hlid 
                    if myhlid <> hlid then
                        drawText y x0 x hlid false
                        hlid <- myhlid 
                        x0 <- x
                drawText y x0 grid_dirty.col_end hlid false
            // TODO find a way to retain render results
            //markClean()

        let doRenderCursor() =
            let mode  = mode_defs.[cursor_modeidx]
            let hlid  = grid_buffer.[cursor_row, cursor_col].hlid
            let hlid' = Option.defaultValue hlid mode.attr_id

            let hlid = if cursor_blinkshow then hlid' else hlid
            let fg, bg, _, _, _ = getDrawAttrs hlid
            let origin = getPoint cursor_row cursor_col

            trace "doRenderCursor" "hlid=%A" hlid

            let cellw p = min (double(p) / 100.0 * glyph_size.Width)  5.0
            let cellh p = min (double(p) / 100.0 * glyph_size.Height) 5.0

            match mode.cursor_shape, mode.cell_percentage with
            | Some(CursorShape.Block), _ ->
                drawText cursor_row cursor_col (cursor_col+1) hlid true
            | Some(CursorShape.Horizontal), Some p ->
                let region = Rect(origin + (getPoint 1 0), origin + (getPoint 1 1) - Point(0.0, cellh p))
                ctx.FillRectangle(bg, region)
            | Some(CursorShape.Vertical), Some p ->
                let region = Rect(origin, origin + (getPoint 1 0) + Point(cellw p, 0.0))
                ctx.FillRectangle(bg, region)
            | _ -> ()


        // do not actually draw the buffer unless there's a pending flush command
        if is_flushed then doRenderBuffer()
        if cursor_en then doRenderCursor()

    member val GridId: int = 0 with get, set

    static member GridIdProperty = 
        AvaloniaProperty.RegisterDirect<Editor, int>(
            "GridId", 
            (fun e -> e.GridId),
            (fun e v -> e.GridId <- v))

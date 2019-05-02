namespace FVim

open FVim.neovim.def

open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open Avalonia.Media
open System

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

    let mutable default_fg = Colors.Black
    let mutable default_bg = Colors.Black
    let mutable default_sp = Colors.Black

    let mutable hi_defs = Array.empty<HighlightAttr>
    let mutable font_family    = "Iosevka Slab"
    let mutable font_size      = 18.0
    let mutable glyph_size     = Size(1., 1.)

    let mutable typeface_normal  = Typeface(font_family, font_size, FontStyle.Normal, FontWeight.Regular)
    let mutable typeface_italic  = Typeface(font_family, font_size, FontStyle.Italic, FontWeight.Regular)
    let mutable typeface_oblique = Typeface(font_family, font_size, FontStyle.Oblique, FontWeight.Regular)
    let mutable typeface_bold    = Typeface(font_family, font_size, FontStyle.Oblique, FontWeight.Bold)

    let mutable grid_size = { rows = 100; cols=50 }
    let mutable grid_buffer: GridBufferCell[,]  = Array2D.create grid_size.rows grid_size.cols GridBufferCell.empty
    let mutable grid_dirty = { row = 0; col = 0; height = 100; width = 50 }

    let mutable cursor_row = 0
    let mutable cursor_col = 0

    let setDefaultColors fg bg sp = 
        default_fg <- fg
        default_bg <- bg
        default_sp <- sp

    let markClean () =
        grid_dirty <- { row = 0; col = 0; height = 0; width = 0}

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
        printfn "markDirty: region is now %A" grid_dirty

    let markAllDirty () =
        grid_dirty   <- { row = 0; col = 0; height = grid_size.rows; width = grid_size.cols }

    let clearBuffer () =
        printfn "clearBuffer."
        grid_buffer  <- Array2D.create grid_size.rows grid_size.cols GridBufferCell.empty
        markAllDirty()

    let initBuffer nrow ncol =
        let txt = FormattedText()
        txt.Text <- "X"
        txt.Typeface <- typeface_normal
        glyph_size   <- txt.Bounds.Size
        grid_size    <- { rows = nrow; cols = ncol }
        clearBuffer()

        printfn "createBuffer: glyph size %A" glyph_size
        printfn "createBuffer: grid buffer size %A" grid_size

    let putBuffer (line: GridLine) =
        let         row  = line.row
        let mutable col  = line.col_start
        let mutable hlid = -1
        let mutable rep = 1
        for cell in line.cells do
            hlid <- Option.defaultValue hlid cell.hl_id
            rep  <- Option.defaultValue 1 cell.repeat
            for i in 0..rep-1 do
                grid_buffer.[row, col].hlid <- hlid
                grid_buffer.[row, col].text <- cell.text
                col <- col + 1
        let dirty = { row = row; col = line.col_start; height = 1; width = col - line.col_start } 
        // printfn "putBuffer: writing to %A" dirty
        markDirty dirty

    let setCursor row col =
        markDirty { row = cursor_row; col = cursor_col; height = 1; width = 1 }
        cursor_row <- row
        cursor_col <- col
        markDirty { row = cursor_row; col = cursor_col; height = 1; width = 1 }

    let setHighlight x =
        if hi_defs.Length < x.id + 1 then
            Array.Resize(&hi_defs, x.id + 1)
            printfn "set hl attr size %d" hi_defs.Length
        hi_defs.[x.id] <- x
        markAllDirty()

    let redraw(cmd: RedrawCommand) =
        match cmd with
        | UnknownCommand x -> printfn "redraw: unknown command %A" x
        | HighlightAttrDefine hls -> Array.iter setHighlight hls
        | DefaultColorsSet(fg,bg,sp,_,_) -> setDefaultColors fg bg sp
        | ModeInfoSet(cs_en, info) -> ()
        | GridResize(id, w, h) -> initBuffer h w
        | GridClear id -> clearBuffer()
        | GridLine lines -> Array.iter putBuffer lines
        | GridCursorGoto(id, row, col) -> setCursor row col
        | Flush -> this.InvalidateVisual()
        | _ -> ()

    do
        Array.Resize(&hi_defs, 256)
        AvaloniaXamlLoader.Load(this)

    override this.OnDataContextChanged(_) =

        match this.DataContext with
        | :? FVimViewModel as ctx ->
            ctx.RedrawCommands.Add (Array.iter redraw)
        | _ -> failwithf "%O" this.DataContext

    //let mutable m_font = Typeface(m_defa)
    //member this.NrLines =

    override this.Render(ctx) =
        use transform = ctx.PushPreTransform(Matrix.CreateScale(1.0, 1.0))

        let w, h = this.Bounds.Width, this.Bounds.Height
        printfn "RENDER! my size is: %f %f" w h

        let drawText row col colend hlid =
            let topLeft     = Point(double(col) * glyph_size.Width, double(row) * glyph_size.Height)
            let bottomRight = topLeft + Point(double(colend - col) * glyph_size.Width, glyph_size.Height)
            let region      = Rect(topLeft, bottomRight)

            let attrs = hi_defs.[hlid].rgb_attr

            let fg = Option.defaultValue default_fg attrs.foreground
            let bg = Option.defaultValue default_bg attrs.background
            let sp = Option.defaultValue default_sp attrs.special

            let typeface = 
                if   attrs.italic then typeface_italic
                elif attrs.bold   then typeface_bold
                else                   typeface_normal

            let bg_brush = SolidColorBrush(bg)
            let fg_brush = SolidColorBrush(fg)

            let text = FormattedText()
            text.Text <- grid_buffer.[row, col..colend-1] |> Array.map (fun x -> x.text) |> String.concat ""
            text.Typeface <- typeface

            // printfn "drawText: %A" text.Text

            // draw background
            ctx.FillRectangle(bg_brush, region)
            ctx.DrawText(fg_brush, topLeft, text)

        for y in grid_dirty.row..grid_dirty.row_end-1 do
            let mutable x0   = grid_dirty.col
            let mutable hlid = grid_buffer.[y, x0].hlid
            for x in grid_dirty.col..grid_dirty.col_end-1 do
                let myhlid = grid_buffer.[y,x].hlid 
                if myhlid <> hlid then
                    drawText y x0 x hlid
                    hlid <- myhlid 
                    x0 <- x
            drawText y x0 grid_dirty.col_end hlid

        markClean()

    //member this.RedrawCommands
    //    with get() : IEvent<RedrawCommand[]> = this.GetValue(Editor.RedrawCommandsProperty)
    //    and  set(v) = this.SetValue(Editor.RedrawCommandsProperty, v)

    //static member RedrawCommandsProperty = AvaloniaProperty.Register<Editor, IEvent<RedrawCommand[]>>("RedrawCommands")


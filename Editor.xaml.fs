namespace FVim
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Controls.Primitives
open FVim.neovim.def
open System
open Avalonia
open Avalonia.Markup.Xaml
open Avalonia.Media.Imaging
open Avalonia.Native
open Avalonia.Native.Interop
open Avalonia.Rendering
open System.Collections.Generic

[<Struct>]
type private GridBufferCell =
    {
        mutable txt:  char
        mutable hlid: int32
    }

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

type Editor() as this =
    inherit Control()

    let mutable hi_defs = Array.empty<HighlightAttr>
    let mutable font_family    = "Iosevka Slab"
    let mutable font_size      = 18.0
    let mutable glyph_size     = Size(1., 1.)

    let mutable typeface_normal  = Typeface(font_family, font_size, FontStyle.Normal, FontWeight.Regular)
    let mutable typeface_italic  = Typeface(font_family, font_size, FontStyle.Italic, FontWeight.Regular)
    let mutable typeface_oblique = Typeface(font_family, font_size, FontStyle.Oblique, FontWeight.Regular)
    let mutable typeface_bold    = Typeface(font_family, font_size, FontStyle.Oblique, FontWeight.Bold)

    let mutable grid_size = { rows = 100; cols=50 }
    let mutable grid_buffer: GridBufferCell[,]  = Array2D.zeroCreate grid_size.rows grid_size.cols
    let mutable grid_dirty = { row = 0; col = 0; height = 100; width = 50 }

    let mutable cursor_row = 0
    let mutable cursor_col = 0

    let markDirty (region: GridRect)

    let clearBuffer () =
        printfn "clearBuffer."
        grid_buffer  <- Array2D.zeroCreate grid_size.rows grid_size.cols

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
        ()

    let setCursor row col =
        cursor_row <- row
        cursor_col <- col
        ()

    let setHighlight x =
        if hi_defs.Length < x.id + 1 then
            Array.Resize(&hi_defs, x.id + 1)
            printfn "set hl attr size %d" hi_defs.Length
        hi_defs.[x.id] <- x


    let redraw(cmd: RedrawCommand) =
        match cmd with
        | UnknownCommand x -> printfn "redraw: unknown command %A" x
        | HighlightAttrDefine hls -> Array.iter setHighlight hls
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
        ctx.FillRectangle(Brushes.Red, Rect(10., 10., w/2., h/2.))

    //member this.RedrawCommands
    //    with get() : IEvent<RedrawCommand[]> = this.GetValue(Editor.RedrawCommandsProperty)
    //    and  set(v) = this.SetValue(Editor.RedrawCommandsProperty, v)

    //static member RedrawCommandsProperty = AvaloniaProperty.Register<Editor, IEvent<RedrawCommand[]>>("RedrawCommands")


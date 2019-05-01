namespace FVim
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Controls.Primitives
open FVim.neovim.def
open System
open Avalonia
open Avalonia.Markup.Xaml
open Avalonia.Media.Imaging

type Editor() as this =
    inherit Control()

    let mutable highlightDefs = Array.empty<HighlightAttr>
    let mutable fontFamily    = "Iosevka Slab"
    let mutable fontSize      = 18.0
    let mutable fontHeight    = 0.0
    let mutable fontWidth     = 0.0

    let mutable typeface_normal  = Typeface(fontFamily, fontSize, FontStyle.Normal, FontWeight.Regular)
    let mutable typeface_italic  = Typeface(fontFamily, fontSize, FontStyle.Italic, FontWeight.Regular)
    let mutable typeface_oblique = Typeface(fontFamily, fontSize, FontStyle.Oblique, FontWeight.Regular)
    let mutable typeface_bold    = Typeface(fontFamily, fontSize, FontStyle.Oblique, FontWeight.Bold)

    let mutable grid_h = 50
    let mutable grid_w = 100

    let createBuffer() =
        let txt = FormattedText()
        txt.Text <- "X"
        txt.Typeface <- typeface_normal
        let w, h = txt.Bounds, txt.Bounds.Height
        let buffer = RenderTargetBitmap()

    let redraw(cmd: RedrawCommand) =
        match cmd with
        | UnknownCommand x ->
            printfn "redraw: unknown command %A" x
        | HighlightAttrDefine hls ->
            hls |> Array.iter (fun x ->
                if highlightDefs.Length < x.id + 1 then
                    Array.Resize(&highlightDefs, x.id + 1)
                    printfn "set hl attr size %d" highlightDefs.Length
                highlightDefs.[x.id] <- x
            )
        | ModeInfoSet(cs_en, info) ->
            ()
        | GridResize(id, w, h) ->
            grid_h <- h
            grid_w <- w
            createBuffer()
        | Flush ->
            this.InvalidateVisual()
            ()
        | _ -> ()

    do
        Array.Resize(&highlightDefs, 256)
        AvaloniaXamlLoader.Load(this)

    override this.OnDataContextChanged(_) =

        match this.DataContext with
        | :? FVimViewModel as ctx ->
            ctx.RedrawCommands.Add (Array.iter redraw)
        | _ -> failwithf "%O" this.DataContext

    //let mutable m_font = Typeface(m_defa)
    //member this.NrLines =

    override this.Render(ctx) =
        //use transform = ctx.PushPreTransform(Matrix.CreateScale(1.0, 1.0))
        //this.Width <- this.Parent.Width
        //this.Height <- this.Parent.Height

        let w, h = this.Bounds.Width, this.Bounds.Height

        printfn "RENDER! my size is: %f %f" w h
        //ctx.FillRectangle(Brushes.Red, Rect(10., 10., w/2., h/2.))

    //member this.RedrawCommands
    //    with get() : IEvent<RedrawCommand[]> = this.GetValue(Editor.RedrawCommandsProperty)
    //    and  set(v) = this.SetValue(Editor.RedrawCommandsProperty, v)

    //static member RedrawCommandsProperty = AvaloniaProperty.Register<Editor, IEvent<RedrawCommand[]>>("RedrawCommands")


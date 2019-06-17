namespace FVim

open wcwidth

open ReactiveUI
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Input
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform
open Avalonia.Skia
open SkiaSharp
open SkiaSharp.HarfBuzz
open System
open System.Reactive.Disposables
open System.Reflection
open System.Runtime.CompilerServices

[<Extension>]
type ActivatableExt() =
    [<Extension>]
    static member inline Watch (this: IActivatable, xs: IDisposable seq) =
        this.WhenActivated(fun (disposables: CompositeDisposable) ->
            xs |> Seq.iter (fun x -> x.DisposeWith(disposables) |> ignore)) |> ignore
    [<Extension>]
    static member inline Watch (this: ISupportsActivation, xs: IDisposable seq) =
        this.WhenActivated(fun (disposables: CompositeDisposable) ->
            xs |> Seq.iter (fun x -> x.DisposeWith(disposables) |> ignore)) |> ignore
    [<Extension>]
    static member inline Do (this: IActivatable, fn: unit -> unit) =
        do fn()
        Disposable.Empty
    [<Extension>]
    static member inline Do (this: ISupportsActivation, fn: unit -> unit) =
        do fn()
        Disposable.Empty

type ViewModelBase() =
    inherit ReactiveObject()
    let activator = new ViewModelActivator()
    interface ISupportsActivation with
        member __.Activator = activator

type IViewModelContainer =
    abstract Target: obj

type ViewLocator() =
    interface IDataTemplate with
        member this.Build(data: obj): Avalonia.Controls.IControl = 
            match data with
            | :? IViewModelContainer as container -> (this :> IDataTemplate).Build(container.Target)
            | _ ->
            let _name = data.GetType().FullName.Replace("ViewModel", "");
            let _type = Type.GetType(_name);
            if _type <> null 
            then Activator.CreateInstance(_type) :?> IControl;
            else TextBlock( Text = "Not Found: " + _name ) :> IControl
        member this.Match(data: obj): bool = data :? ViewModelBase || data :? IViewModelContainer
        member this.SupportsRecycling: bool = false


module ui =

    let mutable antialiased = true
    let mutable drawBounds  = false
    let mutable autohint    = true
    let mutable subpixel    = true
    let mutable lcdrender   = true
    let mutable hintLevel   = SKPaintHinting.Full

    let setHintLevel (v: string) = 
        match v.ToLower() with
        | "none" -> hintLevel   <- SKPaintHinting.NoHinting
        | "slight" -> hintLevel <- SKPaintHinting.Slight
        | "normal" -> hintLevel <- SKPaintHinting.Normal
        | "full" -> hintLevel   <- SKPaintHinting.Full
        | _ -> ()

    type InputEvent = 
    | Key          of mods: InputModifiers * key: Key
    | MousePress   of mods: InputModifiers * row: int * col: int * button: MouseButton * combo: int
    | MouseRelease of mods: InputModifiers * row: int * col: int * button: MouseButton
    | MouseDrag    of mods: InputModifiers * row: int * col: int * button: MouseButton
    | MouseWheel   of mods: InputModifiers * row: int * col: int * dx: float * dy: float
    | TextInput    of text: string


    [<Struct>]
    type GridBufferCell =
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

    let inline private (<<->) a b = fun x -> a <= x && x < b
    let inline private (<->>) a b = fun x -> a < x && x <= b

    [<Struct>]
    type GridRect =
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
        member x.Contains (y: GridRect) =
            y.row     |> (x.row <<-> x.row_end) &&
            y.col     |> (x.col <<-> x.col_end) &&
            y.row_end |> (x.row <->> x.row_end) &&
            y.col_end |> (x.col <->> x.col_end)


        static member Compare (x: GridRect) (y: GridRect) =
            let row = x.row - y.row
            if row <> 0 then row
            else

            let col = x.col - y.col
            if col <> 0 then col
            else

            let height = x.height - y.height
            if height <> 0 then height
            else

            x.width - y.width

    type GridRegion() =
        let rects = ResizeArray<GridRect>()
        member x.Empty() = rects.Count = 0
        member x.Clear = rects.Clear
        member x.Union = rects.Add
        member x.Regions() = 
            let region = ResizeArray()
            for x in rects do
                region
                |> Seq.indexed
                |> Seq.tryPick (fun (i, y) ->
                    // case 1: containment
                    if x.Contains y then Some(i, x)
                    elif y.Contains x then Some(i, y)
                    elif x.row = y.row && 
                        x.height = y.height &&
                        (x.col = y.col_end || x.col_end = y.col) then
                            Some(i, { x with col = min x.col y.col
                                             width = x.width + y.width })
                    elif x.col = y.col && 
                        x.width = y.width &&
                        (x.row = y.row_end || x.row_end = y.row) then
                            Some(i, { x with row = min x.row y.row
                                             height = x.height + y.height })
                    else None
                )
                |> function
                | Some(i, x) -> region.[i] <- x
                | None -> region.Add x
            region


    /// Represents a grid in neovim
    type IGridUI =
        abstract Id: int
        /// Number of rows
        abstract GridHeight: int
        /// Number of columns
        abstract GridWidth: int
        abstract Resized: IEvent<IGridUI>
        abstract Input: IEvent<InputEvent>

    open System.Runtime.InteropServices

    let DefaultFont =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "Consolas"
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "Monospace"
        elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX)   then "Menlo"
        else "Monospace"

    let DefaultFontWide = 
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "DengXian"
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "Noto Sans CJK SC"
        elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX)   then "Heiti SC"
        else "Simsun"

    let DefaultFontEmoji =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "Segoe UI Emoji"
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "Noto Color Emoji" // ?
        elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX)   then "Apple Color Emoji"
        else "Noto Color Emoji"

    let private nerd_typeface = SKTypeface.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream("fvim.nerd.ttf"))
    let private emoji_typeface = SKTypeface.FromFamilyName(DefaultFontEmoji)
    let private fontcache = System.Collections.Generic.Dictionary<string*bool*bool, SKTypeface>()

    let GetReverseColor (c: Color) =
        let inv = UInt32.MaxValue - c.ToUint32()
        Color.FromUInt32(inv ||| 0xFF000000u)

    let GetTypeface(txt, italic, bold, font, wfont) =
        let w = wswidth txt

        let _get fname =
            match fontcache.TryGetValue((fname, italic, bold)) with
            | true, typeface -> typeface
            | _ ->
                let weight   = if bold then SKFontStyleWeight.Bold else SKFontStyleWeight.Normal
                let width    = SKFontStyleWidth.Normal
                let slang    = if italic then SKFontStyleSlant.Italic else SKFontStyleSlant.Upright
                let typeface = SKTypeface.FromFamilyName(fname, weight, width, slang)
                fontcache.[(fname, italic, bold)] <- typeface
                typeface

        let wfont = if String.IsNullOrEmpty wfont then font else wfont

        match w with
        | CharType.Wide  -> _get wfont
        | CharType.Nerd  -> nerd_typeface
        | CharType.Emoji -> emoji_typeface
        | CharType.Powerline -> nerd_typeface
        | _              -> _get font

    let MeasureText (str: string, font: string, wfont: string, fontSize: float) =
        use paint = new SKPaint()
        paint.Typeface <- GetTypeface(str, false, false, font, wfont)
        paint.TextSize <- single fontSize
        paint.IsAntialias <- antialiased
        paint.IsAutohinted <- autohint
        paint.IsLinearText <- false
        paint.HintingLevel <- hintLevel
        paint.LcdRenderText <- lcdrender
        paint.SubpixelText <- subpixel
        paint.TextAlign <- SKTextAlign.Left
        paint.DeviceKerningEnabled <- false
        paint.TextEncoding <- SKTextEncoding.Utf16

        let w = paint.MeasureText str
        let h = paint.FontSpacing

        w, h
         
    let AllocateFramebuffer w h scale =
        let pxsize        = PixelSize(int <| (w * scale), int <| (h * scale))
        new RenderTargetBitmap(pxsize, Vector(96.0 * scale, 96.0 * scale))

    let SetOpacity (paint: SKPaint) (opacity: float) =
        paint.Color <- paint.Color.WithAlpha(byte <| opacity * 255.0)

    let SetForegroundBrush(fgpaint: SKPaint, c: Color, fontFace: SKTypeface, fontSize: float) =
        fgpaint.Color                <- c.ToSKColor()
        fgpaint.Typeface             <- fontFace
        fgpaint.TextSize             <- single fontSize
        fgpaint.IsAntialias          <- antialiased
        fgpaint.IsAutohinted         <- autohint
        fgpaint.IsLinearText         <- false
        fgpaint.HintingLevel         <- hintLevel
        fgpaint.LcdRenderText        <- lcdrender
        fgpaint.SubpixelText         <- subpixel
        fgpaint.TextAlign            <- SKTextAlign.Left
        fgpaint.DeviceKerningEnabled <- false
        fgpaint.TextEncoding         <- SKTextEncoding.Utf16
        ()

    let RenderText (ctx: IDrawingContextImpl, region: Rect, fg: SKPaint, bg: SKPaint, sp: SKPaint, underline: bool, undercurl: bool, text: string, useShaping: bool) =
        //  DrawText accepts the coordinate of the baseline.
        //  h = [padding space 1] + above baseline | below baseline + [padding space 2]
        let h = region.Bottom - region.Y
        //  total_padding = padding space 1 + padding space 2
        let total_padding = h + float fg.FontMetrics.Top - float fg.FontMetrics.Bottom
        let baseline      = region.Y - float fg.FontMetrics.Top + (total_padding / 2.8)
        let fontPos       = Point(region.X, floor baseline)

        let skia = ctx :?> ISkiaDrawingContextImpl

        //lol wat??
        //fg.Shader <- SKShader.CreateCompose(SKShader.CreateColor(fg.Color), SKShader.CreatePerlinNoiseFractalNoise(0.1F, 0.1F, 1, 6.41613F))

        skia.SkCanvas.DrawRect(region.ToSKRect(), bg)
        if not <| String.IsNullOrWhiteSpace text then
            if useShaping then
                use shaper = new SKShaper(fg.Typeface)
                skia.SkCanvas.DrawShapedText(shaper, text.TrimEnd(), single fontPos.X, single fontPos.Y, fg)
            else 
                skia.SkCanvas.DrawText(text.TrimEnd(), fontPos.ToSKPoint(), fg)


        // Text bounding box drawing:
        // --------------------------------------------------
        if drawBounds then
            let mutable bounds = SKRect()
            let text = if String.IsNullOrEmpty text then " " else text
            ignore <| fg.MeasureText(text, &bounds)
            bounds.Left <- bounds.Left + single (fontPos.X)
            bounds.Top <- bounds.Top + single (fontPos.Y)
            bounds.Right <- bounds.Right + single (fontPos.X)
            bounds.Bottom <- bounds.Bottom + single (fontPos.Y)
            fg.Style <- SKPaintStyle.Stroke
            skia.SkCanvas.DrawRect(bounds, fg)
        // --------------------------------------------------

        let sp_thickness = fg.FontMetrics.UnderlineThickness.GetValueOrDefault(1.0F)

        if underline then
            let underline_pos = fg.FontMetrics.UnderlinePosition.GetValueOrDefault()
            let p1 = fontPos + Point(0.0, float <| underline_pos)
            let p2 = p1 + Point(region.Width, 0.0)
            sp.Style <- SKPaintStyle.Stroke
            //sppaint.StrokeWidth <- sp_thickness
            skia.SkCanvas.DrawLine(p1.ToSKPoint(), p2.ToSKPoint(), sp)

        if undercurl then
            let underline_pos  = fg.FontMetrics.UnderlinePosition.GetValueOrDefault()
            let mutable px, py = single fontPos.X, single fontPos.Y 
            py <- py + underline_pos
            let qf             = 1.5F
            let hf             = qf * 2.0F
            let q3f            = qf * 3.0F
            let ff             = qf * 4.0F
            let r              = single region.Right
            let py1            = py - 2.0f
            let py2            = py + 2.0f
            sp.Style <- SKPaintStyle.Stroke
            sp.StrokeWidth <- sp_thickness
            use path = new SKPath()
            path.MoveTo(px, py)
            while px < r do
                path.LineTo(px,       py)
                path.LineTo(px + qf,  py1)
                path.LineTo(px + hf,  py)
                path.LineTo(px + q3f, py2)
                px <- px + ff
            skia.SkCanvas.DrawPath(path , sp)

namespace FVim

open wcwidth

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Input
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform
open Avalonia.Skia
open ReactiveUI
open SkiaSharp
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
    let activator = ViewModelActivator()
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

    type InputEvent = 
    | Key          of mods: InputModifiers * key: Key
    | MousePress   of mods: InputModifiers * row: int * col: int * button: MouseButton * combo: int
    | MouseRelease of mods: InputModifiers * row: int * col: int * button: MouseButton
    | MouseDrag    of mods: InputModifiers * row: int * col: int * button: MouseButton
    | MouseWheel   of mods: InputModifiers * row: int * col: int * dx: int * dy: int
    | TextInput    of text: string

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
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "Droid Sans Mono"
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
                let weight   = if bold then SKFontStyleWeight.Medium else SKFontStyleWeight.Normal
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

    let GetTypefaceA(txt, italic, bold, font, wfont, fontSize) =
        let typeface = GetTypeface(txt, italic, bold, font, wfont)
        let style = if italic then FontStyle.Italic else FontStyle.Normal
        let weight = if bold then FontWeight.Bold else FontWeight.Normal
        Typeface(typeface.FamilyName, fontSize, style, weight)

    let MeasureText (str: string, font: string, wfont: string, fontSize: float) =
        use paint = new SKPaint()
        paint.Typeface <- GetTypeface(str, false, true, font, wfont)
        paint.TextSize <- single fontSize
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
         
    let AllocateFramebuffer w h scale =
        let pxsize        = PixelSize(int <| (w * scale), int <| (h * scale))
        new RenderTargetBitmap(pxsize, Vector(96.0 * scale, 96.0 * scale))


    let GetForegroundBrush(c: Color, fontFace: SKTypeface, fontSize: float) =
        let paint                   = new SKPaint(Color = c.ToSKColor())
        paint.Typeface             <- fontFace
        paint.TextSize             <- single fontSize
        paint.IsAntialias          <- true
        paint.IsAutohinted         <- true
        paint.IsLinearText         <- false
        paint.HintingLevel         <- SKPaintHinting.Full
        paint.LcdRenderText        <- true
        paint.SubpixelText         <- true
        paint.TextAlign            <- SKTextAlign.Left
        paint.DeviceKerningEnabled <- false
        paint.TextEncoding         <- SKTextEncoding.Utf16
        paint

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
    let inline Rounding v scale = ceil(v * scale) / scale
    // let rounding_v (pt: Point) =
    //     let px = pt * grid_scale 
    //     Point(ceil px.X, ceil px.Y) / grid_scale 

    let RenderText (ctx: IDrawingContextImpl, region: Rect, fg: SKPaint, _bg: Color, _sp: Color, underline: bool, undercurl: bool, text: string) =
        //  DrawText accepts the coordinate of the baseline.
        //  h = [padding space 1] + above baseline | below baseline + [padding space 2]
        let h = region.Bottom - region.Y
        //  total_padding = padding space 1 + padding space 2
        let total_padding = h + float fg.FontMetrics.Top - float fg.FontMetrics.Bottom
        let baseline      = region.Y - float fg.FontMetrics.Top + (total_padding / 2.8)
        let fontPos       = Point(region.X, baseline)

        let skia = ctx :?> DrawingContextImpl

        use bg = new SKPaint(Color = _bg.ToSKColor())
        use sp = new SKPaint(Color = _sp.ToSKColor())

        //lol wat??
        //fg.Shader <- SKShader.CreateCompose(SKShader.CreateColor(fg.Color), SKShader.CreatePerlinNoiseFractalNoise(0.1F, 0.1F, 1, 6.41613F))

        skia.Canvas.DrawRect(region.ToSKRect(), bg)
        skia.Canvas.DrawText(text, fontPos.ToSKPoint(), fg)

        // Text bounding box drawing:
        // --------------------------------------------------
        // let bounds = ref <| SKRect()
        // ignore <| fg.MeasureText(String.Concat str, bounds)
        // let mutable bounds = !bounds
        // bounds.Left <- bounds.Left + single (fontPos.X)
        // bounds.Top <- bounds.Top + single (fontPos.Y)
        // bounds.Right <- bounds.Right + single (fontPos.X)
        // bounds.Bottom <- bounds.Bottom + single (fontPos.Y)
        // fg.Style <- SKPaintStyle.Stroke
        // skia.Canvas.DrawRect(bounds, fg)
        // --------------------------------------------------

        let sp_thickness = fg.FontMetrics.UnderlineThickness.GetValueOrDefault(1.0F)

        if underline then
            let underline_pos = fg.FontMetrics.UnderlinePosition.GetValueOrDefault()
            let p1 = fontPos + Point(0.0, float <| underline_pos)
            let p2 = p1 + Point(region.Width, 0.0)
            sp.Style <- SKPaintStyle.Stroke
            //sp.StrokeWidth <- sp_thickness
            skia.Canvas.DrawLine(p1.ToSKPoint(), p2.ToSKPoint(), sp)

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
            skia.Canvas.DrawPath(path , sp)

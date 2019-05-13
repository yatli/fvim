module FVim.ui

open wcwidth
open FVim.neovim.def
open Avalonia.Input
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform
open Avalonia
open SkiaSharp
open Avalonia.Skia
open System.Reflection
open System

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
    abstract Connect: IEvent<RedrawCommand[]> -> IEvent<int> -> unit
    /// Number of rows
    abstract GridHeight: int
    /// Number of columns
    abstract GridWidth: int
    abstract Resized: IEvent<IGridUI>
    abstract Input: IEvent<InputEvent>

let private nerd_typeface = SKTypeface.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream("fvim.nerd.ttf"))
let private fontcache = System.Collections.Generic.Dictionary<string*bool*bool, SKTypeface>()

let GetTypeface(txt, italic, bold, font, wfont) =
    let w = wswidth txt

    let _get fname =
        match fontcache.TryGetValue((fname, italic, bold)) with
        | true, typeface -> typeface
        | _ ->
            let weight   = if bold then SKFontStyleWeight.Medium else SKFontStyleWeight.Thin
            let width    = SKFontStyleWidth.Normal
            let slang    = if italic then SKFontStyleSlant.Italic else SKFontStyleSlant.Upright
            let typeface = SKTypeface.FromFamilyName(fname, weight, width, slang)
            fontcache.[(fname, italic, bold)] <- typeface
            typeface

    let wfont = if String.IsNullOrEmpty wfont then font else wfont

    match w with
    | CharType.Wide -> _get wfont
    | CharType.Nerd -> nerd_typeface
    | _             -> _get font

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

let RenderText (ctx: IDrawingContextImpl, region: Rect, fg: SKPaint, _bg: Color, _sp: Color, underline: bool, undercurl: bool, text: string) =
    //  DrawText accepts the coordinate of the baseline.
    //  h = [padding space 1] + above baseline | below baseline + [padding space 2]
    let h = region.Bottom - region.Y
    //  total_padding = padding space 1 + padding space 2
    let total_padding = h + float fg.FontMetrics.Top - float fg.FontMetrics.Bottom
    let baseline      = region.Y - float fg.FontMetrics.Top + (total_padding / 2.8)
    let fontPos       = Point(region.X, floor baseline)

    let skia = ctx :?> DrawingContextImpl

    use bg = new SKPaint(Color = _bg.ToSKColor())
    use sp = new SKPaint(Color = _sp.ToSKColor())

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

    if underline then
        let underline_pos = fg.FontMetrics.UnderlinePosition.GetValueOrDefault()
        let p1 = fontPos + Point(0.0, float <| underline_pos)
        let p2 = p1 + Point(region.Width, 0.0)
        sp.Style <- SKPaintStyle.Stroke
        skia.Canvas.DrawLine(p1.ToSKPoint(), p2.ToSKPoint(), sp)

    if undercurl then
        let underline_pos  = fg.FontMetrics.UnderlinePosition.GetValueOrDefault()
        let mutable px, py = single fontPos.X, single fontPos.Y 
        py <- py + underline_pos
        let qf             = 0.5F
        let hf             = qf * 2.0F
        let q3f            = qf * 3.0F
        let ff             = qf * 4.0F
        let r              = single region.Right
        let py1            = py - 2.0f
        let py2            = py + 2.0f
        sp.Style <- SKPaintStyle.Stroke
        use path = new SKPath()
        path.MoveTo(px, py)
        while px < r do
            path.LineTo(px,       py)
            path.LineTo(px + qf,  py1)
            path.LineTo(px + hf,  py)
            path.LineTo(px + q3f, py2)
            px <- px + ff
        skia.Canvas.DrawPath(path , sp)

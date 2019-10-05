module FVim.ui

open def
open common
open wcwidth
open log

open ReactiveUI
open Avalonia
open Avalonia.Input
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform
open Avalonia.Skia
open SkiaSharp
open SkiaSharp.HarfBuzz
open System
open System.Reflection

type InputEvent = 
| Key          of mods: KeyModifiers * key: Key
| MousePress   of mods: KeyModifiers * row: int * col: int * button: MouseButton
| MouseRelease of mods: KeyModifiers * row: int * col: int * button: MouseButton
| MouseDrag    of mods: KeyModifiers * row: int * col: int * button: MouseButton
| MouseWheel   of mods: KeyModifiers * row: int * col: int * dx: float * dy: float
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
        let rects = ResizeArray<_>(rects)
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
    abstract Input: IEvent<int*InputEvent>
    abstract HasChildren: bool
    abstract Redraw: RedrawCommand -> unit
    abstract AddChild: int -> int -> int -> IGridUI

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
let private nerd_shaper = new SKShaper(nerd_typeface)
let private emoji_typeface = SKTypeface.FromFamilyName(DefaultFontEmoji)
let private emoji_shaper = new SKShaper(emoji_typeface)
let private fontcache = System.Collections.Generic.Dictionary<string*bool*bool, SKShaper*SKTypeface>()

let private InvalidateFontCache () =
    List.ofSeq fontcache.Keys
    |> List.iter (fun k ->
        let (shaper,font) = fontcache.[k]
        shaper.Dispose()
        font.Dispose()
        ignore(fontcache.Remove k)
    )

ignore(States.Register.Watch "font" InvalidateFontCache)

let GetReverseColor (c: Color) =
    let r = 255uy - c.R
    let g = 255uy - c.G
    let b = 255uy - c.B
    Color(255uy, r, g, b)

let GetTypeface(txt, italic, bold, font, wfont) =
    let w = wswidth txt

    let _get fname =
        match fontcache.TryGetValue((fname, italic, bold)) with
        | true, (shaper, typeface) -> (shaper, typeface)
        | _ ->
            trace "ui" "GetTypeface: allocating new typeface %s:%b:%b" fname italic bold
            let weight   = if bold then States.font_weight_bold else States.font_weight_normal
            let width    = SKFontStyleWidth.Normal
            let slang    = if italic then SKFontStyleSlant.Italic else SKFontStyleSlant.Upright
            let typeface = SKTypeface.FromFamilyName(fname, weight, width, slang)
            let shaper   = new SKShaper(typeface)
            fontcache.[(fname, italic, bold)] <- (shaper, typeface)
            (shaper, typeface)

    let wfont = if String.IsNullOrEmpty wfont then font else wfont

    match w with
    | CharType.Wide  -> _get wfont
    | CharType.Powerline
    | CharType.Nerd  -> (nerd_shaper, nerd_typeface)
    | CharType.Emoji -> (emoji_shaper, emoji_typeface)
    | _              -> _get font

let MeasureText (str: string, font: string, wfont: string, fontSize: float, scaling: float) =
    use paint = new SKPaint()
    paint.Typeface <- snd <| GetTypeface(str, false, false, font, wfont)
    paint.TextSize <- single fontSize
    paint.IsAntialias <- States.font_antialias
    paint.IsAutohinted <- States.font_autohint
    paint.IsLinearText <- false
    paint.HintingLevel <- States.font_hintLevel
    paint.LcdRenderText <- States.font_lcdrender
    paint.SubpixelText <- States.font_subpixel
    paint.TextAlign <- SKTextAlign.Left
    paint.DeviceKerningEnabled <- false
    paint.TextEncoding <- SKTextEncoding.Utf16

    let mutable score = 999999999999.0
    let mutable s = fontSize
    let mutable w = 0.0
    let mutable h = 0.0

    let search (sizeStep: int) =
        let s' = fontSize + float(sizeStep) * 0.01
        paint.TextSize <- single s'

        let w' = float(paint.MeasureText str)
        let h'' = 
            match States.font_lineheight with
            | States.Absolute h' -> h'
            | States.Default -> float paint.FontSpacing
            | States.Add h' -> (float paint.FontSpacing) + h'
        let h' = round(h'' * scaling) / scaling
        let h' = max h' 1.0

        // calculate score
        let score' = 
            abs(w' * scaling - round(w' * scaling)) +
            abs(h' * scaling - round(h'' * scaling))

        if score' < score then
            score <- score'
            w <- w'
            h <- h'
            s <- s'

    if States.font_autosnap then [-50 .. 50] else [0] 
    |> List.iter search

    s, w, h
     
let AllocateFramebuffer w h scale =
    let pxsize        = PixelSize(int <| (w * scale), int <| (h * scale))
    new RenderTargetBitmap(pxsize, Vector(96.0 * scale, 96.0 * scale))

let SetOpacity (paint: SKPaint) (opacity: float) =
    paint.Color <- paint.Color.WithAlpha(byte <| opacity * 255.0)

let SetForegroundBrush(fgpaint: SKPaint, c: Color, fontFace: SKTypeface, fontSize: float) =
    fgpaint.Color                <- c.ToSKColor()
    fgpaint.Typeface             <- fontFace
    fgpaint.TextSize             <- single fontSize
    fgpaint.IsAntialias          <- States.font_antialias
    fgpaint.IsAutohinted         <- States.font_autohint
    fgpaint.IsLinearText         <- false
    fgpaint.HintingLevel         <- States.font_hintLevel
    fgpaint.LcdRenderText        <- States.font_lcdrender
    fgpaint.SubpixelText         <- States.font_subpixel
    fgpaint.TextAlign            <- SKTextAlign.Left
    fgpaint.DeviceKerningEnabled <- false
    fgpaint.TextEncoding         <- SKTextEncoding.Utf16
    ()

let RenderText (ctx: IDrawingContextImpl, region: Rect, scale: float, fg: SKPaint, bg: SKPaint, sp: SKPaint, underline: bool, undercurl: bool, text: string, shaper: SKShaper ValueOption) =

    //  don't clip. see #60
    //  ctx.PushClip(region)

    //  DrawText accepts the coordinate of the baseline.

    let region = region.ToSKRect()

    let h = region.Bottom - region.Top
    let h' = fg.FontMetrics.Bottom - fg.FontMetrics.Top
    let total_padding = h - h'
    let prop = h / h'
    let baseline = region.Top + ceil((total_padding / 2.0f) - (fg.FontMetrics.Top))
    (*let baseline = ceil( region.Bottom - (fg.FontMetrics.Bottom * prop))*)
    (*printfn "scale=%A pad=%A base=%A region=%A" scale total_padding baseline region*)
    let fontPos = SKPoint(region.Left, baseline)

    let skia = ctx :?> ISkiaDrawingContextImpl

    //lol wat??
    //fg.Shader <- SKShader.CreateCompose(SKShader.CreateColor(fg.Color), SKShader.CreatePerlinNoiseFractalNoise(0.1F, 0.1F, 1, 6.41613F))

    skia.SkCanvas.DrawRect(region, bg)
    if not <| String.IsNullOrWhiteSpace text then
        if shaper.IsSome then
            skia.SkCanvas.DrawShapedText(shaper.Value, text.TrimEnd(), single fontPos.X, single fontPos.Y, fg)
        else 
            skia.SkCanvas.DrawText(text.TrimEnd(), fontPos, fg)


    // Text bounding box drawing:
    // --------------------------------------------------
    if States.font_drawBounds then
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
        let p1 = fontPos + SKPoint(0.0f, underline_pos)
        let p2 = p1 + SKPoint(region.Width, 0.0f)
        sp.Style <- SKPaintStyle.Stroke
        //sppaint.StrokeWidth <- sp_thickness
        skia.SkCanvas.DrawLine(p1, p2, sp)

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

    ctx.PopClip()

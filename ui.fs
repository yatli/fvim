module FVim.ui

open common
open def
open wcwidth
open log

open Avalonia
open Avalonia.Input
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform
open Avalonia.Skia
open System
open Avalonia.Media.TextFormatting
open System.Globalization
open SkiaSharp
open Avalonia.Interactivity

#nowarn "0009"

type InputEvent = 
| Key          of mods: KeyModifiers * key: Key
| MousePress   of mods: KeyModifiers * row: int * col: int * button: MouseButton
| MouseRelease of mods: KeyModifiers * row: int * col: int * button: MouseButton
| MouseDrag    of mods: KeyModifiers * row: int * col: int * button: MouseButton
| MouseWheel   of mods: KeyModifiers * row: int * col: int * dx: float * dy: float
| TextInput    of text: string

type GridBufferCell =
    {
        mutable text:  Rune
        mutable hlid:  int32
        mutable marks: Extmark list
    } 
    with 
    member cell.ContainsMark markid =
      cell.marks
      |> List.exists (fun (x: Extmark) -> x.mark = markid)
    static member CreateGrid r c =
      Array2D.init r c (fun _ _ -> { text  = Rune.empty; hlid = 0; marks = [] })

[<Struct>]
type GridSize =
    {
        rows: int32
        cols: int32
    }

let inline (<<->) a b = fun x -> a <= x && x < b
let inline (<->>) a b = fun x -> a < x && x <= b

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
    member x.Disjoint (y: GridRect) =
        y.row >= x.row_end ||
        x.row >= y.row_end ||
        y.col >= x.col_end ||
        x.col >= y.col_end


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

type ICursor =
    interface end

/// Represents a grid in neovim
type IGridUI =
    abstract Id: int
    /// Number of rows
    abstract GridHeight: int
    /// Number of columns
    abstract GridWidth: int
    abstract Resized: IEvent<IGridUI>
    abstract Input: IEvent<int*InputEvent*RoutedEventArgs>
    abstract BackgroundColor: Color
    abstract HasChildren: bool
    abstract RenderScale: float
    abstract Redraw: RedrawCommand -> unit
    abstract CreateChild: id:int -> rows:int -> cols:int -> IGridUI
    abstract AddChild: IGridUI -> unit
    abstract RemoveChild: IGridUI -> unit
    abstract Detach: unit -> unit

and WindowLayout =
| Window of IGridUI
| VSplit of WindowLayout list
| HSplit of WindowLayout list

/// Represents an OS window
and IFrame =
    abstract Title: string with get, set
    abstract MainGrid: IGridUI
    abstract Sync: IFrame -> unit

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

let DefaultFontBraille =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "Segoe UI Symbol"
    elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "Monospace" // ?
    elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX)   then "Menlo" // ?
    else "Monospace"


let private nerd_typeface = 
    let name = if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then "Iosevka NF"
               else "Iosevka"
    Typeface($"resm:fvim.Fonts.nerd.ttf?assembly=FVim#{name}")
let private emoji_typeface = Typeface(DefaultFontEmoji)
let private braille_typeface = Typeface(DefaultFontBraille)
let private fontcache = System.Collections.Generic.Dictionary<string*bool*bool, Typeface>()

let InvalidateFontCache () =
  fontcache.Clear()

let GetReverseColor (c: Color) =
    let r = 255uy - c.R
    let g = 255uy - c.G
    let b = 255uy - c.B
    Color(255uy, r, g, b)

let GetTypeface(txt, italic, bold, font, wfont) =
    let w = wswidth txt

    let _get fname =
        match fontcache.TryGetValue((fname, italic, bold)) with
        | true, typeface -> typeface
        | _ ->
            trace "ui" "GetTypeface: allocating new typeface %s:%b:%b" fname italic bold
            let weight   = if bold then states.font_weight_bold else states.font_weight_normal
            let slang    = if italic then FontStyle.Italic else FontStyle.Normal
            let typeface = 
                try Typeface(fname, slang, weight)
                with | _ -> try Typeface(DefaultFont, slang, weight)
                            with | _ -> Typeface.Default
            fontcache.[(fname, italic, bold)] <- typeface
            typeface

    let wfont = if String.IsNullOrEmpty wfont then font else wfont

    match w with
    | CharType.Wide  -> _get wfont
    | CharType.Powerline
    | CharType.Nerd when not states.font_nonerd -> nerd_typeface
    | CharType.Emoji -> emoji_typeface
    | CharType.Braille -> braille_typeface
    | _              -> _get font

let MeasureText (rune: Rune, font: string, wfont: string, fontSize: float, scaling: float) =
    let typeface = GetTypeface(rune, false, false, font, wfont).GlyphTypeface

    let mutable score = 999999999999.0
    let mutable s = fontSize
    let mutable w = 0.0
    let mutable h = 0.0

    let search (sizeStep: int) =
        // s' is pixels per em
        let s' = fontSize + float(sizeStep) * 0.01
        // u' is pixels per font design unit
        let u' = s' / float typeface.DesignEmHeight
        let glyph = [| typeface.GetGlyph(rune.Codepoint) |]
        use run = new GlyphRun(typeface, s', Utilities.ReadOnlySlice(ReadOnlyMemory(glyph)))
        let bounds = run.Size


        let w' = bounds.Width
        let h'' = 
            match states.font_lineheight with
            | Absolute lh -> lh
            | Default -> float typeface.LineHeight * u'
            | Add lh -> float typeface.LineHeight * u' + lh
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

    if states.font_autosnap then [-50 .. 50] else [0] 
    |> List.iter search

    s, w, h
     
let AllocateFramebuffer w h scale =
    let pxsize        = PixelSize(int <| (w * scale), int <| (h * scale))
    new RenderTargetBitmap(pxsize, Vector(96.0 * scale, 96.0 * scale))

let UpdateOpacity (color: Color) opacity = 
    Color(byte(255.0 * opacity), color.R, color.G, color.B)

let mutable _render_glyph_buf = [||]

let _render_brush = SolidColorBrush()
let _sp_brush = SolidColorBrush()
let _sp_pen = Pen(_sp_brush)
let _sp_points = ResizeArray()

[<Struct>]
type TextRenderSpan =
| Shaped of chars: ReadOnlyMemory<char>
| Unshaped of runes: ReadOnlyMemory<uint>

let RenderText (ctx: IDrawingContextImpl, region: Rect, vm_bounds: Rect, fg: Color, bg: Color, sp: Color, underline: bool, undercurl: bool, text: TextRenderSpan, font: Typeface, fontSize: float, clip: bool) =
    let skcanvas = 
      match ctx with
      | :? Avalonia.Skia.ISkiaDrawingContextImpl as skia -> skia.SkCanvas
      | _ -> failwith "not supported"

    //  emoji, nerd params calibration hack...
    let isEmoji = emoji_typeface = font
    let isNerd = nerd_typeface = font
    let fontSize = if isEmoji then fontSize - 1.0 else fontSize
    let clip = clip || isNerd

    let glyphTypeface = font.GlyphTypeface
    let px_per_unit = fontSize /  float glyphTypeface.DesignEmHeight

    //  h = [padding space 1] + above baseline | below baseline + [padding space 2]
    let h = region.Bottom - region.Top

    //  fh = [above baseline + below baseline]
    let fh = float (glyphTypeface.Descent - glyphTypeface.Ascent) * px_per_unit
    //  total_padding = padding space 1 + padding space 2
    let total_padding = h - fh
    let ascent = float glyphTypeface.Ascent * px_per_unit
    //  Text drawing is done at the coordinate of the baseline.
    let baseline = region.Top + ceil((total_padding / 2.0) - ascent)
    //  If emoji is drawn with the above algorithm, then it 
    //  adds top and left paddings proportionally to font size
    let fontPos = 
      let p = Point(region.Left, baseline)
      if not isEmoji then p
      else
        let emoji_pad = fontSize * 0.1
        p - Point(emoji_pad * 1.5 , emoji_pad)

    let sp_thickness = float glyphTypeface.UnderlineThickness * px_per_unit
    let underline_pos = float glyphTypeface.UnderlinePosition * px_per_unit

    _render_brush.Color <- fg
    _sp_pen.Thickness <- sp_thickness
    _sp_brush.Color <- sp

(* Undocumented clip operators taken from native Skia:
enum Op {
    kDifference_Op,        // 0
    kIntersect_Op,         // 1
    kUnion_Op,             // 2
    kXOR_Op,               // 3
    kReverseDifference_Op, // 4
    kReplace_Op,           // 5
    kLastOp = kReplace_Op
}; *)

    //  clip and fill bg
    skcanvas.ClipRect(region.ToSKRect(), enum 5)
    skcanvas.Clear(bg.ToSKColor())
    if not clip then 
      //  don't clip all along. see #60
      //  but no clipping = symbols overflow bounds. see #164
      //  so we treat symbols & characters differently... with the `clip` arg
      skcanvas.ClipRect(vm_bounds.ToSKRect(), enum 5)

    use glyphrun = 
      match text with
      | Unshaped runes ->
        if _render_glyph_buf.Length < runes.Length then
          _render_glyph_buf <- Array.zeroCreate runes.Length
        for i in 0..runes.Length-1 do
          _render_glyph_buf.[i] <- glyphTypeface.GetGlyph(runes.Span.[i])
        let slice = ReadOnlyMemory(_render_glyph_buf, 0, runes.Length)
        new GlyphRun(glyphTypeface, fontSize, Utilities.ReadOnlySlice(slice))
      | Shaped chars ->
        let slice = Utilities.ReadOnlySlice(chars)
        TextShaper.Current.ShapeText(slice, font, fontSize, CultureInfo.CurrentCulture)

    glyphrun.BaselineOrigin <- fontPos
    ctx.DrawGlyphRun(_render_brush, glyphrun)

    if clip then 
      // note: we explicitly arrange it like this so we don't
      // call ctx.PushClip(..) or ctx.PopClip()
      // It uses undocumented kReplace_Op to force update
      // the clip region, and this improves performance.
      skcanvas.ClipRect(vm_bounds.ToSKRect(), enum 5)

    //  Text bounding box drawing:
    if states.font_drawBounds then
        let sizevec = Point(glyphrun.Size.Width, glyphrun.Size.Height)
        ctx.DrawRectangle(Brushes.Transparent, Pen(_render_brush), RoundedRect(Rect(region.TopLeft, sizevec + region.TopLeft)))

    if underline then
        let p1 = fontPos + Point(0.0, underline_pos)
        let p2 = p1 + Point(region.Width, 0.0)
        ctx.DrawLine(_sp_pen, p1, p2)

    if undercurl then
        let mutable p = fontPos + Point(0.0, underline_pos)
        let qf             = 1.5
        let hf             = qf * 2.0
        let q3f            = qf * 3.0
        let v1             = Point(qf, -2.0)
        let v2             = Point(hf, 0.0)
        let v3             = Point(q3f, 2.0)
        let ff             = Point(qf * 4.0, 0.0)
        let r              = region.Right
        _sp_points.Clear()
        while p.X < r do
            _sp_points.Add(p)
            _sp_points.Add(p + v1)
            _sp_points.Add(p + v2)
            _sp_points.Add(p + v3)
            p <- p + ff
        ctx.DrawGeometry(Brushes.Transparent, _sp_pen, PolylineGeometry(_sp_points, false).PlatformImpl)

type WindowBackgroundComposition =
    | SolidBackground of opacity: float * color: Color
    | TransparentBackground of opacity: float * color: Color
    | GaussianBlur of opacity: float * color: Color
    | AdvancedBlur of opacity: float * color: Color

let SetWindowBackgroundComposition (win: Avalonia.Controls.Window) (composition: WindowBackgroundComposition)=
    match composition with
    | SolidBackground (_, c) -> 
        win.Background <- SolidColorBrush(c)
        win.TransparencyLevelHint <- Controls.WindowTransparencyLevel.None
    | TransparentBackground (op, c) -> 
        let c = Color(byte(op * 255.0), c.R, c.G, c.B)
        win.Background <- SolidColorBrush(c)
        win.TransparencyLevelHint <- Controls.WindowTransparencyLevel.Transparent
    | GaussianBlur(op, c) ->
        let c = Color(byte(op * 255.0), c.R, c.G, c.B)
        win.Background <- SolidColorBrush(c)
        win.TransparencyLevelHint <- Controls.WindowTransparencyLevel.Blur
    | AdvancedBlur(op, c) ->
        let c = Color(byte(op * 255.0), c.R, c.G, c.B)
        win.Background <- SolidColorBrush(c)
        win.TransparencyLevelHint <- 
          if RuntimeInformation.IsOSPlatform OSPlatform.Windows 
          then (int Controls.WindowTransparencyLevel.AcrylicBlur) + 1 |> LanguagePrimitives.EnumOfValue
          else Controls.WindowTransparencyLevel.AcrylicBlur

    trace "ui" "SetWindowBackgroundComposition: desired=%A actual=%A" win.TransparencyLevelHint win.ActualTransparencyLevel

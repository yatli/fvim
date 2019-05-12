namespace FVim

open neovim.def
open log

open Avalonia.Controls
open Avalonia
open System
open Avalonia.Threading
open Avalonia.Skia
open ui
open SkiaSharp
open Avalonia.VisualTree

type Cursor() as this =
    inherit Control()

    let mutable cursor_timer: IDisposable = null

    let cursorTimerRun action time =
        if cursor_timer <> null then
            cursor_timer.Dispose()
            cursor_timer <- null
        if time > 0 then
            cursor_timer <- DispatcherTimer.RunOnce(Action(action), TimeSpan.FromMilliseconds(float time))

    let showCursor v =
        this.IsVisible <- v && this.CursorInfo.enabled

    let rec blinkon() =
        showCursor true
        cursorTimerRun blinkoff this.CursorInfo.blinkon
    and blinkoff() = 
        showCursor false
        cursorTimerRun blinkon this.CursorInfo.blinkoff

    let cursorConfig(cursorInfo) =
        printfn "cursor config: %A" cursorInfo
        this.CursorInfo <- cursorInfo

        this.SetValue(Canvas.LeftProperty, this.CursorInfo.x)
        this.SetValue(Canvas.TopProperty,  this.CursorInfo.y)
        this.Height <- this.CursorInfo.h
        this.Width  <- this.CursorInfo.w

        showCursor true

        cursorTimerRun blinkon this.CursorInfo.blinkwait

    override this.Render(ctx) =
        let ctx' = ctx.PlatformImpl :?> DrawingContextImpl

        let cellw p = min (double(p) / 100.0 * this.CursorInfo.w) 1.0
        let cellh p = min (double(p) / 100.0 * this.CursorInfo.h) 5.0

        let typeface = GetTypeface(this.CursorInfo.text, this.CursorInfo.italic, this.CursorInfo.bold, this.CursorInfo.typeface, this.CursorInfo.wtypeface)
        use fg = GetForegroundBrush(this.CursorInfo.fg, typeface, this.CursorInfo.fontSize)
        use bg = new SKPaint(Color = this.CursorInfo.bg.ToSKColor())
        use sp = new SKPaint(Color = this.CursorInfo.sp.ToSKColor())

        match this.CursorInfo.shape, this.CursorInfo.cellPercentage with
        | CursorShape.Block, _ ->
            RenderText(ctx', this.Bounds, fg, bg, sp, this.CursorInfo.underline, this.CursorInfo.undercurl, this.CursorInfo.text)
        | CursorShape.Horizontal, p ->
            let region = Rect(0.0, this.CursorInfo.h, this.CursorInfo.w, this.CursorInfo.h - (cellh p))
            ctx'.Canvas.DrawRect(region.ToSKRect(), bg)
        | CursorShape.Vertical, p ->
            // FIXME Point(cellw p, -1.0) to avoid spanning to the next row. 
            // rounding should be implemented
            let region = Rect(0.0, 0.0, cellw p, this.CursorInfo.h - 1.0)
            ctx'.Canvas.DrawRect(region.ToSKRect(), bg)

    member val CursorInfo: CursorInfo = CursorInfo.Default with get, set


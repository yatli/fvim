namespace FVim

open neovim.def
open log

open Avalonia.Controls
open Avalonia
open System
open Avalonia.Threading
open ui
open Avalonia.Media

type Cursor() as this =
    inherit Control()

    static let RenderTickProp = AvaloniaProperty.Register<Cursor, int>("RenderTick")

    let mutable cursor_timer: IDisposable = null

    let cursorTimerRun action time =
        if cursor_timer <> null then
            cursor_timer.Dispose()
            cursor_timer <- null
        if time > 0 then
            cursor_timer <- DispatcherTimer.RunOnce(Action(action), TimeSpan.FromMilliseconds(float time))

    let showCursor (v: bool) =
        this.IsVisible <- v && this.ViewModel.enabled

    let rec blinkon() =
        showCursor true
        cursorTimerRun blinkoff this.ViewModel.blinkon
    and blinkoff() = 
        showCursor false
        cursorTimerRun blinkon this.ViewModel.blinkoff

    let cursorConfig id =
        if Object.Equals(this.ViewModel, null) then ()
        else
            showCursor true
            cursorTimerRun blinkon this.ViewModel.blinkwait
            this.InvalidateVisual()

    override this.OnDataContextChanged _ =
        ignore <| this.GetObservable(RenderTickProp).Subscribe(cursorConfig)

    member this.ViewModel: CursorViewModel = this.DataContext :?> CursorViewModel

    override this.Render(ctx) =

        let cellw p = min (double(p) / 100.0 * this.Width) 1.0
        let cellh p = min (double(p) / 100.0 * this.Height) 5.0

        let typeface = GetTypefaceA(this.ViewModel.text, this.ViewModel.italic, this.ViewModel.bold, this.ViewModel.typeface, this.ViewModel.wtypeface, this.ViewModel.fontSize)

        match this.ViewModel.shape, this.ViewModel.cellPercentage with
        | CursorShape.Block, _ ->
            ctx.FillRectangle(SolidColorBrush(this.ViewModel.bg), Rect(0.0, 0.0, this.Width, this.Height))
            let text = FormattedText(Text = this.ViewModel.text, Typeface = typeface)
            ctx.DrawText(SolidColorBrush(this.ViewModel.fg), Point(0.0, 0.0), text)
        | CursorShape.Horizontal, p ->
            ()
            let region = Rect(0.0, this.Height, this.Width, this.Height - (cellh p))
            ctx.FillRectangle(SolidColorBrush(this.ViewModel.bg), region)
        | CursorShape.Vertical, p ->
            ()
            // FIXME Point(cellw p, -1.0) to avoid spanning to the next row. 
            // rounding should be implemented
            let region = Rect(0.0, 0.0, cellw p, this.Height - 1.0)
            ctx.FillRectangle(SolidColorBrush(this.ViewModel.bg), region)

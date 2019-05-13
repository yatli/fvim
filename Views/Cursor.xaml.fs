namespace FVim

open neovim.def
open log

open Avalonia.Controls
open Avalonia
open System
open System.Collections.Generic
open Avalonia.Threading
open ui
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.VisualTree

type Cursor() as this =
    inherit Control()

    static let RenderTickProp = AvaloniaProperty.Register<Cursor, int>("RenderTick")

    let mutable cursor_timer: IDisposable = null
    let mutable bgbrush: SolidColorBrush  = SolidColorBrush(Colors.Black)
    let mutable fgbrush: SolidColorBrush  = SolidColorBrush(Colors.White)
    let mutable spbrush: SolidColorBrush  = SolidColorBrush(Colors.Red)
    let fbs = Dictionary<(float*float), RenderTargetBitmap> ()

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

    let getfb w h =
        let mutable fb = Unchecked.defaultof<RenderTargetBitmap>
        if not <| fbs.TryGetValue((w,h), &fb)
        then 
            fb <- AllocateFramebuffer w h (this.GetVisualRoot().RenderScaling)
            fbs.[(w,h)] <- fb
        fb

    let cursorConfig id =
        trace "cursor" "render tick %A" id
        if Object.Equals(this.ViewModel, null) 
        then ()
        else
            (* update the settings *)
            if this.ViewModel.fg <> fgbrush.Color then
                fgbrush <- SolidColorBrush(this.ViewModel.fg)
            if this.ViewModel.bg <> bgbrush.Color then
                bgbrush <- SolidColorBrush(this.ViewModel.bg)
            if this.ViewModel.sp <> spbrush.Color then
                spbrush <- SolidColorBrush(this.ViewModel.sp)
            (* reconfigure the cursor *)
            showCursor true
            cursorTimerRun blinkon this.ViewModel.blinkwait
            this.InvalidateVisual()

    override this.OnDataContextChanged _ =
        ignore <| this.GetObservable(RenderTickProp).Subscribe(cursorConfig)

    member this.ViewModel: CursorViewModel = this.DataContext :?> CursorViewModel

    override this.Render(ctx) =

        let cellw p = min (double(p) / 100.0 * this.Width) 1.0
        let cellh p = min (double(p) / 100.0 * this.Height) 5.0

        match this.ViewModel.shape, this.ViewModel.cellPercentage with
        | CursorShape.Block, _ ->
            let fb = getfb this.Width this.Height
            use dc = fb.CreateDrawingContext(null)
            let typeface = GetTypeface(this.ViewModel.text, this.ViewModel.italic, this.ViewModel.bold, this.ViewModel.typeface, this.ViewModel.wtypeface)
            let fg = GetForegroundBrush(this.ViewModel.fg, typeface, this.ViewModel.fontSize)
            RenderText(dc, Rect(this.Bounds.Size), fg, this.ViewModel.bg, this.ViewModel.sp, this.ViewModel.underline, this.ViewModel.undercurl, this.ViewModel.text)

            let scale = this.GetVisualRoot().RenderScaling
            ctx.DrawImage(fb, 1.0, Rect(0.0, 0.0, scale * fb.Size.Width, scale * fb.Size.Height), Rect(this.Bounds.Size))
        | CursorShape.Horizontal, p ->
            let h = (cellh p)
            let region = Rect(0.0, this.Height - h, this.Width, h)
            ctx.FillRectangle(SolidColorBrush(this.ViewModel.bg), region)
        | CursorShape.Vertical, p ->
            let region = Rect(0.0, 0.0, cellw p, this.Height)
            ctx.FillRectangle(SolidColorBrush(this.ViewModel.bg), region)

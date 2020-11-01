namespace FVim

open log
open ui
open common
open def

open Avalonia
open Avalonia.Animation
open Avalonia.Controls
open Avalonia.Data
open Avalonia.Markup.Xaml
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Skia
open Avalonia.Threading
open Avalonia.VisualTree
open System
open System.Collections.Generic
open System.Reactive.Disposables
open System.Reactive.Linq
open Avalonia.Visuals.Media.Imaging
open System.Runtime.InteropServices

type Cursor() as this =
    inherit ViewBase<CursorViewModel>()

    static let IsActiveProperty = AvaloniaProperty.Register<Cursor, bool>("IsActive")

    let mutable cursor_timer: IDisposable = null
    let mutable fg = Colors.White
    let mutable bg = Colors.Black
    let mutable sp = Colors.Red

    let mutable cursor_fb = AllocateFramebuffer (20.0) (20.0) 1.0
    let mutable cursor_fb_vm = CursorViewModel(Some -1)
    let mutable cursor_fb_s = 1.0

    let ensure_fb() =
        let s = this.GetVisualRoot().RenderScaling
        if (cursor_fb_vm.FbIntegrityChecksum(),cursor_fb_s) <> (this.ViewModel.FbIntegrityChecksum(),s) then
            cursor_fb_vm <- this.ViewModel.Clone()
            cursor_fb_s <- s
            cursor_fb.Dispose()
            cursor_fb <- AllocateFramebuffer (cursor_fb_vm.Width + 50.0) (cursor_fb_vm.Height + 50.0) s
            true
        elif cursor_fb_vm.text <> this.ViewModel.text then
            cursor_fb_vm.text <- this.ViewModel.text
            true
        else false

    let _buffer_glyph = [| 0u |]
    let _buffer_glyph_mem = ReadOnlyMemory(_buffer_glyph)

    let cursorTimerRun action time =
        if cursor_timer <> null then
            cursor_timer.Dispose()
            cursor_timer <- null
        if time > 0 then
            cursor_timer <- DispatcherTimer.RunOnce(Action(action), TimeSpan.FromMilliseconds(float time), DispatcherPriority.Render)

    let showCursor (v: bool) =
        let opacity = 
            if (v && this.ViewModel.enabled && this.ViewModel.ingrid)
               || not this.IsActive // don't blink if inactive
            then 1.0
            else 0.0
        this.Opacity <- opacity
        
    let rec blinkon() =
        showCursor true
        cursorTimerRun blinkoff this.ViewModel.blinkon
    and blinkoff() = 
        showCursor false
        cursorTimerRun blinkon this.ViewModel.blinkoff

    let cursorConfig id =
        trace "cursor" "render tick %A" id
        if Object.ReferenceEquals(this.ViewModel, null) 
        then ()
        else
            (* update the settings *)
            fg <- this.ViewModel.fg
            bg <- this.ViewModel.bg
            sp <- this.ViewModel.sp
            (* reconfigure the cursor *)
            showCursor true
            cursorTimerRun blinkon this.ViewModel.blinkwait
            // this.InvalidateVisual()

    let setCursorAnimation() =
        let transitions = Transitions()
        if States.cursor_smoothblink && this.IsActive then 
            let blink_transition = DoubleTransition()
            blink_transition.Property <- Cursor.OpacityProperty
            blink_transition.Duration <- TimeSpan.FromMilliseconds(150.0)
            blink_transition.Easing   <- Easings.LinearEasing()
            transitions.Add(blink_transition)
        if States.cursor_smoothmove && this.IsActive then
            let x_transition = DoubleTransition()
            x_transition.Property <- Canvas.LeftProperty
            x_transition.Duration <- TimeSpan.FromMilliseconds(80.0)
            x_transition.Easing   <- Easings.CubicEaseOut()
            let y_transition = DoubleTransition()
            y_transition.Property <- Canvas.TopProperty
            y_transition.Duration <- TimeSpan.FromMilliseconds(80.0)
            y_transition.Easing   <- Easings.CubicEaseOut()
            transitions.Add(x_transition)
            transitions.Add(y_transition)
        trace "cursor" "setCursorAnimation: blink=%b, move=%b" States.cursor_smoothblink States.cursor_smoothmove
        this.Transitions <- transitions
        ()

    do
        this.Watch [
            this.OnRenderTick cursorConfig
            this.GetObservable(IsActiveProperty) 
            |> Observable.subscribe(fun _ -> 
              setCursorAnimation()
              this.InvalidateVisual())
            States.Register.Watch "cursor" setCursorAnimation
        ] 
        AvaloniaXamlLoader.Load(this)

    member this.IsActive
      with get() = this.GetValue(IsActiveProperty)
      and set(v) = this.SetValue(IsActiveProperty, v) |> ignore

    override this.Render(ctx) =
        (*trace "cursor" "Render text: %s" this.ViewModel.text*)

        let cellw p = min (double(p) / 100.0 * this.Width) 1.0
        let cellh p = min (double(p) / 100.0 * this.Height) 5.0
        let scale = this.GetVisualRoot().RenderScaling

        if this.ViewModel = Unchecked.defaultof<CursorViewModel> then ()
        else

        match this.ViewModel.shape, this.ViewModel.cellPercentage with
        | CursorShape.Block, _ ->
            let typeface = GetTypeface(this.ViewModel.text, this.ViewModel.italic, this.ViewModel.bold, this.ViewModel.typeface, this.ViewModel.wtypeface)
            let bounds = Rect(this.Bounds.Size)
            let render_block (ctx: 'a) =
                if this.IsActive then
                    _buffer_glyph.[0] <- this.ViewModel.text.Codepoint
                    RenderText(ctx, bounds, scale, fg, bg, sp, this.ViewModel.underline, this.ViewModel.undercurl, Unshaped _buffer_glyph_mem, typeface, this.ViewModel.fontSize, true)
                else
                    let brush = SolidColorBrush(this.ViewModel.bg)
                    ctx.DrawRectangle(brush, Pen(brush), RoundedRect(bounds))

            try
                match ctx.PlatformImpl with
                | :? ISkiaDrawingContextImpl ->
                    // immediate
                    fg <- UpdateOpacity fg this.Opacity
                    bg <- UpdateOpacity bg this.Opacity
                    sp <- UpdateOpacity sp this.Opacity
                    render_block ctx.PlatformImpl
                | _ ->
                    // deferred
                    let redraw = ensure_fb()
                    if redraw then
                        use cursor_dc = cursor_fb.CreateDrawingContext(null)
                        cursor_dc.PushClip(bounds)
                        render_block cursor_dc
                        cursor_dc.PopClip()
                        cursor_dc.Dispose()
                    ctx.DrawImage(cursor_fb, Rect(0.0, 0.0, bounds.Width * scale, bounds.Height * scale), bounds)
            with
            | ex -> trace "cursor" "render exception: %s" <| ex.ToString()
        | CursorShape.Horizontal, p ->
            let h = (cellh p)
            let region = Rect(0.0, this.Height - h, this.Width, h)
            ctx.FillRectangle(SolidColorBrush(this.ViewModel.bg), region)
        | CursorShape.Vertical, p ->
            let region = Rect(0.0, 0.0, cellw p, this.Height)
            ctx.FillRectangle(SolidColorBrush(this.ViewModel.bg), region)


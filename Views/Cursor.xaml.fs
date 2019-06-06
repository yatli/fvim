namespace FVim

open neovim.def
open neovim.rpc
open log

open ReactiveUI
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
open SkiaSharp
open System
open System.Collections.Generic
open System.Reactive.Disposables
open System.Reactive.Linq
open ui
open Avalonia.Visuals.Media.Imaging
open System.Runtime.InteropServices

type Cursor() as this =
    inherit UserControl()
    // workaround: binding directly to Canvas.Left/Top won't work.
    // so we introduce a proxy DP for x and y.
    static let PosXProperty = AvaloniaProperty.Register<Cursor, float>("PosX")
    static let PosYProperty = AvaloniaProperty.Register<Cursor, float>("PosY")
    static let RenderTickProperty = AvaloniaProperty.Register<Cursor, int>("RenderTick")
    static let ViewModelProp = AvaloniaProperty.Register<Cursor, CursorViewModel>("ViewModel")

    let mutable cursor_timer: IDisposable = null
    let mutable bgbrush: SolidColorBrush  = SolidColorBrush(Colors.Black)
    let mutable fgbrush: SolidColorBrush  = SolidColorBrush(Colors.White)
    let mutable spbrush: SolidColorBrush  = SolidColorBrush(Colors.Red)
    let mutable cursor_fb = AllocateFramebuffer (20.0) (20.0) 1.0
    let mutable cursor_fb_vm = CursorViewModel()
    let mutable cursor_fb_s = 1.0

    let ensure_fb() =
        let s = this.GetVisualRoot().RenderScaling
        if (cursor_fb_vm.VisualChecksum(),cursor_fb_s) <> (this.ViewModel.VisualChecksum(),s) then
            cursor_fb_vm <- this.ViewModel.Clone()
            cursor_fb_s <- s
            cursor_fb.Dispose()
            cursor_fb <- AllocateFramebuffer (cursor_fb_vm.w + 50.0) (cursor_fb_vm.h + 50.0) s
            true
        else false

    let fgpaint = new SKPaint()
    let bgpaint = new SKPaint()
    let sppaint = new SKPaint()

    let cursorTimerRun action time =
        if cursor_timer <> null then
            cursor_timer.Dispose()
            cursor_timer <- null
        if time > 0 then
            cursor_timer <- DispatcherTimer.RunOnce(Action(action), TimeSpan.FromMilliseconds(float time), DispatcherPriority.Render)

    let showCursor (v: bool) =
        let opacity = 
            if v && this.ViewModel.enabled && this.ViewModel.ingrid
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

    let setCursorAnimation (blink_en: bool) (move_en: bool) =
        let transitions = Transitions()
        if blink_en then 
            let blink_transition = DoubleTransition()
            blink_transition.Property <- Cursor.OpacityProperty
            blink_transition.Duration <- TimeSpan.FromMilliseconds(150.0)
            blink_transition.Easing   <- Easings.LinearEasing()
            transitions.Add(blink_transition)
        if move_en then
            let x_transition = DoubleTransition()
            x_transition.Property <- PosXProperty
            x_transition.Duration <- TimeSpan.FromMilliseconds(80.0)
            x_transition.Easing   <- Easings.CubicEaseOut()
            let y_transition = DoubleTransition()
            y_transition.Property <- PosYProperty
            y_transition.Duration <- TimeSpan.FromMilliseconds(80.0)
            y_transition.Easing   <- Easings.CubicEaseOut()
            transitions.Add(x_transition)
            transitions.Add(y_transition)
        this.SetValue(Cursor.TransitionsProperty, transitions)

    do
        this.Watch [
            Model.Notify "SetCursorAnimation" 
                (function 
                 | [| Bool(blink) |] -> setCursorAnimation blink false
                 | [| Bool(blink); Bool(move) |] -> setCursorAnimation blink move
                 | _ -> setCursorAnimation false false) 

            this.GetObservable(PosXProperty).Subscribe(fun x -> this.SetValue(Canvas.LeftProperty, x, BindingPriority.Style))
            this.GetObservable(PosYProperty).Subscribe(fun y -> this.SetValue(Canvas.TopProperty, y, BindingPriority.Style))
            this.GetObservable(RenderTickProperty).Subscribe(cursorConfig)
        ] 
        AvaloniaXamlLoader.Load(this)

    override this.Render(ctx) =
        //trace "cursor" "render begin"

        let cellw p = min (double(p) / 100.0 * this.Width) 1.0
        let cellh p = min (double(p) / 100.0 * this.Height) 5.0

        match this.ViewModel.shape, this.ViewModel.cellPercentage with
        | CursorShape.Block, _ ->
            let typeface = GetTypeface(this.ViewModel.text, this.ViewModel.italic, this.ViewModel.bold, this.ViewModel.typeface, this.ViewModel.wtypeface)
            SetForegroundBrush(fgpaint, this.ViewModel.fg, typeface, this.ViewModel.fontSize)
            bgpaint.Color <- this.ViewModel.bg.ToSKColor()
            sppaint.Color <- this.ViewModel.sp.ToSKColor()
            let bounds = Rect(this.Bounds.Size)

            try
                match ctx.PlatformImpl with
                | :? ISkiaDrawingContextImpl ->
                    // immediate
                    SetOpacity fgpaint this.Opacity
                    SetOpacity bgpaint this.Opacity
                    SetOpacity sppaint this.Opacity
                    RenderText(ctx.PlatformImpl, bounds, fgpaint, bgpaint, sppaint, this.ViewModel.underline, this.ViewModel.undercurl, this.ViewModel.text, false)
                | _ ->
                    // deferred
                    let s = this.GetVisualRoot().RenderScaling
                    let redraw = ensure_fb()
                    if redraw then
                        let dc = cursor_fb.CreateDrawingContext(null)
                        dc.PushClip(bounds)
                        RenderText(dc, bounds, fgpaint, bgpaint, sppaint, this.ViewModel.underline, this.ViewModel.undercurl, this.ViewModel.text, false)
                        dc.PopClip()
                        dc.Dispose()
                    ctx.DrawImage(cursor_fb, 1.0, Rect(0.0, 0.0, bounds.Width * s, bounds.Height * s), bounds)
            with
            | ex -> trace "cursor" "render exception: %s" <| ex.ToString()
        | CursorShape.Horizontal, p ->
            let h = (cellh p)
            let region = Rect(0.0, this.Height - h, this.Width, h)
            ctx.FillRectangle(SolidColorBrush(this.ViewModel.bg), region)
        | CursorShape.Vertical, p ->
            let region = Rect(0.0, 0.0, cellw p, this.Height)
            ctx.FillRectangle(SolidColorBrush(this.ViewModel.bg), region)

    member this.ViewModel: CursorViewModel = 
        let ctx = this.DataContext 
        if ctx = null then Unchecked.defaultof<_> else ctx :?> CursorViewModel

    member this.PosX
        with get() = this.GetValue(PosXProperty)
        and  set(v) = this.SetValue(PosXProperty, v)

    member this.PosY
        with get() = this.GetValue(PosYProperty)
        and  set(v) = this.SetValue(PosYProperty, v)

    member this.RenderTick
        with get() = this.GetValue(RenderTickProperty)
        and  set(v) = this.SetValue(RenderTickProperty, v)

    interface IViewFor<CursorViewModel> with
        member this.ViewModel
            with get (): CursorViewModel = this.GetValue(ViewModelProp)
            and set (v: CursorViewModel): unit = this.SetValue(ViewModelProp, v)
        member this.ViewModel
            with get (): obj = this.GetValue(ViewModelProp) :> obj
            and set (v: obj): unit = this.SetValue(ViewModelProp, v)


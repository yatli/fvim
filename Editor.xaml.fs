namespace FVim

open FVim.neovim.def
open FVim.ui
open FVim.log

open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Markup.Xaml
open Avalonia.Media
open Avalonia.Threading
open Avalonia.Platform
open Avalonia.Utilities
open Avalonia.Skia
open Avalonia.Data
open ReactiveUI
open Avalonia.VisualTree
open Avalonia.Layout
open System.Reactive.Linq
open System.Reactive.Disposables
open System
open Avalonia.Threading

type Editor() as this =
    inherit Canvas()
    let mutable m_size = Size(100.0, 100.0) 

    static let RenderTickProp = AvaloniaProperty.Register<Editor, int>("RenderTick")
    static let FullscreenProp = AvaloniaProperty.Register<Editor, bool>("Fullscreen")
    static let ViewModelProp  = AvaloniaProperty.Register<Editor, EditorViewModel>("ViewModel")

    let toggleFullscreen(v) =
        let win = this.GetVisualRoot() :?> Window
        if not v then
            win.WindowState <- WindowState.Normal
            win.HasSystemDecorations <- true
            win.Topmost <- false
        else
            win.HasSystemDecorations <- false
            win.WindowState <- WindowState.Maximized
            win.Topmost <- true

    let doWithDataContext fn =
        match this.DataContext with
        | :? EditorViewModel as viewModel ->
            fn viewModel
        | _ -> ()

    let redraw frameid =
        printfn "render tick %d" frameid;
        ignore <| Dispatcher.UIThread.InvokeAsync(fun () ->
            let fb = this.FindControl<Image>("FrameBuffer")
            if fb <> null then fb.InvalidateVisual()
        )

    do
        AvaloniaXamlLoader.Load(this)
        ignore <| this.Bind(RenderTickProp, Binding("RenderTick", BindingMode.TwoWay))
        ignore <| this.Bind(FullscreenProp, Binding("Fullscreen", BindingMode.TwoWay))

        this.WhenActivated(fun disposables -> 
            ignore <| this.GetObservable(FullscreenProp).Subscribe(toggleFullscreen).DisposeWith(disposables)
            ignore <| this.GetObservable(RenderTickProp).Subscribe(redraw).DisposeWith(disposables)
            ignore <| this.TextInput.Subscribe(fun e -> doWithDataContext(fun vm -> vm.OnTextInput e)).DisposeWith(disposables)
            this.Focus()
        ) |> ignore

    //static member private MeasureProp = AvaloniaProperty.Register<Editor, Size>("ms")
    override this.MeasureOverride(size) =
        // binding not working yet.
        // ignore <| this.SetAndRaise(Editor.MeasureProp, &m_size, size)
        doWithDataContext (fun vm ->
            vm.MeasuredSize <- size
            vm.RenderScale <- (this :> IVisual).GetVisualRoot().RenderScaling
        )
        size

    ////each event repeats 4 times... use the event instead
    //(*override this.OnTextInput(e) =*)
    //    (*e.Handled <- true*)
    //    (*inputEvent.Trigger <| InputEvent.TextInput(e.Text)*)

    override this.OnKeyDown(e) =
        doWithDataContext(fun vm -> vm.OnKey e)

    override this.OnKeyUp(e) =
        e.Handled <- true

    override this.OnPointerPressed(e) =
        doWithDataContext(fun vm -> vm.OnMouseDown e this)

    override this.OnPointerReleased(e) =
        doWithDataContext(fun vm -> vm.OnMouseUp e this)

    override this.OnPointerMoved(e) =
        doWithDataContext(fun vm -> vm.OnMouseMove e this)

    override this.OnPointerWheelChanged(e) =
        doWithDataContext(fun vm -> vm.OnMouseWheel e this)

    interface IViewFor<EditorViewModel> with
        member this.ViewModel
            with get (): EditorViewModel = this.GetValue(ViewModelProp)
            and set (v: EditorViewModel): unit = this.SetValue(ViewModelProp, v)
        member this.ViewModel
            with get (): obj = this.GetValue(ViewModelProp) :> obj
            and set (v: obj): unit = this.SetValue(ViewModelProp, v)

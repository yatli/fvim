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
open System.Collections.ObjectModel
open System.Collections.Specialized
open System.Collections.Generic

type EmbeddedEditor() as this =
    inherit UserControl()
    do
        AvaloniaXamlLoader.Load(this)

and Editor() as this =
    inherit Canvas()

    let toggleFullscreen(v) =
        let win = this.GetVisualRoot() :?> Window
        if not v then
            win.WindowState <- WindowState.Normal
            win.HasSystemDecorations <- true
            //win.Topmost <- false
        else
            win.HasSystemDecorations <- false
            win.WindowState <- WindowState.Maximized
            //win.Topmost <- true

    let doWithDataContext fn =
        match this.DataContext with
        | :? EditorViewModel as viewModel ->
            fn viewModel
        | _ -> ()

    let redraw (vm: EditorViewModel) =
        trace ("editor #" + (vm:>IGridUI).Id.ToString()) "render tick %d" vm.RenderTick;
        ignore <| Dispatcher.UIThread.InvokeAsync(fun () ->
            let fb = this.FindControl<Image>("FrameBuffer")
            if fb <> null 
            then 
                //doWithDataContext (fun vm -> if not vm.TopLevel then printfn "this: %A %A buf: %A %A" this.Height this.Width vm.BufferHeight vm.BufferWidth)
                //this.Height <- vm.BufferHeight; this.Width <- vm.BufferWidth)
                fb.InvalidateVisual()
        )

    let onViewModelConnected (disposables: CompositeDisposable) (vm:EditorViewModel) =
        ignore <| vm.ObservableForProperty(fun x -> x.RenderTick).Subscribe(fun _ -> redraw vm).DisposeWith(disposables)
        ignore <| vm.ObservableForProperty(fun x -> x.Fullscreen).Subscribe(fun v -> toggleFullscreen <| v.GetValue()).DisposeWith(disposables)

    do
        AvaloniaXamlLoader.Load(this)

        this.WhenActivated(fun disposables -> 
            // bindings stopped working...
            //ignore <| this.Bind(Editor.RenderTickProp, Binding("RenderTick", BindingMode.OneWay))
            //ignore <| this.Bind(Editor.FullscreenProp, Binding("Fullscreen", BindingMode.OneWay))
            //ignore <| this.GetObservable(Editor.FullscreenProp).Subscribe(toggleFullscreen).DisposeWith(disposables)
            //ignore <| this.GetObservable(Editor.RenderTickProp).Subscribe(redraw).DisposeWith(disposables)

            ignore <| this.TextInput
                          .Subscribe(fun e -> doWithDataContext(fun vm -> vm.OnTextInput e))
                          .DisposeWith(disposables)

            ignore <| this.GetObservable(Editor.DataContextProperty)
                          .OfType<EditorViewModel>()
                          .Subscribe(fun vm -> onViewModelConnected disposables vm)
                          .DisposeWith(disposables)

            this.Focus()
        ) |> ignore

    static member RenderTickProp = AvaloniaProperty.Register<Editor, int>("RenderTick")
    static member FullscreenProp = AvaloniaProperty.Register<Editor, bool>("Fullscreen")
    static member ViewModelProp  = AvaloniaProperty.Register<Editor, EditorViewModel>("ViewModel")

    override this.MeasureOverride(size) =
        doWithDataContext (fun vm ->
            if vm.TopLevel then
                vm.MeasuredSize <- size
            vm.RenderScale <- (this :> IVisual).GetVisualRoot().RenderScaling
        )
        size

    (*each event repeats 4 times... use the event instead *)
    (*override this.OnTextInput(e) =*)

    override __.OnKeyDown(e) =
        doWithDataContext(fun vm -> vm.OnKey e)

    override __.OnKeyUp(e) =
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
            with get (): EditorViewModel = this.GetValue(Editor.ViewModelProp)
            and set (v: EditorViewModel): unit = this.SetValue(Editor.ViewModelProp, v)
        member this.ViewModel
            with get (): obj = this.GetValue(Editor.ViewModelProp) :> obj
            and set (v: obj): unit = this.SetValue(Editor.ViewModelProp, v)


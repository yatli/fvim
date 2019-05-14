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

type Editor() as this =
    inherit Canvas()

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
        trace "editor" "render tick %d" frameid;
        ignore <| Dispatcher.UIThread.InvokeAsync(fun () ->
            let fb = this.FindControl<Image>("FrameBuffer")
            if fb <> null 
            then 
                //doWithDataContext (fun vm -> if not vm.TopLevel then printfn "this: %A %A buf: %A %A" this.Height this.Width vm.BufferHeight vm.BufferWidth)
                //this.Height <- vm.BufferHeight; this.Width <- vm.BufferWidth)
                fb.InvalidateVisual()
        )

    let children_editors = Dictionary<int, Editor>()

    let children_change (e: NotifyCollectionChangedEventArgs) =
        match e.Action with
        | NotifyCollectionChangedAction.Reset ->
            children_editors.Values |> Seq.iter (fun x -> ignore <| this.Children.Remove x)
            children_editors.Clear()
        | NotifyCollectionChangedAction.Add ->
            for x in e.NewItems do
                let anchor = x :?> Anchor
                let editor = Editor(DataContext = anchor.child)
                editor.SetValue(Canvas.LeftProperty, anchor.X)
                editor.SetValue(Canvas.TopProperty, anchor.Y)
                let gid = (anchor.child :> IGridUI).Id
                children_editors.[gid] <- editor
                this.Children.Add(editor)
        ()

    do
        AvaloniaXamlLoader.Load(this)
        ignore <| this.Bind(Editor.RenderTickProp, Binding("RenderTick", BindingMode.TwoWay))
        ignore <| this.Bind(Editor.FullscreenProp, Binding("Fullscreen", BindingMode.TwoWay))
        ignore <| this.Bind(Editor.ChildrenProp, Binding("ChildGrids"))

        this.WhenActivated(fun disposables -> 
            ignore <| this.GetObservable(Editor.FullscreenProp).Subscribe(toggleFullscreen).DisposeWith(disposables)
            ignore <| this.GetObservable(Editor.RenderTickProp).Subscribe(redraw).DisposeWith(disposables)
            ignore <| this.GetObservable(Editor.ChildrenProp).Subscribe(fun (c: ObservableCollection<Anchor>) -> if c <> null then ignore <| c.CollectionChanged.Subscribe(children_change).DisposeWith(disposables)).DisposeWith(disposables)
            ignore <| this.TextInput.Subscribe(fun e -> doWithDataContext(fun vm -> vm.OnTextInput e)).DisposeWith(disposables)

            this.Focus()
        ) |> ignore

    static member RenderTickProp = AvaloniaProperty.Register<Editor, int>("RenderTick")
    static member FullscreenProp = AvaloniaProperty.Register<Editor, bool>("Fullscreen")
    static member ViewModelProp  = AvaloniaProperty.Register<Editor, EditorViewModel>("ViewModel")
    static member ChildrenProp   = AvaloniaProperty.Register<Editor, ObservableCollection<Anchor>>("Children")

    override this.MeasureOverride(size) =
        doWithDataContext (fun vm ->
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

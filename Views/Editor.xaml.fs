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
open Avalonia.Win32

type EmbeddedEditor() as this =
    inherit UserControl()
    do
        AvaloniaXamlLoader.Load(this)

and Editor() as this =
    inherit Canvas()

    let mutable m_saved_size  = Size(100.0,100.0)
    let mutable m_saved_pos   = PixelPoint(300, 300)
    let mutable m_saved_state = WindowState.Normal

    let toggleFullscreen(v) =
        let win = this.GetVisualRoot() :?> Window

        if not v then
            win.WindowState <- m_saved_state
            win.PlatformImpl.Resize(m_saved_size)
            win.Position <- m_saved_pos
            win.HasSystemDecorations <- true
        else
            m_saved_size             <- win.ClientSize
            m_saved_pos              <- win.Position
            m_saved_state            <- win.WindowState
            let screen                = win.Screens.ScreenFromVisual(this)
            let screenBounds          = screen.Bounds
            let sz                    = screenBounds.Size.ToSizeWithDpi(96.0 * this.GetVisualRoot().RenderScaling)
            win.HasSystemDecorations <- false
            win.WindowState          <- WindowState.Normal
            win.Position             <- screenBounds.TopLeft
            win.PlatformImpl.Resize(sz)

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
            then fb.InvalidateVisual()
        )

    let onViewModelConnected (vm:EditorViewModel) =
        [
            vm.ObservableForProperty(fun x -> x.RenderTick).Subscribe(fun _ -> redraw vm)
            vm.ObservableForProperty(fun x -> x.Fullscreen).Subscribe(fun v -> toggleFullscreen <| v.GetValue())
            Observable.Interval(TimeSpan.FromMilliseconds(100.0))
                      .FirstAsync(fun _ -> this.IsInitialized)
                      .Subscribe(fun _ -> Model.OnGridReady(vm :> IGridUI))
        ] |> vm.Watch 
        
    do
        AvaloniaXamlLoader.Load(this)
        this.Watch [

            this.TextInput.Subscribe(fun e -> doWithDataContext(fun vm -> vm.OnTextInput e))

            this.GetObservable(Editor.DataContextProperty)
                          .OfType<EditorViewModel>()
                          .Subscribe(onViewModelConnected)

            this.Initialized.Subscribe(fun _ -> this.Focus())

            this.AddHandler(DragDrop.DropEvent, (fun _ (e: DragEventArgs) ->
                if e.Data.Contains(DataFormats.FileNames) then
                    Model.EditFiles <| e.Data.GetFileNames()
                elif e.Data.Contains(DataFormats.Text) then
                    Model.InsertText <| e.Data.GetText()
            ))
        ]

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


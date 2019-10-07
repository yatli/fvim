namespace FVim

open Avalonia.Markup.Xaml
open Avalonia.VisualTree
open Avalonia.Controls
open Avalonia
open Avalonia.Rendering

type TitleBar() as this =
    inherit ViewBase<TitleBarViewModel>()

    static let TitleProperty = AvaloniaProperty.Register<TitleBar, string>("Title")

    let root() = (this:>IVisual).VisualRoot :?> MainWindow

    let toggleMaximize() =
        let win = root()
        win.WindowState <-
            match win.WindowState with
            | WindowState.Normal -> WindowState.Maximized
            | WindowState.Maximized -> WindowState.Normal
            | x -> x

    do
        AvaloniaXamlLoader.Load(this)
        this.Watch [
            this.DoubleTapped |> Observable.subscribe (fun ev -> 
                ev.Handled <- true
                toggleMaximize())
            this.PointerMoved |> Observable.subscribe (fun ev -> 
                ev.Handled <- true
                if ev.GetPointerPoint(null).Properties.IsLeftButtonPressed then
                    root().BeginMoveDrag()
                )
        ]

    member __.Title
        with get() = this.GetValue(TitleProperty)
        and set(v) = this.SetValue(TitleProperty, v)

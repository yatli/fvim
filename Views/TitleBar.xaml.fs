namespace FVim

open Avalonia.Markup.Xaml
open Avalonia.VisualTree
open Avalonia.Controls

type TitleBar() as this =
    inherit ViewBase<TitleBarViewModel>()

    let toggleMaximize() =
        let win = (this:>IVisual).VisualRoot :?> MainWindow
        win.WindowState <-
            match win.WindowState with
            | WindowState.Normal -> WindowState.Maximized
            | WindowState.Maximized -> WindowState.Normal
            | x -> x

    let mutable m_dragmoving = false

    do
        AvaloniaXamlLoader.Load(this)
        this.Watch [
            this.DoubleTapped |> Observable.subscribe (fun ev -> 
                ev.Handled <- true
                toggleMaximize())
            this.PointerPressed |> Observable.subscribe (fun ev -> 
                ev.Handled <- true
                m_dragmoving <- ev.GetPointerPoint(null).Properties.IsLeftButtonPressed
                ())
            this.PointerPressed |> Observable.subscribe (fun ev -> 
                ev.Handled <- true
                m_dragmoving <- ev.GetPointerPoint(null).Properties.IsLeftButtonPressed
                ())
            this.PointerMoved |> Observable.subscribe (fun ev -> 
                ev.Handled <- true
                if m_dragmoving then
                    ())
            this.PointerLeave |> Observable.subscribe (fun ev -> 
                ev.Handled <- true
                m_dragmoving <- false)
        ]

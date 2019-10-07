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

    let mutable m_butmin:Button = null
    let mutable m_butmax:Button = null
    let mutable m_butclose:Button = null

    do
        AvaloniaXamlLoader.Load(this)
        this.Watch [
            this.DoubleTapped.Subscribe(fun ev -> 
                ev.Handled <- true
                toggleMaximize())
            this.PointerMoved.Subscribe(fun ev -> 
                if this.IsPointerOver && ev.GetPointerPoint(null).Properties.IsLeftButtonPressed then
                    ev.Handled <- true
                    root().BeginMoveDrag())
        ]

    override __.OnTemplateApplied _ =
        m_butmin <- this.FindControl("MinimizeButton")
        m_butmax <- this.FindControl("MaximizeButton")
        m_butclose <- this.FindControl("CloseButton")

        this.Watch [
            m_butmin.Click.Subscribe(fun _ -> root().WindowState <- WindowState.Minimized)
            m_butmax.Click.Subscribe(fun _ -> toggleMaximize())
            m_butclose.Click.Subscribe(fun _ -> root().Close())
        ]


    member __.Title
        with get() = this.GetValue(TitleProperty)
        and set(v) = this.SetValue(TitleProperty, v)

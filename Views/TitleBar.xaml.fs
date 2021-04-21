namespace FVim

open Avalonia.Markup.Xaml
open Avalonia.VisualTree
open Avalonia.Controls
open Avalonia
open Avalonia.Rendering

type TitleBar() as this =
    inherit ViewBase<TitleBarViewModel>()

    static let TitleProperty = AvaloniaProperty.Register<TitleBar, string>("Title")
    static let IsActiveProperty = AvaloniaProperty.Register<TitleBar, bool>("IsActive")

    let root() = (this:>IVisual).VisualRoot :?> Frame

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
    let mutable m_title:TextBlock = null

    do
        AvaloniaXamlLoader.Load(this)
        this.Watch [
            this.DoubleTapped.Subscribe(fun ev -> 
                ev.Handled <- true
                toggleMaximize())
            this.PointerPressed.Subscribe(fun ev ->
                ev.Handled <- true
                root().BeginMoveDrag(ev)
                )
        ]

    override __.OnTemplateApplied _ =
        m_butmin <- this.FindControl("MinimizeButton")
        m_butmax <- this.FindControl("MaximizeButton")
        m_butclose <- this.FindControl("CloseButton")
        m_title <- this.FindControl("Title")

        this.Watch [
            m_butmin.Click.Subscribe(fun _ -> root().WindowState <- WindowState.Minimized)
            m_butmax.Click.Subscribe(fun _ -> toggleMaximize())
            m_butclose.Click.Subscribe(fun _ -> root().Close())

            this.GetObservable(IsActiveProperty).Subscribe(fun v ->
              [m_butmin.Classes; m_butmax.Classes; m_butclose.Classes; m_title.Classes]
            |> List.iter (
                if v then fun x -> ignore <| x.Remove("inactive")
                else fun x -> x.Add("inactive")))
        ]


    member __.Title
        with get() = this.GetValue(TitleProperty)
        and set(v) = this.SetValue(TitleProperty, v) |> ignore

    member __.IsActive
        with get() = this.GetValue(IsActiveProperty)
        and set(v) = this.SetValue(IsActiveProperty, v) |> ignore

namespace FVim

open ReactiveUI
open Avalonia
open Avalonia.Controls
open Avalonia.Data

type ViewBase< 'TViewModel when 'TViewModel :> ViewModelBase and 'TViewModel: not struct>() as this =
    inherit UserControl()

    static let RenderTickProperty = AvaloniaProperty.Register<ViewBase< 'TViewModel >, int>("RenderTick")
    static let ViewModelProp = AvaloniaProperty.Register<ViewBase< 'TViewModel >, 'TViewModel>("ViewModel")

    do
        this.Watch [
            this.Bind(RenderTickProperty, Binding("RenderTick"))
        ]

    member this.ViewModel: 'TViewModel = 
        let ctx = this.DataContext 
        if ctx = null then Unchecked.defaultof<_> else ctx :?> 'TViewModel

    member this.RenderTick
        with get() = this.GetValue(RenderTickProperty)
        and  set(v) = this.SetValue(RenderTickProperty, v)

    interface IViewFor<'TViewModel> with
        member this.ViewModel
            with get (): 'TViewModel = this.GetValue(ViewModelProp)
            and set (v: 'TViewModel): unit = this.SetValue(ViewModelProp, v)
        member this.ViewModel
            with get (): obj = this.GetValue(ViewModelProp) :> obj
            and set (v: obj): unit = this.SetValue(ViewModelProp, v)

    member this.OnRenderTick (fn: int -> unit) =
        this.GetObservable(RenderTickProperty).Subscribe fn

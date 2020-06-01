namespace FVim

open ReactiveUI
open Avalonia
open Avalonia.Controls
open Avalonia.Data
open FSharp.Control.Reactive

type ViewBase< 'TViewModel when 'TViewModel :> ViewModelBase and 'TViewModel: not struct>() as this =
    inherit UserControl()

    static let RenderTickProperty = AvaloniaProperty.Register<ViewBase< 'TViewModel >, int>("RenderTick")
    static let ViewModelProperty = AvaloniaProperty.Register<ViewBase< 'TViewModel >, 'TViewModel>("ViewModel")

    let m_vm_connected = Event<'TViewModel>()

    do
        this.Watch [
            this.Bind(RenderTickProperty, Binding("RenderTick"))
            this.Bind(Canvas.LeftProperty, Binding("X"))
            this.Bind(Canvas.TopProperty, Binding("Y"))
            this.GetObservable(ViewBase.DataContextProperty)
            |> Observable.filter (fun x -> not <| obj.ReferenceEquals(x, null))
            |> Observable.ofType
            |> Observable.subscribe m_vm_connected.Trigger
        ]

    member this.ViewModel: 'TViewModel = 
        let ctx = this.DataContext 
        if ctx = null then Unchecked.defaultof<_> else ctx :?> 'TViewModel

    member this.ViewModelConnected = m_vm_connected.Publish

    member this.RenderTick
        with get() = this.GetValue(RenderTickProperty)
        and  set(v) = this.SetValue(RenderTickProperty, v) |> ignore

    interface IViewFor<'TViewModel> with
        member this.ViewModel
            with get (): 'TViewModel = this.GetValue(ViewModelProperty)
            and set (v: 'TViewModel): unit = this.SetValue(ViewModelProperty, v) |> ignore
        member this.ViewModel
            with get (): obj = this.GetValue(ViewModelProperty) :> obj
            and set (v: obj): unit = this.SetValue(ViewModelProperty, v)

    member this.OnRenderTick (fn: int -> unit) =
        this.GetObservable(RenderTickProperty).Subscribe fn

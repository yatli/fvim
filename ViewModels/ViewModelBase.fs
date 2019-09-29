namespace FVim

open def
open common
open wcwidth
open log

open ReactiveUI
open Avalonia
open System
open System.Reactive.Disposables
open System.Runtime.CompilerServices

[<Extension>]
type ActivatableExt() =
    [<Extension>]
    static member inline Watch (this: IActivatable, xs: IDisposable seq) =
        this.WhenActivated(fun (disposables: CompositeDisposable) ->
            xs |> Seq.iter (fun x -> x.DisposeWith(disposables) |> ignore)) |> ignore
    [<Extension>]
    static member inline Watch (this: ISupportsActivation, xs: IDisposable seq) =
        this.WhenActivated(fun (disposables: CompositeDisposable) ->
            xs |> Seq.iter (fun x -> x.DisposeWith(disposables) |> ignore)) |> ignore
    [<Extension>]
    static member inline Do (this: IActivatable, fn: unit -> unit) =
        do fn()
        Disposable.Empty
    [<Extension>]
    static member inline Do (this: ISupportsActivation, fn: unit -> unit) =
        do fn()
        Disposable.Empty


type ViewModelBase(_x: float option, _y: float option, _w: float option, _h: float option) =
    inherit ReactiveObject()

    let activator = new ViewModelActivator()

    let mutable m_x = _d 0.0 _x
    let mutable m_y = _d 0.0 _y
    let mutable m_w = _d 0.0 _w
    let mutable m_h = _d 0.0 _h
    let mutable m_tick = 0

    new() = ViewModelBase(None, None, None, None)
    new(_posX: float option, _posY: float option, _size: Size option) = ViewModelBase(_posX, _posY, Option.map(fun (s: Size) -> s.Width) _size, Option.map(fun (s: Size) -> s.Height) _size)

    interface ISupportsActivation with
        member __.Activator = activator

    member this.X
        with get() : float = m_x
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_x, v, "X")

    member this.Y
        with get() : float = m_y
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_y, v, "Y")

    member this.Height 
        with get(): float = m_h 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_h, v, "Height")

    member this.Width 
        with get(): float = m_w 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_w, v, "Width")

    member this.RenderTick
        with get() : int = m_tick
        and set(v) =
            ignore <| this.RaiseAndSetIfChanged(&m_tick, v, "RenderTick")


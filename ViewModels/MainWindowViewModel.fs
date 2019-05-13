namespace FVim

open ReactiveUI
open Avalonia
open Avalonia.Controls
open System

type MainWindowViewModel(cfg: config.ConfigObject.Workspace option) as this =
    inherit ViewModelBase()

    let mainGrid               = EditorViewModel(1)
    let mutable m_windowWidth  = 800.0
    let mutable m_windowHeight = 600.0
    let mutable m_windowX      = 600
    let mutable m_windowY      = 600
    let mutable m_windowState = WindowState.Normal

    do
        match cfg with
        | Some cfg ->
            m_windowWidth  <- float cfg.Mainwin.W
            m_windowHeight <- float cfg.Mainwin.H
            m_windowX      <- cfg.Mainwin.X
            m_windowY      <- cfg.Mainwin.Y
            match Enum.TryParse<WindowState>(cfg.Mainwin.State) with
            | true, v -> this.WindowState <- v
            | _ -> ()
        | None -> ()

    member __.MainGrid = mainGrid

    member this.WindowState
        with get(): WindowState = m_windowState
        and set(v) = 
            ignore <| this.RaiseAndSetIfChanged(&m_windowState, v)

    member this.WindowX 
        with get(): int = m_windowX
        and set(v) = 
            ignore <| this.RaiseAndSetIfChanged(&m_windowX, v)

    member this.WindowY 
        with get(): int = m_windowY
        and set(v) = 
            ignore <| this.RaiseAndSetIfChanged(&m_windowY, v)

    member this.WindowHeight 
        with get(): float = m_windowHeight
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_windowHeight, v)

    member this.WindowWidth
        with get(): float = m_windowWidth
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_windowWidth, v)
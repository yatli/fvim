namespace FVim

open ReactiveUI
open Avalonia
open Avalonia.Controls
open System

type MainWindowViewModel(cfg: config.ConfigObject.Workspace option) as this =
    inherit ViewModelBase(Some 300.0, Some 300.0, Some 800.0, Some 600.0)

    let mainGrid = EditorViewModel(1)
    let mutable m_windowState = WindowState.Normal

    do
        match cfg with
        | Some cfg ->
            this.Width  <- float cfg.Mainwin.W
            this.Height <- float cfg.Mainwin.H
            this.X      <- float cfg.Mainwin.X
            this.Y      <- float cfg.Mainwin.Y
            match Enum.TryParse<WindowState>(cfg.Mainwin.State) with
            | true, v -> this.WindowState <- v
            | _ -> ()
        | None -> ()

    member __.MainGrid = mainGrid

    member this.WindowState
        with get(): WindowState = m_windowState
        and set(v) = 
            ignore <| this.RaiseAndSetIfChanged(&m_windowState, v)


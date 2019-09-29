namespace FVim

open ReactiveUI
open Avalonia
open Avalonia.Controls
open System

/// <summary>
/// A MainWindow is a top-level container that holds a main grid as the root.
/// Other grids may be anchored to the main grid as children.
/// </summary>
type MainWindowViewModel(cfg: config.ConfigObject.Workspace option, ?_maingrid: EditorViewModel) as this =
    inherit ViewModelBase(Some 300.0, Some 300.0, Some 800.0, Some 600.0)

    let mainGrid = 
        if _maingrid.IsNone then EditorViewModel(1)
        else _maingrid.Value

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


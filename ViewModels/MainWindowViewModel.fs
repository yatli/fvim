namespace FVim

open log
open common

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

    let trace fmt = trace (sprintf "MainWindowVM #%d" mainGrid.GridId) fmt

    let mutable m_windowState = WindowState.Normal
    let mutable m_customTitleBar = false
    let mutable m_fullscreen = false

    let toggleFullScreen(gridid: int) =
        if gridid = mainGrid.GridId then
            this.Fullscreen <- not this.Fullscreen
            trace "ToggleFullScreen %A" this.Fullscreen

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
        this.Watch [
            States.Register.Notify "ToggleFullScreen" (fun [| Integer32(gridid) |] -> toggleFullScreen gridid )
            States.Register.Notify "UseCustomTitleBar" (fun [| Bool(v) |] -> this.UseCustomTitleBar <- v )
        ]

    member __.MainGrid = mainGrid

    member this.Fullscreen
        with get() : bool = m_fullscreen
        and set(v) =
            m_fullscreen <- v
            this.RaisePropertyChanged("CustomTitleBarHeight")
            this.RaisePropertyChanged("Fullscreen")

    member this.WindowState
        with get(): WindowState = m_windowState
        and set(v) = 
            ignore <| this.RaiseAndSetIfChanged(&m_windowState, v)

    member this.CustomTitleBarHeight 
        with get() =
            if this.UseCustomTitleBar && (not this.Fullscreen) then GridLength 26.0
            else GridLength 0.0

    member this.UseCustomTitleBar
        with get() = m_customTitleBar
        and set(v) = 
            ignore <| this.RaiseAndSetIfChanged(&m_customTitleBar, v)
            this.RaisePropertyChanged("CustomTitleBarHeight")

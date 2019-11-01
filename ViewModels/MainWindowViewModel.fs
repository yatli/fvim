namespace FVim

open log
open common

open ReactiveUI
open Avalonia
open Avalonia.Controls
open System
open ui

#nowarn "0025"

/// <summary>
/// A MainWindow is a top-level container that holds a main grid as the root.
/// Other grids may be anchored to the main grid as children.
/// </summary>
type MainWindowViewModel(cfg: config.ConfigObject.Workspace option, ?_maingrid: EditorViewModel) as this =
    inherit ThemableViewModelBase(Some 300.0, Some 300.0, Some 800.0, Some 600.0)

    let mainGrid = 
        if _maingrid.IsNone then EditorViewModel(1)
        else _maingrid.Value

    let trace fmt = trace (sprintf "MainWindowVM #%d" mainGrid.GridId) fmt

    let mutable m_windowState = WindowState.Normal
    let mutable m_customTitleBar = false
    let mutable m_fullscreen = false
    let mutable m_title = "FVim"

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
            match cfg.Mainwin.CustomTitleBar with
            | Some true -> m_customTitleBar <- true
            | _ -> ()
        | None -> ()
        this.Watch [
            States.Register.Notify "ToggleFullScreen" (fun [| Integer32(gridid) |] -> toggleFullScreen gridid )
            States.Register.Notify "CustomTitleBar"   (fun [| Bool(v) |] -> this.CustomTitleBar <- v )
        ]
        Model.OnWindowReady this

    member __.MainGrid = mainGrid

    member this.Title
        with get() = m_title
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_title, v)

    member this.Fullscreen
        with get() : bool = m_fullscreen
        and set(v) =
            m_fullscreen <- v
            this.RaisePropertyChanged("CustomTitleBarHeight")
            this.RaisePropertyChanged("BorderSize")
            this.RaisePropertyChanged("Fullscreen")

    member this.WindowState
        with get(): WindowState = m_windowState
        and set(v) = 
            this.RaisePropertyChanged("BorderSize")
            ignore <| this.RaiseAndSetIfChanged(&m_windowState, v)

    member this.CustomTitleBarHeight 
        with get() =
            if this.CustomTitleBar && (not this.Fullscreen) then GridLength 26.0
            else GridLength 0.0

    member this.BorderSize 
        with get() =
            if this.CustomTitleBar && (not this.Fullscreen) && (this.WindowState <> WindowState.Maximized) 
            then GridLength 1.0
            else GridLength 0.0

    member this.CustomTitleBar
        with get() = m_customTitleBar
        and set(v) = 
            ignore <| this.RaiseAndSetIfChanged(&m_customTitleBar, v)
            this.RaisePropertyChanged("CustomTitleBarHeight")
            this.RaisePropertyChanged("BorderSize")

    interface IWindow with
        member __.RootId = this.MainGrid.GridId
        member __.Title
            with get() = this.Title
            and set (v: string): unit = 
                this.Title <- v

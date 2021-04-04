namespace FVim

open log
open common

open ReactiveUI
open Avalonia
open Avalonia.Controls
open System
open System.IO
open ui
open Avalonia.Media.Imaging
open Avalonia.Media
open Avalonia.Layout

open CubismFramework

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

    let mutable m_windowState = WindowState.Normal
    let mutable m_customTitleBar = false
    let mutable m_fullscreen = false
    let mutable m_title = "FVim"

    let mutable m_bgimg_src: obj    = null
    let mutable m_bgimg_path        = ""
    let mutable m_bgimg_stretch     = Stretch.None
    let mutable m_bgimg_w           = 0.0
    let mutable m_bgimg_h           = 0.0
    let mutable m_bgimg_opacity     = 1.0
    let mutable m_bgimg_halign      = HorizontalAlignment.Left
    let mutable m_bgimg_valign      = VerticalAlignment.Top

    let toggleFullScreen(gridid: int) =
        if gridid = mainGrid.GridId then
            this.Fullscreen <- not this.Fullscreen
            trace (sprintf "MainWindowVM #%d" mainGrid.GridId) "ToggleFullScreen %A" this.Fullscreen

    let updateBackgroundImage() =
        try
            let path = states.background_image_file
            let path = if path.StartsWith("~/") then
                          Path.Join(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            path.[2..])
                        else
                          path
            if path <> m_bgimg_path then
                m_bgimg_path <- path
                trace (sprintf "MainWindowVM #%d" mainGrid.GridId) "%s" path
                let img, w, h = 
                    match Path.GetExtension path with
                    | ".json" ->
                        let live2d = new CubismAsset(path)
                        box live2d, 100.0, 100.0
                    | _ -> 
                        let img = new Bitmap(path)
                        box img, img.Size.Width, img.Size.Height
                ignore <| this.RaiseAndSetIfChanged(&m_bgimg_src, img, "BackgroundImage")
                ignore <| this.RaiseAndSetIfChanged(&m_bgimg_w, w, "BackgroundImageW")
                ignore <| this.RaiseAndSetIfChanged(&m_bgimg_h, h, "BackgroundImageH")
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_opacity, states.background_image_opacity, "BackgroundImageOpacity")
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_halign, states.background_image_halign, "BackgroundImageHAlign")
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_valign, states.background_image_valign, "BackgroundImageVAlign")
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_stretch, states.background_image_stretch, "BackgroundImageStretch")
        with _ -> 
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_src, null, "BackgroundImage")

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
            states.register.notify "ToggleFullScreen" (fun [| Integer32(gridid) |] -> toggleFullScreen gridid )
            states.register.notify "CustomTitleBar"   (fun [| Bool(v) |] -> this.CustomTitleBar <- v )
            states.register.watch "background.image"  (fun _ -> updateBackgroundImage())
        ]
        model.OnWindowReady this

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
            ignore <| this.RaiseAndSetIfChanged(&m_windowState, v)
            this.RaisePropertyChanged("BorderSize")

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

    member __.BackgroundImage with get(): obj = m_bgimg_src
    member __.BackgroundImageHAlign with get(): HorizontalAlignment = m_bgimg_halign
    member __.BackgroundImageVAlign with get(): VerticalAlignment = m_bgimg_valign
    member __.BackgroundImageW with get(): float = m_bgimg_w
    member __.BackgroundImageH with get(): float = m_bgimg_h
    member __.BackgroundImageOpacity with get(): float = m_bgimg_opacity
    member __.BackgroundImageStretch with get(): Stretch = m_bgimg_stretch

    interface IWindow with
        member __.RootId = this.MainGrid.GridId
        member __.Title
            with get() = this.Title
            and set (v: string): unit = 
                this.Title <- v

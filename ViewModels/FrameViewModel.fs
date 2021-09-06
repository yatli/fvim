namespace FVim

open log
open common
open ui
open model

open ReactiveUI
open Avalonia
open Avalonia.Controls
open System
open System.IO
open Avalonia.Media.Imaging
open Avalonia.Media
open Avalonia.Layout

#nowarn "0025"

/// <summary>
/// A Frame is a top-level OS Window, and contains one or more nvim windows.
/// 
/// It also contains one (or more, TBD) cursor widget that can jump around in 
/// the frame, between multiple nvim windows.
///
/// It also contains a completion popup window that can be anchored to a grid 
/// position.
/// 
/// There are multiple ways to organize the nvim windows inside the frame.
/// - The classic "linegrid" model: there is a single nvim grid, the MainGrid.
///   All nvim windows live in this grid, and the Frame knows nothing about them.
/// - The "multigrid" model: the MainGrid is the root (and background), and 
///   each nvim window lives in its own grid, attached to the main grid.
/// - The "windows" model: Each nvim window lives in its own grid, and there is no
///   root grid -- the window management methods are delegated to the frame, and
///   the frame should organize the grids.
/// </summary>
type FrameViewModel(cfg: config.ConfigObject.Workspace option, ?_maingrid: GridViewModel) as this =
    inherit ThemableViewModelBase(Some 300.0, Some 300.0, Some 800.0, Some 600.0)

    let mainGrid = 
        if _maingrid.IsNone then GridViewModel(1)
        else _maingrid.Value

    let mutable m_windowState = WindowState.Normal
    let mutable m_customTitleBar = false
    let mutable m_fullscreen = false
    let mutable m_title = "FVim"

    let mutable m_bgimg_src: Bitmap = null
    let mutable m_bgimg_stretch     = Stretch.None
    let mutable m_bgimg_w           = 0.0
    let mutable m_bgimg_h           = 0.0
    let mutable m_bgimg_opacity     = 1.0
    let mutable m_bgimg_halign      = HorizontalAlignment.Left
    let mutable m_bgimg_valign      = VerticalAlignment.Top

    let toggleFullScreen() =
        if mainGrid.IsFocused then
            this.Fullscreen <- not this.Fullscreen
            trace (sprintf "FrameVM #%d" mainGrid.GridId) "ToggleFullScreen %A" this.Fullscreen

    let updateBackgroundImage() =
        try
            let path = states.background_image_file
            let path = if path.StartsWith("~/") then
                          Path.Join(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            path.[2..])
                        else
                          path
            trace (sprintf "FrameVM #%d" mainGrid.GridId) "%s" path
            let new_img = new Bitmap(path)
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_src, new_img, "BackgroundImage")
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_w, m_bgimg_src.Size.Width, "BackgroundImageW")
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_h, m_bgimg_src.Size.Height, "BackgroundImageH")
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_opacity, states.background_image_opacity, "BackgroundImageOpacity")
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_halign, states.background_image_halign, "BackgroundImageHAlign")
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_valign, states.background_image_valign, "BackgroundImageVAlign")
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_stretch, states.background_image_stretch, "BackgroundImageStretch")
        with _ -> 
            ignore <| this.RaiseAndSetIfChanged(&m_bgimg_src, null, "BackgroundImage")

    do
        match cfg,_maingrid with
        | Some cfg, None ->
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
        | _, Some grid ->
            this.Height <- grid.BufferHeight
            this.Width <- grid.BufferWidth
            this.WindowState <- WindowState.Normal
            match cfg with
            | Some cfg ->
                match cfg.Mainwin.CustomTitleBar with
                | Some true -> m_customTitleBar <- true
                | _ -> ()
            | _ -> ()
        | _ -> ()
        this.Watch [
            rpc.register.notify "ToggleFullScreen" (fun _ -> toggleFullScreen())
            rpc.register.notify "CustomTitleBar"   (fun [| Bool(v) |] -> this.CustomTitleBar <- v )
            rpc.register.watch "background.image"  (fun _ -> updateBackgroundImage())
        ]
        match _maingrid with
        | Some grid -> Async.StartWithContinuations(
                           model.GetBufferPath grid.ExtWinId, 
                           (fun p -> this.Title <- $"[#] {p}"), 
                           ignore, ignore)
        | _ -> ()
        model.OnFrameReady this

    member __.MainGrid = mainGrid :> IGridUI

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

    member __.BackgroundImage with get(): IBitmap = m_bgimg_src :> IBitmap
    member __.BackgroundImageHAlign with get(): HorizontalAlignment = m_bgimg_halign
    member __.BackgroundImageVAlign with get(): VerticalAlignment = m_bgimg_valign
    member __.BackgroundImageW with get(): float = m_bgimg_w
    member __.BackgroundImageH with get(): float = m_bgimg_h
    member __.BackgroundImageOpacity with get(): float = m_bgimg_opacity
    member __.BackgroundImageStretch with get(): Stretch = m_bgimg_stretch

    interface IFrame with
        member __.MainGrid = this.MainGrid
        member __.Title
            with get() = this.Title
            and set (v: string): unit = 
                this.Title <- v
        member __.Sync(_other: IFrame) =
            let that = _other :?> FrameViewModel
            m_customTitleBar <- that.CustomTitleBar
            m_bgimg_src <- that.BackgroundImage :?> Bitmap
            m_bgimg_halign <- that.BackgroundImageHAlign
            m_bgimg_valign <- that.BackgroundImageVAlign
            m_bgimg_w <- that.BackgroundImageW
            m_bgimg_h <- that.BackgroundImageH
            m_bgimg_opacity <- that.BackgroundImageOpacity
            m_bgimg_stretch <- that.BackgroundImageStretch

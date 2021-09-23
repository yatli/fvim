namespace FVim

open common

open ReactiveUI
open Avalonia.Media
open Avalonia

#nowarn "0025"

type ThemableViewModelBase(x, y, w, h) as this = 
    inherit ViewModelBase(x, y, w, h)

    static let mutable s_normalFg: IBrush = Brushes.Black :> IBrush
    static let mutable s_normalBg: IBrush = Brushes.White :> IBrush
    static let mutable s_selectFg: IBrush = Brushes.Black :> IBrush
    static let mutable s_selectBg: IBrush = Brushes.AliceBlue :> IBrush
    static let mutable s_hoverBg:  IBrush = Brushes.AliceBlue :> IBrush
    static let mutable s_scrollbarFg: IBrush = Brushes.DimGray :> IBrush
    static let mutable s_scrollbarBg: IBrush = Brushes.Gray :> IBrush
    static let mutable s_border: IBrush = Brushes.DarkGray :> IBrush
    static let mutable s_inactivefg: IBrush = Brushes.LightGray :> IBrush

    static let s_computeColors(nfg: Color, nbg: Color, sfg: Color, sbg: Color, scfg: Color, scbg: Color, bbg: Color, ifg: Color): IBrush list =
        let tobrush (x: Color) = 
          removeAlpha x
          |> SolidColorBrush 
          :> IBrush
        let (/) (x: Color) (y: float) = 
            Color(
                byte(float x.A), 
                byte(float x.R / y), 
                byte(float x.G / y), 
                byte(float x.B / y))
        let (+) (x: Color) (y: Color) = Color(x.A + y.A, x.R + y.R, x.G + y.G, x.B + y.B)
        let hbg = sbg / 2.0 + nbg / 2.0
        List.map tobrush [ nfg; nbg; sfg; sbg; hbg; scfg; scbg; bbg; ifg ]

    static let s_SetColors(nfg: Color, nbg: Color, sfg: Color, sbg: Color, scfg: Color, scbg: Color, bbg: Color, ifg: Color) =
        let [nfg; nbg; sfg; sbg; hbg; scfg; scbg; bbg; ifg] = s_computeColors(nfg, nbg, sfg, sbg, scfg, scbg, bbg, ifg)
        s_normalFg <- nfg
        s_normalBg <- nbg
        s_selectFg <- sfg
        s_selectBg <- sbg
        s_hoverBg <- hbg
        s_scrollbarFg <- scfg
        s_scrollbarBg <-scbg
        s_border <- bbg
        s_inactivefg <- ifg

    //  Colors
    let mutable m_normalFg: IBrush = s_normalFg
    let mutable m_normalBg: IBrush = s_normalBg
    let mutable m_selectFg: IBrush = s_selectFg
    let mutable m_selectBg: IBrush = s_selectBg
    let mutable m_hoverBg:  IBrush = s_hoverBg
    let mutable m_scrollbarFg: IBrush = s_scrollbarFg
    let mutable m_scrollbarBg: IBrush = s_scrollbarBg
    let mutable m_border: IBrush = s_border
    let mutable m_inactivefg: IBrush = s_inactivefg

    do
        this.Watch [
            theme.themeconfig_ev.Publish
            |> Observable.subscribe this.SetColors
        ]
    static do
        theme.themeconfig_ev.Publish
        |> Observable.subscribe s_SetColors
        |> ignore

    new() = ThemableViewModelBase(None, None, None, None)
    member this.NormalForeground with get() = m_normalFg
    member this.NormalBackground with get() = m_normalBg
    member this.SelectForeground with get() = m_selectFg
    member this.SelectBackground with get() = m_selectBg
    member this.HoverBackground with get() = m_hoverBg
    member this.ScrollbarForeground with get() = m_scrollbarFg
    member this.ScrollbarBackground with get() = m_scrollbarBg
    member this.BorderColor with get() = m_border
    member this.InactiveForeground with get() = m_inactivefg

    member this.SetColors(nfg: Color, nbg: Color, sfg: Color, sbg: Color, scfg: Color, scbg: Color, bbg: Color, ifg: Color) =
        let [nfg; nbg; sfg; sbg; hbg; scfg; scbg; bbg; ifg] = s_computeColors(nfg, nbg, sfg, sbg, scfg, scbg, bbg, ifg)
        [
            this.RaiseAndSetIfChanged(&m_normalFg,    nfg,  "NormalForeground")
            this.RaiseAndSetIfChanged(&m_normalBg,    nbg,  "NormalBackground")
            this.RaiseAndSetIfChanged(&m_selectFg,    sfg,  "SelectForeground")
            this.RaiseAndSetIfChanged(&m_selectBg,    sbg,  "SelectBackground")
            this.RaiseAndSetIfChanged(&m_hoverBg,     hbg,  "HoverBackground")
            this.RaiseAndSetIfChanged(&m_scrollbarFg, scfg, "ScrollbarForeground")
            this.RaiseAndSetIfChanged(&m_scrollbarBg, scbg, "ScrollbarBackground")
            this.RaiseAndSetIfChanged(&m_border,      bbg,  "BorderColor")
            this.RaiseAndSetIfChanged(&m_inactivefg,  ifg,  "InactiveForeground")
        ] |> ignore


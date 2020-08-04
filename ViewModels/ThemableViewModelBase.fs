namespace FVim

open ReactiveUI
open Avalonia.Media
open Avalonia

#nowarn "0025"

type ThemableViewModelBase(x, y, w, h) as this = 
    inherit ViewModelBase(x, y, w, h)

    //  Colors
    let mutable m_normalFg: IBrush = Brushes.Black :> IBrush
    let mutable m_normalBg: IBrush = Brushes.White :> IBrush
    let mutable m_selectFg: IBrush = Brushes.Black :> IBrush
    let mutable m_selectBg: IBrush = Brushes.AliceBlue :> IBrush
    let mutable m_hoverBg:  IBrush = Brushes.AliceBlue :> IBrush
    let mutable m_scrollbarFg: IBrush = Brushes.DimGray :> IBrush
    let mutable m_scrollbarBg: IBrush = Brushes.Gray :> IBrush
    let mutable m_border: IBrush = Brushes.DarkGray :> IBrush
    let mutable m_inactivefg: IBrush = Brushes.LightGray :> IBrush

    do
        this.Watch [
            theme.themeconfig_ev.Publish
            |> Observable.subscribe this.SetColors
        ]

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
        let tobrush (x: Color) = SolidColorBrush(x, 1.0) :> IBrush
        let (/) (x: Color) (y: float) = 
            Color(
                byte(float x.A), 
                byte(float x.R / y), 
                byte(float x.G / y), 
                byte(float x.B / y))
        let (+) (x: Color) (y: Color) = Color(x.A + y.A, x.R + y.R, x.G + y.G, x.B + y.B)
        let hbg = sbg / 2.0 + nbg / 2.0
        let [nfg; nbg; sfg; sbg; hbg; scfg; scbg; bbg; ifg] = List.map tobrush [ nfg; nbg; sfg; sbg; hbg; scfg; scbg; bbg; ifg ]

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


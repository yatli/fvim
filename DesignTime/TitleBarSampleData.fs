namespace FVim

open Avalonia
open Avalonia.Media

type TitleBarSampleData() =
    member __.NormalBackground = Brushes.DarkSlateBlue
    member __.NormalForeground = Brushes.White
    member __.HoverBackground = Brushes.MediumVioletRed
    member __.HoverForeground = Brushes.Blue
    member __.SelectBackground = Brushes.Yellow
    member __.SelectForeground = Brushes.Green
    member __.FontFamily = "Consolas"
    member __.FontSize = 16.0

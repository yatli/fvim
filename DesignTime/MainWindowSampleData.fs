namespace FVim

open Avalonia.Controls

type MainWindowSampleData() =
    member __.Title = "FVim - Test.txt"
    member __.UseCustomTitleBar = true
    member __.CustomTitleBarHeight = GridLength 26.0
    member __.BorderSize = GridLength 1.0
    //member __.MainGrid = GridSampleData()

namespace FVim

open ReactiveUI

type MainWindowViewModel() =
    inherit ViewModelBase()

    let mainGrid = EditorViewModel(1)
    let mutable mainGridWidth  = 800.0
    let mutable mainGridHeight = 600.0

    member __.MainGrid = mainGrid

    member this.WindowHeight 
        with get(): float = mainGridHeight
        and set(v) = 
            ignore <| this.RaiseAndSetIfChanged(&mainGridHeight, v)

    member this.WindowWidth
        with get(): float = mainGridWidth
        and set(v) = 
            ignore <| this.RaiseAndSetIfChanged(&mainGridWidth, v)
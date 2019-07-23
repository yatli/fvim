namespace FVim

open ui

type GridVisualViewModel(area, content) =
    inherit ViewModelBase()
    member val Area: GridRect = area with get, set
    member val Content: obj = content with get, set

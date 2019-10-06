namespace FVim

open ReactiveUI

type TitleBarViewModel() =
    inherit ViewModelBase()

    let mutable m_onright = true
    let mutable m_title = ""

    member this.ButtonsOnRightSide
        with get() = m_onright
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_onright, v)

    member this.Title
        with get() = m_title
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_title, v)

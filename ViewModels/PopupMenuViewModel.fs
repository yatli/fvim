namespace FVim

open neovim.def
open log

open ReactiveUI
open System.Collections.ObjectModel
open FVim.common.helpers
open Avalonia

type CompletionItemViewModel(item: CompleteItem) =
    inherit ViewModelBase()
    do
        trace "CompletionItemViewModel" "item = %A" item
    member __.Text = item.word
    member __.Menu = _d "" item.menu
    member __.Info = _d "" item.info
    member __.ShowIcon = false

type PopupMenuViewModel() =
    inherit ViewModelBase()

    let mutable m_show = false
    let mutable m_selection = -1
    let m_items = ObservableCollection<CompletionItemViewModel>()

    member this.Show
        with get(): bool = m_show
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_show, v, "Show")

    member this.Selection
        with get(): int = m_selection
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_selection, v, "Selection")

    member this.Items
        with get() = m_items

    member this.SetItems(items: CompleteItem[], textArea: Rect, lineHeight: float, desiredSizeVec: Point, editorSizeVec: Point) =
        m_items.Clear()

        //  Decide where to show the menu.
        //  --------------------------------

        let _cap (vec: Point) =
            let x = min editorSizeVec.X (max 0.0 vec.X)
            let y = min editorSizeVec.Y (max 0.0 vec.Y)
            Point(x, y)

        let padding = Point(5.0, 10.0)

        let se_topleft     = textArea.BottomLeft
        let se_bottomright = _cap(se_topleft + desiredSizeVec + padding)

        let ne_topleft     = _cap(textArea.TopLeft - Point(0.0, desiredSizeVec.Y) - padding)
        let ne_bottomright = _cap(textArea.TopLeft + Point(desiredSizeVec.X, 0.0))

        let r_se = Rect(se_topleft, se_bottomright)
        let r_ne = Rect(ne_topleft, ne_bottomright)

        let region = 
            if r_se.Width > desiredSizeVec.X / 3.0 || r_se.Width > r_ne.Width then
                r_se
            else
                r_ne

        this.X <- region.X
        this.Y <- region.Y
        this.Width <- region.Width
        this.Height <- region.Height

        items 
        |> Array.map CompletionItemViewModel 
        |> Array.iter (fun x -> 
            x.Height <- lineHeight
            this.Items.Add x)

namespace FVim

open neovim.def
open log

open ReactiveUI
open System.Collections.ObjectModel

type CompletionItemViewModel(item: CompleteItem) as this =
    inherit ViewModelBase()
    do
        trace "CompletionItemViewModel" "item = %A" item
    member __.Text = item.word

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

    member this.SetItems(items: CompleteItem[]) =
        m_items.Clear()
        items 
        |> Array.map CompletionItemViewModel 
        |> Array.iter m_items.Add

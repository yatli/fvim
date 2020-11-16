namespace FVim

open ReactiveUI
open Avalonia
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia.Data

open FVim.log

open FSharp.Control.Reactive

type PopupMenu() as this =
    inherit ViewBase<PopupMenuViewModel>()

    let relayToParent (e: #Avalonia.Interactivity.RoutedEventArgs) =
        if this.Parent <> null then
            trace "PopupMenu" "relay to parent"
            this.Parent.Focus()

    do
        AvaloniaXamlLoader.Load(this)
        let lst = this.FindControl<ListBox>("List")
        this.Watch [
            lst.SelectionChanged.Subscribe(fun x -> 
                let items = x.AddedItems
                if items.Count > 0 then Some items.[0] else None
                |> Option.iter lst.ScrollIntoView)
        ]

    override this.OnKeyDown(e) =
        relayToParent e

    override this.OnTextInput(e) =
        relayToParent e

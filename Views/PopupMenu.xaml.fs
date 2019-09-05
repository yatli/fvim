namespace FVim

open ReactiveUI
open Avalonia
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia.Data

open System.Linq
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
            lst.SelectionChanged.Subscribe(fun x -> for item in x.AddedItems do lst.ScrollIntoView item)
            (*this.ViewModelConnected.Subscribe(fun vm -> vm.Watch [*)
                (*vm.ObservableForProperty(fun x -> x.SelectBackground)*)
                (*|> Observable.subscribe(fun selectBg -> lst.Resources.["HighlightBrush"] <- selectBg.Value)*)
            (*])*)
        ]

    override this.OnKeyDown(e) =
        relayToParent e

    override this.OnTextInput(e) =
        relayToParent e

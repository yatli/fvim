namespace FVim
open System.Windows.Input
open ReactiveUI
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia
open Avalonia.Input
open Avalonia.Interactivity
open FVim.log

type CompletionItem() as this =
    inherit ViewBase<CompletionItemViewModel>()

    let onDoubleTapped (ev: RoutedEventArgs) =
        ev.Handled <- true
        this.ViewModel.OnCommit()

    do
        this.Watch [
            this.DoubleTapped.Subscribe onDoubleTapped
        ]
        AvaloniaXamlLoader.Load(this)


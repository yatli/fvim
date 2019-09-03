namespace FVim
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia

type CompletionItem() as this =
    inherit ViewBase<CompletionItemViewModel>()
    do
        AvaloniaXamlLoader.Load(this)

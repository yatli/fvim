namespace FVim
open Avalonia.Markup.Xaml

type CompletionItem() as this =
    inherit ViewBase<CompletionItemViewModel>()
    do
        AvaloniaXamlLoader.Load(this)

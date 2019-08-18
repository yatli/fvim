namespace FVim
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia

type CompletionItem() as this =
    inherit ViewBase<CompletionItemViewModel>()
    do
        this.Watch [
            this.Bind(UserControl.MaxHeightProperty, Avalonia.Data.Binding("Height"))
        ]
        AvaloniaXamlLoader.Load(this)

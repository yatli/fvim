namespace FVim

open Avalonia.Markup.Xaml

type TitleBar() as this =
    inherit ViewBase<TitleBarViewModel>()

    do
        AvaloniaXamlLoader.Load(this)

namespace FVim

open Avalonia
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia.Data

type PopupMenu() as this =
    inherit ViewBase<PopupMenuViewModel>()

    do
        this.Watch [
            this.Bind(UserControl.IsVisibleProperty, Binding("Show"))
            this.Bind(UserControl.HeightProperty, Binding("Height"))
        ]
        this.Height <- System.Double.NaN
        AvaloniaXamlLoader.Load(this)

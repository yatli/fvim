namespace FVim

open Avalonia
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia.Data

open System.Linq
open FVim.log

type PopupMenu() as this =
    inherit ViewBase<PopupMenuViewModel>()

    let relayToParent (e: #Avalonia.Interactivity.RoutedEventArgs) =
        if this.Parent <> null then
            trace "PopupMenu" "relay to parent"
            this.Parent.Focus()

    do
        this.Watch [
            this.Bind(UserControl.IsVisibleProperty, Binding("Show"))
            this.Bind(UserControl.HeightProperty, Binding("Height"))
            this.Bind(UserControl.WidthProperty, Binding("Width"))
        ]
        this.Height <- System.Double.NaN
        AvaloniaXamlLoader.Load(this)

    override this.OnKeyDown(e) =
        relayToParent e

    override this.OnTextInput(e) =
        relayToParent e

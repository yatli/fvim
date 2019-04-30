namespace FVim

open Avalonia
open Avalonia.Markup.Xaml
open Avalonia.Controls

type MainWindow() as this =
    inherit Window()
    let mutable nvim = neovim.create() |> neovim.start
    do
        printf "nvim created: %A" nvim
        this.Closing.Add this.onClosing
        this.Closed.Add this.onClosed
        AvaloniaXamlLoader.Load this
    member this.onClosing(args) =
        //TODO send closing request to neovim
        ()
    member this.onClosed(args) =
        nvim <- neovim.stop nvim 1000

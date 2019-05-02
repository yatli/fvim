namespace FVim

open Avalonia
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia.Threading
open FVim.neovim
open System.Threading
open System.Collections.Generic
open System.Threading.Tasks
open System
open FVim.neovim.rpc
open FVim.neovim.def

type MainWindow(datactx: FVimViewModel) as this =
    inherit Window()

    do
        printfn "initialize avalonia UI..."
        this.DataContext <- datactx
        this.Closing.Add datactx.OnTerminating
        this.Closed.Add  datactx.OnTerminated

        AvaloniaXamlLoader.Load this
        Avalonia.DevToolsExtensions.AttachDevTools(this);

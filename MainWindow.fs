namespace FVim

open Avalonia
open Avalonia.Markup.Xaml
open Avalonia.Controls
open FVim.neovim
open Avalonia.Threading
open System.Threading

type MainWindow() as this =
    inherit Window()
    let nvim = NeovimProcess()
    do
        nvim.start()
        printfn "nvim created: %O" nvim
        nvim.subscribe (AvaloniaSynchronizationContext.Current) (printfn "%A") |> ignore
        //nvim.subscribe (printfn "%A") |> ignore
        this.Closing.Add this.onClosing
        this.Closed.Add this.onClosed
        //Async.Start <| nvim.ui_attach(100, 30)
        let attach = Async.StartAsTask(nvim.ui_attach(100, 30))
        //printfn "%A" attach
        AvaloniaXamlLoader.Load this
    member this.onClosing(args) =
        //TODO send closing request to neovim
        ()
    member this.onClosed(args) =
        printfn "window closed. terminating nvim"
        nvim.stop 1
        printfn "nvim terminated: %O" nvim
        System.Console.ReadKey() |> ignore

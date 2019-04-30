namespace FVim

open Avalonia
open Avalonia.Markup.Xaml
open Avalonia.Controls
open FVim.neovim
open Avalonia.Threading
open System.Threading
open System.Collections.Generic
open System.Threading.Tasks
open System
open FVim.neovim.rpc
open FVim.neovim.def



type MainWindow() as this =
    inherit Window()
    let nvim = Process()
    let requestHandlers      = Dictionary<string, obj[] -> Response Async>()
    let notificationHandlers = Dictionary<string, obj[] -> unit Async>()

    let request  name fn = requestHandlers.Add(name, fn)
    let notify   name fn = notificationHandlers.Add(name, fn)

    do
        printfn "initialize avalonia UI..."
        this.Closing.Add this.OnClosing
        this.Closed.Add  this.OnClosed
        AvaloniaXamlLoader.Load this

        // the UI should be ready for some drawing now.
        printfn "registering msgpack-rpc handlers..."

        notify "redraw" (fun args -> async {
            printfn "redraw: %A" args
            ()
        })


        printfn "starting neovim instance..."
        nvim.start()
        ignore <|
        nvim.subscribe 
            (AvaloniaSynchronizationContext.Current) 
            (function 
             | Request(id, req, reply) -> 
                Async.Start(async { 
                    let! rsp = requestHandlers.[req.method](req.parameters)
                    do! reply id rsp
                })
             | Notification req -> 
                Async.Start(notificationHandlers.[req.method](req.parameters))
             | _ -> ())
        ignore <| nvim.ui_attach(100, 30)
    member this.OnClosing(args) =
        //TODO send closing request to neovim
        ()
    member this.OnClosed(args) =
        printfn "terminating nvim..."
        nvim.stop 1

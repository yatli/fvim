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
    let nvim = Process()
    let requestHandlers      = Dictionary<string, obj[] -> Response Async>()
    let notificationHandlers = Dictionary<string, obj[] -> unit Async>()

    let request  name fn = requestHandlers.Add(name, fn)
    let notify   name fn = notificationHandlers.Add(name, fn)

    let on_closing(args) =
        //TODO send closing request to neovim
        ()

    let on_closed (args) =
        printfn "terminating nvim..."
        nvim.stop 1

    let msg_dispatch =
        function
        | Request(id, req, reply) -> 
           Async.Start(async { 
               let! rsp = requestHandlers.[req.method](req.parameters)
               do! reply id rsp
           })
        | Notification req -> 
           Async.Start(notificationHandlers.[req.method](req.parameters))
        | Redraw cmd -> datactx.Redraw cmd
        | _ -> ()

    do
        printfn "initialize avalonia UI..."
        this.DataContext <- datactx
        this.Closing.Add on_closing
        this.Closed.Add  on_closed
        this.SizeToContent <- SizeToContent.WidthAndHeight

        AvaloniaXamlLoader.Load this
        Avalonia.DevToolsExtensions.AttachDevTools(this);

        // the UI should be ready for some drawing now.
        printfn "registering msgpack-rpc handlers..."

        printfn "starting neovim instance..."
        nvim.start()
        ignore <|
        nvim.subscribe 
            (AvaloniaSynchronizationContext.Current) 
            (msg_dispatch)
        ignore <| nvim.ui_attach(100, 30)


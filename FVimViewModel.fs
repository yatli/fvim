namespace FVim

open FVim.neovim.def
open FVim.neovim.rpc
open Avalonia.Diagnostics.ViewModels
open Avalonia.Media
open System
open System.Collections.Generic
open Avalonia.Threading

type FVimViewModel() =
    inherit ViewModelBase()
    let redraw = Event<RedrawCommand[]>()
    let nvim = Process()
    let requestHandlers      = Dictionary<string, obj[] -> Response Async>()
    let notificationHandlers = Dictionary<string, obj[] -> unit Async>()

    let request  name fn = requestHandlers.Add(name, fn)
    let notify   name fn = notificationHandlers.Add(name, fn)

    let msg_dispatch =
        function
        | Request(id, req, reply) -> 
           Async.Start(async { 
               let! rsp = requestHandlers.[req.method](req.parameters)
               do! reply id rsp
           })
        | Notification req -> 
           Async.Start(notificationHandlers.[req.method](req.parameters))
        | Redraw cmd -> redraw.Trigger cmd
        | _ -> ()

    do
        printfn "starting neovim instance..."
        nvim.start()
        ignore <|
        nvim.subscribe 
            (AvaloniaSynchronizationContext.Current) 
            (msg_dispatch)

        printfn "registering msgpack-rpc handlers..."


    member val WindowHeight: int         = 700 with get,set
    member val WindowWidth:  int         = 900 with get,set

    member this.RedrawCommands = redraw.Publish
    member this.Redraw(cmds: RedrawCommand[]) = redraw.Trigger cmds

    member this.OnTerminated (args) =
        printfn "terminating nvim..."
        nvim.stop 1

    member this.OnTerminating(args) =
        //TODO send closing request to neovim
        ()

    member this.OnGridReady(gridui: IGridUI) =
        // connect the redraw commands
        gridui.Connect redraw.Publish

        // the UI should be ready for events now. notify nvim about its presence
        if gridui.Id = 1 then
            printfn "attaching to nvim on first grid ready signal. size = %A %A" gridui.GridWidth gridui.GridHeight
            ignore <| nvim.ui_attach(gridui.GridWidth, gridui.GridHeight)
        else
            failwithf "grid: unsupported: %A" gridui.Id


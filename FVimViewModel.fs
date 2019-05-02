namespace FVim

open FVim.neovim.def
open Avalonia.Diagnostics.ViewModels
open Avalonia.Media
open System

type FVimViewModel() =
    inherit ViewModelBase()
    let redraw = Event<RedrawCommand[]>()

    member val WindowHeight: int         = 700 with get,set
    member val WindowWidth:  int         = 900 with get,set

    member this.RedrawCommands = redraw.Publish
    member this.Redraw(cmds: RedrawCommand[]) = redraw.Trigger cmds

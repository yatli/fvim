namespace FVim

open FVim.neovim.def
open Avalonia.Input

type IGridUI =
    abstract Id: int
    abstract Connect: IEvent<RedrawCommand[]> -> unit
    abstract GridHeight: int
    abstract GridWidth: int
    abstract Resized: IEvent<IGridUI>
    abstract KeyInput: IEvent<KeyEventArgs>

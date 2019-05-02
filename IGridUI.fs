namespace FVim

open FVim.neovim.def

type IGridUI =
    abstract Id: int
    abstract Connect: IEvent<RedrawCommand[]> -> unit
    abstract GridHeight: int
    abstract GridWidth: int

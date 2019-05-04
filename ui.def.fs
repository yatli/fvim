namespace FVim

open FVim.neovim.def
open Avalonia.Input

type InputEvent = 
| Key          of mods: InputModifiers * key: Key
| MousePress   of mods: InputModifiers * row: int * col: int * button: MouseButton * combo: int
| MouseRelease of mods: InputModifiers * row: int * col: int * button: MouseButton
| MouseDrag    of mods: InputModifiers * row: int * col: int * button: MouseButton
| MouseWheel   of mods: InputModifiers * row: int * col: int * dx: int * dy: int

type IGridUI =
    abstract Id: int
    abstract Connect: IEvent<RedrawCommand[]> -> unit
    abstract GridHeight: int
    abstract GridWidth: int
    abstract Resized: IEvent<IGridUI>
    abstract Input: IEvent<InputEvent>

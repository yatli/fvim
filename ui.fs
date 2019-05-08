module FVim.ui

open FVim.neovim.def
open Avalonia.Input
open Avalonia.Media
open System.Runtime.InteropServices

type InputEvent = 
| Key          of mods: InputModifiers * key: Key
| MousePress   of mods: InputModifiers * row: int * col: int * button: MouseButton * combo: int
| MouseRelease of mods: InputModifiers * row: int * col: int * button: MouseButton
| MouseDrag    of mods: InputModifiers * row: int * col: int * button: MouseButton
| MouseWheel   of mods: InputModifiers * row: int * col: int * dx: int * dy: int
| TextInput    of text: string

type IGridUI =
    abstract Id: int
    abstract Connect: IEvent<RedrawCommand[]> -> IEvent<int> -> unit
    abstract GridHeight: int
    abstract GridWidth: int
    abstract Resized: IEvent<IGridUI>
    abstract Input: IEvent<InputEvent>

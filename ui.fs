module FVim.ui

open FVim.neovim.def
open Avalonia.Input
open Avalonia.Media
open System.Runtime.InteropServices

let private _systemFonts = 
    FontFamily.SystemFontFamilies
    |> Seq.collect (fun ff -> Seq.map (fun name -> (name, ff)) ff.FamilyNames)
    |> Map.ofSeq

let private fallback_font = 
    if   RuntimeInformation.IsOSPlatform OSPlatform.Windows then FontFamily("Consolas")
    elif RuntimeInformation.IsOSPlatform OSPlatform.Linux   then FontFamily("Monospace Regular")
    elif RuntimeInformation.IsOSPlatform OSPlatform.OSX     then FontFamily("Courier")
    else FontFamily("Monospace") // ??

let FindFontFace = _systemFonts.TryFind >> Option.defaultValue fallback_font

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

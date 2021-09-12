module FVim.widgets

open common
open SkiaSharp
open Avalonia.Media.Imaging
open System.IO

type GuiWidgetType =
| ImageWidget of Bitmap
| UnknownWidget of mime: string * data: byte[]
| NotFound

let private guiWidgets = hashmap[]
let loadGuiResource (id:int) (mime: string) (data: byte[]) =
    if mime.StartsWith("image/") then
        use stream = new MemoryStream(data)
        guiWidgets.[id] <- ImageWidget(new Bitmap(stream))
    else
        guiWidgets.[id] <- UnknownWidget(mime, data)

let getGuiWidget (id: int) =
    match guiWidgets.TryGetValue id with
    | true, x -> x
    | _ -> 
        // TODO make another request here
        NotFound

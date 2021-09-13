module FVim.widgets

open common
open Avalonia.Media.Imaging
open Avalonia.Svg
open System.IO
open Avalonia

type GuiWidgetType =
| BitmapWidget of Bitmap
| VectorImageWidget of SvgImage
| UnknownWidget of mime: string * data: byte[]
| NotFound

let private guiWidgets = hashmap[]
let loadGuiResource (id:int) (mime: string) (data: byte[]) =
    if mime = "image/svg" then
      let tmp = System.IO.Path.GetTempFileName()
      System.IO.File.WriteAllBytes(tmp, data)
      let img = new SvgImage()
      img.Source <- SvgSource.Load(tmp, null)
      guiWidgets.[id] <- VectorImageWidget(img)
    elif mime.StartsWith("image/") then
        use stream = new MemoryStream(data)
        guiWidgets.[id] <- BitmapWidget(new Bitmap(stream))
    else
        guiWidgets.[id] <- UnknownWidget(mime, data)

let getGuiWidget (id: int) =
    match guiWidgets.TryGetValue id with
    | true, x -> x
    | _ -> 
        // TODO make another request here
        NotFound

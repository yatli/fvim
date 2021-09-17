module FVim.widgets

open common
open def
open Avalonia.Media.Imaging
open Avalonia.Svg
open System.IO
open Avalonia

type GuiWidgetType =
| BitmapWidget of Bitmap
| VectorImageWidget of SvgImage
| UnknownWidget of mime: string * data: byte[]
| NotFound

let private widgets = hashmap[]
let private placements = hashmap[] // the placements of gui-widgets

let loadGuiWidgetPlacements (buf:int) (M: WidgetPlacement[]) =
  let index = hashmap[]
  for {mark = mark} as p in M do
    index.[mark] <- p
  placements.[buf] <- index

let private _noPlacements = hashmap[]

let getGuiWidgetPlacements (buf:int) =
  match placements.TryGetValue buf with
  | true, p -> p
  | _ -> _noPlacements

let loadGuiResource (id:int) (mime: string) (data: byte[]) =
    if mime = "image/svg" then
      let tmp = System.IO.Path.GetTempFileName()
      System.IO.File.WriteAllBytes(tmp, data)
      let img = new SvgImage()
      img.Source <- SvgSource.Load(tmp, null)
      widgets.[id] <- VectorImageWidget(img)
    elif mime.StartsWith("image/") then
        use stream = new MemoryStream(data)
        widgets.[id] <- BitmapWidget(new Bitmap(stream))
    else
        widgets.[id] <- UnknownWidget(mime, data)

let getGuiWidget (id: int) =
    match widgets.TryGetValue id with
    | true, x -> x
    | _ -> 
        // TODO make another request here
        NotFound

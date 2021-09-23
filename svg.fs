module FVim.svg

open common

open ShimSkiaSharp
open Avalonia.Svg.Commands
open System.Collections.Generic
open System
open Avalonia.Media.Immutable
open Avalonia
open Avalonia.Media
open Avalonia.Svg

let private s_assetLoader = new AvaloniaAssetLoader() :> Svg.Model.IAssetLoader
let private s_factory = AvaloniaLocator.Current.GetService<Platform.IPlatformRenderInterface>()

type SvgPicture(data: string) =
  let svg = Svg.SvgDocument.FromSvg(data)
  let picture = 
    match Svg.Model.SvgExtensions.ToModel(svg, s_assetLoader) with
    | pic,_,_ -> pic
  let _commands = ResizeArray<DrawCommand>()
  let mutable m_brush: SolidColorBrush = null
  let mutable m_fg = Colors.Black
  let mutable m_bg = Colors.White
  let b (brush: IBrush) = 
    if isNull m_brush then brush else 
    let color = 
      match brush with
      | :? SolidColorBrush as brush -> brush.Color
      | :? ImmutableSolidColorBrush as brush -> brush.Color
      | _ -> Colors.Blue
    if color = Colors.White then
      m_brush.Color <- m_bg
      m_brush :> IBrush
    elif color = Colors.Black then 
      m_brush.Color <- m_fg
      m_brush :> IBrush
    else brush
  let p (pen: IPen) = 
    match pen with
    | :? Pen as pen ->
      pen.Brush <- b pen.Brush
    | _ -> ()
    pen
  do
    if isNull picture.Commands then () else
    for cmd in picture.Commands do
    match cmd with
    | :? ClipPathCanvasCommand as clipPathCanvasCommand ->
      let path = clipPathCanvasCommand.ClipPath.ToGeometry(false)
      // TODO: clipPathCanvasCommand.Operation
      // TODO: clipPathCanvasCommand.Antialias
      if notNull path then
        _commands.Add(new GeometryClipDrawCommand(path))
    | :? ClipRectCanvasCommand as clipRectCanvasCommand ->
      let rect = clipRectCanvasCommand.Rect.ToRect()
      // TODO: clipRectCanvasCommand.Operation
      // TODO: clipRectCanvasCommand.Antialias
      _commands.Add(new ClipDrawCommand(rect))
    | :? SaveCanvasCommand ->
      // TODO: SaveCanvasCommand
      _commands.Add(new SaveDrawCommand())
    | :? RestoreCanvasCommand ->
      // TODO: RestoreCanvasCommand
      _commands.Add(new RestoreDrawCommand())
    | :? SetMatrixCanvasCommand as setMatrixCanvasCommand ->
      let matrix = setMatrixCanvasCommand.Matrix.ToMatrix()
      _commands.Add(new SetTransformDrawCommand(matrix))
    | :? SaveLayerCanvasCommand as saveLayerCanvasCommand ->
      // TODO: SaveLayerCanvasCommand
      _commands.Add(new SaveLayerDrawCommand())
    | :? DrawImageCanvasCommand as drawImageCanvasCommand ->
      if isNull drawImageCanvasCommand.Image then () else
      let image = drawImageCanvasCommand.Image.ToBitmap()
      if image = null then () else
      let source = drawImageCanvasCommand.Source.ToRect()
      let dest = drawImageCanvasCommand.Dest.ToRect()
      let paint = drawImageCanvasCommand.Paint
      let bitmapInterpolationMode =
        if paint <> null then paint.FilterQuality.ToBitmapInterpolationMode()
        else Visuals.Media.Imaging.BitmapInterpolationMode.Default
      _commands.Add(new ImageDrawCommand(image, source, dest, bitmapInterpolationMode))
    | :? DrawPathCanvasCommand as drawPathCanvasCommand ->
      if (isNull drawPathCanvasCommand.Path) || (isNull drawPathCanvasCommand.Paint) then () else
      let struct(brush, pen) = drawPathCanvasCommand.Paint.ToBrushAndPen()
      match drawPathCanvasCommand.Path.Commands ?-> (fun x -> x.Count) with
      | ValueSome 1 ->
        let pathCommand = drawPathCanvasCommand.Path.Commands.[0]
        match pathCommand with
        | :? AddRectPathCommand as addRectPathCommand ->
          let rect = addRectPathCommand.Rect.ToRect()
          _commands.Add(new RectangleDrawCommand(brush, pen, rect, 0.0, 0.0))
          true
        | :? AddRoundRectPathCommand as addRoundRectPathCommand ->
          let rect = addRoundRectPathCommand.Rect.ToRect()
          let rx = float addRoundRectPathCommand.Rx
          let ry = float addRoundRectPathCommand.Ry
          _commands.Add(new RectangleDrawCommand(brush, pen, rect, rx, ry))
          true
        | :? AddOvalPathCommand as addOvalPathCommand ->
          let rect = addOvalPathCommand.Rect.ToRect()
          let ellipseGeometry = s_factory.CreateEllipseGeometry(rect)
          _commands.Add(new GeometryDrawCommand(brush, pen, ellipseGeometry))
          true
        | :? AddCirclePathCommand as addCirclePathCommand ->
          let x = float addCirclePathCommand.X
          let y = float addCirclePathCommand.Y
          let radius = float addCirclePathCommand.Radius
          let rect = new Rect(x - radius, y - radius, radius + radius, radius + radius)
          let ellipseGeometry = s_factory.CreateEllipseGeometry(rect)
          _commands.Add(new GeometryDrawCommand(brush, pen, ellipseGeometry))
          true
        | :? AddPolyPathCommand as addPolyPathCommand ->
          if isNull addPolyPathCommand.Points then false else
          let close = addPolyPathCommand.Close
          let polylineGeometry = addPolyPathCommand.Points.ToGeometry(close)
          _commands.Add(new GeometryDrawCommand(brush, pen, polylineGeometry))
          true
        | _ -> false
      | ValueSome 2 ->
        match drawPathCanvasCommand.Path.Commands.[0], drawPathCanvasCommand.Path.Commands.[1] with
        | :? MoveToPathCommand as moveTo, (:? LineToPathCommand as lineTo) ->
          let p1 = new Point(float moveTo.X, float moveTo.Y)
          let p2 = new Point(float lineTo.X, float lineTo.Y)
          _commands.Add(new LineDrawCommand(pen, p1, p2))
          true
        | _ -> false
      | _ -> false
      |> function
         | false ->
           let geometry = drawPathCanvasCommand.Path.ToGeometry(notNull brush)
           if notNull geometry then
             _commands.Add(new GeometryDrawCommand(brush, pen, geometry))
         | _ -> ()
    | :? DrawTextBlobCanvasCommand as drawPositionedTextCanvasCommand ->
      // TODO: DrawTextBlobCanvasCommand
      ()
    | :? DrawTextCanvasCommand as drawTextCanvasCommand ->
      if isNull drawTextCanvasCommand.Paint then () else
      let struct(brush, _) = drawTextCanvasCommand.Paint.ToBrushAndPen()
      let text = drawTextCanvasCommand.Paint.ToFormattedText(drawTextCanvasCommand.Text)
      let x = float drawTextCanvasCommand.X
      let y = float drawTextCanvasCommand.Y
      let origin = new Point(x, y - float drawTextCanvasCommand.Paint.TextSize)
      _commands.Add(new TextDrawCommand(brush, origin, text))
    | :? DrawTextOnPathCanvasCommand as drawTextOnPathCanvasCommand ->
      // TODO: DrawTextOnPathCanvasCommand
      ()
    | _ -> ()
  member __.Draw(context: Avalonia.Media.DrawingContext) =
    let pushedStates = new Stack<Stack<IDisposable>>()
    use _transformContainerState = context.PushTransformContainer()
    for cmd in _commands do
    match cmd with
    | :? GeometryClipDrawCommand as geometryClipDrawCommand ->
      let geometryPushedState = context.PushGeometryClip(geometryClipDrawCommand.Clip)
      let currentPushedStates = pushedStates.Peek()
      currentPushedStates.Push(geometryPushedState)
    | :? ClipDrawCommand as clipDrawCommand ->
      let clipPushedState = context.PushClip(clipDrawCommand.Clip)
      let currentPushedStates = pushedStates.Peek()
      currentPushedStates.Push(clipPushedState)
    | :? SaveDrawCommand ->
      pushedStates.Push(new Stack<IDisposable>())
    | :? RestoreDrawCommand ->
      let currentPushedStates = pushedStates.Pop()
      while currentPushedStates.Count > 0 do
        let pushedState = currentPushedStates.Pop()
        pushedState.Dispose()
    | :? SetTransformDrawCommand as setTransformDrawCommand ->
      let transformPreTransform = context.PushSetTransform(setTransformDrawCommand.Matrix)
      let currentPushedStates = pushedStates.Peek()
      currentPushedStates.Push(transformPreTransform)
    | :? SaveLayerDrawCommand as saveLayerDrawCommand ->
      pushedStates.Push(new Stack<IDisposable>())
    | :? ImageDrawCommand as imageDrawCommand ->
      context.DrawImage(
          imageDrawCommand.Source,
          imageDrawCommand.SourceRect,
          imageDrawCommand.DestRect,
          imageDrawCommand.BitmapInterpolationMode)
    | :? GeometryDrawCommand as geometryDrawCommand ->
      context.DrawGeometry(
          b geometryDrawCommand.Brush,
          p geometryDrawCommand.Pen,
          geometryDrawCommand.Geometry)
    | :? LineDrawCommand as lineDrawCommand ->
      context.DrawLine(
          p lineDrawCommand.Pen,
          lineDrawCommand.P1,
          lineDrawCommand.P2)
    | :? RectangleDrawCommand as rectangleDrawCommand ->
      context.DrawRectangle(
          b rectangleDrawCommand.Brush,
          p rectangleDrawCommand.Pen,
          rectangleDrawCommand.Rect,
          rectangleDrawCommand.RadiusX,
          rectangleDrawCommand.RadiusY)
    | :? TextDrawCommand as textDrawCommand ->
      context.DrawText(
          b textDrawCommand.Brush,
          textDrawCommand.Origin,
          textDrawCommand.FormattedText)
    | _ -> ()
  member __.Width = float picture.CullRect.Width
  member __.Height = float picture.CullRect.Height
  member __.SetTheme(brush, fg, bg) =
    m_brush <- brush
    m_fg <- fg
    m_bg <- bg

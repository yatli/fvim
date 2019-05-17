namespace FVim

open neovim.rpc
open log

open Avalonia.Markup.Xaml
open Avalonia.Controls
open ReactiveUI
open Avalonia
open Avalonia.Data

type MainWindow() as this =
    inherit Window()

    static let XProp = AvaloniaProperty.Register<MainWindow,int>("PosX")
    static let YProp = AvaloniaProperty.Register<MainWindow,int>("PosY")

    do
        this.Closing.Add (fun _ -> Model.OnTerminating())
        this.Closed.Add  (fun _ -> Model.OnTerminated())
        //this.Renderer.DrawDirtyRects <- true

        AvaloniaXamlLoader.Load this
        #if DEBUG
        this.Renderer.DrawFps <- true
        Avalonia.DevToolsExtensions.AttachDevTools(this)
        #endif

        ignore <| this.Bind(XProp, Binding("WindowX", BindingMode.TwoWay))
        ignore <| this.Bind(YProp, Binding("WindowY", BindingMode.TwoWay))

        this.PositionChanged.Add (fun p ->
            this.SetValue(XProp, p.Point.X)
            this.SetValue(YProp, p.Point.Y))

        Model.Notify "DrawFPS" (fun [| Bool(v) |] -> 
            trace "Model" "DrawFPS: %A" v
            Avalonia.Application.Current.MainWindow.Renderer.DrawFps <- v
        ) |> ignore

    override this.OnDataContextChanged _ =
        let ctx = this.DataContext :?> MainWindowViewModel
        let pos = PixelPoint(ctx.WindowX, ctx.WindowY)
        this.Position <- pos
        this.WindowState <- ctx.WindowState


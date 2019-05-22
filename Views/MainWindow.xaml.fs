namespace FVim

open neovim.rpc
open log

open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia.Input
open ReactiveUI
open Avalonia
open Avalonia.Data

type MainWindow() as this =
    inherit ReactiveWindow<MainWindowViewModel>()

    static let XProp = AvaloniaProperty.Register<MainWindow,int>("PosX")
    static let YProp = AvaloniaProperty.Register<MainWindow,int>("PosY")

    do
        AvaloniaXamlLoader.Load this
        #if DEBUG
        this.Renderer.DrawFps <- true
        Avalonia.DevToolsExtensions.AttachDevTools(this)
        #endif

        this.Watch [
            this.Closing.Subscribe (fun _ -> Model.OnTerminating())
            this.Closed.Subscribe  (fun _ -> Model.OnTerminated())
            this.Bind(XProp, Binding("WindowX", BindingMode.TwoWay))
            this.Bind(YProp, Binding("WindowY", BindingMode.TwoWay))

            this.PositionChanged.Subscribe (fun p ->
                this.SetValue(XProp, p.Point.X)
                this.SetValue(YProp, p.Point.Y))

            Model.Notify "DrawFPS" (fun [| Bool(v) |] -> 
                trace "Model" "DrawFPS: %A" v
                Avalonia.Application.Current.MainWindow.Renderer.DrawFps <- v)
        ]


    override this.OnDataContextChanged _ =
        let ctx = this.DataContext :?> MainWindowViewModel
        let pos = PixelPoint(ctx.WindowX, ctx.WindowY)
        this.Position <- pos
        this.WindowState <- ctx.WindowState


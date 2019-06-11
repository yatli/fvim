namespace FVim

open neovim.rpc
open log

open ReactiveUI
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia
open Avalonia.Data
open Avalonia.ReactiveUI

type MainWindow() as this =
    inherit ReactiveWindow<MainWindowViewModel>()

    static let XProp = AvaloniaProperty.Register<MainWindow,int>("PosX")
    static let YProp = AvaloniaProperty.Register<MainWindow,int>("PosY")

    do
        #if DEBUG
        this.Renderer.DrawFps <- true
        Avalonia.DevToolsExtensions.AttachDevTools(this)
        #endif

        DragDrop.SetAllowDrop(this, true)

        this.Watch [
            this.Closing.Subscribe (fun e -> Model.OnTerminating e)
            this.Closed.Subscribe  (fun _ -> Model.OnTerminated())
            this.Bind(XProp, Binding("WindowX", BindingMode.TwoWay))
            this.Bind(YProp, Binding("WindowY", BindingMode.TwoWay))

            this.PositionChanged.Subscribe (fun p ->
                this.SetValue(XProp, p.Point.X)
                this.SetValue(YProp, p.Point.Y))

            Model.Notify "DrawFPS" (fun [| Bool(v) |] -> 
                trace "Model" "DrawFPS: %A" v
                Avalonia.Application.Current.MainWindow.Renderer.DrawFps <- v)

            this.AddHandler(DragDrop.DropEvent, (fun _ (e: DragEventArgs) ->
                if e.Data.Contains(DataFormats.FileNames) then
                    Model.EditFiles <| e.Data.GetFileNames()
                elif e.Data.Contains(DataFormats.Text) then
                    Model.InsertText <| e.Data.GetText()
            ))

            this.AddHandler(DragDrop.DragOverEvent, (fun _ (e:DragEventArgs) ->
                e.DragEffects <- DragDropEffects.Move ||| DragDropEffects.Link ||| DragDropEffects.Copy
            ))
        ]
        AvaloniaXamlLoader.Load this


    override this.OnDataContextChanged _ =
        let ctx = this.DataContext :?> MainWindowViewModel
        let pos = PixelPoint(ctx.WindowX, ctx.WindowY)
        this.Position <- pos
        this.WindowState <- ctx.WindowState


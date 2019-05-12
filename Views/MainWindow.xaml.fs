namespace FVim

open Avalonia.Markup.Xaml
open Avalonia.Controls

type MainWindow() as this =
    inherit Window()

    do
        this.Closing.Add (fun _ -> Model.OnTerminating())
        this.Closed.Add  (fun _ -> Model.OnTerminated())
        //this.Renderer.DrawDirtyRects <- true

        AvaloniaXamlLoader.Load this
        #if DEBUG
        this.Renderer.DrawFps <- true
        Avalonia.DevToolsExtensions.AttachDevTools(this)
        #endif


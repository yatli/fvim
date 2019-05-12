namespace FVim

open log
open Avalonia.Markup.Xaml
open Avalonia.Controls

type MainWindow() as this =
    inherit Window()

    do
        //this.Closing.Add datactx.OnTerminating
        //this.Closed.Add  datactx.OnTerminated
        //this.Renderer.DrawDirtyRects <- true
        this.Renderer.DrawFps <- true

        AvaloniaXamlLoader.Load this
        #if DEBUG
        Avalonia.DevToolsExtensions.AttachDevTools(this)
        #endif


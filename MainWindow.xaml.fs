namespace FVim

open log
open Avalonia.Markup.Xaml
open Avalonia.Controls

type MainWindow(datactx: FVimViewModel) as this =
    inherit Window()

    do
        trace "Mainwindow" "initialize avalonia UI..."
        this.DataContext <- datactx
        this.Closing.Add datactx.OnTerminating
        this.Closed.Add  datactx.OnTerminated
        //this.Renderer.DrawDirtyRects <- true
        //this.Renderer.DrawFps <- true

        AvaloniaXamlLoader.Load this
        Avalonia.DevToolsExtensions.AttachDevTools(this)


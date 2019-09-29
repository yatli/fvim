namespace FVim

open def
open log
open common
open ui

open ReactiveUI
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia
open Avalonia.Data
open Avalonia.ReactiveUI
open System.Runtime.InteropServices

#nowarn "0025"

open System.Runtime.InteropServices
open System.Runtime
open Avalonia.Media

type MainWindow() as this =
    inherit ReactiveWindow<MainWindowViewModel>()

    static let XProp = AvaloniaProperty.Register<MainWindow,int>("PosX")
    static let YProp = AvaloniaProperty.Register<MainWindow,int>("PosY")

    let mutable m_bgcolor: Color = Color()
    let mutable m_bgopacity: float = 1.0
    let mutable m_bgblur = false
    let mutable m_bgacrylic = false

    let configBackground() =
        let comp =
            if m_bgacrylic then ui.AdvancedBlur(m_bgopacity, m_bgcolor)
            elif m_bgblur then ui.GaussianBlur(m_bgopacity, m_bgcolor)
            else ui.SolidBackground m_bgcolor
        trace "mainwindow" "configBackground: %A" comp
        ui.SetWindowBackgroundComposition this comp

    do
        #if DEBUG
        this.Renderer.DrawFps <- true
        Avalonia.DevToolsExtensions.AttachDevTools(this)
        #endif

        DragDrop.SetAllowDrop(this, true)

        let flushop = 
            if RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then
                fun () -> 
                    let editor: Avalonia.VisualTree.IVisual = this.GetEditor()
                    editor.InvalidateVisual()
            else this.InvalidateVisual

        this.Watch [
            this.Closing.Subscribe (fun e -> Model.OnTerminating e)
            this.Closed.Subscribe  (fun _ -> Model.OnTerminated())
            this.Bind(XProp, Binding("X", BindingMode.TwoWay))
            this.Bind(YProp, Binding("Y", BindingMode.TwoWay))

            States.Register.Watch "background.composition" (fun () ->
                match States.background_composition.ToLower() with
                | "blur" ->
                    m_bgblur <- true
                    m_bgacrylic <- false
                | "acrylic" ->
                    m_bgblur <- false
                    m_bgacrylic <- true
                | "none" | _ -> 
                    m_bgblur <- false
                    m_bgacrylic <- false
                configBackground()
                )

            States.Register.Watch "background.opacity" (fun () ->
                m_bgopacity <- States.background_opacity
                configBackground()
                )

            States.Register.Notify "DrawFPS" (fun [| Bool(v) |] -> 
                trace "mainwindow" "DrawFPS: %A" v
                this.Renderer.DrawFps <- v)

            Model.Flush |> Observable.subscribe flushop

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

    member this.GetEditor() =
        this.LogicalChildren.[0] :?> Avalonia.VisualTree.IVisual

    override this.OnDataContextChanged _ =
        let ctx = this.DataContext :?> MainWindowViewModel
        let pos = PixelPoint(int ctx.X, int ctx.Y)
        let mutable firstPoschange = true
        let mutable deltaX = 0
        let mutable deltaY = 0

        trace "mainwindow" "set position: %d, %d" pos.X pos.Y
        this.Position <- pos
        this.WindowState <- ctx.WindowState
        this.Watch [
            this.PositionChanged.Subscribe (fun p ->
                if firstPoschange then
                    firstPoschange <- false
                    deltaX <- p.Point.X - pos.X
                    deltaY <- p.Point.Y - pos.Y
                    trace "mainwindow" "first PositionChanged event: %d, %d (delta=%d, %d)" p.Point.X p.Point.Y deltaX deltaY
                else
                    this.SetValue(XProp, p.Point.X - deltaX)
                    this.SetValue(YProp, p.Point.Y - deltaY)
                )
            ctx.MainGrid.ObservableForProperty(fun x -> x.BackgroundColor) 
            |> Observable.subscribe(fun c -> 
                trace "mainwindow" "update background color: %s" (c.Value.ToString())
                m_bgcolor <- c.Value
                configBackground())
        ]

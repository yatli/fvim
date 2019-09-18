namespace FVim

open neovim.def
open log
open common

open ReactiveUI
open Avalonia.Markup.Xaml
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia
open Avalonia.Data
open Avalonia.ReactiveUI

open System.Runtime.InteropServices
open System.Runtime

type internal AccentState =
    | ACCENT_DISABLED = 0
    | ACCENT_ENABLE_GRADIENT = 1
    | ACCENT_ENABLE_TRANSPARENTGRADIENT = 2
    | ACCENT_ENABLE_BLURBEHIND = 3
    | ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
    | ACCENT_INVALID_STATE = 5

[<Struct>]
[<StructLayout(LayoutKind.Sequential)>]
type internal AccentPolicy =
    {
        AccentState: AccentState
        AccentFlags: uint32
        GradientColor: uint32
        AnimationId: uint32
    }

type internal WindowCompositionAttribute =
    // ...
    | WCA_ACCENT_POLICY = 19
    // ...


[<Struct>]
[<StructLayout(LayoutKind.Sequential)>]
type internal WindowCompositionAttributeData =
    {
        Attribute: WindowCompositionAttribute
        Data: nativeint
        SizeOfData: int32
    }

type MainWindow() as this =
    inherit ReactiveWindow<MainWindowViewModel>()

    static let XProp = AvaloniaProperty.Register<MainWindow,int>("PosX")
    static let YProp = AvaloniaProperty.Register<MainWindow,int>("PosY")

    [<DllImport("user32.dll")>]
    static extern int internal SetWindowCompositionAttribute(nativeint hwnd, WindowCompositionAttributeData& data);

    do
        #if DEBUG
        this.Renderer.DrawFps <- true
        Avalonia.DevToolsExtensions.AttachDevTools(this)
        #endif

        DragDrop.SetAllowDrop(this, true)

        this.Watch [
            this.Closing.Subscribe (fun e -> Model.OnTerminating e)
            this.Closed.Subscribe  (fun _ -> Model.OnTerminated())
            this.Bind(XProp, Binding("X", BindingMode.TwoWay))
            this.Bind(YProp, Binding("Y", BindingMode.TwoWay))

            States.Register.Notify "DrawFPS" (fun [| Bool(v) |] -> 
                trace "mainwindow" "DrawFPS: %A" v
                this.Renderer.DrawFps <- v)

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
        ]

        let _blurOpacity = 1u
        let _blurBackgroundColor = 0x990000u

        let accent = 
            { 
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND
                GradientColor = (_blurOpacity <<< 24) ||| (_blurBackgroundColor &&& 0xFFFFFFu) 
                AccentFlags = 0u
                AnimationId = 0u
            }
        let accentStructSize = Marshal.SizeOf(accent);
        let accentPtr = Marshal.AllocHGlobal(accentStructSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        let mutable data = { Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY; SizeOfData = accentStructSize; Data = accentPtr }
        SetWindowCompositionAttribute(this.PlatformImpl.Handle.Handle, &data) |> ignore

        Marshal.FreeHGlobal(accentPtr);


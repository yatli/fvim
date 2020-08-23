namespace FVim

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Markup.Xaml
open System

type IViewModelContainer =
    abstract Target: obj

type ViewLocator() =
    interface IDataTemplate with
        member this.Build(data: obj): Avalonia.Controls.IControl = 
            match data with
            | :? IViewModelContainer as container -> (this :> IDataTemplate).Build(container.Target)
            | _ ->
            let _name = data.GetType().FullName.Replace("ViewModel", "");
            let _type = Type.GetType(_name);
            if _type <> null 
            then Activator.CreateInstance(_type) :?> IControl;
            else TextBlock( Text = "Not Found: " + _name ) :> IControl
        member this.Match(data: obj): bool = data :? ViewModelBase || data :? IViewModelContainer
        // member this.SupportsRecycling: bool = false

type App() =
    inherit Application()

    override this.Initialize() =
        AvaloniaXamlLoader.Load this

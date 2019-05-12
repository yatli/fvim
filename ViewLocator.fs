namespace FVim

open Avalonia.Controls
open Avalonia.Controls.Templates
open System
open ReactiveUI

type ViewModelBase() =
    inherit ReactiveObject()

type ViewLocator() =
    interface IDataTemplate with
        member this.Build(data: obj): Avalonia.Controls.IControl = 
            let _name = data.GetType().FullName.Replace("ViewModel", "");
            let _type = Type.GetType(_name);
            if _type <> null 
            then Activator.CreateInstance(_type) :?> IControl;
            else TextBlock( Text = "Not Found: " + _name ) :> IControl
        member this.Match(data: obj): bool = data :? ViewModelBase
        member this.SupportsRecycling: bool = false

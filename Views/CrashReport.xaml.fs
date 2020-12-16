namespace FVim

open ui
open log
open common
open Avalonia.Markup.Xaml
open Avalonia.Controls

type CrashReport() as this =
    inherit Window()

    do
        AvaloniaXamlLoader.Load(this)


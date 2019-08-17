module FVim.config

open FSharp.Data
open System.Runtime.InteropServices
open System.IO
open Avalonia
open System
open Avalonia.Controls
open System.Diagnostics

[<Literal>]
let sample_config = """
[
{
    "workspace": [
        {
            "path": "C:\foo\bar",
            "mainwin": {
                "x": 1030,
                "y": 200,
                "w": 800.00,
                "h": 600.00,
                "state": "Normal"
            }
        }
    ],
    "Logging": {
        "EchoOnConsole": false,
        "LogToFile": ""
    }
},
{}
]
"""

type ConfigObject = JsonProvider<sample_config, SampleIsList=true>

let homedir = if RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
              then Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
              else Environment.GetEnvironmentVariable("HOME")

let configdir  = if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                 then "AppData/Local/fvim"
                 else ".config/fvim"
                 |> fun cfgdir ->
                 Path.Combine(homedir, cfgdir)

let configfile = Path.Combine(configdir, "config.json")

try ignore <| Directory.CreateDirectory(configdir)
with _ -> ()

let load() = 
    try 
        let cfg = ConfigObject.Load configfile
        cfg
    with _ -> ConfigObject.Parse("{}")

let save (cfg: ConfigObject.Root) (x: int) (y: int) (w: float) (h: float) (state: WindowState) = 
    let dict = cfg.Workspace |> Array.map (fun ws -> (ws.Path, ws)) |> Map.ofArray
    let cwd  = Environment.CurrentDirectory |> Path.GetFullPath
    let ws   = ConfigObject.Workspace(cwd, ConfigObject.Mainwin(x, y, int w, int h, state.ToString()))
    let dict = dict.Add(cwd, ws)
    let cfg  = ConfigObject.Root(dict |> Map.toArray |> Array.map snd, cfg.Logging)
    try File.WriteAllText(configfile, cfg.ToString())
    with _ -> ()

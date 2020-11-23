module FVim.config

open FSharp.Data
open System.Runtime.InteropServices
open System.IO
open System

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
                "w": 800,
                "h": 600,
                "state": "Normal",
                "BackgroundComposition": "acrylic",
                "CustomTitleBar": false
            }
        },
        {
            "path": "C:\foo\bar",
            "mainwin": {
                "x": 1030,
                "y": 200,
                "w": 800,
                "h": 600,
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

let configroot = if RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                 then Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                 else Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
let configdir  = Path.Combine(configroot, "fvim")
let configfile = Path.Combine(configdir, "config.json")

try ignore <| Directory.CreateDirectory(configdir)
with _ -> ()

let load() = 
    try 
        let cfg = ConfigObject.Load configfile
        cfg
    with _ -> ConfigObject.Parse("{}")

let save (cfg: ConfigObject.Root) (x: int) (y: int) (w: int) (h: int) (state: string) (composition: string) (customTitleBar: bool) = 
    let dict = cfg.Workspace |> Array.map (fun ws -> (ws.Path, ws)) |> Map.ofArray
    let cwd  = Environment.CurrentDirectory |> Path.GetFullPath
    let ws   = ConfigObject.Workspace(cwd, ConfigObject.Mainwin(x, y, w, h, state, Some composition, Some customTitleBar))
    let dict = dict.Add(cwd, ws)
    let cfg  = ConfigObject.Root(dict |> Map.toArray |> Array.map snd, cfg.Logging)
    try File.WriteAllText(configfile, cfg.ToString())
    with _ -> ()

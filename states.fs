module FVim.states

open common
open def
open SkiaSharp
open Avalonia.Media
open Avalonia.Layout

// channel
let mutable channel_id = 1

// keyboard mapping
let mutable key_disableShiftSpace = false
let mutable key_autoIme = false
let mutable key_altGr = false

// clipboard
let mutable clipboard_lines: string[] = [||]
let mutable clipboard_regtype: string = ""

// cursor
let mutable cursor_smoothmove  = false
let mutable cursor_smoothblink = false

// font rendering
let mutable font_antialias     = true
let mutable font_drawBounds    = false
let mutable font_autohint      = false
let mutable font_subpixel      = true
let mutable font_autosnap      = true
let mutable font_ligature      = true
let mutable font_hintLevel     = SKPaintHinting.NoHinting
let mutable font_weight_normal = FontWeight.Normal
let mutable font_weight_bold   = FontWeight.Bold
let mutable font_lineheight    = LineHeightOption.Default
let mutable font_nonerd        = false

// ui
//  - option literals
let [<Literal>] uiopt_rgb            = "rgb"
let [<Literal>] uiopt_ext_linegrid   = "ext_linegrid"
let [<Literal>] uiopt_ext_multigrid  = "ext_multigrid"
let [<Literal>] uiopt_ext_popupmenu  = "ext_popupmenu"
let [<Literal>] uiopt_ext_tabline    = "ext_tabline"
let [<Literal>] uiopt_ext_cmdline    = "ext_cmdline"
let [<Literal>] uiopt_ext_wildmenu   = "ext_wildmenu"
let [<Literal>] uiopt_ext_messages   = "ext_messages"
let [<Literal>] uiopt_ext_hlstate    = "ext_hlstate"
let [<Literal>] uiopt_ext_termcolors = "ext_termcolors"
let [<Literal>] uiopt_ext_windows    = "ext_windows"
//  - options supported by neovim, collected at startup
let mutable ui_available_opts        = Set.empty<string>
//  - options supported by fvim
let mutable ui_multigrid             = true
let mutable ui_popupmenu             = true
let mutable ui_tabline               = false
let mutable ui_cmdline               = false
let mutable ui_wildmenu              = false
let mutable ui_messages              = false
let mutable ui_termcolors            = false
let mutable ui_hlstate               = false
//let mutable ui_windows               = false

// background
let mutable background_composition   = NoComposition
let mutable background_opacity       = 1.0
let mutable background_altopacity    = 1.0
let mutable background_image_file    = ""
let mutable background_image_opacity = 1.0
let mutable background_image_stretch = Stretch.None
let mutable background_image_halign  = HorizontalAlignment.Left
let mutable background_image_valign  = VerticalAlignment.Top

// defaults
let mutable default_width            = 800
let mutable default_height           = 600

///  !Note does not include rgb and ext_linegrid
let PopulateUIOptions (opts: hashmap<_,_>) =
    let c k v = 
        if ui_available_opts.Contains k then
            opts.[k] <- v
    c uiopt_ext_popupmenu ui_popupmenu
    c uiopt_ext_multigrid ui_multigrid
    c uiopt_ext_tabline ui_tabline
    c uiopt_ext_cmdline ui_cmdline
    c uiopt_ext_wildmenu ui_wildmenu
    c uiopt_ext_messages ui_messages
    c uiopt_ext_hlstate ui_hlstate
    c uiopt_ext_termcolors ui_termcolors
    //c uiopt_ext_windows ui_windows

type Foo = A

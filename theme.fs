module FVim.theme
open ui
open def
open log

open Avalonia
open Avalonia.Media

#nowarn "0025"

let private trace fmt = trace "theme" fmt

// global theme configuration about colors, fonts, cursor, etc.
let mutable hi_defs        = Array.create<HighlightAttr> 256 HighlightAttr.Default
let mutable mode_defs      = Array.empty<ModeInfo>
let mutable semhl          = Map.empty
let mutable guifont        = DefaultFont
let mutable guifontwide    = DefaultFontWide
let mutable fontsize       = 16.0
let mutable cursor_enabled = true
let mutable default_fg     = Colors.White
let mutable default_bg     = Colors.Black
let mutable default_sp     = Colors.Red

let hlchange_ev    = Event<int>()
let fontconfig_ev  = Event<unit>()
let cursoren_ev    = Event<bool>()
let themeconfig_ev = Event<Color*Color*Color*Color*Color*Color*Color>()

let fontConfig() =
    fontsize <- max fontsize 1.0
    trace "fontConfig: guifont=%s guifontwide=%s" guifont guifontwide
    fontconfig_ev.Trigger()

let _ = States.Register.Watch "font" fontConfig

let setHighlight x =
    if hi_defs.Length < x.id + 1 then
        System.Array.Resize(&hi_defs, x.id + 100)
    hi_defs.[x.id] <- x
    if x.id = 0 then
        default_fg <- x.rgb_attr.foreground.Value
        default_bg <- x.rgb_attr.background.Value
        default_sp <- x.rgb_attr.special.Value
    hlchange_ev.Trigger(x.id)

let setModeInfo (cs_en: bool) (info: ModeInfo[]) =
    mode_defs <- info
    cursoren_ev.Trigger cs_en

let GetDrawAttrs hlid = 
    let attrs = hi_defs.[hlid].rgb_attr

    let mutable fg = Option.defaultValue default_fg attrs.foreground
    let mutable bg = Option.defaultValue default_bg attrs.background
    let mutable sp = Option.defaultValue default_sp attrs.special

    if attrs.reverse then
        let tmp = fg
        fg <- bg
        bg <- tmp

    if (States.background_composition = "acrylic" || States.background_composition = "blur") then
        let alpha = 
            if bg = default_bg then 0uy
            else byte(States.background_altopacity * 255.0)
        bg <- Avalonia.Media.Color(alpha, bg.R, bg.G, bg.B)
    fg, bg, sp, attrs


do
    let fg,bg,sp,_ = GetDrawAttrs 0
    default_bg <- bg
    default_fg <- fg
    default_sp <- sp


let setSemanticHighlightGroups grp =
    semhl <- grp
    // update popupmenu color
    let [ nfg, nbg, _, _
          sfg, sbg, _, _
          scfg, scbg, _, _
          _, bbg, _, _ ] = 
        [
            SemanticHighlightGroup.Pmenu
            SemanticHighlightGroup.PmenuSel
            SemanticHighlightGroup.PmenuSbar
            SemanticHighlightGroup.VertSplit
        ] 
        |> List.map (semhl.TryFind >> Option.defaultValue 1 >> GetDrawAttrs)

    themeconfig_ev.Trigger(nfg, nbg, sfg, sbg, scfg, scbg, bbg)

let setDefaultColors fg bg sp = 

    let bg = 
        if fg = bg && bg = sp then GetReverseColor bg
        else bg

    setHighlight {
        id = 0
        info = [||]
        cterm_attr = RgbAttr.Empty
        rgb_attr = { 
            foreground = Some fg
            background = Some bg
            special = Some sp
            reverse = false
            italic = false
            bold = false
            underline = false
            undercurl = false
        }
    }
    trace "setDefaultColors: %A %A %A" fg bg sp


let setOption (opt: UiOption) = 
    trace "setOption: %A" opt

    let (|FN|_|) (x: string) =
        // try to parse with 'font\ name:hNN'
        match x.Split(':') with
        | [|name; size|] when size.Length > 0 && size.[0] = 'h' -> Some(name.Trim('\'', '"'), size.Substring(1).TrimEnd('\'','"') |> float)
        | _ -> None

    let mutable config_font = true

    match opt with
    | Guifont(FN(name, sz))             -> guifont     <- name; fontsize <- sz
    | GuifontWide(FN(name, sz))         -> guifontwide <- name; fontsize <- sz
    | Guifont("+") | GuifontWide("+")   -> fontsize    <- fontsize + 1.0
    | Guifont("-") | GuifontWide("-")   -> fontsize    <- fontsize - 1.0
    | Guifont(".+") | GuifontWide(".+") -> fontsize    <- fontsize + 0.1
    | Guifont(".-") | GuifontWide(".-") -> fontsize    <- fontsize - 0.1
    | _                                 -> config_font <- false

    if config_font then fontConfig()

let hiattrDefine (hls: HighlightAttr[]) =
    Array.iter setHighlight hls


module FVim.neovim.def

open FVim.log

open Avalonia.Media
open System.Collections.Generic

let private trace fmt = trace "neovim.def" fmt

type Request = 
    {
        method:     string
        parameters: obj[]
    }

type Response = 
    {
        result: Choice<obj, obj>
    }

type CursorShape =
| Block
| Horizontal
| Vertical

type ModeInfo =
    {
        cursor_shape: CursorShape option
        cell_percentage: int option
        blinkwait: int option
        blinkon: int option
        blinkoff: int option
        attr_id: int option
        attr_id_lm: int option
        short_name: string
        name: string
    }

type AmbiWidth = Single | Double
type ShowTabline = Never | AtLeastTwo | Always

type UiOption =
    ///  When on and 'termbidi' is off, the required visual character
    ///  corrections that need to take place for displaying the Arabic language
    ///  take effect.  Shaping, in essence, gets enabled; the term is a broad
    ///  one which encompasses:
    ///    a) the changing/morphing of characters based on their location
    ///       within a word (initial, medial, final and stand-alone).
    ///    b) the enabling of the ability to compose characters
    ///    c) the enabling of the required combining of some characters
    ///  When disabled the display shows each character's true stand-alone
    ///  form.
    | ArabicShape of bool
    ///  Tells Vim what to do with characters with East Asian Width Class
    ///  Ambiguous (such as Euro, Registered Sign, Copyright Sign, Greek
    ///  letters, Cyrillic letters).
    | AmbiWidth of AmbiWidth
    ///  When on all Unicode emoji characters are considered to be full width.
    | Emoji of bool
    ///  This is a list of fonts which will be used for the GUI version of Vim.
    ///  In its simplest form the value is just one font name.  
    | Guifont of string
    ///  When not empty, specifies two (or more) fonts to be used.  The first
    ///  one for normal English, the second one for your special language.  
    | GuifontSet of string list
    ///  When not empty, specifies a comma-separated list of fonts to be used
    ///  for double-width characters.  The first font that can be loaded is
    ///  used.
    | GuifontWide of string
    ///  Number of pixel lines inserted between characters.  Useful if the font
    ///  uses the full character cell height, making lines touch each other.
    ///  When non-zero there is room for underlining.
    | LineSpace of int
    ///  This is both for the GUI and non-GUI implementation of the tab pages
    ///  line.
    | ShowTabline of ShowTabline
    ///  When on, uses |highlight-guifg| and |highlight-guibg| attributes in
    ///  the terminal (thus using 24-bit color). Requires a ISO-8613-3
    ///  compatible terminal.
    | TermGuiColors of bool
    // TODO ui-ext-options
    | UnknownOption of obj

[<Struct>]
type RgbAttr =
    {
        foreground : Color option
        background : Color option
        special : Color option
        reverse : bool 
        italic : bool
        bold : bool
        underline : bool
        undercurl : bool
    }
    with 
    static member Empty =
        {
            foreground = None
            background = None
            special = None
            reverse = false
            italic = false
            bold = false
            underline = false
            undercurl = false
        }

type GridCell = 
    {
        text: string
        hl_id: int option
        repeat: int option
    }

type HighlightAttr = 
    {
        id: int 
        rgb_attr: RgbAttr 
        cterm_attr: RgbAttr 
        info: obj[]
    }
    with
    static member Default =
        {
            id = -1
            rgb_attr = RgbAttr.Empty
            cterm_attr = RgbAttr.Empty
            info = [||]
        }

type GridLine =
    {
        grid: int 
        row: int 
        col_start: int 
        cells: GridCell[]
    }

type RedrawCommand =
// global
| SetOption of UiOption[]
| SetTitle of string
| SetIcon of string
| ModeInfoSet of cursor_style_enabled: bool * mode_info: ModeInfo[] 
| ModeChange of name: string * index: int
| Mouse of on: bool
| Busy of on: bool
| Bell
| VisualBell
//| Suspend // ??
//| UpdateMenu // ??
| Flush
// grid events
// note, cterm_* are transmitted as term 256-color codes
| DefaultColorsSet of fg: Color * bg: Color * sp: Color * cterm_fg: Color * cterm_bg: Color
| HighlightAttrDefine of HighlightAttr[]
| GridLine of GridLine[]
| GridClear of grid: int
| GridDestroy of grid: int
| GridCursorGoto of grid: int * row: int * col: int
| GridScroll of grid:int * top:int * bot:int * left:int * right:int * rows:int * cols: int
| GridResize of grid: int * width: int * height: int
// multigrid events
| WinPos of grid: int * win: int * start_row: int * start_col: int * width: int * height:int
// legacy events
//| UpdateFg of Color
//| UpdateBg of Color
//| UpdateSp of Color
| UnknownCommand of data: obj

type Event =
| Request      of int32 * Request * (int32 -> Response -> unit Async)
| Response     of int32 * Response
| Notification of Request
| Redraw       of RedrawCommand[]
| Error        of string
| Crash        of code: int32
| Exit

let uiopt_rgb            = "rgb"
let uiopt_ext_linegrid   = "ext_linegrid"
let uiopt_ext_multigrid  = "ext_multigrid"
let uiopt_ext_popupmenu  = "ext_popupmenu"
let uiopt_ext_tabline    = "ext_tabline"
let uiopt_ext_cmdline    = "ext_cmdline"
let uiopt_ext_wildmenu   = "ext_wildmenu"
let uiopt_ext_messages   = "ext_messages"
let uiopt_ext_hlstate    = "ext_hlstate"
let uiopt_ext_termcolors = "ext_termcolors"

type EventParseException(data: obj) =
    inherit exn()
    member __.Input = data
    override __.Message = sprintf "Could not parse the neovim message: %A" data

let mkparams1 (t1: 'T1)                                = [| box t1 |]
let mkparams2 (t1: 'T1) (t2: 'T2)                      = [| box t1; box t2 |]
let mkparams3 (t1: 'T1) (t2: 'T2) (t3: 'T3)            = [| box t1; box t2; box t3 |]
let mkparams4 (t1: 'T1) (t2: 'T2) (t3: 'T3) (t4: 'T4)  = [| box t1; box t2; box t3; box t4|]

let (|ObjArray|_|) (x:obj) =
    match x with
    | :? (obj[]) as x    -> Some x
    | :? (obj list) as x -> Some(Array.ofList x)
    | :? (obj seq) as x  -> Some(Array.ofSeq x)
    | _                  -> None

let (|Bool|_|) (x:obj) =
    match x with
    | :? bool as x       -> Some x
    | _ -> None

let (|String|_|) (x:obj) =
    match x with
    | :? string as x     -> Some x
    | _ -> None

let (|Integer32|_|) (x:obj) =
    match x with
    | :? int32  as x     -> Some(int32 x)
    | :? int16  as x     -> Some(int32 x)
    | :? int8   as x     -> Some(int32 x)
    | :? uint16 as x     -> Some(int32 x)
    | :? uint32 as x     -> Some(int32 x)
    | :? uint8  as x     -> Some(int32 x)
    | _ -> None

let (|C|_|) (x:obj) =
    match x with
    | ObjArray x -> 
        match x.[0] with
        | (String cmd) -> Some(cmd, x |> Array.skip 1)
        | _ -> None
    | _ -> None

let (|C1|_|) (x:obj) =
    match x with
    | ObjArray [| (String cmd); ObjArray ps |] -> Some(cmd, ps)
    | _ -> None

let (|P|_|) (parser: obj -> 'a option) (xs:obj) =
    match xs with
    | :? (obj seq) as xs ->
        let result = Seq.choose parser xs |> Array.ofSeq
        Some result
    | _ -> None

let (|KV|_|) (k: string) (x: obj) =
    match x with
    | ObjArray [| (String key); x |] when key = k -> Some x
    | _ -> None

let (|AmbiWidth|_|) (x: obj) =
    match x with
    | String "single" -> Some AmbiWidth.Single
    | String "double" -> Some AmbiWidth.Double
    | _ -> None

let (|ShowTabline|_|) (x: obj) =
    match x with
    | Integer32 0 -> Some ShowTabline.Never
    | Integer32 1 -> Some ShowTabline.AtLeastTwo
    | Integer32 2 -> Some ShowTabline.Always
    | _ -> None

let (|Color|_|) (x: obj) =
    match x with
    | Integer32 x -> 
        // fill in the alpha channel
        Some <| Color.FromUInt32((uint32 x) ||| 0xFF000000u)
    | _ -> None

let (|CursorShape|_|) (x: obj) =
    match x with
    | String "block"      -> Some Block
    | String "horizontal" -> Some Horizontal
    | String "vertical"   -> Some Vertical
    | _                   -> None

let private _cs = (|CursorShape|_|)
let private _i  = (|Integer32|_|)
let private _c  = (|Color|_|)
let private _s  = (|String|_|)
let private _b  = (|Bool|_|)

let _get (map: Dictionary<obj, obj>) (key: string) (fn: obj -> 'a option) =
    let (|OK_FN|_|) = fn
    match map.TryGetValue key with
    | true, OK_FN x -> Some x
    | _ -> None
let _getd (map: Dictionary<obj, obj>) (key: string) (fn: obj -> 'a option) d =
    let (|OK_FN|_|) = fn
    match map.TryGetValue key with
    | true, OK_FN x -> x
    | _ -> d

let (|HighlightAttr|_|) (x: obj) =
    match x with
    | :? Dictionary<obj, obj> as map ->
        let inline _get a b = _get map a b
        let inline _getd a b = _getd map a b
        Some {
            foreground = _get "foreground" _c
            background = _get "background" _c
            special    = _get "special" _c
            reverse    = _getd "reverse" _b false
            italic     = _getd "italic" _b false
            bold       = _getd "bold" _b false
            underline  = _getd "underline" _b false
            undercurl  = _getd "undercurl" _b false
        }
    | _ -> None

let parse_uioption (x: obj) =
    match x with
    | KV "arabicshape"   (Bool x)        -> Some <| ArabicShape x
    | KV "ambiwidth"     (AmbiWidth x)   -> Some <| AmbiWidth x
    | KV "emoji"         (Bool x)        -> Some <| Emoji x
    | KV "guifont"       (String x)      -> Some <| Guifont x
    //| KV "guifontset"   (String x)     -> Some <| Guifont x
    | KV "guifontwide"   (String x)      -> Some <| GuifontWide x
    | KV "linespace"     (Integer32 x)   -> Some <| LineSpace x
    //| KV "pumblend"    (Integer32 x)   -> Some <| Pumblend x
    | KV "showtabline"   (ShowTabline x) -> Some <| ShowTabline x
    | KV "termguicolors" (Bool x)        -> Some <| TermGuiColors x
    | _                                  -> Some <| UnknownOption x

let parse_default_colors (x: obj) =
    match x with
    | ObjArray [| (Color fg); (Color bg); (Color sp); (Color cfg); (Color cbg) |] -> 
        Some <| DefaultColorsSet(fg,bg,sp,cfg,cbg)
    | _ -> None

let parse_mode_info (x: obj) =
    match x with
    | :? Dictionary<obj, obj> as map ->
        let inline _get a b = _get map a b
        Some {
                cursor_shape    =  _get  "cursor_shape"    _cs
                short_name      = (_get  "short_name" _s).Value
                name            = (_get  "name" _s).Value
                cell_percentage =  _get  "cell_percentage" _i
                blinkwait       =  _get  "blinkwait"       _i
                blinkon         =  _get  "blinkon"         _i
                blinkoff        =  _get  "blinkoff"        _i
                attr_id         =  _get  "attr_id"         _i
                attr_id_lm      =  _get  "attr_id_lm"      _i
             }
    | _ -> None

let parse_hi_attr (x: obj) =
    match x with
    | ObjArray [| (Integer32 id); (HighlightAttr rgb); (HighlightAttr cterm); (ObjArray info) |] 
        -> Some {id = id; rgb_attr = rgb; cterm_attr = cterm; info = info }
    | _ -> None

let parse_grid_cell (x: obj) =
    match x with
    | ObjArray [| (String txt) |] 
        -> Some { text = txt; hl_id = None; repeat = None}
    | ObjArray [| (String txt); (Integer32 hl_id) |] 
        -> Some { text = txt; hl_id = Some hl_id; repeat = None}
    | ObjArray [| (String txt); (Integer32 hl_id); (Integer32 repeat) |] 
        -> Some { text = txt; hl_id = Some hl_id; repeat = Some repeat}
    | _ -> None

let parse_grid_line (x: obj) =
    match x with
    | ObjArray [| (Integer32 grid); (Integer32 row) ; (Integer32 col_start) ; P(parse_grid_cell)cells |] 
        -> Some {grid = grid; row=row; col_start=col_start; cells=cells}
    | _ -> None

let parse_redrawcmd (x: obj) =
    match x with
    | C("option_set", P(parse_uioption)options)                                            -> SetOption options
    | C("default_colors_set", P(parse_default_colors)dcolors )                             -> dcolors |> Array.last
    | C1("set_title", [|String title|])                                                    -> SetTitle title
    | C("set_icon", [|String icon|])                                                       -> SetIcon icon
    | C1("mode_info_set", [| (Bool csen); P(parse_mode_info)info |])                       -> ModeInfoSet(csen, info)
    | C1("mode_change", [| (String m); (Integer32 i) |])                                   -> ModeChange(m, i)
    | C("mouse_on", _)                                                                     -> Mouse(true)
    | C("mouse_off", _)                                                                    -> Mouse(false)
    | C("busy_start", _)                                                                   -> Busy(true)
    | C("busy_stop", _)                                                                    -> Busy(false)
    | C("bell", _)                                                                         -> Bell
    | C("visual_bell", _)                                                                  -> VisualBell
    | C("flush", _)                                                                        -> Flush
    | C("hl_attr_define", P(parse_hi_attr) attrs)                                          -> HighlightAttrDefine attrs
    | C1("grid_clear", [| (Integer32 id) |])                                               -> GridClear id
    | C1("grid_resize", [| (Integer32 id); (Integer32 w); (Integer32 h) |])                -> GridResize(id, w, h)
    | C1("grid_destroy", [| (Integer32 id) |])                                             -> GridDestroy id
    | C1("grid_cursor_goto", [| (Integer32 grid); (Integer32 row); (Integer32 col) |])     -> GridCursorGoto(grid, row, col)
    | C1("grid_scroll", 
         [| (Integer32 grid)
            (Integer32 top); (Integer32 bot) 
            (Integer32 left); (Integer32 right) 
            (Integer32 rows); (Integer32 cols) 
         |])                                                                               -> GridScroll(grid, top, bot, left, right, rows, cols)
    | C("grid_line", P(parse_grid_line)lines)                                              -> GridLine lines
    | C1("win_pos", [| (Integer32 grid);      (Integer32 win); 
                       (Integer32 start_row); (Integer32 start_col); 
                       (Integer32 width);     (Integer32 height) |])                       -> WinPos(grid,win,start_row,start_col,width,height)
    | _                                                                                    -> UnknownCommand x
    //| C("suspend", _)                                                                    -> 
    //| C("update_menu", _)                                                                -> 


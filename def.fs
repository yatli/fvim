module FVim.def

open FVim.log
open FVim.common

open Avalonia.Media
open System.Collections.Generic

let inline private trace fmt = trace "def" fmt

[<Struct>]
type CursorShape =
| Block
| Horizontal
| Vertical

[<Struct>]
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

[<Struct>]
type AmbiWidth = Single | Double
[<Struct>]
type ShowTabline = Never | AtLeastTwo | Always

[<Struct>]
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
    | ArabicShape of arabicShape: bool
    ///  Tells Vim what to do with characters with East Asian Width Class
    ///  Ambiguous (such as Euro, Registered Sign, Copyright Sign, Greek
    ///  letters, Cyrillic letters).
    | AmbiWidth of ambiWidth: AmbiWidth
    ///  When on all Unicode emoji characters are considered to be full width.
    | Emoji of emoji: bool
    ///  This is a list of fonts which will be used for the GUI version of Vim.
    ///  In its simplest form the value is just one font name.  
    | Guifont of guifont: string
    ///  When not empty, specifies two (or more) fonts to be used.  The first
    ///  one for normal English, the second one for your special language.  
    | GuifontSet of guifontSet: string list
    ///  When not empty, specifies a comma-separated list of fonts to be used
    ///  for double-width characters.  The first font that can be loaded is
    ///  used.
    | GuifontWide of guifontWide: string
    ///  Number of pixel lines inserted between characters.  Useful if the font
    ///  uses the full character cell height, making lines touch each other.
    ///  When non-zero there is room for underlining.
    | LineSpace of lineSpace: int
    ///  This is both for the GUI and non-GUI implementation of the tab pages
    ///  line.
    | ShowTabline of showTabline: ShowTabline
    ///  When on, uses |highlight-guifg| and |highlight-guibg| attributes in
    ///  the terminal (thus using 24-bit color). Requires a ISO-8613-3
    ///  compatible terminal.
    | TermGuiColors of termguicolors: bool
    // TODO ui-ext-options
    | UnknownOption of unknownOption: obj

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

[<Struct>]
type GridCell = 
    {
        text: string
        hl_id: int option
        repeat: int option
    }

[<Struct>]
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

[<Struct>]
type GridLine =
    {
        grid: int 
        row: int 
        col_start: int 
        cells: GridCell[]
    }

[<Struct>]
type Anchor =
| NorthWest
| NorthEast
| SouthWest
| SouthEast

///  <summary>
///  The "kind" item uses a single letter to indicate the kind of completion.  This
///  may be used to show the completion differently (different color or icon).
///  Currently these types can be used:
///      v    variable
///      f    function or method
///      m    member of a struct or class
///      t    typedef
///      d    #define or macro
///  </summary>
[<Struct>]
type VimCompleteKind =
| Variable
| Function
| Member
| Typedef
| Macro

[<Struct>]
type CompleteItem =
    {
        word: string
        abbr: string option
        menu: string option
        info: string option
    }
with 
    static member empty = { word = ""; abbr = None; menu = None; info = None }
    static member GetLength (x: CompleteItem) =
        let _len (x: string option) = (_d "" x).Length
        x.word.Length + _len x.abbr + _len x.menu + _len x.info

type SemanticHighlightGroup =
    | SpecialKey   = 0
    | EndOfBuffer  = 1
    | TermCursor   = 2
    | TermCursorNC = 3
    | NonText      = 4
    | Directory    = 5
    | ErrorMsg     = 6
    | IncSearch    = 7
    | Search       = 8
    | MoreMsg      = 9
    | ModeMsg      = 10
    | LineNr       = 11
    | CursorLineNr = 12
    | Question     = 13
    | StatusLine   = 14
    | StatusLineNC = 15
    | VertSplit    = 16
    | Title        = 17
    | Visual       = 18
    | VisualNC     = 19
    | WarningMsg   = 20
    | WildMenu     = 21
    | Folded       = 22
    | FoldColumn   = 23
    | DiffAdd      = 24
    | DiffChange   = 25
    | DiffDelete   = 26
    | DiffText     = 27
    | SignColumn   = 28
    | Conceal      = 29
    | SpellBad     = 30
    | SpellCap     = 31
    | SpellRare    = 32
    | SpellLocal   = 33
    | Pmenu        = 34
    | PmenuSel     = 35
    | PmenuSbar    = 36
    | PmenuThumb   = 37
    | TabLine      = 38
    | TabLineSel   = 39
    | TabLineFill  = 40
    | CursorColumn = 41
    | CursorLine   = 42
    | ColorColumn  = 43
    | QuickFixLine = 44
    | Whitespace   = 45
    | NormalNC     = 46
    | MsgSeparator = 47
    | NormalFloat  = 48

type RedrawCommand =
///  -- global --
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
///  -- grid events --
///  note, cterm_* are transmitted as term 256-color codes
| DefaultColorsSet of fg: Color * bg: Color * sp: Color * cterm_fg: Color * cterm_bg: Color
| HighlightAttrDefine of HighlightAttr[]
| GridLine of GridLine[]
| GridClear of grid: int
| GridDestroy of grid: int
| GridCursorGoto of grid: int * row: int * col: int
| GridScroll of grid:int * top:int * bot:int * left:int * right:int * rows:int * cols: int
| GridResize of grid: int * width: int * height: int
/// -- multigrid events --
///  Set the position and size of the grid in Nvim (i.e. the outer grid
///  size). If the window was previously hidden, it should now be shown
///  again.
| WinPos of grid: int * win: int * start_row: int * start_col: int * width: int * height:int
///  Display or reconfigure floating window `win`. The window should be
///  displayed above another grid `anchor_grid` at the specified position
///  `anchor_row` and `anchor_col`. For the meaning of `anchor` and more
///  details of positioning, see |nvim_open_win()|.
| WinFloatPos of grid: int * win: int * anchor: Anchor * anchor_grid: int * anchor_row: int * anchor_col: int * focusable: bool
///  Display or reconfigure external window `win`. The window should be
///  displayed as a separate top-level window in the desktop environment,
///  or something similar.
| WinExternalPos of grid: int * win: int
///  Stop displaying the window. The window can be shown again later.
| WinHide of grid: int
///  Hint that following `grid_scroll` on the default grid should
///  scroll over windows. This is a temporary workaround to allow
///  UIs to use the builtin message drawing. Later on, messages will be
///  drawn on a dedicated grid. Using |ui-messages| also avoids this issue.
| WinScrollOverStart 
///  Hint that scrolled over windows should be redrawn again, and not be
///  overdrawn by default grid scrolling anymore.
| WinScrollOverReset
///  Close the window
| WinClose of grid: int
///  Display messages on `grid`.  The grid will be displayed at `row` on the
///  default grid (grid=1), covering the full column width. `scrolled`
///  indicates whether the message area has been scrolled to cover other
///  grids. It can be useful to draw a separator then ('display' msgsep
///  flag). The Builtin TUI draws a full line filled with `sep_char` and
///  |hl-MsgSeparator| highlight.
///  
///  When |ext_messages| is active, no message grid is used, and this event
///  will not be sent.
| MsgSetPos of grid: int * row: int *  scrolled: bool * sep_char: string

///  Set message position
///  -- popupmenu events --
///  Show |popupmenu-completion|. `items` is an array of completion items
///  to show; each item is an array of the form [word, kind, menu, info] as
///  defined at |complete-items|, except that `word` is replaced by `abbr`
///  if present.  `selected` is the initially-selected item, a zero-based
///  index into the array of items (-1 if no item is selected). `row` and
///  `col` give the anchor position, where the first character of the
///  completed word will be. When |ui-multigrid| is used, `grid` is the
///  grid for the anchor position. When `ext_cmdline` is active, `grid` is
///  set to -1 to indicate the popupmenu should be anchored to the external
///  cmdline. Then `col` will be a byte position in the cmdline text.
| PopupMenuShow of items: CompleteItem[] * selected: int * row: int * col: int * grid : int
///  Select an item in the current popupmenu. `selected` is a zero-based
///  index into the array of items from the last popupmenu_show event, or
///  -1 if no item is selected.
| PopupMenuSelect of selected: int
///  Hide the popupmenu.
| PopupMenuHide
| SemanticHighlightGroupSet of groups: Map<SemanticHighlightGroup, int>
///  -- legacy events --
//| UpdateFg of Color
//| UpdateBg of Color
//| UpdateSp of Color
| UnknownCommand of data: obj

type EventParseException(data: obj) =
    inherit exn()
    member __.Input = data
    override __.Message = sprintf "Could not parse the neovim message: %A" data

///  Matches ObjArray against the [|string; p1; p2; ... |] form
let (|C|_|) (x:obj) =
    match x with
    | ObjArray x -> 
        match x.[0] with
        | (String cmd) -> Some(cmd, x |> Array.skip 1)
        | _ -> None
    | _ -> None

///  Matches ObjArray against the [|string; [|p1; p2; ...|] |] form.
let (|C1|_|) (x:obj) =
    match x with
    | ObjArray [| (String cmd); ObjArray ps |] -> Some(cmd, ps)
    | _ -> None

///  Chooses from ObjArray with a parser
let (|P|_|) (parser: obj -> 'a option) (xs:obj) =
    match xs with
    | :? (obj seq) as xs ->
        let result = Seq.choose parser xs |> Array.ofSeq
        Some result
    | _ -> None

///  Matches ObjArray against the form [|key; value|]
let (|KV|_|) (k: string) (x: obj) =
    match x with
    | ObjArray [| (String key); x |] when key = k -> Some x
    | _ -> None

///  Finds [|key; value|] in (ObjArray | hashmap<obj, obj>)
let FindKV (k: string) (x: obj) =
    match x with
    | ObjArray arr ->
        Array.tryPick (function | (KV(k)x) -> Some x | _ -> None) arr
    | :? hashmap<obj, obj> as dict ->
        match dict.TryGetValue k with
        | true, x -> Some x
        | _ -> None
    | _ -> None

///  Finds [|key; value|] in (ObjArray | hashmap<obj, obj>)
let (|FindKV|_|) (k: string) (x: obj) =
    match x with
    | ObjArray arr ->
        Array.tryPick (function | (KV(k)x) -> Some x | _ -> None) arr
    | :? hashmap<obj, obj> as dict ->
        match dict.TryGetValue k with
        | true, x -> Some x
        | _ -> None
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

let _get (map: hashmap<obj, obj>) (key: string) (fn: obj -> 'a option) =
    let (|OK_FN|_|) = fn
    match map.TryGetValue key with
    | true, OK_FN x -> Some x
    | _ -> None
let _getd (map: hashmap<obj, obj>) (key: string) (fn: obj -> 'a option) d =
    let (|OK_FN|_|) = fn
    match map.TryGetValue key with
    | true, OK_FN x -> x
    | _ -> d

let (|HighlightAttr|_|) (x: obj) =
    match x with
    | :? hashmap<obj, obj> as map ->
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
    | :? hashmap<obj, obj> as map ->
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

let (|Anchor|_|) =
    function
    | String "NE" -> Some NorthEast
    | String "NW" -> Some NorthWest
    | String "SE" -> Some SouthEast
    | String "SW" -> Some SouthWest
    | _ -> None

let parse_complete_item =
    function
    | ObjArray [| (String word); (String abbr); (String menu); (String info) |] -> 
        Some {
            word = word
            abbr = Some abbr
            menu = Some menu
            info = Some info
        }
    | x -> 
        trace "parse_complete_item: unrecognized: %A" x
        None

let parse_semantic_hlgroup =
    function
    | ObjArray [| (String key); (Integer32 id) |] ->
        match SemanticHighlightGroup.TryParse key with
        | true, key -> Some(key, id)
        | _ -> None
    | _ -> None

let parse_redrawcmd (x: obj) =
    match x with
    | C("option_set", P(parse_uioption)options)                                            -> SetOption options
    | C("default_colors_set", P(parse_default_colors)dcolors)                              -> Array.last dcolors
    | C1("set_title", [|String title|])                                                    -> SetTitle title
    | C1("set_icon", [|String icon|])                                                      -> SetIcon icon
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
    | C1("grid_scroll", [| 
        (Integer32 grid)
        (Integer32 top); (Integer32 bot) 
        (Integer32 left); (Integer32 right) 
        (Integer32 rows); (Integer32 cols) |])                                             -> GridScroll(grid, top, bot, left, right, rows, cols)
    | C("grid_line", P(parse_grid_line)lines)                                              -> GridLine lines
    | C1("win_pos", [| 
        (Integer32 grid); (Integer32 win) 
        (Integer32 start_row); (Integer32 start_col)
        (Integer32 width); (Integer32 height) |])                                          -> WinPos(grid,win,start_row,start_col,width,height)
    | C1("win_float_pos", [| 
        (Integer32 grid); (Integer32 win)
        (Anchor anchor); (Integer32 anchor_grid)
        (Integer32 anchor_row); (Integer32 anchor_col)
        (Bool focusable) |])                                                               -> WinFloatPos(grid, win, anchor, anchor_grid, anchor_row, anchor_col, focusable)
    | C1("win_external_pos", [| 
        (Integer32 grid); (Integer32 win) |])                                              -> WinExternalPos(grid, win)
    | C1("win_hide", [| (Integer32 grid) |])                                               -> WinHide(grid)
    | C("win_scroll_over_start", _)                                                        -> WinScrollOverStart
    | C("win_scroll_over_reset", _)                                                        -> WinScrollOverReset
    | C1("win_close", [| (Integer32 grid) |])                                              -> WinClose(grid)
    | C1("msg_set_pos", [| 
        (Integer32 grid); (Integer32 row)
        (Bool scrolled); (String sep_char) |])                                             -> MsgSetPos(grid, row,scrolled, sep_char)
    | C1("popupmenu_show", [|
        P(parse_complete_item)items; (Integer32 selected); 
        (Integer32 row); (Integer32 col); (Integer32 grid) |])                             -> PopupMenuShow(items, selected, row, col, grid)
    | C1("popupmenu_select", [| Integer32 selected |])                                     -> PopupMenuSelect(selected)
    | C("popupmenu_hide", _)                                                               -> PopupMenuHide
    | C("hl_group_set", P(parse_semantic_hlgroup)gs)                                       -> SemanticHighlightGroupSet(Map.ofArray gs)
    | _                                                                                    -> UnknownCommand x
    //| C("suspend", _)                                                                    -> 
    //| C("update_menu", _)                                                                -> 


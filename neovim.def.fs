module FVim.neovim.def

open System.Drawing
open MessagePack

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
        cursor_shape: CursorShape
        cell_percentage: int
        blinkwait: int
        blinkon: int
        blinkoff: int
        attr_id: int
        attr_id_lm: int
        short_name: string
        name: string
        // mouse_shape (to be implemented)
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
    | Unknown of obj

[<Struct>]
type RgbAttr =
    {
        foreground : Color option
        background : Color option
        special : Color option
        reverse : bool option
        italic : bool option
        bold : bool option
        underline : bool option
        undercurl : bool option
    }

type GridCell = 
    {
        text: string
        hl_id: int option
        repeat: int option
    }

type RedrawCommand =
// global
| SetOption of UiOption list
| SetTitle of string
| SetIcon of string
| ModeInfoSet of bool * ModeInfo list
| ModeChange of name: string * index: int
| Mouse of on: bool
| Busy of on: bool
| Bell
| VisualBell
| Suspend
| UpdateMenu // ??
| Flush
// grid events
// note, cterm_* are transmitted as term 256-color codes
| DefaultColorsSet of fg: Color * bg: Color * sp: Color * cterm_fg: Color * cterm_bg: Color
| HighlightAttrDefine of id: int * rgb_attr: RgbAttr * cterm_attr: RgbAttr * info: obj[]
| GridLine of grid: int * row: int * col_start: int * cells: GridCell list
| GridClear of grid: int
| GridDestroy of grid: int
| GridCursorGoto of grid: int * row: int * col: int
| GridScroll of grid:int * top:int * bot:int * left:int * right:int * rows:int * cols: int
| GridResize of grid: int * width: int * height: int
// legacy events
//| UpdateFg of Color
//| UpdateBg of Color
//| UpdateSp of Color

type Event =
| Request      of int32 * Request * (int32 -> Response -> unit Async)
| Response     of int32 * Response
| Notification of Request
| Redraw       of RedrawCommand list
| Error        of string

[<Struct>]
[<MessagePackObject(keyAsPropertyName=true)>]
type UiOptions =
    {
        rgb            : bool
        //ext_popupmenu  : bool
        //ext_tabline    : bool
        //ext_cmdline    : bool
        //ext_wildmenu   : bool
        //ext_messages   : bool
        ext_linegrid   : bool
        //ext_multigrid  : bool
        //ext_hlstate    : bool
        //ext_termcolors : bool
    }


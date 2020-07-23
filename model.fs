module FVim.Model

open ui
open common
open States
open def
open neovim

open Avalonia.Media
open System
open System.Collections.Generic
open Avalonia.Threading
open Avalonia.Input
open Avalonia.Input.Raw
open FSharp.Control.Reactive
open System.ComponentModel
open SkiaSharp
open Avalonia.Layout
open System.Threading.Tasks

#nowarn "0058"
#nowarn "0025"

open FSharp.Control.Tasks.V2
open System.Runtime.InteropServices

let inline private trace x = FVim.log.trace "model" x

[<AutoOpen>]
module ModelImpl =

    let nvim = Nvim()
    let ev_uiopt      = Event<unit>()
    let ev_flush      = Event<unit>()
    let grids         = hashmap[]
    let windows       = hashmap[]

    let add_grid(grid: IGridUI) =
        let id = grid.Id
        grids.[id] <- grid

    let destroy_grid(id) =
      match grids.TryGetValue id with
      | false, _ -> ()
      | true, grid ->
        ignore <| grids.Remove id
        grid.Detach()

    let add_window(win: IWindow) = 
        let id = win.RootId
        windows.[id] <- win

    let setTitle id title = windows.[id].Title <- title

    let unicast id cmd = 
        match grids.TryGetValue id with
        | true, grid -> grid.Redraw cmd
        | _ -> trace "unicast into non-existing grid #%d: %A" id cmd

    let broadcast cmd =
        for KeyValue(_,grid) in grids do
            grid.Redraw cmd

    let bell (visual: bool) =
        // TODO
        trace "bell: %A" visual

    let flush_throttle =
        if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then id
        else Observable.throttle(TimeSpan.FromMilliseconds 10.0)

    let rec redraw cmd = 
        match cmd with
        //  Global
        | UnknownCommand x                 -> trace "unknown command %A" x
        | SetTitle title                   -> setTitle 1 title
        | SetIcon icon                     -> trace "icon: %s" icon // TODO
        | Bell                             -> bell true
        | VisualBell                       -> bell false
        | Flush                            -> ev_flush.Trigger()
        | HighlightAttrDefine hls          -> theme.hiattrDefine hls
        | SemanticHighlightGroupSet groups -> theme.setSemanticHighlightGroups groups
        | DefaultColorsSet(fg,bg,sp,_,_)   -> theme.setDefaultColors fg bg sp
        | SetOption opts                   -> Array.iter theme.setOption opts
        | ModeInfoSet(cs_en, info)         -> theme.setModeInfo cs_en info
        //  Broadcast
        | PopupMenuShow _       | PopupMenuSelect _             | PopupMenuHide _
        | Busy _                | Mouse _
        | ModeChange _          | Flush 
        | GridCursorGoto(_,_,_) 
                                            -> broadcast cmd
        //  Unicast
        | GridClear id          | GridScroll(id,_,_,_,_,_,_) ->
            unicast id cmd
        | WinFloatPos(id, _, _, _, _, _, _) -> 
            trace "win_float_pos %A" cmd
            unicast id cmd
        | MsgSetPos(id, _, _, _)            ->
            if not(grids.ContainsKey id) then
              add_grid <| grids.[1].CreateChild id 1 grids.[1].GridWidth
            unicast id cmd
        | WinPos(id, _, _, _, w, h)
        | GridResize(id, w, h)              -> 
              trace "GridResize %d" id
              if not(grids.ContainsKey id) then
                add_grid <| grids.[1].CreateChild id h w
              unicast id cmd
        | GridLine lines                    -> 
            lines 
            |> Array.groupBy (fun (line: GridLine) -> line.grid)
            |> Array.iter (fun (id, lines) -> unicast id (GridLine lines))
        | GridDestroy id                    -> trace "GridDestroy %d" id; destroy_grid id
        | WinClose id                       -> trace "WinClose %d (unimplemented)" id // TODO
        | MultiRedrawCommand xs             -> Array.iter redraw xs
        | x                                 -> trace "unimplemented command: %A" x

    let onGridResize(gridui: IGridUI) =
        trace "Grid #%d resized to %d %d" gridui.Id gridui.GridWidth gridui.GridHeight
        ignore <| nvim.grid_resize gridui.Id gridui.GridWidth gridui.GridHeight

    //  notation                                    meaning                                         equivalent                    decimal value(s)      ~
    //  -----------------------------------------------------------------------------------------------------------------------------------------------------------------------
    //  <Nul>                                       zero                                            CTRL-@                          0 (stored as 10)    *<Nul>*
    //  <BS>                                        backspace                                       CTRL-H                          8                   *backspace*
    //  <Tab>                                       tab                                             CTRL-I                          9                   *tab* *Tab*
    //  <NL>                                        linefeed                                        CTRL-J                         10 (used for <Nul>)  *linefeed*
    //  <FF>                                        formfeed                                        CTRL-L                         12                   *formfeed*
    //  <CR>                                        carriage return                                 CTRL-M                         13                   *carriage-return*
    //  <Return>                                    same as <CR>                                                                                        *<Return>*                    
    //  <Enter>                                     same as <CR>                                                                                        *<Enter>*
    //  <Esc>                                       escape                                          CTRL-[                         27                   *escape* *<Esc>*
    //  <Space>                                     space                                                                          32                   *space*
    //  <lt>                                        less-than                                       <                              60                   *<lt>*
    //  <Bslash>                                    backslash                                       \                              92                   *backslash* *<Bslash>*
    //  <Bar>                                       vertical bar                                    |                             124                   *<Bar>*
    //  <Del>                                       delete                                          127
    //  <CSI>                                       command sequence intro                          ALT-Esc                       155                   *<CSI>*
    //  <xCSI>                                      CSI when typed in the GUI                                                                           *<xCSI>*
    //  <EOL>                                       end-of-line (can be <CR>, <LF> or <CR><LF>, depends on system and 'fileformat')                     *<EOL>*
    //  <Up>                                        cursor-up                                                                                           *cursor-up* *cursor_up*
    //  <Down>                                      cursor-down                                                                                         *cursor-down* *cursor_down*
    //  <Left>                                      cursor-left                                                                                         *cursor-left* *cursor_left*
    //  <Right>                                     cursor-right                                                                                        *cursor-right* *cursor_right*
    //  <S-Up>                                      shift-cursor-up
    //  <S-Down>                                    shift-cursor-down
    //  <S-Left>                                    shift-cursor-left
    //  <S-Right>                                   shift-cursor-right
    //  <C-Left>                                    control-cursor-left
    //  <C-Right>                                   control-cursor-right
    //  <F1> - <F12>                                function keys 1 to 12                                                                               *function_key* *function-key*
    //  <S-F1> - <S-F12>                            shift-function keys 1 to 12                                                                         *<S-F1>*
    //  <Help>                                      help key
    //  <Undo>                                      undo key
    //  <Insert>                                    insert key
    //  <Home>                                      home                                                                                                *home*
    //  <End>                                       end                                                                                                 *end*
    //  <PageUp>                                    page-up                                                                                             *page_up* *page-up*
    //  <PageDown>                                  page-down                                                                                           *page_down* *page-down*
    //  <kHome>                                     keypad home (upper left)                                                                            *keypad-home*
    //  <kEnd>                                      keypad end (lower left)                                                                             *keypad-end*
    //  <kPageUp>                                   keypad page-up (upper right)                                                                        *keypad-page-up*
    //  <kPageDown>                                 keypad page-down (lower right)                                                                      *keypad-page-down*
    //  <kPlus>                                     keypad +                                                                                            *keypad-plus*
    //  <kMinus>                                    keypad -                                                                                            *keypad-minus*
    //  <kMultiply>                                 keypad *                                                                                            *keypad-multiply*
    //  <kDivide>                                   keypad /                                                                                            *keypad-divide*
    //  <kEnter>                                    keypad Enter                                                                                        *keypad-enter*
    //  <kPoint>                                    keypad Decimal point                                                                                *keypad-point*
    //  <k0> - <k9>                                 keypad 0 to 9                                                                                       *keypad-0* *keypad-9*
    //  <S-...>                                     shift-key                                                                                           *shift* *<S-*
    //  <C-...>                                     control-key                                                                                         *control* *ctrl* *<C-*
    //  <M-...>                                     alt-key or meta-key                                                                                 *META* *ALT* *<M-*
    //  <A-...>                                     same as <M-...>                                                                                     *<A-*
    //  <D-...>                                     command-key or "super" key                                                                          *<D-*

    let (|HasFlag|_|) (flag: KeyModifiers) (x: KeyModifiers) =
        if x.HasFlag flag then Some() else None
    let (|NoFlag|_|) (flag: KeyModifiers) (x: KeyModifiers) =
        if x.HasFlag flag then None else Some()
    let MB (x: MouseButton) = 
        match x with
        | MouseButton.Left -> "left"
        | MouseButton.Right -> "right"
        | MouseButton.Middle -> "middle"
    let DIR (dx: float, dy: float, horizontal: bool) =
        match sign dx, sign dy, horizontal with
        | -1, _, true   -> "right"
        | _, _,  true   -> "left"
        | _, -1, false  -> "down"
        | _, _, false   -> "up"
    let suffix (suf: string, r: int, c: int) =
        sprintf "%s><%d,%d" suf r c

    let mutable accumulatedX = 0.0
    let mutable accumulatedY = 0.0

    let (|Mouse|Special|Normal|ImeEvent|TextInput|Unrecognized|) (x: InputEvent) =
        match x with
        // | Key(HasFlag(KeyModifiers.Control), Key.H)
        // | Key(HasFlag(KeyModifiers.Control), Key.I)
        // | Key(HasFlag(KeyModifiers.Control), Key.J)
        // | Key(HasFlag(KeyModifiers.Control), Key.M)
        // | Key(HasFlag(KeyModifiers.Control), Key.Oem4) // Oem4 is '['
        // | Key(HasFlag(KeyModifiers.Control), Key.L) // if ^L is sent as <FF> then neovim discards the key.

        //  Avoid sending key sequence, e.g. "capslock"
        | Key(_, Key.None)              | Key(_, Key.Cancel)     
        | Key(_, Key.Clear)             | Key(_, Key.Pause)
        | Key(_, Key.CapsLock)          | Key(_, Key.Capital)
        | Key(_, Key.HangulMode)        // | Key(_, Key.KanaMode)   
        | Key(_, Key.JunjaMode)         | Key(_, Key.FinalMode)
        | Key(_, Key.KanjiMode)         // | Key(_, Key.HanjaMode)
        | Key(_, Key.Select)            | Key(_, Key.Print)
        | Key(_, Key.Execute)           | Key(_, Key.PrintScreen)
        | Key(_, Key.Apps)              | Key(_, Key.Sleep)
        | Key(_, Key.BrowserBack)       | Key(_, Key.BrowserForward)
        | Key(_, Key.BrowserRefresh)    | Key(_, Key.BrowserStop)
        | Key(_, Key.BrowserSearch)     | Key(_, Key.BrowserFavorites) 
        | Key(_, Key.BrowserHome)
        | Key(_, Key.VolumeUp)          | Key(_, Key.VolumeDown)
        | Key(_, Key.VolumeMute)
        | Key(_, Key.MediaNextTrack)    | Key(_, Key.MediaPreviousTrack)
        | Key(_, Key.MediaStop)         | Key(_, Key.MediaPlayPause)
        | Key(_, Key.LaunchMail)        | Key(_, Key.SelectMedia)
        | Key(_, Key.LaunchApplication1)| Key(_, Key.LaunchApplication2)
        | Key(_, Key.Oem8)              
        | Key(_, Key.AbntC1)            | Key(_, Key.AbntC2)
        | Key(_, Key.System)            
        | Key(_, Key.OemAttn)   //| Key(_, Key.DbeAlphanumeric)   
        | Key(_, Key.OemFinish) // | Key(_, Key.DbeKatakana)       
        | Key(_, Key.DbeHiragana) // | Key(_, Key.OemCopy)           
        | Key(_, Key.DbeSbcsChar) // | Key(_, Key.OemAuto)           
        | Key(_, Key.DbeDbcsChar) // | Key(_, Key.OemEnlw)           
        | Key(_, Key.OemBackTab) // | Key(_, Key.DbeRoman)          
        | Key(_, Key.DbeNoRoman) // | Key(_, Key.Attn)              
        | Key(_, Key.CrSel) // | Key(_, Key.DbeEnterWordRegisterMode)
        | Key(_, Key.ExSel) // | Key(_, Key.DbeEnterImeConfigureMode)
        | Key(_, Key.EraseEof) // | Key(_, Key.DbeFlushString)    
        | Key(_, Key.Play) // | Key(_, Key.DbeCodeInput)      
        | Key(_, Key.DbeNoCodeInput)    | Key(_, Key.Zoom)
        | Key(_, Key.NoName) //| Key(_, Key.DbeDetermineString)
        | Key(_, Key.DbeEnterDialogConversionMode) // | Key(_, Key.Pa1)
        | Key(_, Key.OemClear)
        | Key(_, Key.DeadCharProcessed) 
        | Key(_, Key.FnLeftArrow)       | Key(_, Key.FnRightArrow)
        | Key(_, Key.FnUpArrow)         | Key(_, Key.FnDownArrow)

            -> Unrecognized  

        | Key(_, Key.Back)                                            -> Special "BS"
        | Key(_, Key.Tab)                                             -> Special "Tab"
        | Key(_, Key.LineFeed)                                        -> Special "NL"
        | Key(_, Key.Return)                                          -> Special "CR"
        | Key(_, Key.Escape)                                          -> Special "Esc"
        | Key(_, Key.Space)                                           -> Special "Space"
        | Key(HasFlag(KeyModifiers.Shift), Key.OemComma)              -> Special "LT"
        // note, on Windows '\' is recognized as OemPipe but on macOS it's OemBackslash
        | Key(NoFlag(KeyModifiers.Shift), 
             (Key.OemPipe | Key.OemBackslash))                        -> Special "Bslash"
        | Key(HasFlag(KeyModifiers.Shift), 
             (Key.OemPipe | Key.OemBackslash))                        -> Special "Bar"
        | Key(_, Key.Delete)                                          -> Special "Del"
        | Key(HasFlag(KeyModifiers.Alt), Key.Escape)                  -> Special "xCSI"
        | Key(_, Key.Up)                                              -> Special "Up"
        | Key(_, Key.Down)                                            -> Special "Down"
        | Key(_, Key.Left)                                            -> Special "Left"
        | Key(_, Key.Right)                                           -> Special "Right"
        | Key(_, Key.Help)                                            -> Special "Help"
        | Key(_, Key.Insert)                                          -> Special "Insert"
        | Key(_, Key.Home)                                            -> Special "Home"
        | Key(_, Key.End)                                             -> Special "End"
        | Key(_, Key.PageUp)                                          -> Special "PageUp"
        | Key(_, Key.PageDown)                                        -> Special "PageDown"
        | Key(_, x &
          (Key.F1  | Key.F2  | Key.F3  | Key.F4 
        |  Key.F5  | Key.F6  | Key.F7  | Key.F8 
        |  Key.F9  | Key.F10 | Key.F11 | Key.F12
        |  Key.F13 | Key.F14 | Key.F15 | Key.F16
        |  Key.F17 | Key.F18 | Key.F19 | Key.F20
        |  Key.F21 | Key.F22 | Key.F23 | Key.F24))                    -> Special(x.ToString())
        | Key(NoFlag(KeyModifiers.Shift), x &
          (Key.D0 | Key.D1 | Key.D2 | Key.D3 
        |  Key.D4 | Key.D5 | Key.D6 | Key.D7 
        |  Key.D8 | Key.D9))                                          -> Normal(x.ToString().TrimStart('D'))
        | Key(_, x &
          (Key.NumPad0 | Key.NumPad1 | Key.NumPad2 | Key.NumPad3 
        |  Key.NumPad4 | Key.NumPad5 | Key.NumPad6 | Key.NumPad7 
        |  Key.NumPad8 | Key.NumPad9))                                -> Special("k" + string(x.ToString() |> Seq.last))
        |  Key(NoFlag(KeyModifiers.Shift), Key.OemComma)              -> Normal ","
        |  Key(NoFlag(KeyModifiers.Shift), Key.OemPeriod)             -> Normal "."
        |  Key(HasFlag(KeyModifiers.Shift), Key.OemPeriod)            -> Normal ">"
        |  Key(NoFlag(KeyModifiers.Shift), Key.Oem2)                  -> Normal "/"
        |  Key(HasFlag(KeyModifiers.Shift), Key.Oem2)                 -> Normal "?"
        |  Key(NoFlag(KeyModifiers.Shift), Key.OemSemicolon)          -> Normal ";"
        |  Key(HasFlag(KeyModifiers.Shift), Key.OemSemicolon)         -> Normal ":"
        |  Key(NoFlag(KeyModifiers.Shift), Key.OemQuotes)             -> Normal "'"
        |  Key(HasFlag(KeyModifiers.Shift), Key.OemQuotes)            -> Normal "\""
        |  Key(NoFlag(KeyModifiers.Shift), Key.Oem4)                  -> Normal "["
        |  Key(HasFlag(KeyModifiers.Shift), Key.Oem4)                 -> Normal "{"
        |  Key(NoFlag(KeyModifiers.Shift), Key.OemCloseBrackets)      -> Normal "]"
        |  Key(HasFlag(KeyModifiers.Shift), Key.OemCloseBrackets)     -> Normal "}"
        |  Key(NoFlag(KeyModifiers.Shift), Key.OemMinus)              -> Normal "-"
        |  Key(HasFlag(KeyModifiers.Shift), Key.OemMinus)             -> Normal "_"
        |  Key(NoFlag(KeyModifiers.Shift), Key.OemPlus)               -> Normal "="
        |  Key(HasFlag(KeyModifiers.Shift), Key.OemPlus)              -> Normal "+"
        |  Key(NoFlag(KeyModifiers.Shift), Key.OemTilde)              -> Normal "`"
        |  Key(HasFlag(KeyModifiers.Shift), Key.OemTilde)             -> Normal "~"
        |  Key(HasFlag(KeyModifiers.Shift), Key.D1)                   -> Normal "!"
        |  Key(HasFlag(KeyModifiers.Shift), Key.D2)                   -> Normal "@"
        |  Key(HasFlag(KeyModifiers.Shift), Key.D3)                   -> Normal "#"
        |  Key(HasFlag(KeyModifiers.Shift), Key.D4)                   -> Normal "$"
        |  Key(HasFlag(KeyModifiers.Shift), Key.D5)                   -> Normal "%"
        |  Key(HasFlag(KeyModifiers.Shift), Key.D6)                   -> Normal "^"
        |  Key(HasFlag(KeyModifiers.Shift), Key.D7)                   -> Normal "&"
        |  Key(HasFlag(KeyModifiers.Shift), Key.D8)                   -> Normal "*"
        |  Key(HasFlag(KeyModifiers.Shift), Key.D9)                   -> Normal "("
        |  Key(HasFlag(KeyModifiers.Shift), Key.D0)                   -> Normal ")"
        |  Key(_, Key.Multiply)                                       -> Special("kMultiply")
        |  Key(_, Key.Add)                                            -> Special("kPlus")
        |  Key(_, Key.Subtract)                                       -> Special("kMinus")
        |  Key(_, Key.Divide)                                         -> Special("kDivide")
        // TODO|  Key(_, Key.Decimal) -> ???
        // TODO|  Key(_, Key.Separator) -> ???
        |  Key(_, (
           Key.ImeProcessed  | Key.ImeAccept | Key.ImeConvert
        |  Key.ImeNonConvert | Key.ImeModeChange))                    -> ImeEvent
        |  Key(NoFlag(KeyModifiers.Shift), x)                         -> Normal (x.ToString().ToLowerInvariant())
        |  Key(_, x)                                                  -> Normal (x.ToString())
        |  MousePress(_, r, c, but)                                   -> Mouse(MB but, "press", r, c, 1)
        |  MouseRelease(_, r, c, but)                                 -> Mouse(MB but, "release", r, c, 1)
        |  MouseDrag(_, r, c, but   )                                 -> Mouse(MB but, "drag", r, c, 1)
        |  MouseWheel(_, r, c, dx, dy)                                -> 
            // duh! don't like this
            accumulatedX <- accumulatedX + dx
            accumulatedY <- accumulatedY + dy
            let ax, ay = abs accumulatedX, abs accumulatedY
            let rpt = max ax ay
            let dir = DIR(dx, dy, ax >= ay)
            if rpt >= 1.0 then
                if ax >= ay then
                    accumulatedX <- accumulatedX - truncate accumulatedX
                else
                    accumulatedY <- accumulatedY - truncate accumulatedY
                Mouse("wheel", dir, r, c, int rpt)
            else
                Unrecognized
        |  TextInput txt                                              -> TextInput txt
        |  _                                                          -> Unrecognized
    let rec (|ModifiersPrefix|_|) (x: InputEvent) =
        match x with
        // -------------- keys with special form do not carry shift modifiers
        |  Key(m & HasFlag(KeyModifiers.Shift), x &
          (Key.OemComma | Key.OemPipe | Key.OemBackslash | Key.OemPeriod | Key.Oem2 | Key.OemSemicolon | Key.OemQuotes
        |  Key.Oem4 | Key.OemCloseBrackets | Key.OemMinus | Key.OemPlus | Key.OemTilde
        |  Key.D0 | Key.D1 | Key.D2 | Key.D3 
        |  Key.D4 | Key.D5 | Key.D6 | Key.D7 
        |  Key.D8 | Key.D9)) 
            -> (|ModifiersPrefix|_|) <| InputEvent.Key(m &&& (~~~KeyModifiers.Shift), x)
        | Key(m & HasFlag(KeyModifiers.Shift), Key.Space) when States.key_disableShiftSpace
            -> (|ModifiersPrefix|_|) <| InputEvent.Key(m &&& (~~~KeyModifiers.Shift), Key.Space)
        | Key(m, _)
        | MousePress(m, _, _, _) 
        | MouseRelease(m, _, _, _) 
        | MouseDrag(m, _, _, _) 
        | MouseWheel(m, _, _, _, _) 
            ->
            let c = if m.HasFlag(KeyModifiers.Control) then "C-" else ""
            let a = if m.HasFlag(KeyModifiers.Alt)     then "A-" else ""
            let d = if m.HasFlag(KeyModifiers.Meta) then "D-" else ""
            let s = if m.HasFlag(KeyModifiers.Shift)   then "S-" else ""
            match (sprintf "%s%s%s%s" c a d s).TrimEnd('-') with
            | "" -> None
            | x -> Some x
        | TextInput _ -> None
        | _ -> None

    let mutable _imeArmed = false

    let onInput (input: IObservable<int*InputEvent>) =
        let inputClassifier = 
            input
            // filter out pure modifiers
            |> Observable.filter (fun (_, x) -> 
                match x with
                | InputEvent.Key(_, (Key.LeftCtrl | Key.LeftShift | Key.LeftAlt | Key.RightCtrl | Key.RightShift | Key.RightAlt | Key.LWin | Key.RWin)) 
                    -> false
                | _ -> true)
            // translate to nvim input sequence
            |> Observable.map(fun (gridid, x) ->
                trace "grid #%d: OnInput: %A" gridid x

                match x with
                | ImeEvent    -> _imeArmed <- true
                | TextInput _ -> ()
                | _           -> _imeArmed <- false
                // TODO anything that cancels ime input state?

                match x with
                | (Special sp) & (ModifiersPrefix pref) -> Choice1Of3(sprintf "<%s-%s>" pref sp)
                | (Special sp)                          -> Choice1Of3(sprintf "<%s>" sp)
                | (Normal n) & (ModifiersPrefix pref)   -> Choice1Of3(sprintf "<%s-%s>" pref n)
                | (Normal n)                            -> Choice1Of3 n 
                | (Mouse m)                             -> Choice2Of3(gridid, m, (|ModifiersPrefix|_|)x)
                | ImeEvent                              -> Choice3Of3 ()
                | TextInput txt when _imeArmed          -> Choice1Of3 txt
                | x                                     -> trace "rejected: %A" x; Choice3Of3 ()
            )
        let key   = inputClassifier |> Observable.choose (function | Choice1Of3 x -> Some x | _ -> None)
        let mouse = inputClassifier |> Observable.choose (function | Choice2Of3 x -> Some x | _ -> None)
        Disposables.compose [
            key |> Observable.subscribe(fun x -> 
                nvim.input x
                |> ignore
            )
            mouse |> Observable.subscribe(fun (grid, (but, act, r, c, rep), mods) -> 
                let mods = match mods with Some mods -> mods | _ -> ""
                for _ in 1..rep do
                    nvim.input_mouse but act mods grid r c
                    |> ignore
            )
        ]

let Detach() =
    nvim.stop(0)
    States.Shutdown(0)

let UpdateUICapabilities() =
    let opts = hashmap[]
    States.PopulateUIOptions opts
    trace "UpdateUICapabilities: %A" <| String.Join(", ", Seq.map (fun (KeyValue(k, v)) -> sprintf "%s=%b" k v) opts)
    task {
        for KeyValue(k, v) in opts do
            let! _ = nvim.call { method="nvim_ui_set_option"; parameters = mkparams2 k v }
            in ()
    } |> run

/// <summary>
/// Call this once at initialization.
/// </summary>
let Start (opts: getopt.Options) =
    trace "%s" "starting neovim instance..."
    trace "opts = %A" opts
    States.ui_multigrid <- opts.debugMultigrid
    nvim.start opts
    nvim.subscribe 
        (AvaloniaSynchronizationContext.Current) 
        (States.msg_dispatch)

    // rpc handlers
    States.Register.Bool "font.autosnap"
    States.Register.Bool "font.antialias"
    States.Register.Bool "font.drawBounds"
    States.Register.Bool "font.autohint"
    States.Register.Bool "font.subpixel"
    States.Register.Bool "font.lcdrender"
    States.Register.Bool "font.ligature"
    States.Register.Prop<SKPaintHinting> States.parseHintLevel "font.hintLevel"
    States.Register.Prop<SKFontStyleWeight> States.parseFontWeight "font.weight.normal"
    States.Register.Prop<SKFontStyleWeight> States.parseFontWeight "font.weight.bold"
    States.Register.Prop<States.LineHeightOption> States.parseLineHeightOption "font.lineheight"
    States.Register.Bool "font.nonerd"
    States.Register.Bool "cursor.smoothblink"
    States.Register.Bool "cursor.smoothmove"
    States.Register.Bool "key.disableShiftSpace"
    States.Register.Bool "ui.multigrid"
    States.Register.Bool "ui.popupmenu"
    States.Register.Bool "ui.tabline"
    States.Register.Bool "ui.cmdline"
    States.Register.Bool "ui.wildmenu"
    States.Register.Bool "ui.messages"
    States.Register.Bool "ui.termcolors"
    States.Register.Bool "ui.hlstate"

    States.Register.Prop<States.BackgroundComposition> States.parseBackgroundComposition "background.composition"
    States.Register.Float "background.opacity"
    States.Register.Float "background.altopacity"
    States.Register.Float "background.image.opacity"
    States.Register.String "background.image.file"
    States.Register.Prop<Stretch> States.parseStretch "background.image.stretch"
    States.Register.Prop<HorizontalAlignment> States.parseHorizontalAlignment "background.image.halign"
    States.Register.Prop<VerticalAlignment> States.parseVerticalAlignment "background.image.valign"


    List.iter ignore [
        ev_uiopt.Publish
        |> Observable.throttle(TimeSpan.FromMilliseconds 20.0)
        |> Observable.subscribe(UpdateUICapabilities)
        States.Register.Notify "redraw" (Array.map parse_redrawcmd >> Array.iter redraw)
        States.Register.Notify "remote.detach" (fun _ -> Detach())
        States.Register.Watch "ui" ev_uiopt.Trigger
    ]

    States.Register.Request "set-clipboard" (fun [| P(|String|_|)lines; String regtype |] -> task {
        States.clipboard_lines <- lines
        States.clipboard_regtype <- regtype
        let! _ = Avalonia.Application.Current.Clipboard.SetTextAsync(String.Join("\n", lines))
        trace "set-clipboard called. regtype=%s" regtype
        return { result = Ok(box [||]) }
    })

    States.Register.Request "get-clipboard" (fun _ -> task {
        let! sysClipboard = Avalonia.Application.Current.Clipboard.GetTextAsync()
        let sysClipboard = if String.IsNullOrEmpty sysClipboard then "" else sysClipboard
        let sysClipboardLines = sysClipboard.Replace("\r\n", "\n").Split("\n")
        let clipboard_eq = Array.compareWith (fun a b -> String.Compare(a,b)) States.clipboard_lines sysClipboardLines

        let lines, regtype =
            if clipboard_eq = 0 then
                trace "get-clipboard: match, using clipboard lines with regtype %s" States.clipboard_regtype
                States.clipboard_lines, States.clipboard_regtype
            else
                trace "%s" "get-clipboard: mismatch, using system clipboard"
                sysClipboardLines, "v"

        return { result = Ok(box [| box lines; box regtype |])}
    })

    trace "%s" "commencing early initialization..."

    async {
        let! api_info = Async.AwaitTask(nvim.call { method = "nvim_get_api_info"; parameters = [||] })
        let api_query_result = 
            match api_info.result with
            | Ok(ObjArray [| Integer32 _; metadata |]) -> Ok metadata
            | _ -> Result.Error("nvim_get_api_info")
            >?= fun metadata ->
            match metadata with
            | FindKV "ui_options" (P (|String|_|) ui_options) -> Ok(Set.ofArray ui_options)
            | _ -> Result.Error("find ui_options")
            >?= fun ui_options ->
            trace "available ui options: %A" ui_options
            ui_available_opts <- ui_options
            Ok()
        match api_query_result with
        | Ok() ->
            if not (ui_available_opts.Contains uiopt_ext_linegrid) ||
               not (ui_available_opts.Contains uiopt_rgb) then
               failwithf "api_query_result: your NeoVim version is too low. fvim requires \"rgb\" and \"ext_linegrid\" options, got: %A" ui_available_opts
        | Result.Error(msg) ->
            failwithf "api_query_result: %s" msg

        // for remote, send open file args as edit commands
        if nvim.isRemote then
            for file in opts.args do
                let! _ = Async.AwaitTask(nvim.edit file)
                in ()

        let clientId = nvim.Id.ToString()
        let clientName = "FVim"
        let clientVersion = 
            hashmap [
                "major", "0"
                "minor", "2"
                "prerelease", "dev"
            ]
        let clientType = "ui"
        let clientMethods = hashmap []
        let clientAttributes = 
            hashmap [
                "InstanceId", clientId
            ]

        let! _ = Async.AwaitTask(nvim.set_var "fvim_loaded" 1)
        let! _ = Async.AwaitTask(nvim.set_client_info clientName clientVersion clientType clientMethods clientAttributes)
        let! channels = Async.AwaitTask(nvim.list_chans())

        let ch_finder ch =
            FindKV("id") ch 
            >>= Integer32
            >>= fun chid ->
            FindKV("client")ch
            >>= fun client -> 
                match client with
                | FindKV("name")(String name) when name = clientName -> Some client
                | _ -> None
            >>= FindKV("attributes")
            >>= FindKV("InstanceId")
            >>= IsString
            >>= (fun iid -> Some(iid, chid))
        
        let fvimChannels = Seq.choose ch_finder channels |> List.ofSeq
        let _, myChannel = List.find (fun (iid, _) -> iid = clientId) fvimChannels

        trace "FVim connected clients: %A" fvimChannels
        trace "FVim client channel is: %d" myChannel

        FVim.States.channel_id <- myChannel

        // Another instance is already up
        if fvimChannels.Length > 1 then
            Environment.Exit(0)

        // Register clipboard provider by setting g:clipboard
        let clipboard = """let g:clipboard = {
  'name': 'FVimClipboard',
  'copy': {
     '+': {lines, regtype -> rpcrequest(MY_CHANNEL, 'set-clipboard', lines, regtype)},
     '*': {lines, regtype -> rpcrequest(MY_CHANNEL, 'set-clipboard', lines, regtype)},
   },
  'paste': {
     '+': {-> rpcrequest(MY_CHANNEL, 'get-clipboard')},
     '*': {-> rpcrequest(MY_CHANNEL, 'get-clipboard')},
  },
}"""
        let! _ = Async.AwaitTask(nvim.command <| clipboard.Replace("MY_CHANNEL", string myChannel).Replace("\r", "").Replace("\n",""))

        let! _ = Async.AwaitTask(nvim.set_var "fvim_channel" myChannel)

        let! _ = Async.AwaitTask(nvim.``command!`` "FVimDetach" 0 (sprintf "call rpcnotify(%d, 'remote.detach')" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "FVimToggleFullScreen" 0 (sprintf "call rpcnotify(%d, 'ToggleFullScreen', 1)" myChannel))

        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimCursorSmoothMove" 1 (sprintf "call rpcnotify(%d, 'cursor.smoothmove', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimCursorSmoothBlink" 1 (sprintf "call rpcnotify(%d, 'cursor.smoothblink', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontLineHeight" 1 (sprintf "call rpcnotify(%d, 'font.lineheight', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontAutoSnap" 1 (sprintf "call rpcnotify(%d, 'font.autosnap', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontAntialias" 1 (sprintf "call rpcnotify(%d, 'font.antialias', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontLigature" 1 (sprintf "call rpcnotify(%d, 'font.ligature', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontDrawBounds" 1 (sprintf "call rpcnotify(%d, 'font.drawBounds', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontAutohint" 1 (sprintf "call rpcnotify(%d, 'font.autohint', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontSubpixel" 1 (sprintf "call rpcnotify(%d, 'font.subpixel', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontHintLevel" 1 (sprintf "call rpcnotify(%d, 'font.hintLevel', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontNormalWeight" 1 (sprintf "call rpcnotify(%d, 'font.weight.normal', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontBoldWeight" 1 (sprintf "call rpcnotify(%d, 'font.weight.bold', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontNoBuiltinSymbols" 1 (sprintf "call rpcnotify(%d, 'font.nonerd', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimKeyDisableShiftSpace" 1 (sprintf "call rpcnotify(%d, 'key.disableShiftSpace', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimUIMultiGrid" 1 (sprintf "call rpcnotify(%d, 'ui.multigrid', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimUIPopupMenu" 1 (sprintf "call rpcnotify(%d, 'ui.popupmenu', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimUITabLine" 1 (sprintf "call rpcnotify(%d, 'ui.tabline', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimUICmdLine" 1 (sprintf "call rpcnotify(%d, 'ui.cmdline', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimUIWildMenu" 1 (sprintf "call rpcnotify(%d, 'ui.wildmenu', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimUIMessages" 1 (sprintf "call rpcnotify(%d, 'ui.messages', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimUITermColors" 1 (sprintf "call rpcnotify(%d, 'ui.termcolors', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimUIHlState" 1 (sprintf "call rpcnotify(%d, 'ui.hlstate', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimDrawFPS" 1 (sprintf "call rpcnotify(%d, 'DrawFPS', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimCustomTitleBar" 1 (sprintf "call rpcnotify(%d, 'CustomTitleBar', <args>)" myChannel))

        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimBackgroundOpacity" 1 (sprintf "call rpcnotify(%d, 'background.opacity', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimBackgroundComposition" 1 (sprintf "call rpcnotify(%d, 'background.composition', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimBackgroundAltOpacity" 1 (sprintf "call rpcnotify(%d, 'background.altopacity', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimBackgroundImage" 1 (sprintf "call rpcnotify(%d, 'background.image.file', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimBackgroundImageOpacity" 1 (sprintf "call rpcnotify(%d, 'background.image.opacity', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimBackgroundImageStretch" 1 (sprintf "call rpcnotify(%d, 'background.image.stretch', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimBackgroundImageHAlign" 1 (sprintf "call rpcnotify(%d, 'background.image.halign', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimBackgroundImageVAlign" 1 (sprintf "call rpcnotify(%d, 'background.image.valign', <args>)" myChannel))


        // trigger ginit upon VimEnter
        let! _ = Async.AwaitTask(nvim.command "autocmd VimEnter * runtime! ginit.vim")
        ()
    } |> Async.RunSynchronously

let Flush =
    ev_flush.Publish
    |> flush_throttle
    |> Observable.observeOn Avalonia.Threading.AvaloniaScheduler.Instance

let OnWindowReady(win: IWindow) =
    add_window win


// connect the grid redraw commands and events
let OnGridReady(gridui: IGridUI) =

    gridui.Resized 
    |> Observable.throttle (TimeSpan.FromMilliseconds 20.0)
    |> Observable.add onGridResize

    add_grid gridui

    gridui.Input 
    |> onInput
    |> nvim.pushSubscription

    if gridui.Id = 1 then
        // Grid #1 is the main grid.
        // When ready, the UI should be ready for events now. 
        // Notify nvim about its presence
        trace 
              "attaching to nvim on first grid ready signal. size = %A %A" 
              gridui.GridWidth gridui.GridHeight
        task {
            let! _ = nvim.ui_attach gridui.GridWidth gridui.GridHeight
            ()
        } |> run

let SelectPopupMenuItem (index: int) (insert: bool) (finish: bool) =
    trace "SelectPopupMenuItem: index=%d insert=%b finish=%b" index insert finish
    task {
        let insert = if insert then "v:true" else "v:false"
        let finish = if finish then "v:true" else "v:false"
        let! _ = nvim.command (sprintf "call nvim_select_popupmenu_item(%d, %s, %s, {})" index insert finish)
        in ()
    } |> run

let SetPopupMenuPos width height row col =
    trace "SetPopupMenuPos: w=%f h=%f r=%f c=%f" width height row col
    task {
      let! _ = nvim.call { method = "nvim_ui_pum_set_bounds";  parameters = mkparams4 width height row col}
      in ()
    } |> run

let OnFocusLost() =
    task { 
      let! _ = nvim.command "if exists('#FocusLost') | doautocmd <nomodeline> FocusLost | endif"
      in ()
    } |> run

// see: https://github.com/equalsraf/neovim-qt/blob/e13251a6774ec8c38e7f124b524cc36e4453eb35/src/gui/shell.cpp#L1405
let OnFocusGained() =
    task { 
      let! _ = nvim.command "if exists('#FocusGained') | doautocmd <nomodeline> FocusGained | endif"
      in ()
    } |> run

let OnTerminated (args) =
    trace "%s" "terminating nvim..."
    nvim.stop 1

let OnTerminating(args: CancelEventArgs) =
    args.Cancel <- true
    trace "%s" "window is closing"
    task {
        if nvim.isRemote then
            Detach()
        else
            let! _ = nvim.quitall()
            ()
    } |> run
    ()

let EditFiles (files: string seq) =
    task {
        for file in files do
            let! _ = nvim.edit file
            ()
    } |> run

let InsertText text =
    let sb = new Text.StringBuilder()
    // wrap it as put ='text', escape accordingly
    ignore <| sb.Append("put ='")
    for ch in text do
        match ch with
        | '|'  -> sb.Append("\\|")
        | '"'  -> sb.Append("\\\"")
        | '\'' -> sb.Append("''")
        | x    -> sb.Append(x)
        |> ignore
    ignore <| sb.Append("'")

    if not <| String.IsNullOrEmpty text then
        let text = sb.ToString()
        ignore <| nvim.command text

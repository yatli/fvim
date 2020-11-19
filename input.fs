module FVim.input
open ui
open log
open neovim

open Avalonia.Input
open System
open FSharp.Control.Reactive

let inline trace fmt = trace "input" fmt

#nowarn "0058"

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
let (|NvimSupportedMouseButton|_|) (mb: MouseButton) =
    match mb with
    | MouseButton.Left | MouseButton.Right | MouseButton.Middle -> Some mb
    | _ -> None
let MB (x: MouseButton) = 
    match x with
    | MouseButton.Left -> "left"
    | MouseButton.Right -> "right"
    | MouseButton.Middle -> "middle"
    | _ -> "none"
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
    | Key(_, Key.CapsLock)          | Key(_, Key.Capital) | Key(_, Key.NumLock)
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
    |  MousePress(_, r, c, NvimSupportedMouseButton but)          -> Mouse(MB but, "press", r, c, 1)
    |  MouseRelease(_, r, c, NvimSupportedMouseButton but)        -> Mouse(MB but, "release", r, c, 1)
    |  MouseDrag(_, r, c, NvimSupportedMouseButton but   )        -> Mouse(MB but, "drag", r, c, 1)
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
    | Key(m & HasFlag(KeyModifiers.Shift), Key.Space) when states.key_disableShiftSpace
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

let mutable private _imeArmed = false

let onInput (nvim: Nvim) (input: IObservable<int*InputEvent>) =
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
            nvim.input x |> ignore
        )
        mouse |> Observable.subscribe(fun (grid, (but, act, r, c, rep), mods) -> 
            let mods = match mods with Some mods -> mods | _ -> ""
            for _ in 1..rep do
                nvim.input_mouse but act mods grid r c |> ignore
        )
    ]


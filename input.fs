module FVim.input
open ui
open log
open neovim

open Avalonia.Input
open System
open FSharp.Control.Reactive
open Avalonia.Interactivity

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
let (|DoesntBlockTextInput|_|) (x: KeyModifiers) =
    if x.HasFlag (KeyModifiers.Alt) || x.HasFlag (KeyModifiers.Control) || x.HasFlag (KeyModifiers.Meta) then None else Some()
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

let mutable accumulatedX = 0.0
let mutable accumulatedY = 0.0

// Avoid sending Rejected key as a sequence, e.g. "capslock"
let RejectKeys = set [
  Key.None
  Key.Clear
  Key.Cancel
  Key.Pause
  Key.CapsLock
  Key.Capital
  Key.NumLock
  Key.HangulMode
  Key.KanaMode
  Key.JunjaMode
  Key.FinalMode
  Key.KanjiMode
  Key.HanjaMode
  Key.Select
  Key.Execute
  Key.Apps
  Key.Print
  Key.PrintScreen
  Key.Sleep
  Key.BrowserBack
  Key.BrowserForward
  Key.BrowserRefresh
  Key.BrowserStop
  Key.BrowserSearch
  Key.BrowserFavorites
  Key.BrowserHome
  Key.VolumeUp
  Key.VolumeDown
  Key.VolumeMute
  Key.MediaNextTrack
  Key.MediaPreviousTrack
  Key.MediaStop
  Key.MediaPlayPause
  Key.LaunchMail
  Key.SelectMedia
  Key.LaunchApplication1
  Key.LaunchApplication2
  Key.Oem8
  Key.AbntC1
  Key.AbntC2
  Key.System
  Key.OemAttn
  Key.DbeAlphanumeric
  Key.OemFinish
  Key.DbeKatakana
  Key.DbeHiragana
  Key.OemCopy
  Key.DbeSbcsChar
  Key.OemAuto
  Key.DbeDbcsChar
  Key.OemEnlw
  Key.OemBackTab
  Key.DbeRoman
  Key.DbeNoRoman
  Key.Attn
  Key.CrSel
  Key.DbeEnterWordRegisterMode
  Key.ExSel
  Key.DbeEnterImeConfigureMode
  Key.EraseEof
  Key.DbeFlushString
  Key.Play
  Key.DbeCodeInput
  Key.DbeNoCodeInput
  Key.Zoom
  Key.NoName
  Key.DbeDetermineString
  Key.DbeEnterDialogConversionMode
  Key.Pa1
  Key.OemClear
  Key.DeadCharProcessed

  Key.FnLeftArrow
  Key.FnRightArrow
  Key.FnUpArrow
  Key.FnDownArrow

  Key.ImeProcessed  
  Key.ImeAccept 
  Key.ImeConvert
  Key.ImeNonConvert 
  Key.ImeModeChange

  // filter out pure modifiers
  Key.LeftCtrl 
  Key.RightCtrl 
  Key.LeftShift 
  Key.RightShift 
  Key.LeftAlt 
  Key.RightAlt 
  Key.LWin 
  Key.RWin
]

// Avoid sending unmapped key that also triggers a TextInput
// To match against this set, ensure there's no modifiers or just shift
let TextInputKeys = set [
  Key.Space
  Key.Oem1
  Key.Oem102
  Key.Oem2
  Key.Oem3
  Key.Oem4
  Key.Oem5
  Key.Oem6
  Key.Oem7
  Key.OemBackslash
  Key.OemCloseBrackets
  Key.OemComma
  Key.OemMinus
  Key.OemOpenBrackets
  Key.OemPeriod
  Key.OemPipe
  Key.OemPlus
  Key.OemQuestion
  Key.OemQuotes
  Key.OemSemicolon
  Key.OemTilde
  Key.D0
  Key.D1
  Key.D2
  Key.D3
  Key.D4
  Key.D5
  Key.D6
  Key.D7
  Key.D8
  Key.D9
  Key.NumPad0 
  Key.NumPad1 
  Key.NumPad2 
  Key.NumPad3 
  Key.NumPad4 
  Key.NumPad5 
  Key.NumPad6 
  Key.NumPad7 
  Key.NumPad8 
  Key.NumPad9
  Key.Multiply
  Key.Add
  Key.Subtract
  Key.Divide
  Key.Decimal
  Key.A
  Key.B
  Key.C
  Key.D
  Key.E
  Key.F
  Key.G
  Key.H
  Key.I
  Key.J
  Key.K
  Key.L
  Key.M
  Key.N
  Key.O
  Key.P
  Key.Q
  Key.R
  Key.S
  Key.T
  Key.U
  Key.V
  Key.W
  Key.X
  Key.Y
  Key.Z
]

let (|Special|Normal|Rejected|) (x: InputEvent) =
    match x with
    | Key(_, k) when RejectKeys.Contains k                        -> Rejected  
    | Key(DoesntBlockTextInput, k) when TextInputKeys.Contains k  -> Rejected  
    // !!Note from here on, all TextInput-triggering keys should have been filtered.
    // All that survived the purge are either non-textinput-triggering keys, or
    // textinput-triggering keys with non-shift modifier active.
    // The (possibly unwanted) side effect of this approach is the inconsistency
    // between TextInput and modifier-active Key:
    //
    // | en-US kbd sequence | en result (sent to nvim) | de result (sent to nvim) |
    // | ------------------ | ------------------------ | ------------------------ |
    // | z                  | z                        | y                        |
    // | Ctrl-z             | <C-z>                    | <C-z>                    |
    // | '                  | '                        | ä                        |
    // | Ctrl-'             | <C-'>                    | <C-'>                    |
    //
    | Key(m, Key.Back)                                            -> Special "BS"
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
    |  Key(_, Key.Separator)                                      -> Special("kEnter")
    |  Key(_, Key.Decimal)                                        -> Special("kPoint")
    |  Key(NoFlag(KeyModifiers.Shift), x)                         -> Normal (x.ToString().ToLowerInvariant())
    |  Key(_, x)                                                  -> Normal (x.ToString())
    |  _                                                          -> Rejected

let rec ModifiersPrefix (x: InputEvent) =
    match x with
    // -------------- keys with special form do not carry shift modifiers
    |  Key(m & HasFlag(KeyModifiers.Shift), x &
      (Key.OemComma | Key.OemPipe | Key.OemBackslash | Key.OemPeriod | Key.Oem2 | Key.OemSemicolon | Key.OemQuotes
    |  Key.Oem4 | Key.OemCloseBrackets | Key.OemMinus | Key.OemPlus | Key.OemTilde
    |  Key.D0 | Key.D1 | Key.D2 | Key.D3 
    |  Key.D4 | Key.D5 | Key.D6 | Key.D7 
    |  Key.D8 | Key.D9)) 
        -> ModifiersPrefix <| InputEvent.Key(m &&& (~~~KeyModifiers.Shift), x)
    | Key(m & HasFlag(KeyModifiers.Shift), Key.Space) when states.key_disableShiftSpace
        -> ModifiersPrefix <| InputEvent.Key(m &&& (~~~KeyModifiers.Shift), Key.Space)
    | Key(m, _)
    | MousePress(m, _, _, _) 
    | MouseRelease(m, _, _, _) 
    | MouseDrag(m, _, _, _) 
    | MouseWheel(m, _, _, _, _) 
        ->
        let c = if m.HasFlag(KeyModifiers.Control) then "C-" else ""
        let a = if m.HasFlag(KeyModifiers.Alt)     then "A-" else ""
        let d = if m.HasFlag(KeyModifiers.Meta)    then "D-" else ""
        let s = if m.HasFlag(KeyModifiers.Shift)   then "S-" else ""
        $"{c}{a}{d}{s}".TrimEnd('-')
    | TextInput _ -> ""
    | _ -> ""

let onInput (nvim: Nvim) (input: IObservable<int*InputEvent*RoutedEventArgs>) =
  let key,mouse = input |> Observable.partition(function | _, InputEvent.Key _, _ | _, InputEvent.TextInput _, _ -> true | _ -> false)
  // translate to nvim input sequence
  let key = 
    key
    |> Observable.choose(fun (_, x, ev) ->
        match x with
        | TextInput txt -> 
          ev.Handled <- true
          if txt = "<" then Some "<LT>" else Some txt
        | InputEvent.Key _     -> 
          ev.Handled <- true
          let pref = ModifiersPrefix x
          match x,pref with
          | (Special sp), ""   -> Some($"<{sp}>")
          | (Special sp), pref -> Some($"<{pref}-{sp}>")
          | (Normal n), ""     -> Some n 
          | (Normal n), pref   -> Some($"<{pref}-{n}>")
          | x                  -> 
            #if DEBUG
            trace "rejected: %A" x
            #endif
            ev.Handled <- false
            None
        | _ -> None
        )

  let mouse =
    mouse
    |> Observable.choose(fun (gridid, x, ev) -> 
      ev.Handled <- true
      let pref = ModifiersPrefix x
      match x with
      |  MousePress(_, r, c, NvimSupportedMouseButton but)   -> Some(gridid, MB but, "press", r, c, 1, pref)
      |  MouseRelease(_, r, c, NvimSupportedMouseButton but) -> Some(gridid, MB but, "release", r, c, 1, pref)
      |  MouseDrag(_, r, c, NvimSupportedMouseButton but   ) -> Some(gridid, MB but, "drag", r, c, 1, pref)
      |  MouseWheel(_, r, c, dx, dy)                         -> 
           // filter bogus wheel events...
           if abs dx >= 10.0 || abs dy >= 10.0 then None
           else
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
               Some(gridid, "wheel", dir, r, c, int rpt, pref)
           else
               None
      | _ -> None)

  Disposables.compose [
      key |> Observable.subscribe(fun x -> 
      #if DEBUG
          trace "OnInput: key: %A" x
      #endif
          nvim.input x |> ignore
      )
      mouse |> Observable.subscribe(fun ((grid, but, act, r, c, rep, mods) as ev) -> 
      #if DEBUG
          trace "grid #%d: OnInput: mouse: %A" grid ev
      #endif
          for _ in 1..rep do
              nvim.input_mouse but act mods grid r c |> ignore
      )
  ]


module FVim.Model

open getopt
open log
open ui
open neovim.def
open neovim.rpc

open Avalonia.Diagnostics.ViewModels
open Avalonia.Media
open System
open System.Collections.Generic
open Avalonia.Threading
open Avalonia.Input
open Avalonia.Input.Raw
open FSharp.Control.Reactive

#nowarn "0058"

open FSharp.Control.Tasks.V2

[<AutoOpen>]
module ModelImpl =

    let nvim = Process()

    let ev_redraw     = Event<RedrawCommand[]>()

    // request handlers are explicitly registered, 1:1, with no broadcast.
    let requestHandlers      = Dictionary<string, obj[] -> Response Async>()
    // notification events are broadcasted to all subscribers.
    let notificationEvents   = Dictionary<string, Event<obj[]>>()
    let grids                = Dictionary<int, IGridUI>()

    let getNotificationEvent eventName =
        match notificationEvents.TryGetValue eventName with
        | true, ev -> ev
        | _ ->
        let ev = Event<obj[]>()
        notificationEvents.[eventName] <- ev
        ev

    let add_grid(grid: IGridUI) =
        grids.[grid.Id] <- grid

    let redraw cmd = 
        ev_redraw.Trigger cmd

    let msg_dispatch =
        function
        | Request(id, req, reply) -> 
            match requestHandlers.TryGetValue req.method with
            | true, method ->
                Async.Start(async { 
                    try
                        let! rsp = method(req.parameters)
                        do! reply id rsp
                    with
                    | Failure msg -> error "rpc" "request %d(%s) failed: %s" id req.method msg
                })
            | _ -> error "rpc" "request handler [%s] not found" req.method

        | Notification(req) ->
            let event = getNotificationEvent req.method
            try event.Trigger req.parameters
            with | Failure msg -> error "rpc" "notification trigger [%s] failed: %s" req.method msg
        | Redraw cmd -> redraw cmd 
        | Exit -> Avalonia.Application.Current.Shutdown()
        | _ -> ()

    let onGridResize(gridui: IGridUI) =
        trace "Model" "Grid #%d resized to %d %d" gridui.Id gridui.GridWidth gridui.GridHeight
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

    let (|HasFlag|_|) (flag: InputModifiers) (x: InputModifiers) =
        if x.HasFlag flag then Some() else None
    let (|NoFlag|_|) (flag: InputModifiers) (x: InputModifiers) =
        if x.HasFlag flag then None else Some()
    let MB (x: MouseButton, c: int) = 
        let name = x.ToString()
        if c = 1 then name
        else sprintf "%d-%s" c name
    let DIR (dx: int, dy: int) =
        match sign dx, sign dy with
        | -1, _  -> "Right"
        | 1, _   -> "Left"
        | _, -1  -> "Down"
        | _, 1   -> "Up"
        | _ -> ""
    let suffix (suf: string, r: int, c: int) =
        sprintf "%s><%d,%d" suf r c
    let (|Special|Normal|ImeEvent|TextInput|Unrecognized|) (x: InputEvent) =
        match x with
        | Key(_, Key.Back) 
        | Key(HasFlag(InputModifiers.Control), Key.H)                 -> Special "BS"
        | Key(_, Key.Tab) 
        | Key(HasFlag(InputModifiers.Control), Key.I)                 -> Special "Tab"
        | Key(_, Key.LineFeed)
        | Key(HasFlag(InputModifiers.Control), Key.J)                 -> Special "NL"
        (* 
        | Key(HasFlag(InputModifiers.Control), Key.L)                 -> Special "FF" 
          ^^^ Note: if ^L is sent as <FF> then neovim discards the key.
        *)
        | Key(_, Key.Return)
        | Key(HasFlag(InputModifiers.Control), Key.M)                 -> Special "CR"
        | Key(_, Key.Escape)
        | Key(HasFlag(InputModifiers.Control), Key.Oem4)              -> Special "Esc"
        | Key(_, Key.Space)                                           -> Special "Space"
        | Key(HasFlag(InputModifiers.Shift), Key.OemComma)            -> Special "LT"
        | Key(NoFlag(InputModifiers.Shift), Key.OemPipe)              -> Special "Bslash"
        | Key(HasFlag(InputModifiers.Shift), Key.OemPipe)             -> Special "Bar"
        | Key(_, Key.Delete)                                          -> Special "Del"
        | Key(HasFlag(InputModifiers.Alt), Key.Escape)                -> Special "xCSI"
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
          (Key.F1 | Key.F2 | Key.F3 | Key.F4 
        |  Key.F5 | Key.F6 | Key.F7 | Key.F8 
        |  Key.F9 | Key.F10 | Key.F11 | Key.F12))                     -> Special(x.ToString())
        | Key(NoFlag(InputModifiers.Shift), x &
          (Key.D0 | Key.D1 | Key.D2 | Key.D3 
        |  Key.D4 | Key.D5 | Key.D6 | Key.D7 
        |  Key.D8 | Key.D9))                                          -> Normal(x.ToString().TrimStart('D'))
        | Key(_, x &
          (Key.NumPad0 | Key.NumPad1 | Key.NumPad2 | Key.NumPad3 
        |  Key.NumPad4 | Key.NumPad5 | Key.NumPad6 | Key.NumPad7 
        |  Key.NumPad8 | Key.NumPad9))                                -> Special("k" + string(x.ToString() |> Seq.last))
        |  Key(NoFlag(InputModifiers.Shift), Key.OemComma)            -> Normal ","
        |  Key(NoFlag(InputModifiers.Shift), Key.OemPeriod)           -> Normal "."
        |  Key(HasFlag(InputModifiers.Shift), Key.OemPeriod)          -> Normal ">"
        |  Key(NoFlag(InputModifiers.Shift), Key.Oem2)                -> Normal "/"
        |  Key(HasFlag(InputModifiers.Shift), Key.Oem2)               -> Normal "?"
        |  Key(NoFlag(InputModifiers.Shift), Key.OemSemicolon)        -> Normal ";"
        |  Key(HasFlag(InputModifiers.Shift), Key.OemSemicolon)       -> Normal ":"
        |  Key(NoFlag(InputModifiers.Shift), Key.OemQuotes)           -> Normal "'"
        |  Key(HasFlag(InputModifiers.Shift), Key.OemQuotes)          -> Normal "\""
        |  Key(NoFlag(InputModifiers.Shift), Key.Oem4)                -> Normal "["
        |  Key(HasFlag(InputModifiers.Shift), Key.Oem4)               -> Normal "{"
        |  Key(NoFlag(InputModifiers.Shift), Key.OemCloseBrackets)    -> Normal "]"
        |  Key(HasFlag(InputModifiers.Shift), Key.OemCloseBrackets)   -> Normal "}"
        |  Key(NoFlag(InputModifiers.Shift), Key.OemMinus)            -> Normal "-"
        |  Key(HasFlag(InputModifiers.Shift), Key.OemMinus)           -> Normal "_"
        |  Key(NoFlag(InputModifiers.Shift), Key.OemPlus)             -> Normal "="
        |  Key(HasFlag(InputModifiers.Shift), Key.OemPlus)            -> Normal "+"
        |  Key(NoFlag(InputModifiers.Shift), Key.OemTilde)            -> Normal "`"
        |  Key(HasFlag(InputModifiers.Shift), Key.OemTilde)           -> Normal "~"
        |  Key(HasFlag(InputModifiers.Shift), Key.D1)                 -> Normal "!"
        |  Key(HasFlag(InputModifiers.Shift), Key.D2)                 -> Normal "@"
        |  Key(HasFlag(InputModifiers.Shift), Key.D3)                 -> Normal "#"
        |  Key(HasFlag(InputModifiers.Shift), Key.D4)                 -> Normal "$"
        |  Key(HasFlag(InputModifiers.Shift), Key.D5)                 -> Normal "%"
        |  Key(HasFlag(InputModifiers.Shift), Key.D6)                 -> Normal "^"
        |  Key(HasFlag(InputModifiers.Shift), Key.D7)                 -> Normal "&"
        |  Key(HasFlag(InputModifiers.Shift), Key.D8)                 -> Normal "*"
        |  Key(HasFlag(InputModifiers.Shift), Key.D9)                 -> Normal "("
        |  Key(HasFlag(InputModifiers.Shift), Key.D0)                 -> Normal ")"
        |  Key(_, (
           Key.ImeProcessed  | Key.ImeAccept | Key.ImeConvert
        |  Key.ImeNonConvert | Key.ImeModeChange))                    -> ImeEvent
        |  Key(NoFlag(InputModifiers.Shift), x)                       -> Normal (x.ToString().ToLowerInvariant())
        |  Key(_, x)                                                  -> Normal (x.ToString())
        |  MousePress(_, r, c, but, cnt)                              -> Special(MB(but, cnt) + suffix("Mouse", c, r))
        |  MouseRelease(_, r, c, but)                                 -> Special(MB(but, 1) + suffix("Release", c, r))
        |  MouseDrag(_, r, c, but   )                                 -> Special(MB(but, 1) + suffix("Drag", c, r))
        |  MouseWheel(_, r, c, dx, dy)                                -> Special("ScrollWheel" + suffix(DIR(dx, dy), c, r))
        |  TextInput txt                                              -> TextInput txt
        |  _                                                          -> Unrecognized
    //| Key.Oem
    let rec (|ModifiersPrefix|_|) (x: InputEvent) =
        match x with
        |  Key(m & HasFlag(InputModifiers.Shift), x &
          (Key.OemComma | Key.OemPipe | Key.OemPeriod | Key.Oem2 | Key.OemSemicolon | Key.OemQuotes
        |  Key.Oem4 | Key.OemCloseBrackets | Key.OemMinus | Key.OemPlus | Key.OemTilde
        |  Key.D0 | Key.D1 | Key.D2 | Key.D3 
        |  Key.D4 | Key.D5 | Key.D6 | Key.D7 
        |  Key.D8 | Key.D9)) -> 
            (|ModifiersPrefix|_|) <| InputEvent.Key(m &&& (~~~InputModifiers.Shift), x)
        | Key(m & HasFlag(InputModifiers.Control), x & (Key.H | Key.I | Key.J | Key.M)) ->
            (|ModifiersPrefix|_|) <| InputEvent.Key(m &&& (~~~InputModifiers.Control), x)
        | Key(m, _)
        | MousePress(m, _, _, _, _) 
        | MouseRelease(m, _, _, _) 
        | MouseDrag(m, _, _, _) 
        | MouseWheel(m, _, _, _, _) ->
            let c = if m.HasFlag(InputModifiers.Control) then "C-" else ""
            let a = if m.HasFlag(InputModifiers.Alt)     then "A-" else ""
            let d = if m.HasFlag(InputModifiers.Windows) then "D-" else ""
            let s = if m.HasFlag(InputModifiers.Shift)   then "S-" else ""
            match (sprintf "%s%s%s%s" c a d s).TrimEnd('-') with
            | "" -> None
            | x -> Some x
        | TextInput _ -> None
        | _ -> None

    let mutable _imeArmed = false

    let onInput: (IEvent<InputEvent> -> unit) =
        // filter out pure modifiers
        Observable.filter (fun x -> 
            match x with
            | InputEvent.Key(_, (Key.LeftCtrl | Key.LeftShift | Key.LeftAlt | Key.RightCtrl | Key.RightShift | Key.RightAlt | Key.LWin | Key.RWin))
                -> false
            | _ -> true) >>
        // translate to nvim keycode
        Observable.choose (fun x ->
            trace "Model" "OnInput: %A" x

            match x with
            | ImeEvent    -> _imeArmed <- true
            | TextInput _ -> ()
            | _           -> _imeArmed <- false
            // TODO anything that cancels ime input state?

            match x with
            | (Special sp) & (ModifiersPrefix pref) -> Some <| sprintf "<%s-%s>" pref sp
            | (Special sp)                          -> Some <| sprintf "<%s>" sp
            | (Normal n) & (ModifiersPrefix pref)   -> Some <| sprintf "<%s-%s>" pref n
            | (Normal n)                            -> Some <| n
            | ImeEvent                              -> None
            | TextInput txt when _imeArmed          -> Some txt
            | x                                     -> trace "input" "rejected: %A" x; None
        ) >>
        // hook up nvim_input
        // TODO dispose the subscription when grid is destroyed
        Observable.add (fun key ->
            ignore <| nvim.input [|key|]
        )

let Request name fn = requestHandlers.Add(name, fn)
let Notify name (fn: obj[] -> unit) = 
    (getNotificationEvent name).Publish.Subscribe(fun objs -> 
        try fn objs
        with | x -> error "Notify" "exception thrown: %A" <| x.ToString())
let Redraw (fn: RedrawCommand[] -> unit) = ev_redraw.Publish |> Observable.subscribe(fn)

/// <summary>
/// Call this once at initialization.
/// </summary>
let Start opts =

    trace "Model" "starting neovim instance..."
    trace "Model" "opts = %A" opts
    nvim.start opts
    ignore <|
    nvim.subscribe 
        (AvaloniaSynchronizationContext.Current) 
        (msg_dispatch)

    trace "Model" "commencing early initialization..."
    task {
        let! _ = nvim.set_var "fvim_loaded" 1
        let! _ = nvim.set_var "gui_running" 1
        ()
    } |> ignore

    [
        Notify "font.antialias"   (fun [| Bool(v) |] -> ui.antialiased <- v)
        Notify "font.bounds"      (fun [| Bool(v) |] -> ui.drawBounds <- v)
        Notify "font.autohint"    (fun [| Bool(v) |] -> ui.autohint <- v)
        Notify "font.subpixel"    (fun [| Bool(v) |] -> ui.subpixel <- v)
        Notify "font.lcdrender"   (fun [| Bool(v) |] -> ui.lcdrender <- v)
        Notify "font.hintLevel"   (fun [| String(v) |] -> ui.setHintLevel v)
    ] |> List.iter ignore

let OnGridReady(gridui: IGridUI) =
    // connect the redraw commands
    gridui.Resized 
    |> Observable.throttle (TimeSpan.FromMilliseconds 20.0)
    |> Observable.add onGridResize

    add_grid gridui

    gridui.Input |> onInput

    if gridui.Id = 1 then
        // Grid #1 is the main grid.
        // When ready, the UI should be ready for events now. 
        // Notify nvim about its presence
        trace "Model" 
              "attaching to nvim on first grid ready signal. size = %A %A" 
              gridui.GridWidth gridui.GridHeight
        task {
            let! _ = nvim.ui_attach gridui.GridWidth gridui.GridHeight
            let! _ = nvim.command "runtime! ginit.vim"
            ()
        } |> ignore

let OnTerminated (args) =
    trace "Model" "terminating nvim..."
    nvim.stop 1

let OnTerminating(args) =
    //TODO send closing request to neovim
    ()

let EditFiles (files: string seq) =
    task {
        for file in files do
            let! _ = nvim.command <| "edit " + file
            ()
    } |> ignore

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

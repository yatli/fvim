module FVim.Model

open getopt
open log
open ui
open common
open neovim.def
open neovim.proc

open Avalonia.Diagnostics.ViewModels
open Avalonia.Media
open System
open System.Collections.Generic
open Avalonia.Threading
open Avalonia.Input
open Avalonia.Input.Raw
open FSharp.Control.Reactive
open System.ComponentModel
open SkiaSharp

#nowarn "0058"

open FSharp.Control.Tasks.V2

let inline private trace x = trace "neovim.model" x

[<AutoOpen>]
module ModelImpl =

    let nvim = Nvim()
    let ev_redraw     = Event<RedrawCommand[]>()
    let grids                = hashmap[]

    let add_grid(grid: IGridUI) =
        grids.[grid.Id] <- grid

    let redraw cmd = 
        ev_redraw.Trigger cmd

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

    let (|HasFlag|_|) (flag: InputModifiers) (x: InputModifiers) =
        if x.HasFlag flag then Some() else None
    let (|NoFlag|_|) (flag: InputModifiers) (x: InputModifiers) =
        if x.HasFlag flag then None else Some()
    let MB (x: MouseButton, c: int) = 
        let name = x.ToString()
        if c = 1 then name
        else sprintf "%d-%s" c name

    let mutable accumulatedX = 0.0
    let mutable accumulatedY = 0.0

    let DIR (dx: float, dy: float, horizontal: bool) =
        match sign dx, sign dy, horizontal with
        | -1, _, true   -> "Right"
        | _, _,  true   -> "Left"
        | _, -1, false  -> "Down"
        | _, _, false   -> "Up"
    let suffix (suf: string, r: int, c: int) =
        sprintf "%s><%d,%d" suf r c
    let (|Repeat|Special|Normal|ImeEvent|TextInput|Unrecognized|) (x: InputEvent) =
        match x with
        // | Key(HasFlag(InputModifiers.Control), Key.H)                 
        // | Key(HasFlag(InputModifiers.Control), Key.J)                 
        // | Key(HasFlag(InputModifiers.Control), Key.I)                 
        // | Key(HasFlag(InputModifiers.Control), Key.M)                 
        // | Key(HasFlag(InputModifiers.Control), Key.Oem4) // Oem4 is '['
        // | Key(HasFlag(InputModifiers.Control), Key.L) // if ^L is sent as <FF> then neovim discards the key.
        | Key(_, Key.Back)                                            -> Special "BS"
        | Key(_, Key.Tab)                                             -> Special "Tab"
        | Key(_, Key.LineFeed)                                        -> Special "NL"
        | Key(_, Key.Return)                                          -> Special "CR"
        | Key(_, Key.Escape)                                          -> Special "Esc"
        | Key(_, Key.Space)                                           -> Special "Space"
        | Key(HasFlag(InputModifiers.Shift), Key.OemComma)            -> Special "LT"
        // note, on Windows '\' is recognized as OemPipe but on macOS it's OemBackslash
        | Key(NoFlag(InputModifiers.Shift), 
             (Key.OemPipe | Key.OemBackslash))                        -> Special "Bslash"
        | Key(HasFlag(InputModifiers.Shift), 
             (Key.OemPipe | Key.OemBackslash))                        -> Special "Bar"
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
        |  Key(_, Key.None)                                           -> Unrecognized
        |  Key(_, x)                                                  -> Normal (x.ToString())
        |  MousePress(_, r, c, but, cnt)                              -> Special(MB(but, cnt) + suffix("Mouse", c, r))
        |  MouseRelease(_, r, c, but)                                 -> Special(MB(but, 1) + suffix("Release", c, r))
        |  MouseDrag(_, r, c, but   )                                 -> Special(MB(but, 1) + suffix("Drag", c, r))
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
                Repeat(int rpt, "ScrollWheel" + suffix(dir, c, r))
            else
                Unrecognized
        |  TextInput txt                                              -> TextInput txt
        |  _                                                          -> Unrecognized
    //| Key.Oem
    let rec (|ModifiersPrefix|_|) (x: InputEvent) =
        match x with
        // -------------- keys with special form do not carry shift modifiers
        |  Key(m & HasFlag(InputModifiers.Shift), x &
          (Key.OemComma | Key.OemPipe | Key.OemBackslash | Key.OemPeriod | Key.Oem2 | Key.OemSemicolon | Key.OemQuotes
        |  Key.Oem4 | Key.OemCloseBrackets | Key.OemMinus | Key.OemPlus | Key.OemTilde
        |  Key.D0 | Key.D1 | Key.D2 | Key.D3 
        |  Key.D4 | Key.D5 | Key.D6 | Key.D7 
        |  Key.D8 | Key.D9)) 
            -> (|ModifiersPrefix|_|) <| InputEvent.Key(m &&& (~~~InputModifiers.Shift), x)
        // -------------- C-x special forms do not carry control modifiers
        | Key(m & HasFlag(InputModifiers.Control), x & (Key.H | Key.I | Key.J | Key.M)) 
            -> (|ModifiersPrefix|_|) <| InputEvent.Key(m &&& (~~~InputModifiers.Control), x)
        | Key(m, _)
        | MousePress(m, _, _, _, _) 
        | MouseRelease(m, _, _, _) 
        | MouseDrag(m, _, _, _) 
        | MouseWheel(m, _, _, _, _) 
            ->
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

    let onInput: (IObservable<int*InputEvent> -> IDisposable) =
        // filter out pure modifiers
        Observable.filter (fun (_, x) -> 
            match x with
            | InputEvent.Key(_, (Key.LeftCtrl | Key.LeftShift | Key.LeftAlt | Key.RightCtrl | Key.RightShift | Key.RightAlt | Key.LWin | Key.RWin))
                -> false
            | _ -> true) >>
        // translate to nvim keycode
        Observable.map(fun (gridid, x) ->
            trace "grid #%d: OnInput: %A" gridid x

            match x with
            | ImeEvent    -> _imeArmed <- true
            | TextInput _ -> ()
            | _           -> _imeArmed <- false
            // TODO anything that cancels ime input state?

            match x with
            | (Repeat(n, sp)) & (ModifiersPrefix pref) -> List.replicate n (sprintf "<%s-%s>" pref sp)
            | (Special sp) & (ModifiersPrefix pref) -> [ sprintf "<%s-%s>" pref sp ]
            | (Special sp)                          -> [ sprintf "<%s>" sp ]
            | (Repeat(n, sp))                       -> List.replicate n (sprintf "<%s>" sp)
            | (Normal n) & (ModifiersPrefix pref)   -> [ sprintf "<%s-%s>" pref n ]
            | (Normal n)                            -> [ n ]
            | ImeEvent                              -> []
            | TextInput txt when _imeArmed          -> [ txt ]
            | x                                     -> trace "rejected: %A" x; []
        ) >>
        // hook up nvim_input
        // TODO dispose the subscription when nvim is stopped
        Observable.subscribe (fun keys ->
            for k in keys do
                ignore <| nvim.input [|k|]
        )

let Redraw (fn: RedrawCommand[] -> unit) = ev_redraw.Publish |> Observable.subscribe(fn)

let Detach() =
    nvim.stop(0)
    States.Shutdown(0)

/// <summary>
/// Call this once at initialization.
/// </summary>
let Start opts =
    trace "starting neovim instance..."
    trace "opts = %A" opts
    nvim.start opts
    nvim.subscribe 
        (AvaloniaSynchronizationContext.Current) 
        (States.msg_dispatch)

    ignore(States.Register.Notify "redraw" (fun cmds -> ev_redraw.Trigger (Array.map parse_redrawcmd cmds)))

    // rpc handlers
    States.Register.Bool "font.autosnap"
    States.Register.Bool "font.antialias"
    States.Register.Bool "font.drawBounds"
    States.Register.Bool "font.autohint"
    States.Register.Bool "font.subpixel"
    States.Register.Bool "font.lcdrender"
    States.Register.Prop<SKPaintHinting> States.parseHintLevel "font.hintLevel"
    States.Register.Prop<SKFontStyleWeight> States.parseFontWeight "font.weight.normal"
    States.Register.Prop<SKFontStyleWeight> States.parseFontWeight "font.weight.bold"
    States.Register.Prop<States.LineHeightOption> States.parseLineHeightOption "font.lineheight"
    States.Register.Bool "cursor.smoothblink"
    States.Register.Bool "cursor.smoothmove"
    ignore(States.Register.Notify "remote.detach" (fun _ -> Detach()))

    trace "commencing early initialization..."
    async {
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
                "minor", "1"
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

        // Another instance is already up
        if fvimChannels.Length > 1 then
            Environment.Exit(0)

        let! _ = Async.AwaitTask(nvim.set_var "fvim_channel" myChannel)

        let! _ = Async.AwaitTask(nvim.``command!`` "FVimToggleFullScreen" 0 (sprintf "call rpcnotify(%d, 'ToggleFullScreen', 1)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimCursorSmoothMove" 1 (sprintf "call rpcnotify(%d, 'cursor.smoothmove', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimCursorSmoothBlink" 1 (sprintf "call rpcnotify(%d, 'cursor.smoothblink', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimDrawFPS" 1 (sprintf "call rpcnotify(%d, 'DrawFPS', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "FVimDetach" 0 (sprintf "call rpcnotify(%d, 'remote.detach')" myChannel))

        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontLineHeight" 1 (sprintf "call rpcnotify(%d, 'font.lineheight', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontAutoSnap" 1 (sprintf "call rpcnotify(%d, 'font.autosnap', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontAntialias" 1 (sprintf "call rpcnotify(%d, 'font.antialias', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontDrawBounds" 1 (sprintf "call rpcnotify(%d, 'font.drawBounds', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontAutohint" 1 (sprintf "call rpcnotify(%d, 'font.autohint', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontSubpixel" 1 (sprintf "call rpcnotify(%d, 'font.subpixel', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontLcdRender" 1 (sprintf "call rpcnotify(%d, 'font.lcdrender', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontHintLevel" 1 (sprintf "call rpcnotify(%d, 'font.hindLevel', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontNormalWeight" 1 (sprintf "call rpcnotify(%d, 'font.weight.normal', <args>)" myChannel))
        let! _ = Async.AwaitTask(nvim.``command!`` "-complete=expression FVimFontBoldWeight" 1 (sprintf "call rpcnotify(%d, 'font.weight.bold', <args>)" myChannel))

        ()
    }

    
let OnGridReady(gridui: IGridUI) =
    // connect the redraw commands
    gridui.Resized 
    |> Observable.throttle (TimeSpan.FromMilliseconds 20.0)
    |> Observable.add onGridResize

    add_grid gridui

    gridui.Input 
    |> Observable.filter (fun _ -> not gridui.HasChildren)
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
            // TODO ideally this should be triggered in `nvim_command("autocmd VimEnter * call rpcrequest(1, 'vimenter')")`
            // as per :help ui-start
            let! _ = nvim.command "runtime! ginit.vim"
            in ()
        } |> ignore

let OnTerminated (args) =
    trace "terminating nvim..."
    nvim.stop 1

let OnTerminating(args: CancelEventArgs) =
    args.Cancel <- true
    trace "window is closing"
    task {
        if nvim.isRemote then
            Detach()
        else
            let! _ = nvim.quitall()
            ()
    } |> ignore
    ()

let EditFiles (files: string seq) =
    task {
        for file in files do
            let! _ = nvim.edit file
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

namespace FVim

open log
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

type FVimViewModel() =
    inherit ViewModelBase()
    let redraw = Event<RedrawCommand[]>()
    let nvim = Process()
    let requestHandlers      = Dictionary<string, obj[] -> Response Async>()
    let notificationHandlers = Dictionary<string, obj[] -> unit Async>()

    let request  name fn = requestHandlers.Add(name, fn)
    let notify   name fn = notificationHandlers.Add(name, fn)

    let msg_dispatch =
        function
        | Request(id, req, reply) -> 
           Async.Start(async { 
               let! rsp = requestHandlers.[req.method](req.parameters)
               do! reply id rsp
           })
        | Notification req -> 
           Async.Start(notificationHandlers.[req.method](req.parameters))
        | Redraw cmd -> redraw.Trigger cmd
        | Exit -> Avalonia.Application.Current.Exit()
        | _ -> ()

    do
        trace "ViewModel" "starting neovim instance..."
        nvim.start()
        ignore <|
        nvim.subscribe 
            (AvaloniaSynchronizationContext.Current) 
            (msg_dispatch)

        trace "ViewModel" "registering msgpack-rpc handlers..."


    member val WindowWidth:  int         = 824 with get,set
    member val WindowHeight: int         = 721 with get,set

    member this.RedrawCommands = redraw.Publish
    member this.Redraw(cmds: RedrawCommand[]) = redraw.Trigger cmds

    member this.OnTerminated (args) =
        trace "ViewModel" "terminating nvim..."
        nvim.stop 1

    member this.OnTerminating(args) =
        //TODO send closing request to neovim
        ()

    member this.OnGridResize(gridui: IGridUI) =
        trace "ViewModel" "Grid #%d resized to %d %d" gridui.Id gridui.GridWidth gridui.GridHeight
        ignore <| nvim.grid_resize gridui.Id gridui.GridWidth gridui.GridHeight

    member this.OnKeyInput: (IEvent<KeyEventArgs> -> unit) =
        // filter out pure modifiers
        Observable.filter (fun x -> 
            match x.Key with
            | Key.LeftCtrl | Key.LeftShift | Key.LeftAlt | Key.RightCtrl | Key.RightShift | Key.RightAlt | Key.LWin | Key.RWin
                -> false
            | _ -> true) >>
        // translate to nvim keycode
        //notation	meaning		    equivalent	decimal value(s)	~
        //-----------------------------------------------------------------------
        //<Nul>		zero			CTRL-@	  0 (stored as 10) *<Nul>*
        //<BS>		backspace		CTRL-H	  8	*backspace*
        //<Tab>		tab			CTRL-I	  9	*tab* *Tab*
        //							*linefeed*
        //<NL>		linefeed		CTRL-J	 10 (used for <Nul>)
        //<FF>		formfeed		CTRL-L	 12	*formfeed*
        //<CR>		carriage return		CTRL-M	 13	*carriage-return*
        //<Return>	same as <CR>				*<Return>*
        //<Enter>		same as <CR>				*<Enter>*
        //<Esc>		escape			CTRL-[	 27	*escape* *<Esc>*
        //<Space>		space				 32	*space*
        //<lt>		less-than		<	 60	*<lt>*
        //<Bslash>	backslash		\	 92	*backslash* *<Bslash>*
        //<Bar>		vertical bar		|	124	*<Bar>*
        //<Del>		delete				127
        //<CSI>		command sequence intro  ALT-Esc 155	*<CSI>*
        //<xCSI>		CSI when typed in the GUI		*<xCSI>*
        //<EOL>		end-of-line (can be <CR>, <LF> or <CR><LF>,
        //		depends on system and 'fileformat')	*<EOL>*
        //<Up>		cursor-up			*cursor-up* *cursor_up*
        //<Down>		cursor-down			*cursor-down* *cursor_down*
        //<Left>		cursor-left			*cursor-left* *cursor_left*
        //<Right>		cursor-right			*cursor-right* *cursor_right*
        //<S-Up>		shift-cursor-up
        //<S-Down>	shift-cursor-down
        //<S-Left>	shift-cursor-left
        //<S-Right>	shift-cursor-right
        //<C-Left>	control-cursor-left
        //<C-Right>	control-cursor-right
        //<F1> - <F12>	function keys 1 to 12		*function_key* *function-key*
        //<S-F1> - <S-F12> shift-function keys 1 to 12	*<S-F1>*
        //<Help>		help key
        //<Undo>		undo key
        //<Insert>	insert key
        //<Home>		home				*home*
        //<End>		end				*end*
        //<PageUp>	page-up				*page_up* *page-up*
        //<PageDown>	page-down			*page_down* *page-down*
        //<kHome>		keypad home (upper left)	*keypad-home*
        //<kEnd>		keypad end (lower left)		*keypad-end*
        //<kPageUp>	keypad page-up (upper right)	*keypad-page-up*
        //<kPageDown>	keypad page-down (lower right)	*keypad-page-down*
        //<kPlus>		keypad +			*keypad-plus*
        //<kMinus>	keypad -			*keypad-minus*
        //<kMultiply>	keypad *			*keypad-multiply*
        //<kDivide>	keypad /			*keypad-divide*
        //<kEnter>	keypad Enter			*keypad-enter*
        //<kPoint>	keypad Decimal point		*keypad-point*
        //<k0> - <k9>	keypad 0 to 9			*keypad-0* *keypad-9*
        //<S-...>		shift-key			*shift* *<S-*
        //<C-...>		control-key			*control* *ctrl* *<C-*
        //<M-...>		alt-key or meta-key		*META* *ALT* *<M-*
        //<A-...>		same as <M-...>			*<A-*
        //<D-...>		command-key or "super" key	*<D-*
        Observable.map (fun x ->
            let (|HasFlag|_|) (flag: InputModifiers) (x: InputModifiers) =
                if x.HasFlag flag then Some() else None
            let (|NoFlag|_|) (flag: InputModifiers) (x: InputModifiers) =
                if x.HasFlag flag then None else Some()
            let (|Special|Normal|) (x: KeyEventArgs) =
                match x.Key, x.Modifiers with
                |  Key.Back, _ 
                |  Key.H, HasFlag(InputModifiers.Control)         -> Special "BS"
                |  Key.Tab, _ 
                |  Key.I, HasFlag(InputModifiers.Control)         -> Special "Tab"
                |  Key.LineFeed, _
                |  Key.J, HasFlag(InputModifiers.Control)         -> Special "NL"
                |  Key.L, HasFlag(InputModifiers.Control)         -> Special "FF"
                |  Key.Return, _ 
                |  Key.M, HasFlag(InputModifiers.Control)         -> Special "CR"
                |  Key.Escape, _ 
                |  Key.Oem4, HasFlag(InputModifiers.Control)      -> Special "Esc"
                |  Key.Space, _                                   -> Special "Space"
                |  Key.OemComma, HasFlag(InputModifiers.Shift)    -> Special "LT"
                |  Key.OemPipe, NoFlag(InputModifiers.Shift)      -> Special "Bslash"
                |  Key.OemPipe, HasFlag(InputModifiers.Shift)     -> Special "Bar"
                |  Key.Delete, _                                  -> Special "Del"
                |  Key.Escape, HasFlag(InputModifiers.Alt)        -> Special "xCSI"
                |  Key.Up, _                                      -> Special "Up"
                |  Key.Down, _                                    -> Special "Down"
                |  Key.Left, _                                    -> Special "Left"
                |  Key.Right, _                                   -> Special "Right"
                | (Key.F1 | Key.F2 | Key.F3 | Key.F4 
                |  Key.F5 | Key.F6 | Key.F7 | Key.F8 
                |  Key.F9 | Key.F10 | Key.F11 | Key.F12), _       -> Special(x.Key.ToString())
                |  Key.Help, _                                    -> Special "Help"
                |  Key.Insert, _                                  -> Special "Insert"
                |  Key.Home, _                                    -> Special "Home"
                |  Key.End, _                                     -> Special "End"
                |  Key.PageUp, _                                  -> Special "PageUp"
                //| Key.NumPadHome, _                             -> Special "kHome"
                //| Key.NumPadEnd, _                              -> Special "kEnd"
                //| Key.NumPadPageUp, _                           -> Special "kPageUp"
                //| Key.NumPadPageDown, _                         -> Special "kPageDown"
                | (Key.D0 | Key.D1 | Key.D2 | Key.D3 
                |  Key.D4 | Key.D5 | Key.D6 | Key.D7 
                |  Key.D8 | Key.D9), NoFlag(InputModifiers.Shift) -> Normal(x.Key.ToString().TrimStart('D'))
                | (Key.NumPad0 | Key.NumPad1 | Key.NumPad2 | Key.NumPad3 
                |  Key.NumPad4 | Key.NumPad5 | Key.NumPad6 | Key.NumPad7 
                |  Key.NumPad8 | Key.NumPad9), _                  -> Special("k" + string(x.Key.ToString() |> Seq.last))
                |  Key.OemComma, NoFlag(InputModifiers.Shift)     -> Normal ","
                |  Key.OemPeriod, NoFlag(InputModifiers.Shift)    -> Normal "."
                |  Key.OemPeriod, HasFlag(InputModifiers.Shift)   -> Normal ">"
                |  Key.Oem2, NoFlag(InputModifiers.Shift)         -> Normal "/"
                |  Key.Oem2, HasFlag(InputModifiers.Shift)        -> Normal "?"
                |  Key.OemSemicolon, NoFlag(InputModifiers.Shift)         -> Normal ";"
                |  Key.OemSemicolon, HasFlag(InputModifiers.Shift)        -> Normal ":"
                |  Key.OemQuotes, NoFlag(InputModifiers.Shift)         -> Normal "'"
                |  Key.OemQuotes, HasFlag(InputModifiers.Shift)        -> Normal "\""
                |  Key.Oem4, NoFlag(InputModifiers.Shift)         -> Normal "["
                |  Key.Oem4, HasFlag(InputModifiers.Shift)        -> Normal "{"
                |  Key.OemCloseBrackets, NoFlag(InputModifiers.Shift)   -> Normal "]"
                |  Key.OemCloseBrackets, HasFlag(InputModifiers.Shift)  -> Normal "}"
                |  Key.OemMinus, NoFlag(InputModifiers.Shift)   -> Normal "-"
                |  Key.OemMinus, HasFlag(InputModifiers.Shift)  -> Normal "_"
                |  Key.OemPlus, NoFlag(InputModifiers.Shift)   -> Normal "="
                |  Key.OemPlus, HasFlag(InputModifiers.Shift)  -> Normal "+"
                |  Key.OemTilde, NoFlag(InputModifiers.Shift)   -> Normal "`"
                |  Key.OemTilde, HasFlag(InputModifiers.Shift)  -> Normal "~"
                |  Key.D1, HasFlag(InputModifiers.Shift) -> Normal "!"
                |  Key.D2, HasFlag(InputModifiers.Shift) -> Normal "@"
                |  Key.D3, HasFlag(InputModifiers.Shift) -> Normal "#"
                |  Key.D4, HasFlag(InputModifiers.Shift) -> Normal "$"
                |  Key.D5, HasFlag(InputModifiers.Shift) -> Normal "%"
                |  Key.D6, HasFlag(InputModifiers.Shift) -> Normal "^"
                |  Key.D7, HasFlag(InputModifiers.Shift) -> Normal "&"
                |  Key.D8, HasFlag(InputModifiers.Shift) -> Normal "*"
                |  Key.D9, HasFlag(InputModifiers.Shift) -> Normal "("
                |  Key.D0, HasFlag(InputModifiers.Shift) -> Normal ")"
                |  _, NoFlag(InputModifiers.Shift)                -> Normal (x.Key.ToString().ToLowerInvariant())
                |  _                                              -> Normal (x.Key.ToString())
                //| Key.Oem
            let rec (|ModifiersPrefix|_|) (x: KeyEventArgs) =
                match x.Key, x.Modifiers with
                | (Key.OemComma | Key.OemPipe | Key.OemPeriod | Key.Oem2 | Key.OemSemicolon | Key.OemQuotes
                |  Key.Oem4 | Key.OemCloseBrackets | Key.OemMinus | Key.OemPlus | Key.OemTilde
                |  Key.D0 | Key.D1 | Key.D2 | Key.D3 
                |  Key.D4 | Key.D5 | Key.D6 | Key.D7 
                |  Key.D8 | Key.D9), m & HasFlag(InputModifiers.Shift) -> 
                    let x' = KeyEventArgs()
                    x'.Key <- x.Key
                    x'.Modifiers <- m &&& (~~~InputModifiers.Shift)
                    (|ModifiersPrefix|_|) x'
                | (Key.H | Key.I | Key.J | Key.L | Key.M), m & HasFlag(InputModifiers.Control) ->
                    let x' = KeyEventArgs()
                    x'.Key <- x.Key
                    x'.Modifiers <- m &&& (~~~InputModifiers.Control)
                    (|ModifiersPrefix|_|) x'
                | _, InputModifiers.None -> None
                | _, m ->
                    let c = if m.HasFlag(InputModifiers.Control) then "C-" else ""
                    let a = if m.HasFlag(InputModifiers.Alt)     then "A-" else ""
                    let d = if m.HasFlag(InputModifiers.Windows) then "D-" else ""
                    let s = if m.HasFlag(InputModifiers.Shift)   then "S-" else ""
                    Some <| (sprintf "%s%s%s%s" c a d s).TrimEnd('-')
                | _ -> None

            match x with
            | (Special sp) & (ModifiersPrefix pref) -> sprintf "<%s-%s>" pref sp
            | (Special sp) -> sprintf "<%s>" sp
            | (Normal n) & (ModifiersPrefix pref) -> sprintf "<%s-%s>" pref n
            | (Normal n) -> sprintf "%s" n
            | _ -> sprintf "[? %A-%A]" x.Key x.Modifiers
        ) >>
        // hook up nvim_input
        Observable.add (fun key ->
            trace "ViewModel" "OnInput: %A" key
            ignore <| nvim.input [|key|]
        )

    member this.OnGridReady(gridui: IGridUI) =
        // connect the redraw commands
        gridui.Connect redraw.Publish
        gridui.Resized 
        |> Observable.throttle (TimeSpan.FromMilliseconds 100.0)
        |> Observable.add this.OnGridResize

        gridui.KeyInput |> this.OnKeyInput

        // the UI should be ready for events now. notify nvim about its presence
        if gridui.Id = 1 then
            trace "ViewModel" "attaching to nvim on first grid ready signal. size = %A %A" gridui.GridWidth gridui.GridHeight
            ignore <| nvim.ui_attach gridui.GridWidth gridui.GridHeight
        else
            failwithf "grid: unsupported: %A" gridui.Id


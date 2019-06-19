module FVim.neovim.rpc

open FVim.neovim.def
open FVim.log
open FVim.getopt

open System
open System.Diagnostics
open System.Text
open System.Collections.Concurrent
open MessagePack
open System.Collections.Generic
open System.Threading.Tasks
open System.Threading
open FSharp.Control.Reactive
open FSharp.Control.Tasks.V2.ContextSensitive
open Avalonia.Media

let private trace fmt = trace "neovim" fmt

type EventParseException(data: obj) =
    inherit exn()
    member __.Input = data
    override __.Message = sprintf "Could not parse the neovim message: %A" data

let private default_notify (e: Request) =
    failwithf "%A" e

let private default_call (e: Request) =
    failwithf "%A" e

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

let private (|AmbiWidth|_|) (x: obj) =
    match x with
    | String "single" -> Some AmbiWidth.Single
    | String "double" -> Some AmbiWidth.Double
    | _ -> None

let private (|ShowTabline|_|) (x: obj) =
    match x with
    | Integer32 0 -> Some ShowTabline.Never
    | Integer32 1 -> Some ShowTabline.AtLeastTwo
    | Integer32 2 -> Some ShowTabline.Always
    | _ -> None

let private (|Color|_|) (x: obj) =
    match x with
    | Integer32 x -> 
        // fill in the alpha channel
        Some <| Color.FromUInt32((uint32 x) ||| 0xFF000000u)
    | _ -> None

let private (|CursorShape|_|) (x: obj) =
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

let private _get (map: Dictionary<obj, obj>) (key: string) (fn: obj -> 'a option) =
    let (|OK_FN|_|) = fn
    match map.TryGetValue key with
    | true, OK_FN x -> Some x
    | _ -> None
let private _getd (map: Dictionary<obj, obj>) (key: string) (fn: obj -> 'a option) d =
    let (|OK_FN|_|) = fn
    match map.TryGetValue key with
    | true, OK_FN x -> x
    | _ -> d

let private (|HighlightAttr|_|) (x: obj) =
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

let private parse_uioption (x: obj) =
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

let private parse_default_colors (x: obj) =
    match x with
    | ObjArray [| (Color fg); (Color bg); (Color sp); (Color cfg); (Color cbg) |] -> 
        Some <| DefaultColorsSet(fg,bg,sp,cfg,cbg)
    | _ -> None

let private parse_mode_info (x: obj) =
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

let private parse_hi_attr (x: obj) =
    match x with
    | ObjArray [| (Integer32 id); (HighlightAttr rgb); (HighlightAttr cterm); (ObjArray info) |] 
        -> Some {id = id; rgb_attr = rgb; cterm_attr = cterm; info = info }
    | _ -> None

let private parse_grid_cell (x: obj) =
    match x with
    | ObjArray [| (String txt) |] 
        -> Some { text = txt; hl_id = None; repeat = None}
    | ObjArray [| (String txt); (Integer32 hl_id) |] 
        -> Some { text = txt; hl_id = Some hl_id; repeat = None}
    | ObjArray [| (String txt); (Integer32 hl_id); (Integer32 repeat) |] 
        -> Some { text = txt; hl_id = Some hl_id; repeat = Some repeat}
    | _ -> None

let private parse_grid_line (x: obj) =
    match x with
    | ObjArray [| (Integer32 grid); (Integer32 row) ; (Integer32 col_start) ; P(parse_grid_cell)cells |] 
        -> Some {grid = grid; row=row; col_start=col_start; cells=cells}
    | _ -> None

let private parse_redrawcmd (x: obj) =
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

type Process() = 
    let m_id = Guid.NewGuid()
    let mutable m_notify = default_notify
    let mutable m_call   = default_call
    let mutable m_proc   = None
    let mutable m_events = None

    member private this.events =
        match m_events with
        | Some events -> events
        | None -> failwith "events"

    member private this.proc =
        match m_proc with
        | Some proc -> proc
        | None -> failwith "process"


    member __.start { args = args; program = prog; preArgs = preargs; stderrenc = enc } =
        match m_proc, m_events with
        | Some(_) , _
        | _, Some(_) -> failwith "neovim: already started"
        | _ -> ()

        let args = "--embed" :: args 
        let psi  = ProcessStartInfo(prog, String.Join(" ", preargs @ args))
        psi.CreateNoWindow          <- true
        psi.ErrorDialog             <- false
        psi.RedirectStandardError   <- true
        psi.RedirectStandardInput   <- true
        psi.RedirectStandardOutput  <- true
        psi.StandardErrorEncoding   <- enc
        psi.UseShellExecute         <- false
        psi.WindowStyle             <- ProcessWindowStyle.Hidden
        psi.WorkingDirectory        <- Environment.CurrentDirectory

        let proc   = Process.Start(psi)
        let stdout = proc.StandardOutput.BaseStream
        let stdin  = proc.StandardInput.BaseStream

        let read (ob: IObserver<obj>) (cancel: CancellationToken) = 
            Task.Factory.StartNew(fun () -> 
                 trace "READ!"
                 while not proc.HasExited && not cancel.IsCancellationRequested do
                    try
                        let data = MessagePackSerializer.Deserialize<obj>(stdout, true)
                        (*trace "stdout message: %A" data*)
                        ob.OnNext(data)
                    with :? InvalidOperationException as ex ->
                        ob.OnCompleted()
                 trace "READ COMPLETE!"
                 ob.OnCompleted()
            , cancel, TaskCreationOptions.LongRunning, TaskScheduler.Current)

        let reply (id: int) (rsp: Response) = async {
            let result, error = 
                match rsp.result with
                | Choice1Of2 r -> r, null
                | Choice2Of2 e -> null, e
            do! Async.AwaitTask(MessagePackSerializer.SerializeAsync(stdin, mkparams4 1 id result error))
            do! Async.AwaitTask(stdin.FlushAsync())
        }

        let pending = ConcurrentDictionary<int, TaskCompletionSource<Response>>()
        let parse (data: obj) : Event =
            match data :?> obj[] with
            // request
            | [| (Integer32 0); (Integer32 msg_id) ; (String method); :? (obj[]) as parameters |] 
                -> Request(msg_id, { method = method; parameters = parameters }, reply)
            // response
            | [| (Integer32 1); (Integer32 msg_id) ; err; result |]
                -> Response(msg_id, { result = if err = null then Choice1Of2 result else Choice2Of2 err })
            // redraw
            | [| (Integer32 2); (String "redraw"); :? (obj[]) as cmds |] 
                -> Redraw (Array.map parse_redrawcmd cmds)
            // notification
            | [| (Integer32 2); (String method); :? (obj[]) as parameters |]
                -> Notification { method = method; parameters = parameters }
            | _ -> raise <| EventParseException(data)

        let intercept (ev: Event) =
            match ev with
            | Response(msgid, rsp) ->
                // intercept response message, if it can be completed successfully
                match pending.TryRemove msgid with
                | true, src -> src.TrySetResult rsp |> not
                | _ -> false
            | _ -> true

        let rec _startRead() =
            System.Reactive.Linq.Observable.Create(read)
            |> Observable.map       parse
            |> Observable.catchWith exhandler

        and exhandler (ex: exn) = 
            trace "exhandler: %A" ex
            _startRead()

        let stdout = 
            _startRead()
            |> Observable.filter    intercept
            |> Observable.concat    (Observable.single Exit)
        let stderr = 
            proc.ErrorDataReceived 
            |> Observable.map (fun data -> Error data.Data )
        let events = Observable.merge stdout stderr
        

        let notify (ev: Request) = task {
            let payload = mkparams3 2 ev.method ev.parameters
            // MessagePackSerializer.ToJson(payload) |> trace "notify: %s"
            do! MessagePackSerializer.SerializeAsync(stdin, payload)
            do! stdin.FlushAsync()
        }

        let mutable call_id = 0
        let call (ev: Request) = task {
            let myid = call_id
            call_id <- call_id + 1
            let src = TaskCompletionSource<Response>()
            if not <| pending.TryAdd(myid, src)
            then failwith "call: cannot create call request"

            let payload = mkparams4 0 myid ev.method ev.parameters
            MessagePackSerializer.ToJson(payload) |> trace "call: %s"
            do! MessagePackSerializer.SerializeAsync(stdin, payload)
            do! stdin.FlushAsync()
            let! response = src.Task

            match response.result with
            | Choice2Of2 err -> trace "call #%d(%s) failed with: %A" myid ev.method err
            | _ -> ()

            return response
        }

        proc.BeginErrorReadLine()

        m_proc   <- Some proc
        m_events <- Some events
        m_notify <- notify
        m_call   <- call

    member __.stop (timeout: int) =
        match m_proc with
        | Some proc ->
            proc.CancelErrorRead()
            if not <| proc.WaitForExit(timeout) then
                proc.Kill()
            proc.Close()
            m_proc <- None
            m_events <- None
            m_notify <- default_notify
            m_call <- default_call
        | _ -> ()

    member this.subscribe (ctx: SynchronizationContext) (fn: Event -> unit) =
        this.events
        |> Observable.observeOnContext ctx
        |> Observable.subscribe        fn

    member __.input (keys: string[]) =
        let keys = Array.map (fun x -> box x) keys
        m_call { method = "nvim_input"; parameters = keys }

    member __.grid_resize (id: int) (w: int) (h: int) =
        m_call { method = "nvim_ui_try_resize"; parameters = mkparams2 w h }

    member __.ui_attach (w:int) (h:int) =
        let opts = Dictionary<string, bool>()
        opts.[uiopt_rgb]          <- true
        (*opts.[uiopt_ext_multigrid] <- true*)
        opts.[uiopt_ext_linegrid] <- true

        m_call { method = "nvim_ui_attach"; parameters = mkparams3 w h opts }

    member __.set_var (var: string) (value: obj) =
        m_call { method = "nvim_set_var"; parameters = mkparams2 var value }

    member __.command (cmd: string) =
        m_call { method = "nvim_command"; parameters = mkparams1 cmd }


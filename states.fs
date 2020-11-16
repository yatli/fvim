module FVim.States

open common
open SkiaSharp
open log
open def
open System.Reflection
open Avalonia.Threading
open Avalonia.Media
open Avalonia.Layout
open System.Threading.Tasks
open FSharp.Control.Tasks.V2

[<Struct>]
type Request = 
    {
        method:     string
        parameters: obj[]
    }

[<Struct>]
type Response = 
    {
        result: Result<obj, obj>
    }

[<Struct>]
type Event =
| Request      of reqId: int32 * req: Request * handler: (int32 -> Response -> unit Task)
| Response     of rspId: int32 * rsp: Response
| Notification of nreq: Request
| Error        of emsg: string
| Crash        of ccode: int32
| Exit

let private _stateChangeEvent = Event<string>()
let private _appLifetime = Avalonia.Application.Current.ApplicationLifetime :?> Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
let mutable private _crashcode = 0
let private _errormsgs = ResizeArray<string>()

// request handlers are explicitly registered, 1:1, with no broadcast.
let requestHandlers      = hashmap[]
// notification events are broadcasted to all subscribers.
let notificationEvents   = hashmap[]

let getNotificationEvent eventName =
    match notificationEvents.TryGetValue eventName with
    | true, ev -> ev
    | _ ->
    let ev = Event<obj[]>()
    notificationEvents.[eventName] <- ev
    ev

type LineHeightOption =
| Absolute of float
| Add of float
| Default

// channel
let mutable channel_id = 1

// keyboard mapping
let mutable key_disableShiftSpace = false

// clipboard
let mutable clipboard_lines: string[] = [||]
let mutable clipboard_regtype: string = ""

// cursor
let mutable cursor_smoothmove  = false
let mutable cursor_smoothblink = false

// font rendering
let mutable font_antialias     = true
let mutable font_drawBounds    = false
let mutable font_autohint      = false
let mutable font_subpixel      = true
let mutable font_autosnap      = true
let mutable font_ligature      = true
let mutable font_hintLevel     = SKPaintHinting.NoHinting
let mutable font_weight_normal = FontWeight.Normal
let mutable font_weight_bold   = FontWeight.Bold
let mutable font_lineheight    = LineHeightOption.Default
let mutable font_nonerd        = false

// ui
let mutable ui_available_opts  = Set.empty<string>
let mutable ui_multigrid       = false
let mutable ui_popupmenu       = true
let mutable ui_tabline         = false
let mutable ui_cmdline         = false
let mutable ui_wildmenu        = false
let mutable ui_messages        = false
let mutable ui_termcolors      = false
let mutable ui_hlstate         = false

type BackgroundComposition =
  | NoComposition
  | Transparent
  | Blur
  | Acrylic

// background
let mutable background_composition   = NoComposition
let mutable background_opacity       = 1.0
let mutable background_altopacity    = 1.0
let mutable background_image_file    = ""
let mutable background_image_opacity = 1.0
let mutable background_image_stretch = Stretch.None
let mutable background_image_halign  = HorizontalAlignment.Left
let mutable background_image_valign  = VerticalAlignment.Top


[<Literal>]
let uiopt_rgb            = "rgb"
[<Literal>]
let uiopt_ext_linegrid   = "ext_linegrid"
[<Literal>]
let uiopt_ext_multigrid  = "ext_multigrid"
[<Literal>]
let uiopt_ext_popupmenu  = "ext_popupmenu"
[<Literal>]
let uiopt_ext_tabline    = "ext_tabline"
[<Literal>]
let uiopt_ext_cmdline    = "ext_cmdline"
[<Literal>]
let uiopt_ext_wildmenu   = "ext_wildmenu"
[<Literal>]
let uiopt_ext_messages   = "ext_messages"
[<Literal>]
let uiopt_ext_hlstate    = "ext_hlstate"
[<Literal>]
let uiopt_ext_termcolors = "ext_termcolors"

///  !Note does not include rgb and ext_linegrid
let PopulateUIOptions (opts: hashmap<_,_>) =
    let c k v = 
        if ui_available_opts.Contains k then
            opts.[k] <- v
    c uiopt_ext_popupmenu ui_popupmenu
    c uiopt_ext_multigrid ui_multigrid
    c uiopt_ext_tabline ui_tabline
    c uiopt_ext_cmdline ui_cmdline
    c uiopt_ext_wildmenu ui_wildmenu
    c uiopt_ext_messages ui_messages
    c uiopt_ext_hlstate ui_hlstate
    c uiopt_ext_termcolors ui_termcolors

module private Helper =
    type Foo = A
    let _StatesModuleType = typeof<Foo>.DeclaringType.DeclaringType
    let SetProp name v =
        let propDesc = _StatesModuleType.GetProperty(name, BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)
        if propDesc <> null then
            propDesc.SetValue(null, v)
        else
            error "states" "The property %s is not found" name
    let GetProp name =
        let propDesc = _StatesModuleType.GetProperty(name, BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)
        if propDesc <> null then
            Some <| propDesc.GetValue(null)
        else
            error "states" "The property %s is not found" name
            None

let parseHintLevel (v: obj) = 
    match v with
    | String v ->
        match v.ToLower() with
        | "none" -> Some SKPaintHinting.NoHinting
        | "slight" -> Some SKPaintHinting.Slight
        | "normal" -> Some SKPaintHinting.Normal
        | "full" -> Some SKPaintHinting.Full
        | _ -> None
    | _ -> None

let parseFontWeight (v: obj) =
    match v with
    | Integer32 v -> Some(LanguagePrimitives.EnumOfValue v)
    | _ -> None

let parseLineHeightOption (v: obj) =
    match v with
    | String v ->
        if v.StartsWith("+") then
            Some(Add(float v.[1..]))
        elif v.StartsWith("-") then
            Some(Add(-float v.[1..]))
        elif v.ToLowerInvariant() = "default" then
            Some Default
        else
            let v = float v
            if v > 0.0 then Some(Absolute v) 
            else None
    | _ -> None

let parseBackgroundComposition (v: obj) = 
    match v with
    | String v ->
        match v.ToLower() with
        | "none" -> Some NoComposition
        | "blur" -> Some Blur
        | "acrylic" -> Some Acrylic
        | "transparent" -> Some Transparent
        | _ -> None
    | _ -> None

let parseStretch (v: obj) = 
    match v with
    | String v ->
        match v.ToLower() with
        | "none" -> Some Stretch.None
        | "fill" -> Some Stretch.Fill
        | "uniform" -> Some Stretch.Uniform
        | "uniformfill" -> Some Stretch.UniformToFill
        | _ -> None
    | _ -> None

let parseHorizontalAlignment (v: obj) = 
    match v with
    | String v ->
        match v.ToLower() with
        | "left" -> Some HorizontalAlignment.Left
        | "center" -> Some HorizontalAlignment.Center
        | "right" -> Some HorizontalAlignment.Right
        | "stretch" -> Some HorizontalAlignment.Stretch
        | _ -> None
    | _ -> None

let parseVerticalAlignment (v: obj) = 
    match v with
    | String v ->
        match v.ToLower() with
        | "top" -> Some VerticalAlignment.Top
        | "center" -> Some VerticalAlignment.Center
        | "bottom" -> Some VerticalAlignment.Bottom
        | "stretch" -> Some VerticalAlignment.Stretch
        | _ -> None
    | _ -> None

let backgroundCompositionToString = 
  function
    | NoComposition -> "none"
    | Blur -> "blur"
    | Acrylic -> "acrylic"
    | Transparent -> "transparent" 

let Shutdown code = _appLifetime.Shutdown code

let get_crash_info() =
  _crashcode, _errormsgs

let msg_dispatch =
    function
    | Request(id, req, reply) -> 
        match requestHandlers.TryGetValue req.method with
        | true, method ->
            task { 
                try
                    let! rsp = method(req.parameters)
                    do! reply id rsp
                with
                | Failure msg -> error "rpc" "request %d(%s) failed: %s" id req.method msg
            } |> run
        | _ -> error "rpc" "request handler [%s] not found" req.method

    | Notification(req) ->
        let event = getNotificationEvent req.method
        try event.Trigger req.parameters
        with | Failure msg -> error "rpc" "notification trigger [%s] failed: %s" req.method msg
    | Error err -> 
      error "rpc" "neovim: %s" err
      _errormsgs.Add err
    | Exit -> 
      trace "rpc" "shutting down application lifetime"
      _appLifetime.Shutdown()
    | Crash code -> 
      trace "rpc" "neovim crashed with code %d" code
      _crashcode <- code
      failwithf "neovim crashed"
    | other -> 
      trace "rpc" "unrecognized event: %A" other

module Register =
    /// Register an rpc handler.
    let Request name fn = 
        requestHandlers.Add(name, fun objs ->
            try fn objs
            with x -> 
                error "Request" "exception thrown: %O" x
                Task.FromResult {  result = Result.Error(box x) })

    /// Register an event handler.
    let Notify name (fn: obj[] -> unit) = 
        (getNotificationEvent name).Publish.Subscribe(fun objs -> 
            try fn objs
            with x -> error "Notify" "exception thrown: %A" <| x.ToString())

    /// Watch for registered state
    let Watch (name: string) fn =
        _stateChangeEvent.Publish
        |> Observable.filter (fun x -> x.StartsWith(name))
        |> Observable.subscribe (fun _ -> fn())

    /// Registers a state variable. Raises notification on change.
    let Prop<'T> (parser: obj -> 'T option) (fullname: string) =
        let section = fullname.Split(".").[0]
        let fieldName = fullname.Replace(".", "_")
        Notify fullname (fun v ->
            match v with
            | [| v |] ->
                match parser(v) with
                | Some v -> 
                    Helper.SetProp fieldName v
                    _stateChangeEvent.Trigger fullname
                | None -> ()
            | _ -> ())
        |> ignore
        Request fullname (fun _ -> task { 
            let result = 
                match Helper.GetProp fieldName with
                | Some v -> Ok v
                | None -> Result.Error(box "not found")
            return { result=result }
        })

    let Bool = Prop<bool> (|Bool|_|)
    let String = Prop<string> (|String|_|)
    let Float = Prop<float> (function
        | Integer32 x -> Some(float x)
        | :? float as x -> Some x
        | _ -> None)



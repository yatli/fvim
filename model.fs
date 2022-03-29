module FVim.model

open ui
open common
open states
open def
open neovim
open getopt
open log
open widgets

open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open FSharp.Control.Reactive
open SkiaSharp
open System
open System.ComponentModel
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open System.Threading.Tasks

#nowarn "0025"

let inline private trace x = trace "model" x

[<AutoOpen>]
module private ModelImpl =

    let nvim          = Nvim()
    let ev_uiopt      = Event<unit>()
    let ev_flush      = Event<unit>()
    let grids         = hashmap[]
    let frames        = hashmap[]
    let init          = new TaskCompletionSource<unit>()

    let add_grid(grid: IGridUI) =
        let id = grid.Id
        grids.[id] <- grid

    let destroy_grid(id) =
      match grids.TryGetValue id with
      | false, _ -> ()
      | true, grid ->
        ignore <| grids.Remove id
        grid.Detach()

    let add_frame(win: IFrame) = 
        let id = win.MainGrid.Id
        frames.[id] <- win

    let setTitle id title = frames.[id].Title <- title

    let _unicast_fail id cmd =
        trace "unicast into non-existing grid #%d: %A" id cmd

    let unicast id cmd = 
        match grids.TryGetValue id with
        | true, grid -> grid.Redraw cmd
        | _ -> trace "unicast into non-existing grid #%d: %A" id cmd

    let unicast_detach id cmd =
        match grids.TryGetValue id with
        | true, grid -> 
            grid.Detach()
            grid.Redraw cmd
        | _ -> trace "unicast into non-existing grid #%d: %A" id cmd

    let unicast_change_parent id pid cmd =
        match (grids.TryGetValue id),(grids.TryGetValue pid) with
        | (true, grid),(true, parent) -> 
            grid.Detach()
            parent.AddChild(grid)
            grid.Redraw cmd
        | _ -> trace "unicast into non-existing grid #%d or parent #%d: %A" id pid cmd


    let unicast_create id cmd w h = 
        if not(grids.ContainsKey id) then
          add_grid <| grids.[1].CreateChild id h w
        unicast id cmd

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
        | UnknownCommand x                  -> trace "unknown command %A" x
        | SetTitle title                    -> setTitle 1 title
        | SetIcon icon                      -> trace "icon: %s" icon // TODO
        | Bell                              -> bell true
        | VisualBell                        -> bell false
        | Flush                             -> ev_flush.Trigger()
        | HighlightAttrDefine hls           -> theme.hiattrDefine hls
        | SemanticHighlightGroupSet groups  -> theme.setSemanticHighlightGroups groups
        | DefaultColorsSet(fg,bg,sp,_,_)    -> theme.setDefaultColors fg bg sp
        | SetOption opts                    -> Array.iter theme.setOption opts
        | ModeInfoSet(cs_en, info)          -> theme.setModeInfo cs_en info
        //  Broadcast
        | PopupMenuShow _
        | PopupMenuSelect _             | PopupMenuHide _
        | Busy _                        | Mouse _
        | ModeChange _                  | GridCursorGoto _
        | WinExtmarks _                 
        | WinExtmarksClear _                -> broadcast cmd
        //  Unicast
        | GridClear id                  | GridScroll(id,_,_,_,_,_,_)    
        | WinClose id                   | WinHide(id) 
        | WinViewport(id,_,_,_,_,_,_)       -> unicast id cmd
        | WinExternalPos(id,_)              -> unicast_detach id cmd
        | WinFloatPos(id, _, _, pid, _, _, _, _)  
                                            -> unicast_change_parent id pid cmd
        | MsgSetPos(id, _, _, _)            -> unicast_create id cmd grids.[1].GridWidth 1
        | WinPos(id, _, _, _, w, h)
        | GridResize(id, w, h)              -> unicast_create id cmd w h
        | GridLine lines                    -> 
            if lines.Length <= 0 then () else
            let span = lines.Span
            let mutable prev = 0
            let mutable prev_grid = span.[0].grid
            for i in 1 .. lines.Length do
              let grid = if i < lines.Length then span.[i].grid else -1
              if grid <> prev_grid then
                unicast prev_grid (GridLine <| lines.Slice(prev, i-prev))
                prev_grid <- grid
                prev <- i

        | GridDestroy id                    -> trace "GridDestroy %d" id; destroy_grid id
        | MultiRedrawCommand xs             -> Array.iter redraw xs
        | x                                 -> trace "unimplemented command: %A" x

    let onRedraw arr =
        for o in arr do
            parse_redrawcmd o |> redraw

    let onGridResize(gridui: IGridUI) =
        trace "Grid #%d resized to %d %d" gridui.Id gridui.GridWidth gridui.GridHeight
        ignore <| nvim.grid_resize gridui.Id gridui.GridWidth gridui.GridHeight

    let onInitComplete _ =
      backgroundTask {
        trace "init complete."
        // watch gui-widgets extmark namespace
        match! nvim.call { method = "nvim_get_namespaces"; parameters = [||] } with
        | Ok(FindKV("GuiWidget")(Integer32 id)) -> 
          trace "nvim supports gui-widgets."
          let! _ = nvim.call { method = "nvim_ui_watch_extmark"; parameters = mkparams1 id }
          let! _ = nvim.call { method = "nvim_call_function"; parameters = mkparams2 "GuiWidgetClientAttach" (mkparams1 channel_id) }
          guiwidgetNamespace <- id
        | _ -> ()
      } |> ignore

    let onSignUpdate [| Integer32 bufnr; signs |] =
        let signs = parseBufferSignPlacements signs
                    |> Array.map (fun (ln,name) -> 
                        let kind = 
                            if name.Contains("Warning") then SignKind.Warning
                            elif name.Contains("Error") then SignKind.Error
                            elif name.Contains("Add") then SignKind.Add
                            elif name.Contains("Delete") then SignKind.Delete
                            elif name.Contains("Change") then SignKind.Change
                            else SignKind.Other
                        { line = ln; kind = kind }
                    )
        loadSignPlacements bufnr signs

    let onGuiWidgetPut [| obj |] =
        match obj with
        | FindKV("id")(Integer32 id) & FindKV("data")(ByteArray data) & FindKV("mime")(String mime) ->
            #if DEBUG
            trace "onGuiWidgetPut: id = %d mime = %s, data len: %d" id mime (data.Length)
            #endif
            loadGuiResource id mime data
        | _ -> ()

    let onGuiWidgetUpdateView [| obj |] =
        match obj with
        | FindKV("buf")(Integer32 buf) & FindKV("widgets")(PX(parse_placement)placements) ->
          loadGuiWidgetPlacements buf placements
          #if DEBUG
          trace $"onGuiWidgetUpdateView: buf = {buf}, #placements={placements.Length}"
          #endif
        | _ -> 
          trace $"onGuiWidgetUpdateView: unrecognized arguments: %A{obj}"


let private _appLifetime = lazy(Avalonia.Application.Current.ApplicationLifetime :?> Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
let Shutdown code = 
  try _appLifetime.Value.Shutdown code
  with _ -> ()

module rpc =
    let mutable _crashcode = 0
    let _errormsgs = ResizeArray<string>()
    let _bytemsg = ResizeArray<byte>()
    // request handlers are explicitly registered, 1:1, with no broadcast.
    let private requestHandlers      = hashmap[]
    // notification events are broadcasted to all subscribers.
    let private notificationEvents   = hashmap[]

    let private getNotificationEvent eventName =
        match notificationEvents.TryGetValue eventName with
        | true, ev -> ev
        | _ ->
        let ev = Event<obj[]>()
        notificationEvents.[eventName] <- ev
        ev

    let msg_dispatch =
        function
        | RpcRequest(id, req, reply) -> 
            match requestHandlers.TryGetValue req.method with
            | true, method ->
                backgroundTask { 
                    try
                        let! rsp = method(req.parameters)
                        do! reply id rsp
                    with
                    | Failure msg -> error "rpc" "request %d(%s) failed: %s" id req.method msg
                } |> ignore
            | _ -> error "rpc" "request handler [%s] not found" req.method
        | Notification(req) ->
            let event = getNotificationEvent req.method
            try event.Trigger req.parameters
            with | Failure msg -> error "rpc" "notification trigger [%s] failed: %s" req.method msg
        | StdError err -> 
          error "rpc" "neovim: %s" err
          _errormsgs.Add err
        | ByteMessage bmsg -> 
          _bytemsg.Add bmsg
        | Exit ecode -> 
          if _bytemsg.Count <> 0 then
            let _bytemsg = System.Text.Encoding.UTF8.GetString(_bytemsg.ToArray())
            failwithf "neovim says:\n%s" _bytemsg
          trace "shutting down application lifetime"
          Shutdown ecode
        | Crash code -> 
          trace "neovim crashed with code %d" code
          _crashcode <- code
          failwithf "neovim crashed"
        | UnhandledException ex -> 
          raise ex
        | other -> 
          trace "unrecognized event: %A" other

    module private Helper =
        let _StatesModuleType = typeof<states.Foo>.DeclaringType
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
    module register =
        let private _stateChangeEvent = Event<string>()
        /// Register an rpc handler.
        let request name fn = 
            requestHandlers.Add(name, fun objs ->
                try fn objs
                with x -> 
                    error "Request" "exception thrown: %O" x
                    Task.FromResult(Error(box x)))

        /// Register an event handler.
        let notify name (fn: obj[] -> unit) = 
            (getNotificationEvent name).Publish.Subscribe(fun objs -> 
                try fn objs
                with x -> error "Notify" "exception thrown: %A" <| x.ToString())

        /// Watch for registered state
        let watch (name: string) fn =
            _stateChangeEvent.Publish
            |> Observable.filter (fun x -> x.StartsWith(name))
            |> Observable.subscribe (fun _ -> fn())

        /// Registers a state variable. Raises notification on change.
        let prop<'T> (parser: obj -> 'T option) (fullname: string) =
            let section = fullname.Split(".").[0]
            let fieldName = fullname.Replace(".", "_")
            notify fullname (fun v ->
                match v with
                | [| v |] ->
                    match parser(v) with
                    | Some v -> 
                        Helper.SetProp fieldName v
                        _stateChangeEvent.Trigger fullname
                    | None -> ()
                | _ -> ())
            |> ignore
            request fullname (fun _ -> task { 
                match Helper.GetProp fieldName with
                | Some v -> return Ok v
                | None -> return Error(box "not found")
            })

        let bool = prop<bool> (|Bool|_|)
        let string = prop<string> (|String|_|)
        let int = prop<int> (|Integer32|_|)
        let float = prop<float> (function
            | Integer32 x -> Some(float x)
            | :? float as x -> Some x
            | _ -> None)

let get_crash_info() =
  rpc._crashcode, rpc._errormsgs

let Detach() =
    if nvim.isRemote then
      Shutdown(0)

let private UpdateUICapabilities() =
    let opts = hashmap[]
    states.PopulateUIOptions opts
    trace "UpdateUICapabilities: %A" <| String.Join(", ", Seq.map (fun (KeyValue(k, v)) -> $"{k}={v}") opts)
    backgroundTask {
      do! init.Task
      for KeyValue(k, v) in opts do
        let! _ = nvim.call { method="nvim_ui_set_option"; parameters = mkparams2 k v }
        in ()
    } |> ignore

/// <summary>
/// Call this once at initialization.
/// </summary>
let Start (serveropts, norc, remote) =
    trace "starting neovim instance..."
    trace "opts = %A" serveropts
    nvim.start serveropts
    nvim.subscribe 
        (AvaloniaSynchronizationContext.Current) 
        (rpc.msg_dispatch)

    // rpc handlers
    rpc.register.bool "font.autosnap"
    rpc.register.bool "font.antialias"
    rpc.register.bool "font.drawBounds"
    rpc.register.bool "font.autohint"
    rpc.register.bool "font.subpixel"
    rpc.register.bool "font.lcdrender"
    rpc.register.bool "font.ligature"
    rpc.register.prop<SKPaintHinting> parseHintLevel "font.hintLevel"
    rpc.register.prop<FontWeight> parseFontWeight "font.weight.normal"
    rpc.register.prop<FontWeight> parseFontWeight "font.weight.bold"
    rpc.register.prop<LineHeightOption> parseLineHeightOption "font.lineheight"
    rpc.register.bool "font.nonerd"
    rpc.register.bool "cursor.smoothblink"
    rpc.register.bool "cursor.smoothmove"
    rpc.register.bool "key.disableShiftSpace"
    rpc.register.bool "key.autoIme"
    rpc.register.bool "key.altGr"
    //rpc.register.bool "ui.multigrid"
    rpc.register.bool "ui.popupmenu"
    //rpc.register.bool "ui.tabline"
    //rpc.register.bool "ui.cmdline"
    rpc.register.bool "ui.wildmenu"
    //rpc.register.bool "ui.messages"
    //rpc.register.bool "ui.termcolors"
    //rpc.register.bool "ui.hlstate"
    //rpc.register.bool "ui.windows"

    rpc.register.prop<BackgroundComposition> parseBackgroundComposition "background.composition"
    rpc.register.float "background.opacity"
    rpc.register.float "background.altopacity"
    rpc.register.float "background.image.opacity"
    rpc.register.string "background.image.file"
    rpc.register.prop<Stretch> parseStretch "background.image.stretch"
    rpc.register.prop<HorizontalAlignment> parseHorizontalAlignment "background.image.halign"
    rpc.register.prop<VerticalAlignment> parseVerticalAlignment "background.image.valign"

    rpc.register.int "default.width"
    rpc.register.int "default.height"


    List.iter ignore [
        ev_uiopt.Publish
        |> Observable.throttle(TimeSpan.FromMilliseconds 20.0)
        |> Observable.subscribe(UpdateUICapabilities)
        rpc.register.notify "redraw" onRedraw 
        rpc.register.notify "remote.detach" (fun _ -> Detach())
        rpc.register.watch "ui" ev_uiopt.Trigger
        rpc.register.watch "font" theme.fontConfig
        rpc.register.watch "font" ui.InvalidateFontCache
        rpc.register.notify "OnInitComplete" onInitComplete
        rpc.register.notify "OnSignUpdate" onSignUpdate
        rpc.register.notify "GuiWidgetPut" onGuiWidgetPut
        rpc.register.notify "GuiWidgetUpdateView" onGuiWidgetUpdateView
    ]

    rpc.register.request "set-clipboard" (fun [| P(|String|_|)lines; String regtype |] -> task {
        states.clipboard_lines <- lines
        states.clipboard_regtype <- regtype
        let text = String.Join("\n", lines)
        let! _ = Avalonia.Application.Current.Clipboard.SetTextAsync(text)
        trace "set-clipboard called. regtype=%s" regtype
        return Ok(box [||])
    })

    rpc.register.request "get-clipboard" (fun _ -> task {
        let! sysClipboard = Avalonia.Application.Current.Clipboard.GetTextAsync()
        let sysClipboard = if String.IsNullOrEmpty sysClipboard then "" else sysClipboard
        let sysClipboardLines = sysClipboard.Replace("\r\n", "\n").Split("\n")
        let clipboard_eq = Array.compareWith (fun a b -> String.Compare(a,b)) states.clipboard_lines sysClipboardLines

        let lines, regtype =
            if clipboard_eq = 0 then
                trace "get-clipboard: match, using clipboard lines with regtype %s" states.clipboard_regtype
                states.clipboard_lines, states.clipboard_regtype
            else
                trace "get-clipboard: mismatch, using system clipboard"
                sysClipboardLines, "v"

        return Ok(box [| box lines; box regtype |])
    })

    trace "commencing early initialization..."

    backgroundTask {
      let! api_info = nvim.call { method = "nvim_get_api_info"; parameters = [||] }
      let api_query_result = 
          match api_info with
          | Ok(ObjArray [| Integer32 _; metadata |]) -> Ok metadata
          | _ -> Error("nvim_get_api_info")
          >?= fun metadata ->
          match metadata with
          | FindKV "ui_options" (P (|String|_|) ui_options) -> Ok(Set.ofArray ui_options)
          | _ -> Error("find ui_options")
          >?= fun ui_options ->
          trace "available ui options: %A" ui_options
          ui_available_opts <- ui_options
          Ok()
      match api_query_result with
      | Ok() ->
          if not (ui_available_opts.Contains uiopt_ext_linegrid) ||
             not (ui_available_opts.Contains uiopt_rgb) then
             failwithf "api_query_result: your NeoVim version is too low. fvim requires \"rgb\" and \"ext_linegrid\" options, got: %A" ui_available_opts
      | Error(msg) ->
          failwithf "api_query_result: %s" msg

      // for remote, send open file args as edit commands
      let remoteEditFiles =
          match serveropts with
          // for embedded & fvr new session, edit file args are passed thru to neovim
          | Embedded _             
          | FVimRemote(_, _, NewSession _, _) -> []
          | NeovimRemote(_, files) 
          | FVimRemote(_, _, _, files)  -> files
      for file in remoteEditFiles do
          let! _ = nvim.edit file
          in ()

      let clientId = nvim.Id.ToString()
      let clientName = "FVim"
      let clientVersion = 
          hashmap [
              "major", "0"
              "minor", "3"
              "prerelease", "dev"
          ]
      let clientType = "ui"
      let clientMethods = hashmap []
      let clientAttributes = 
          hashmap [
              "InstanceId", clientId
          ]

      let! _ = nvim.set_var "fvim_loaded" 1
      let! _ = nvim.set_var "fvim_os" <| if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "windows"
                                         elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "linux"
                                         elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then "osx"
                                         else "unknown"
      let! _ = nvim.set_client_info clientName clientVersion clientType clientMethods clientAttributes
      let! channels = nvim.list_chans()

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

      states.channel_id <- myChannel

      // Another instance is already up
      if fvimChannels.Length > 1 then
          Environment.Exit(0)

      if not norc then
        let! _ = nvim.set_var "fvim_channel" myChannel
        let fvimPath =
            Assembly.GetExecutingAssembly().Location
            |> Path.GetDirectoryName 
        let scriptPath = Path.Combine(fvimPath, "fvim.vim")
        trace "script path: %A" scriptPath
        if remote then
          let! scriptText = File.ReadAllTextAsync(scriptPath)
          let luaText = $"""local uv = vim.loop
local txt = [[{scriptText}]]
local template = "fvim-init.vim-XXXXXX"
local temp_env = os.getenv("TEMP")
if temp_env == nil then
  template = "/tmp/"..template
else
  template = temp_env.."\\"..template
end
uv.fs_mkstemp(template, function(err,fd,path)
  assert(not err, err)
  uv.fs_write(fd,txt,nil,function(err,n)
    assert(not err, err)
    uv.fs_close(fd, vim.schedule_wrap(function(err)
      assert(not err, err)
      vim.cmd("source "..path)
      uv.fs_unlink(path)
      vim.g.fvim_init_complete = true
    end))
  end)
end)
"""
          let! _ = nvim.exec_lua (luaText.Replace("\r\n", "\n")) [||]
          ()
        else 
          let! _ = nvim.command ("source " + scriptPath)
          ()
        ()

      // initialization complete. no more messages will be sent from this thread.
      init.SetResult()
    } |> ignore

let Flush =
    ev_flush.Publish
    |> flush_throttle
    |> Observable.observeOn Avalonia.Threading.AvaloniaScheduler.Instance

let mutable CreateFrame: IGridUI -> unit = fun _ -> raise (new NotImplementedException())

let OnFrameReady(win: IFrame) =
    let id = win.MainGrid.Id
    add_frame win
    if id <> 1 then
        win.Sync(frames.[1])


// connect the grid redraw commands and events
let OnGridReady(gridui: IGridUI) =

    add_grid gridui

    // do not send messages until the initializer has done its job.
    // otherwise the two threads will be racing over the channel.
    init.Task.Wait()

    gridui.Resized 
    |> Observable.throttle (TimeSpan.FromMilliseconds 20.0)
    |> Observable.add onGridResize

    gridui.Input 
    |> input.onInput nvim
    |> nvim.pushSubscription

    if gridui.Id = 1 then
        // Grid #1 is the main grid.
        // When ready, the UI should be ready for events now. 
        // Notify nvim about its presence
        trace "attaching to nvim on first grid ready signal. size = %A %A" 
              gridui.GridWidth gridui.GridHeight
        backgroundTask {
          let! _ = nvim.set_var "fvim_render_scale" gridui.RenderScale
          let! _ = nvim.ui_attach gridui.GridWidth gridui.GridHeight
          let! _ = nvim.exec "autocmd InsertEnter * call rpcnotify(g:fvim_channel, 'EnableIme', v:true)" false
          let! _ = nvim.exec "autocmd InsertLeave * call rpcnotify(g:fvim_channel, 'EnableIme', v:false)" false
          in ()
        } |> ignore

let private _pum_arg4: hashmap<obj,obj> = hashmap[]

let SelectPopupMenuItem (index: int) (insert: bool) (finish: bool) =
  #if DEBUG
    trace "SelectPopupMenuItem: index=%d insert=%b finish=%b" index insert finish
  #endif
    nvim.call { method = "nvim_select_popupmenu_item"; parameters = mkparams4 index insert finish _pum_arg4 }
              |> ignore

let SetPopupMenuPos width height row col =
  #if DEBUG
    trace "SetPopupMenuPos: w=%f h=%f r=%f c=%f" width height row col
  #endif
    nvim.call { method = "nvim_ui_pum_set_bounds";  parameters = mkparams4 width height row col}
    |> ignore

let OnFocusLost() =
    nvim.command "if exists('#FocusLost') | doautocmd <nomodeline> FocusLost | endif"
    |> ignore

// see: https://github.com/equalsraf/neovim-qt/blob/e13251a6774ec8c38e7f124b524cc36e4453eb35/src/gui/shell.cpp#L1405
let OnFocusGained() =
    nvim.command "if exists('#FocusGained') | doautocmd <nomodeline> FocusGained | endif"
    |> ignore

let OnTerminated () =
    trace "terminating nvim..."
    nvim.stop 1

let mutable _detaching = false

let OnTerminating(args: CancelEventArgs) =
    trace "window is closing"
    if nvim.isRemote then 
        if not _detaching then
            _detaching <- true
            Detach()
    else 
        args.Cancel <- true
        nvim.quitall() |> ignore

let OnExtClosed(win: int) =
    nvim.call {method = "nvim_win_close"; parameters = mkparams2 win true} 
    |> ignore

let EditFiles (files: string seq) =
    backgroundTask {
      for file in files do
          let! _ = nvim.edit file
          in ()
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
        nvim.command text |> ignore

let GetBufferPath (win: int) =
    task {
        match! nvim.call { method = "nvim_win_get_buf"; parameters = mkparams1 win } with
        | Error(_) -> return ""
        | Ok(Integer32 bufnr) -> 
        match! nvim.call { method = "nvim_buf_get_name"; parameters = mkparams1 bufnr } with
        | Ok(String path) -> return path
        | _ -> return ""
    }

let GuiWidgetMouseUp (buf: int) (mark: int) =
   nvim.exec_lua $"require('gui-widgets').mouse_up({buf},{mark})" [||]
   |> ignore

let GuiWidgetMouseDown (buf: int) (mark: int) =
   nvim.exec_lua $"require('gui-widgets').mouse_down({buf},{mark})" [||]
   |> ignore

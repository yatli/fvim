module FVim.model

open ui
open common
open states
open def
open neovim
open getopt
open log

open Avalonia.Media
open System
open Avalonia.Threading
open FSharp.Control.Reactive
open System.ComponentModel
open SkiaSharp
open Avalonia.Layout
open System.Threading.Tasks

#nowarn "0025"

open FSharp.Control.Tasks.V2
open System.Runtime.InteropServices

let inline private trace x = trace "model" x

[<AutoOpen>]
module ModelImpl =

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
        let id = win.RootId
        frames.[id] <- win

    let setTitle id title = frames.[id].Title <- title

    let unicast id cmd = 
        match grids.TryGetValue id with
        | true, grid -> grid.Redraw cmd
        | _ -> trace "unicast into non-existing grid #%d: %A" id cmd

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
        | PopupMenuShow _         | PopupMenuSelect _             | PopupMenuHide _
        | Busy _                  | Mouse _
        | ModeChange _            | Flush 
        | GridCursorGoto(_,_,_) 
                                            -> broadcast cmd
        //  Unicast
        | GridClear id            | GridScroll(id,_,_,_,_,_,_)    
        | WinClose id             | WinFloatPos(id, _, _, _, _, _, _) 
        | WinViewport(id, _, _, _, _, _ )   -> unicast id cmd
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

    let onGridResize(gridui: IGridUI) =
        trace "Grid #%d resized to %d %d" gridui.Id gridui.GridWidth gridui.GridHeight
        ignore <| nvim.grid_resize gridui.Id gridui.GridWidth gridui.GridHeight

let Detach() =
    if nvim.isRemote then
      states.Shutdown(0)

let UpdateUICapabilities() =
    let opts = hashmap[]
    states.PopulateUIOptions opts
    trace "UpdateUICapabilities: %A" <| String.Join(", ", Seq.map (fun (KeyValue(k, v)) -> sprintf "%s=%b" k v) opts)
    async {
      do! Async.AwaitTask(init.Task)
      for KeyValue(k, v) in opts do
        let! _ = nvim.call { method="nvim_ui_set_option"; parameters = mkparams2 k v }
        in ()
    } |> runAsync

/// <summary>
/// Call this once at initialization.
/// </summary>
let Start (serveropts, norc, debugMultigrid) =
    trace "starting neovim instance..."
    trace "opts = %A" serveropts
    states.ui_multigrid <- debugMultigrid
    nvim.start serveropts
    nvim.subscribe 
        (AvaloniaSynchronizationContext.Current) 
        (states.msg_dispatch)

    // rpc handlers
    states.register.bool "font.autosnap"
    states.register.bool "font.antialias"
    states.register.bool "font.drawBounds"
    states.register.bool "font.autohint"
    states.register.bool "font.subpixel"
    states.register.bool "font.lcdrender"
    states.register.bool "font.ligature"
    states.register.prop<SKPaintHinting> states.parseHintLevel "font.hintLevel"
    states.register.prop<FontWeight> states.parseFontWeight "font.weight.normal"
    states.register.prop<FontWeight> states.parseFontWeight "font.weight.bold"
    states.register.prop<states.LineHeightOption> states.parseLineHeightOption "font.lineheight"
    states.register.bool "font.nonerd"
    states.register.bool "cursor.smoothblink"
    states.register.bool "cursor.smoothmove"
    states.register.bool "key.disableShiftSpace"
    states.register.bool "ui.multigrid"
    states.register.bool "ui.popupmenu"
    states.register.bool "ui.tabline"
    states.register.bool "ui.cmdline"
    states.register.bool "ui.wildmenu"
    states.register.bool "ui.messages"
    states.register.bool "ui.termcolors"
    states.register.bool "ui.hlstate"
    states.register.bool "ui.windows"

    states.register.prop<states.BackgroundComposition> states.parseBackgroundComposition "background.composition"
    states.register.float "background.opacity"
    states.register.float "background.altopacity"
    states.register.float "background.image.opacity"
    states.register.string "background.image.file"
    states.register.prop<Stretch> states.parseStretch "background.image.stretch"
    states.register.prop<HorizontalAlignment> states.parseHorizontalAlignment "background.image.halign"
    states.register.prop<VerticalAlignment> states.parseVerticalAlignment "background.image.valign"


    List.iter ignore [
        ev_uiopt.Publish
        |> Observable.throttle(TimeSpan.FromMilliseconds 20.0)
        |> Observable.subscribe(UpdateUICapabilities)
        states.register.notify "redraw" (Array.map parse_redrawcmd >> Array.iter redraw)
        states.register.notify "remote.detach" (fun _ -> Detach())
        states.register.watch "ui" ev_uiopt.Trigger
    ]

    states.register.request "set-clipboard" (fun [| P(|String|_|)lines; String regtype |] -> task {
        states.clipboard_lines <- lines
        states.clipboard_regtype <- regtype
        let text = String.Join("\n", lines)
        let! _ = Avalonia.Application.Current.Clipboard.SetTextAsync(text)
        trace "set-clipboard called. regtype=%s" regtype
        return { result = Ok(box [||]) }
    })

    states.register.request "get-clipboard" (fun _ -> task {
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

        return { result = Ok(box [| box lines; box regtype |])}
    })

    trace "commencing early initialization..."

    async {
      do! Async.SwitchToNewThread()

      let! api_info = nvim.call { method = "nvim_get_api_info"; parameters = [||] }
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
              "minor", "2"
              "prerelease", "dev"
          ]
      let clientType = "ui"
      let clientMethods = hashmap []
      let clientAttributes = 
          hashmap [
              "InstanceId", clientId
          ]

      let! _ = nvim.set_var "fvim_loaded" 1
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
      let! _ = nvim.set_var "fvim_channel" myChannel

      // Register clipboard provider by setting g:clipboard
      let clipboard = """let g:clipboard = {
'name': 'FVimClipboard',
'copy': {
   '+': {lines, regtype -> rpcrequest(g:fvim_channel, 'set-clipboard', lines, regtype)},
   '*': {lines, regtype -> rpcrequest(g:fvim_channel, 'set-clipboard', lines, regtype)},
 },
'paste': {
   '+': {-> rpcrequest(g:fvim_channel, 'get-clipboard')},
   '*': {-> rpcrequest(g:fvim_channel, 'get-clipboard')},
}
}"""
      let! _ = nvim.command <| clipboard.Replace("\r", "").Replace("\n","")

      let! _ = nvim.``command!`` "FVimDetach" 0 "call rpcnotify(g:fvim_channel, 'remote.detach')"
      let! _ = nvim.``command!`` "FVimToggleFullScreen" 0 "call rpcnotify(g:fvim_channel, 'ToggleFullScreen', 1)"

      let! _ = nvim.``command!`` "-complete=expression FVimCursorSmoothMove" 1 "call rpcnotify(g:fvim_channel, 'cursor.smoothmove', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimCursorSmoothBlink" 1 "call rpcnotify(g:fvim_channel, 'cursor.smoothblink', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimFontLineHeight" 1 "call rpcnotify(g:fvim_channel, 'font.lineheight', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimFontAutoSnap" 1 "call rpcnotify(g:fvim_channel, 'font.autosnap', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimFontAntialias" 1 "call rpcnotify(g:fvim_channel, 'font.antialias', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimFontLigature" 1 "call rpcnotify(g:fvim_channel, 'font.ligature', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimFontDrawBounds" 1 "call rpcnotify(g:fvim_channel, 'font.drawBounds', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimFontAutohint" 1 "call rpcnotify(g:fvim_channel, 'font.autohint', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimFontSubpixel" 1 "call rpcnotify(g:fvim_channel, 'font.subpixel', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimFontHintLevel" 1 "call rpcnotify(g:fvim_channel, 'font.hintLevel', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimFontNormalWeight" 1 "call rpcnotify(g:fvim_channel, 'font.weight.normal', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimFontBoldWeight" 1 "call rpcnotify(g:fvim_channel, 'font.weight.bold', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimFontNoBuiltinSymbols" 1 "call rpcnotify(g:fvim_channel, 'font.nonerd', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimKeyDisableShiftSpace" 1 "call rpcnotify(g:fvim_channel, 'key.disableShiftSpace', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimUIMultiGrid" 1 "call rpcnotify(g:fvim_channel, 'ui.multigrid', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimUIPopupMenu" 1 "call rpcnotify(g:fvim_channel, 'ui.popupmenu', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimUITabLine" 1 "call rpcnotify(g:fvim_channel, 'ui.tabline', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimUICmdLine" 1 "call rpcnotify(g:fvim_channel, 'ui.cmdline', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimUIWildMenu" 1 "call rpcnotify(g:fvim_channel, 'ui.wildmenu', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimUIMessages" 1 "call rpcnotify(g:fvim_channel, 'ui.messages', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimUITermColors" 1 "call rpcnotify(g:fvim_channel, 'ui.termcolors', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimUIHlState" 1 "call rpcnotify(g:fvim_channel, 'ui.hlstate', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimDrawFPS" 1 "call rpcnotify(g:fvim_channel, 'DrawFPS', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimCustomTitleBar" 1 "call rpcnotify(g:fvim_channel, 'CustomTitleBar', <args>)"

      let! _ = nvim.``command!`` "-complete=expression FVimBackgroundOpacity" 1 "call rpcnotify(g:fvim_channel, 'background.opacity', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimBackgroundComposition" 1 "call rpcnotify(g:fvim_channel, 'background.composition', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimBackgroundAltOpacity" 1 "call rpcnotify(g:fvim_channel, 'background.altopacity', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimBackgroundImage" 1 "call rpcnotify(g:fvim_channel, 'background.image.file', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimBackgroundImageOpacity" 1 "call rpcnotify(g:fvim_channel, 'background.image.opacity', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimBackgroundImageStretch" 1 "call rpcnotify(g:fvim_channel, 'background.image.stretch', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimBackgroundImageHAlign" 1 "call rpcnotify(g:fvim_channel, 'background.image.halign', <args>)"
      let! _ = nvim.``command!`` "-complete=expression FVimBackgroundImageVAlign" 1 "call rpcnotify(g:fvim_channel, 'background.image.valign', <args>)"


      // trigger ginit upon VimEnter
      if not norc then
        let! _ = nvim.command "if v:vim_did_enter | runtime! ginit.vim | else | execute \"autocmd VimEnter * runtime! ginit.vim\" | endif"
        ()

      // initialization complete. no more messages will be sent from this thread.
      init.SetResult()
    } |> runAsync

let Flush =
    ev_flush.Publish
    |> flush_throttle
    |> Observable.observeOn Avalonia.Threading.AvaloniaScheduler.Instance

let OnFrameReady(win: IFrame) =
    add_frame win


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
        async {
          let! _ = nvim.ui_attach gridui.GridWidth gridui.GridHeight
          in ()
        } |> runAsync

let SelectPopupMenuItem (index: int) (insert: bool) (finish: bool) =
    trace "SelectPopupMenuItem: index=%d insert=%b finish=%b" index insert finish
    let insert = if insert then "v:true" else "v:false"
    let finish = if finish then "v:true" else "v:false"
    nvim.command (sprintf "call nvim_select_popupmenu_item(%d, %s, %s, {})" index insert finish)
    |> runAsync

let SetPopupMenuPos width height row col =
    trace "SetPopupMenuPos: w=%f h=%f r=%f c=%f" width height row col
    nvim.call { method = "nvim_ui_pum_set_bounds";  parameters = mkparams4 width height row col}
    |> runAsync

let OnFocusLost() =
    nvim.command "if exists('#FocusLost') | doautocmd <nomodeline> FocusLost | endif"
    |> runAsync

// see: https://github.com/equalsraf/neovim-qt/blob/e13251a6774ec8c38e7f124b524cc36e4453eb35/src/gui/shell.cpp#L1405
let OnFocusGained() =
    nvim.command "if exists('#FocusGained') | doautocmd <nomodeline> FocusGained | endif"
    |> runAsync

let OnTerminated () =
    trace "terminating nvim..."
    nvim.stop 1

let OnTerminating(args: CancelEventArgs) =
    args.Cancel <- true
    trace "window is closing"
    if nvim.isRemote then Detach()
    else nvim.quitall() |> runAsync

let EditFiles (files: string seq) =
    async {
      for file in files do
          let! _ = nvim.edit file
          in ()
    } |> runAsync

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
        nvim.command text |> runAsync

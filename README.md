# FVim<img src="https://github.com/yatli/fvim/raw/master/Assets/fvim.png" width="40" height="40"> [![Build Status](https://dev.azure.com/v-yadli/fvim/_apis/build/status/yatli.fvim?branchName=master)](https://dev.azure.com/v-yadli/fvim/_build/latest?definitionId=2&branchName=master)


Cross platform Neovim front-end UI, built with [F#](https://fsharp.org/) + [Avalonia](http://avaloniaui.net/).

![Screenshot](https://github.com/yatli/fvim/raw/master/images/screenshot.png)


### Installation
[Download](https://github.com/yatli/fvim/releases) the latest release package for your system, extract and run `FVim`!

- For Windows 7 / Vista / 8.1 / Server 2008 R2 / Server 2012 R2, use the `win7-x64` package.
    - Follow [these additional steps to install compatibility patches](https://docs.microsoft.com/en-us/dotnet/core/install/windows?tabs=netcore31&pivots=os-windows#additional-deps).
    - The link to the KB update is no longer functioning. [The issue is tracked here](https://github.com/dotnet/docs/issues/20459).
- For Windows 10, use the `win-x64` package -- this version has faster startup.
- For macOS, it's packaged as an app bundle -- unzip and drag it to your applications folder.
- For Linux:
    - Debian based distributions: `dpkg -i fvim_package_name.deb`
    - Arch Linux:  [Install via AUR](https://aur.archlinux.org/packages/fvim/)
    - RPM-based distributions: `rpm -ivh fvim_package_name.rpm`
    - Fedora: `dnf install fvim_package_name.rpm`
    - Compile from Source (having dotnet-sdk-6.0.x installed):
        ```
            git clone https://github.com/yatli/fvim && cd fvim && dotnet publish -f net6.0 -c Release -r linux-x64 --self-contained
        ```

### Features

- Theming done the (Neo)Vim way
  - Cursor color/blink
  - Background image/composition
  - Custom UI elements are themed with `colorscheme` settings
  - And more!
- Font handling
  - Proper font rendering -- respects font style, baseline, [ligatures](https://github.com/tonsky/FiraCode) etc.
  - Built-in support for Nerd font -- no need to patch your fonts!
  - East Asia wide glyph display with font fallback options
  - Fine-grained font tweaking knobs for personal font rendering
  - Emojis!
- GUI framework
  - HiDPI support -- try dragging it across two screens with different DPIs ;)
  - High performance rendering, low latency (60FPS on 4K display with reasonable font size!)
  - GPU acceleration
  - Multi-grid support -- try `Ctrl-w ge` to detach a window into a separate OS window!
  - Input method support built from scratch
  - Rich information scrollbar (currently read-only)
  - [Extend with UI Server Protocol](https://github.com/yatli/gui-widgets.nvim) -- UI widgets as NeoVim plugins
- Remoting
  - Use a Windows FVim frontend with a WSL neovim: `fvim --wsl`
  - Use custom neovim binary: `fvim --nvim ~/bin/nvim.appimage`
  - Use the front end with a remote neovim: `fvim --ssh user@host`
  - Connect to a remote NeoVim backend: `fvim --connect localhost:9527`
  - tmux-like session server: `fvim --fvr attach --ssh user@host`
  - As a terminal emulator: `fvim --terminal`

Try these bindings (note, fvim-specific settings only work in `ginit.vim`, not `init.vim`!):
```vimL
if exists('g:fvim_loaded')
    " good old 'set guifont' compatibility with HiDPI hints...
    if g:fvim_os == 'windows' || g:fvim_render_scale > 1.0
      set guifont=Iosevka\ Slab:h14
    else
      set guifont=Iosevka\ Slab:h28
    endif
      
    " Ctrl-ScrollWheel for zooming in/out
    nnoremap <silent> <C-ScrollWheelUp> :set guifont=+<CR>
    nnoremap <silent> <C-ScrollWheelDown> :set guifont=-<CR>
    nnoremap <A-CR> :FVimToggleFullScreen<CR>
endif
```

Some fancy cursor effects:
```vimL
if exists('g:fvim_loaded')
    FVimCursorSmoothMove v:true
    FVimCursorSmoothBlink v:true
endif
```
![fluent_cursor](https://raw.githubusercontent.com/yatli/fvim/master/images/fluent_cursor.gif)

Detaching a window into an external OS window with `Ctrl-w ge`:
![ext_win](https://raw.githubusercontent.com/yatli/fvim/master/images/ext_win.gif)
Detach as many and span them over your monitors!

Custom popup menu entry icons (see below for how to configure):
![image](https://user-images.githubusercontent.com/20684720/159672096-2630cbda-243d-46c3-b8f7-6d0a4743dffe.png)


### Building from source
We're now targeting `net6.0` so make sure to install the latest preview SDK from the [.NET site](https://dotnet.microsoft.com/download/dotnet/6.0).
We're actively tracking the head of `Avalonia`, and fetch the nightly packages from myget (see `NuGet.config`).

Then, simply:

```
git clone https://github.com/yatli/fvim
cd fvim
dotnet build -c Release
dotnet run -c Release
```
### FVim-specific commands

The following new commands are available:
```vimL
" Toggle between normal and fullscreen
FVimToggleFullScreen

" Cursor tweaks
FVimCursorSmoothMove v:true
FVimCursorSmoothBlink v:true

" Background composition
FVimBackgroundComposition 'acrylic'   " 'none', 'transparent', 'blur' or 'acrylic'
FVimBackgroundOpacity 0.85            " value between 0 and 1, default bg opacity.
FVimBackgroundAltOpacity 0.85         " value between 0 and 1, non-default bg opacity.
FVimBackgroundImage 'C:/foobar.png'   " background image
FVimBackgroundImageVAlign 'center'    " vertial position, 'top', 'center' or 'bottom'
FVimBackgroundImageHAlign 'center'    " horizontal position, 'left', 'center' or 'right'
FVimBackgroundImageStretch 'fill'     " 'none', 'fill', 'uniform', 'uniformfill'
FVimBackgroundImageOpacity 0.85       " value between 0 and 1, bg image opacity

" Title bar tweaks
FVimCustomTitleBar v:true             " themed with colorscheme

" Debug UI overlay
FVimDrawFPS v:true

" Font tweaks
FVimFontAntialias v:true
FVimFontAutohint v:true
FVimFontHintLevel 'full'
FVimFontLigature v:true
FVimFontLineHeight '+1.0' " can be 'default', '14.0', '-1.0' etc.
FVimFontSubpixel v:true
FVimFontNoBuiltinSymbols v:true " Disable built-in Nerd font symbols

" Try to snap the fonts to the pixels, reduces blur
" in some situations (e.g. 100% DPI).
FVimFontAutoSnap v:true

" Font weight tuning, possible valuaes are 100..900
FVimFontNormalWeight 400
FVimFontBoldWeight 700

" Font debugging -- draw bounds around each glyph
FVimFontDrawBounds v:true

" UI options (all default to v:false)
FVimUIPopupMenu v:true      " external popup menu
FVimUIWildMenu v:false      " external wildmenu -- work in progress

" Keyboard mapping options
FVimKeyDisableShiftSpace v:true " disable unsupported sequence <S-Space>
FVimKeyAutoIme v:true           " Automatic input method engagement in Insert mode
FVimKeyAltGr v:true             " Recognize AltGr. Side effect is that <C-A-Key> is then impossible

" Detach from a remote session without killing the server
" If this command is executed on a standalone instance,
" the embedded process will be terminated anyway.
FVimDetach

" =========== BREAKING CHANGES -- the following commands are disabled ============
" FVimUIMultiGrid v:true     -- per-window grid system -- done and enabled by default
" FVimUITabLine v:false      -- external tabline -- not implemented
" FVimUICmdLine v:false      -- external cmdline -- not implemented
" FVimUIMessages v:false     -- external messages -- not implemented
" FVimUITermColors v:false   -- not implemented
" FVimUIHlState v:false      -- not implemented

```

### Startup options

```
Usage: FVim [FVim-args] [NeoVim-args]

FVim-args:

    =========================== Client options ===================================

    --ssh user@host             Start NeoVim remotely over ssh
    --wsl                       Start NeoVim in WSL
    --nvim path-to-program      Use an alternative nvim program

    --nvr target                Connect to a remote NeoVim backend. The target
                                can be an IP endpoint (127.0.0.1:9527), or a
                                Unix socket address (/tmp/path/to/socket), or a
                                Windows named pipe (PipeName).

    --setup                     Registers FVim as a text editor, and updates
                                file association and icons. Requires UAC
                                elevation on Windows.
    --uninstall                 Unregisters FVim as a text editor, and removes
                                file association and icons. Requires UAC
                                elevation on Windows.

    =========================== FVim Remoting ====================================
                                
    --daemon                    Start a FVR multiplexer server.
                                Can be used with --nvim for alternative program.

    --pipe name                 Override the named pipe address of the daemon.
                                When this option is not given, defaults to
                                '/tmp/fvr-main'

    --fvr id [FILES...]         Connects to a FVR server.
    --fvr a[ttach] [FILES...]    - id: an integer session id to connect
    --fvr n[ew] [args...]        - attach: attach to the first available session 
                                 - new: create a new session with args passed to
                                   NeoVim.
                                Can be used with --ssh or --wsl for connecting a
                                remote server. If neither is specified, connects
                                to the local server.
                                Can be used with --pipe to override the server 
                                address.

    =========================== Debug options ====================================

    --trace-to-stdout           Trace to stdout.
    --trace-to-file             Trace to a file.
    --trace-patterns            Filter trace output by a list of keyword strings

    =========================== Terminal emulator ================================

    --terminal                  Start as a terminal emulator.
    --terminal-cmd              Command to run instead of the default shell.


The FVim arguments will be consumed and filtered before the rest are passed to NeoVim.
```

### Custom PUM icons

| Category      | PUM text | FVim                                                                                                | NERD equivalent |
|---------------|----------|-----------------------------------------------------------------------------------------------------|-----------------|
| Text          | t        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Text_16x.png)                |                |
| Method        | :        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Method_16x.png)              |                |
| Function      | f        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Method_16x.png)              |                |
| Constructor   | c        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/NewClass_16x.png)            |                |
| Field         | .        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Field_16x.png)               | ﰠ               |
| Variable      | v        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/LocalVariable_16x.png)       |                |
| Class         | C        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Class_16x.png)               | ﴯ               |
| Interface     | I        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Interface_16x.png)           |                |
| Module        | M        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Module_16x.png)              |                |
| Property      | p        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Property_16x.png)            | ﰠ               |
| Unit          | U        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Dimension_16x.png)           | 塞              |
| Value         | l        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Literal_16x.png)             |                |
| Enum          | E        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Enumerator_16x.png)          |                |
| Keyword       | k        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/IntelliSenseKeyword_16x.png) |                |
| Snippet       | s        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Snippet_16x.png)             |                |
| Color         | K        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/ColorPalette_16x.png)        |                |
| File          | F        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/TextFile_16x.png)            |                |
| Reference     | r        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Reference_16x.png)           |                |
| Folder        | d        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Folder_16x.png)              |                |
| EnumMember    | m        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/EnumItem_16x.png)            |                |
| Constant      | 0        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Constant_16x.png)            |                |
| Struct        | S        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Structure_16x.png)           | פּ               |
| Event         | e        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Event_16x.png)               |                |
| Operator      | o        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Operator_16x.png)            |                |
| TypeParameter | T        | ![Symbol](https://github.com/yatli/fvim/raw/master/Assets/intellisense/Type_16x.png)                | T               |


So instead of populating your symbol dictionary with the NERD-specific characters, use textual characters. FVim will pick them up and display graphical icons stored in `Assets/intellisense` instead.

### Goals

- Keep up with the latest NeoVim features
- Ergonomics improvements via GUI/native OS integration
- Drive the flexible and accessible UI extension method "UI Server Protocol"
  - The idea is to establish a standard protocol for UI extensions, so that the nice 
    GUI additions are not limited to one specific front-end. Think of a front end as
    a UI server handling UI Server Protocol requests issued from front-end-agnostic 
    plugins. It's like Language Server Protocol, but for UI.

### Non-Goals

- Electron ecosystem integration :p
- No walled garden. Everything should be accessible from the NeoVim core, which means:
  - No project explorers -- use a NeoVim plugin
  - No custom tab lines / document wells -- use a NeoVim plugin
  - No side-by-side markdown viewer, unless it's a NeoVim plugin, implemented via the
    UI-Protocol extensions.

### Fellow Front-Ends (to name a few)

- [Neovide](https://github.com/neovide/neovide)
- [goneovim](https://github.com/akiyosi/goneovim)
- [Gnvim](https://github.com/vhakulinen/gnvim)
- [Uivonim](https://github.com/smolck/uivonim)
- [firenvim](https://github.com/glacambre/firenvim)

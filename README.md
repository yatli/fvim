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
    - Compile from Source (having dotnet-sdk-3.1+ installed):
        ```
            git clone https://github.com/yatli/fvim && cd fvim && dotnet publish -f netcoreapp3.1 -c Release -r linux-x64 --self-contained
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
    " good old 'set guifont' compatibility
    set guifont=Iosevka\ Slab:h16
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

### Building from source
We're now targeting `netcoreapp3.1` so make sure to install the latest preview SDK from the [.NET site](https://dotnet.microsoft.com/download/dotnet-core/3.1).
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
FVimFontNoBuiltInSymbols v:true " Disable built-in Nerd font symbols

" Try to snap the fonts to the pixels, reduces blur
" in some situations (e.g. 100% DPI).
FVimFontAutoSnap v:true

" Font weight tuning, possible valuaes are 100..900
FVimFontNormalWeight 400
FVimFontBoldWeight 700

" Font debugging -- draw bounds around each glyph
FVimFontDrawBounds v:true

" UI options (all default to v:false)
FVimUIMultiGrid v:false     " per-window grid system -- work in progress
FVimUIPopupMenu v:true      " external popup menu
FVimUITabLine v:false       " external tabline -- not implemented
FVimUICmdLine v:false       " external cmdline -- not implemented
FVimUIWildMenu v:false      " external wildmenu -- not implemented
FVimUIMessages v:false      " external messages -- not implemented
FVimUITermColors v:false    " not implemented
FVimUIHlState v:false       " not implemented

" Detach from a remote session without killing the server
" If this command is executed on a standalone instance,
" the embedded process will be terminated anyway.
FVimDetach
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
    --termcmd                   Command to run instead of the default shell.


The FVim arguments will be consumed and filtered before the rest are passed to NeoVim.
```

### Goals

- Input method support built from scratch (wip)
- Multi-grid <=> Multi-window mapping (multiple windows in the OS sense, not Vim "frames")
- Extend with UI-Protocol -- UI widgets as NeoVim plugins


### Non-Goals

- Electron ecosystem integration

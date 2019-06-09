# FVim<img src="https://github.com/yatli/fvim/raw/master/Assets/fvim.png" width="40" height="40"> [![Build status](https://ci.appveyor.com/api/projects/status/7uat5poa5bksqa89?svg=true)](https://ci.appveyor.com/project/yatli/fvim)


Cross platform Neovim front-end UI, built with [F#](https://fsharp.org/) + [Avalonia](http://avaloniaui.net/).

![Screenshot](https://github.com/yatli/fvim/raw/master/images/screenshot.png)

### Features

- HiDPI support -- try dragging it across two screens with different DPIs ;)
- Proper font rendering -- respects font style, baseline, [ligatures](https://github.com/tonsky/FiraCode) etc.
- Proper cursor rendering -- color, blink etc.
- Built-in support for Nerd font -- no need to patch your fonts!
- East Asia wide glyph display with font fallback options
- Emojis!
- High performance rendering, low latency (60FPS on 4K display with reasonable font size!)
- GPU acceleration
- Use a Windows FVim frontend with a WSL neovim: `fvim --wsl`
- Use the front end with a remote neovim: `fvim --ssh user@host`
- Use custom neovim binary: `fvim --nvim ~/bin/nvim.appimage`

Try these bindings (note, fvim-specific settings only work in `ginit.vim`, not `init.vim`!):
```vimL
if exists('g:fvim_loaded')
    " good old 'set guifont' compatibility
    set guifont=Iosevka\ Slab:h16
    " Ctrl-ScrollWheel for zooming in/out
    nnoremap <silent> <C-ScrollWheelUp> :set guifont=+<CR>
    nnoremap <silent> <C-ScrollWheelDown> :set guifont=-<CR>
    nnoremap <A-CR> :call rpcnotify(1, 'ToggleFullScreen', 1)<CR>
endif
```

Some work-in-progress fancy cursor effects:
```vimL
if exists('g:fvim_loaded')
    " 1st param = blink animation
    " 2nd param = move animation
    call rpcnotify(1, 'SetCursorAnimation', v:true, v:true)
endif
```
![fluent_cursor](https://raw.githubusercontent.com/yatli/fvim/master/images/fluent_cursor.gif)

### Goals

- Input method support built from scratch (wip)
- Multi-grid <=> Multi-window mapping (multiple windows in the OS sense, not Vim "frames")
- Extend with XAML -- UI widgets as NeoVim plugins


### Non-Goals

- Electron ecosystem integration

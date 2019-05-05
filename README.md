# FVim<img src="https://github.com/yatli/fvim/raw/master/Assets/fvim.png" width="40" height="40"> [![Build status](https://ci.appveyor.com/api/projects/status/7uat5poa5bksqa89?svg=true)](https://ci.appveyor.com/project/yatli/fvim)


Cross platform Neovim front-end UI, built with [F#](https://fsharp.org/) + [Avalonia](http://avaloniaui.net/).

### Features

- HiDPI support -- try dragging it across two screens with different DPIs ;)
- Proper font rendering -- bold, italic etc.

Try these bindings:
```vimL
    " good old 'set guifont' compatibility
    set guifont=Iosevka\ Slab:h16
    " Ctrl-ScrollWheel for zooming in/out
    nnoremap <silent> <C-ScrollWheelUp> :set guifont=+<CR>
    nnoremap <silent> <C-ScrollWheelDown> :set guifont=-<CR>
```

### Goals

- High performance rendering, low latency
- Multi-grid <=> Multi-window mapping
- Extend with XAML -- UI widgets as NeoVim plugins

### Non-Goals

- Electron ecosystem integration

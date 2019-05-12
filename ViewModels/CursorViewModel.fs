namespace FVim

open Avalonia.Media
open ReactiveUI
open FVim.neovim.def

type CursorViewModel() =
    inherit ViewModelBase()
    let mutable m_enabled: bool       = true              
    let mutable m_typeface: string    = ""                
    let mutable m_wtypeface: string   = ""                
    let mutable m_fontSize: float     = 8.0               
    let mutable m_text: string        = ""                
    let mutable m_fg: Color           = Colors.White      
    let mutable m_bg: Color           = Colors.Black      
    let mutable m_sp: Color           = Colors.Red        
    let mutable m_underline: bool     = false             
    let mutable m_undercurl: bool     = false             
    let mutable m_bold: bool          = false             
    let mutable m_italic: bool        = false             
    let mutable m_blinkon: int        = 0                 
    let mutable m_blinkoff: int       = 0                 
    let mutable m_blinkwait: int      = 0                 
    let mutable m_cellPercentage: int = 100               
    let mutable m_shape: CursorShape  = CursorShape.Block 
    let mutable m_h: float            = 1.0               
    let mutable m_w: float            = 1.0               
    let mutable m_x: float            = 0.0               
    let mutable m_y: float            = 0.0
    let mutable m_tick: int           = 0
    
    member this.enabled 
        with get(): bool = m_enabled 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_enabled, v)
    member this.typeface 
        with get(): string = m_typeface 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_typeface, v)
    member this.wtypeface 
        with get(): string = m_wtypeface 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_wtypeface, v)
    member this.fontSize 
        with get(): float = m_fontSize 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_fontSize, v)
    member this.text 
        with get(): string = m_text 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_text, v)
    member this.fg 
        with get(): Color = m_fg 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_fg, v)
    member this.bg 
        with get(): Color = m_bg 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_bg, v)
    member this.sp 
        with get(): Color = m_sp 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_sp, v)
    member this.underline 
        with get(): bool = m_underline 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_underline, v)
    member this.undercurl 
        with get(): bool = m_undercurl 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_undercurl, v)
    member this.bold 
        with get(): bool = m_bold 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_bold, v)
    member this.italic 
        with get(): bool = m_italic 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_italic, v)
    member this.blinkon 
        with get(): int = m_blinkon 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_blinkon, v)
    member this.blinkoff 
        with get(): int = m_blinkoff 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_blinkoff, v)
    member this.blinkwait 
        with get(): int = m_blinkwait 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_blinkwait, v)
    member this.cellPercentage 
        with get(): int = m_cellPercentage 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_cellPercentage, v)
    member this.shape 
        with get(): CursorShape = m_shape 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_shape, v)
    member this.h 
        with get(): float = m_h 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_h, v)
    member this.w 
        with get(): float = m_w 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_w, v)
    member this.x 
        with get(): float = m_x 
        and set(v) = 
            ignore <| this.RaiseAndSetIfChanged(&m_x, v)
    member this.y 
        with get(): float = m_y 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_y, v)
    member this.RenderTick 
        with get(): int = m_tick 
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_tick, v)

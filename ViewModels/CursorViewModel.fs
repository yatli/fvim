namespace FVim

open Avalonia.Media
open ReactiveUI
open FVim.neovim.def

type CursorViewModel() =
    inherit ViewModelBase()

    let mutable m_h: float            = 1.0               
    let mutable m_w: float            = 1.0               
    let mutable m_x: float            = 0.0               
    let mutable m_y: float            = 0.0
    let mutable m_tick: int           = 0

    member val enabled: bool       = true with get,set
    member val ingrid: bool        = true with get,set
    member val typeface: string    = "" with get,set
    member val wtypeface: string   = "" with get,set
    member val fontSize: float     = 8.0 with get,set
    member val text: string        = "" with get,set
    member val fg: Color           = Colors.White with get,set
    member val bg: Color           = Colors.Black with get,set
    member val sp: Color           = Colors.Red with get,set
    member val underline: bool     = false with get,set
    member val undercurl: bool     = false with get,set
    member val bold: bool          = false with get,set
    member val italic: bool        = false with get,set
    member val blinkon: int        = 0 with get,set
    member val blinkoff: int       = 0 with get,set
    member val blinkwait: int      = 0 with get,set
    member val cellPercentage: int = 100 with get,set
    member val shape: CursorShape  = CursorShape.Block with get,set
    
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

    member this.Clone() =
        this.MemberwiseClone() :?> CursorViewModel

    member this.VisualChecksum() =
        seq [
            hash this.typeface
            hash this.wtypeface
            hash this.fontSize
            hash this.text
            hash this.fg
            hash this.bg
            hash this.sp
            hash this.underline
            hash this.undercurl
            hash this.bold
            hash this.italic
            hash this.h
            hash this.w
        ] |> Seq.fold (^^^) 0


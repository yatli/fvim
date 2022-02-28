namespace FVim

open def
open log

open ReactiveUI
open System.Collections.ObjectModel
open System.Collections.Specialized
open System.ComponentModel
open System.Threading
open Avalonia
open Avalonia.Threading

type ObservableFastCollection<'T>() =
  inherit ObservableCollection<'T>()
  member this.AddRange(xs: 'T[]) =
    Array.iter this.Items.Add xs
    this.OnPropertyChanged(new PropertyChangedEventArgs("Count"));
    this.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));


type PopupMenuViewModel() =
    inherit ThemableViewModelBase()

    let mutable m_show = false
    let mutable m_selection = -1
    let mutable m_fontFamily = Avalonia.Media.FontFamily("Consolas")
    let mutable m_fontSize = 12.0

    let m_items = ObservableFastCollection<CompletionItemViewModel>()
    let mutable m_cancelSrc: CancellationTokenSource = new CancellationTokenSource()
    let m_itemCommit = Event<int>()

    let trace x = FVim.log.trace "CompletionItem" x

    member this.Show
        with get(): bool = m_show
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_show, v, "Show")

    member this.Selection
        with get(): int = m_selection
        and set(v) = ignore <| this.RaiseAndSetIfChanged(&m_selection, v, "Selection")

    member this.Items with get() = m_items
    member this.FontFamily with get() = m_fontFamily
    member this.FontSize with get() = m_fontSize
    member this.Commit = m_itemCommit.Publish

    member this.SetFont(fontfamily, fontsize) =
        ignore <| this.RaiseAndSetIfChanged(&m_fontFamily, Avalonia.Media.FontFamily(fontfamily), "FontFamily")
        ignore <| this.RaiseAndSetIfChanged(&m_fontSize, fontsize, "FontSize")

    member this.SetItems(items: CompleteItem[], startPos: Point, cursorPos: Point, lineHeight: float, desiredSizeVec: Point, editorSizeVec: Point) =
        m_items.Clear()

        //  New completion items coming in while old still being added?
        //  Cancel them, otherwise it blocks the UI thread/add dups.
        //  --------------------------------
        if m_cancelSrc <> null then
            m_cancelSrc.Cancel()
            m_cancelSrc.Dispose()

        //  Decide where to show the menu.
        //  --------------------------------

        let _cap (vec: Point) =
            let x = min editorSizeVec.X (max 0.0 vec.X)
            let y = min editorSizeVec.Y (max 0.0 vec.Y)
            Point(x, y)

        let lineArea = Rect(startPos, cursorPos)

        let padding = Point(20.0 + 36.0, 16.0) // extra 36 for icons etc

        let se_topleft     = lineArea.BottomLeft
        let se_bottomright = _cap(se_topleft + desiredSizeVec + padding)

        let ne_topleft     = _cap(lineArea.TopLeft - Point(0.0, desiredSizeVec.Y + padding.Y))
        let ne_bottomright = _cap(lineArea.TopLeft + Point(desiredSizeVec.X + padding.X, 0.0))

        let sw_topleft     = _cap(lineArea.BottomRight - Point(desiredSizeVec.X + padding.X, 0.0))
        let sw_bottomright = _cap(lineArea.BottomRight + Point(0.0, desiredSizeVec.Y + padding.Y))

        let nw_bottomright = lineArea.TopRight
        let nw_topleft     = _cap(nw_bottomright - desiredSizeVec - padding)

        let r_se = Rect(se_topleft, se_bottomright)
        let r_ne = Rect(ne_topleft, ne_bottomright)
        let r_sw = Rect(sw_topleft, sw_bottomright)
        let r_nw = Rect(nw_topleft, nw_bottomright)

        let r_e = 
          if r_se.Height > desiredSizeVec.Y / 3.0 || r_se.Height > r_ne.Height then
            trace "r_e: choose region SE: %A" r_se
            r_se
          else
            trace "r_e: choose region NE: %A" r_ne
            r_ne

        let r_w = 
          if r_sw.Height > desiredSizeVec.Y / 3.0 || r_sw.Height > r_nw.Height then
            trace "r_w: choose region SW: %A" r_sw
            r_sw
          else
            trace "r_w: choose region NW: %A" r_nw
            r_nw

        let region =
          if r_e.Width > desiredSizeVec.X * 0.75 || r_e.Width > r_w.Width then
            trace "region: choose region EAST: %A" r_e
            r_e
          else
            trace "region: choose region WEST: %A" r_w
            r_w

        this.X <- region.X
        this.Y <- region.Y
        this.Width <- region.Width
        this.Height <- region.Height

        let updateItem (x: CompletionItemViewModel) =
            x.Height <- lineHeight

        let chunks = 
            items 
            |> Array.mapi (fun idx item -> CompletionItemViewModel(item, fun () -> m_itemCommit.Trigger idx))
            |> Array.chunkBySize 16

        m_cancelSrc <- new CancellationTokenSource()
        m_cancelSrc.CancelAfter(200)
        let token = m_cancelSrc.Token
        backgroundTask {
            for chunk in chunks do
                if token.IsCancellationRequested then return ()
                do! Dispatcher.UIThread.InvokeAsync(fun () -> 
                    // new items made their way to the UI thread, abort
                    if not token.IsCancellationRequested then 
                      Array.iter updateItem chunk
                      m_items.AddRange chunk
                      )
        } |> ignore

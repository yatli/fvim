namespace FVim

open neovim.def
open log
open common
open Avalonia.Media.Imaging
open Avalonia.Platform
open Avalonia

module CompletionItemHelper =
    //  Taken from coc.nvim/src/languages.ts
    type CompletionItemKind =
        | Text //'v'],
        | Method //'f'],
        | Function //'f'],
        | Constructor //'function' ? 'f' : labels['con' + 'structor']],
        | Field //'m'],
        | Variable //'v'],
        | Class //'C'],
        | Interface //'I'],
        | Module //'M'],
        | Property //'m'],
        | Unit //'U'],
        | Value //'v'],
        | Enum //'E'],
        | Keyword //'k'],
        | Snippet //'S'],
        | Color //'v'],
        | File //'F'],
        | Reference //'r'],
        | Folder //'F'],
        | EnumMember //'m'],
        | Constant //'v'],
        | Struct //'S'],
        | Event //'E'],
        | Operator //'O'],
        | TypeParameter //'T'],
        | Unknown
    let CompletionItemKindIconNames = Map [
        Text, "Text"
        Method, "Method"
        Function, "Method"
        Constructor, "NewClass"
        Field, "Field"
        Variable, "LocalVariable"
        Class, "Class"
        Interface, "Interface"
        Module, "Module"
        Property, "Property"
        Unit, "Dimension"
        Value, "Literal"
        Enum, "Enumerator"
        Keyword, "IntelliSenseKeyword"
        Snippet, "Snippet"
        Color, "ColorPalette"
        File, "TextFile"
        Reference, "Reference"
        Folder, "Folder"
        EnumMember, "EnumItem"
        Constant, "Constant"
        Struct, "Structure"
        Event, "Event"
        Operator, "Operator"
        TypeParameter, "Type"
        Unknown, "Unknown"
    ]

    let ParseCompletionItemKind abbr = 
        match _d "" abbr with
        | "t" -> Text
        | ":" -> Method
        | "f" -> Function
        | "c" -> Constructor
        | "." -> Field
        | "v" -> Variable
        | "C" -> Class
        | "I" -> Interface
        | "M" -> Module
        | "p" -> Property
        | "U" -> Unit
        | "l" -> Value
        | "E" -> Enum
        | "k" -> Keyword
        | "s" -> Snippet
        | "K" -> Color
        | "F" -> File
        | "r" -> Reference
        | "d" -> Folder
        | "m" -> EnumMember
        | "0" -> Constant
        | "S" -> Struct
        | "e" -> Event
        | "o" -> Operator
        | "T" -> TypeParameter
        | _ -> Unknown

    let assets = AvaloniaLocator.Current.GetService<IAssetLoader>()
    

    let CompletionItemKindIcons = 
        CompletionItemKindIconNames |>
        Map.map (fun k name -> 
            if name = "Unknown" then null
            else
            let fname = sprintf "avares://FVim/Assets/intellisense/%s_16x.png" name
            (*trace "CompleteItem" "loading intellisense icon %s" fname*)
            new Bitmap(assets.Open(new System.Uri(fname)))) 

open CompletionItemHelper

type CompletionItemViewModel(item: CompleteItem) =
    inherit ViewModelBase()
    let kind = ParseCompletionItemKind item.abbr
    (*do*)
        (*trace "CompletionItemViewModel" "item = %A" item*)
    member __.Text = item.word
    member __.Menu = _d "" item.menu
    member __.Info = _d "" item.info
    member __.Kind = kind
    member __.Icon = CompletionItemKindIcons.[kind]


#r "packages/FSharp.Data/lib/netstandard2.0/FSharp.Data.dll"
open FSharp.Data
open System

type Emoji = CsvProvider<"emoji.tsv", "\t">

let emoji = Emoji.Load("emoji.tsv")

type EmojiCodepoint = 
    {
        Codepoint: int
        Remarks: string
    }

let _null = {Codepoint=0x0;Remarks=""}

let getCodepoint (cp: string) =
    let cp = cp.Split([|' '|], StringSplitOptions.RemoveEmptyEntries).[0]
    Int32.Parse(cp.Substring(2), Globalization.NumberStyles.HexNumber)

let _, (folded: (EmojiCodepoint * EmojiCodepoint) list) = 
    emoji.Rows
    |> Seq.map (fun row -> { Codepoint = getCodepoint row.Codepoints; Remarks = row.Remarks})
    |> Seq.fold (fun ((prev_from, prev_to), lst) ({Codepoint=cp} as current) -> 
            if prev_to.Codepoint + 1 = cp || prev_to.Codepoint = cp then
                ((prev_from, current), lst)
            else
                (current, current), ((prev_from, prev_to) :: lst)
                ) ((_null, _null), [])

folded 
    |> List.rev 
    |> List.iter (fun ({Codepoint=cp_from; Remarks=r_from}, {Codepoint=cp_to; Remarks=r_to}) ->
        printfn "    (0x%X, 0x%X)  // %-24s..%-24s" cp_from cp_to r_from r_to
    )

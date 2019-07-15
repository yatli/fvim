module FVim.Common

open System.Runtime.InteropServices

let ParseUInt16 (x: string) =
    match System.UInt16.TryParse x with
    | true, x -> Some x
    | _ -> None

let (|ParseUInt16|_|) (x: string) =
    match System.UInt16.TryParse x with
    | true, x -> Some x
    | _ -> None

let (|ParseInt32|_|) (x: string) =
    match System.Int32.TryParse x with
    | true, x -> Some x
    | _ -> None

let (|ParseIp|_|) (x: string) =
    match System.Net.IPAddress.TryParse x with
    | true, x -> Some x
    | _ -> 
        try 
            System.Net.Dns.GetHostEntry(x).AddressList
            |> Seq.tryFind (fun addr -> addr.AddressFamily = System.Net.Sockets.AddressFamily.InterNetwork)
        with _ -> None

type hashmap<'a, 'b> = System.Collections.Generic.Dictionary<'a, 'b>
let hashmap (xs: seq<'a*'b>) = new hashmap<'a,'b>(xs |> Seq.map (fun (a,b) -> System.Collections.Generic.KeyValuePair(a,b)))

let join (xs: string seq) = System.String.Join(" ", xs)

let inline (>>=) (x: 'a option) (f: 'a -> 'b option) =
    match x with
    | Some x -> f x
    | _ -> None


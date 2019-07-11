module FVim.Common

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
        try System.Net.Dns.GetHostEntry(x).AddressList.[0] |> Some
        with _ -> None

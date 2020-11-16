module FVim.Daemon

open System
open System.IO.Pipes
open System.Runtime.InteropServices
open System.Diagnostics
open System.Threading.Tasks

open FSharp.Control.Tasks.V2.ContextSensitive
open FSharp.Span.Utils
open FSharp.Json

open log
open common
open getopt
open System.Security.Principal
open MessagePack
open System.IO

type Session =
  {
    id: int
    // None=Free to connect
    // Some=Exclusively connected
    server: NamedPipeServerStream option
    proc: Process
    // in case the daemon crashed and we happen to be running Windows(tm)...
    killHandle: IDisposable
  }

let private sessions = hashmap []
let mutable private sessionId = 0
let FVR_MAGIC = [| 0x46uy ; 0x56uy ; 0x49uy ; 0x4Duy |]

let inline private trace x = trace "daemon" x

let pipeaddr x =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    then @"\\.\pipe\" + x
    else "/tmp/" + x

let pipename = sprintf "fvr_%s"

let defaultDaemonName = pipename "main"

let attachSession id svrpipe =
  match sessions.TryGetValue id with
  | true, ({ server = None } as s) -> 
    let ns = {s with server = Some svrpipe}
    sessions.[id] <- ns
    Some ns
  | _ -> None

let newSession nvim stderrenc args svrpipe = 
  let myid = sessionId

  let pname = pipename (string myid)
  let paddr = pipeaddr pname
  let proc = runProcess nvim ("--headless" :: "--listen" :: paddr :: args) stderrenc
  let sub = AppDomain.CurrentDomain.ProcessExit.Subscribe(fun _ -> proc.Kill(true))
  let session = 
    { 
      id = myid 
      server = Some svrpipe
      proc = proc
      killHandle = sub
    }

  sessionId <- sessionId + 1
  sessions.[myid] <- session
  Some session


let attachFirstSession svrpipe =
  sessions |> Seq.tryFind (fun kv -> kv.Value.server.IsNone)
  >>= fun kv ->
    let ns = {kv.Value with server = Some svrpipe}
    sessions.[kv.Key] <- ns
    Some ns

let serveSession (session: Session) =
  task {
    let pname = pipename (string session.id)
    use client = new NamedPipeClientStream(".", pname, IO.Pipes.PipeDirection.InOut, IO.Pipes.PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation)

    let fromNvim = client.CopyToAsync(session.server.Value)
    let toNvim = session.server.Value.CopyToAsync(client)
    let! _ = Task.WhenAny [| fromNvim; toNvim |]
    // Something is completed, let's investigate why
    if not client.IsConnected then
      // the connection to neovim server is gone
      session.proc.Kill(true)
      // remove the session
      sessions.Remove(session.id) |> ignore
    else
      // the connection from the remote FVim is gone
      sessions.[session.id] <- { session with server = None }
    return ()
  }

let serve nvim stderrenc (pipe: NamedPipeServerStream) = 
  run <| task {
    try
      let rbuf = Array.zeroCreate 8192
      let rmem = rbuf.AsMemory()
      // read protocol header
      // [magic header FVIM] 4B
      // [payload len] 4B, little-endian
      do! read pipe rmem.[0..7]
      if rbuf.[0..3] <> FVR_MAGIC then return()
      let len = rbuf.[4..7] |> toInt32LE
      if len >= rbuf.Length || len <= 0 then return()
      do! read pipe rmem.[0..len-1]
      let request: FVimRemoteVerb = 
        (rbuf, 0, len)
        |> Text.Encoding.UTF8.GetString
        |> Json.deserialize
      let session = 
        match request with
        | NewSession args -> newSession nvim stderrenc args pipe
        | AttachTo id -> attachSession id pipe
        | AttachFirst -> attachFirstSession pipe

      match session with
      | None -> return()
      | Some session -> do! serveSession session
    finally
      pipe.Dispose()
  }

let daemon (pname: string option) (nvim: string) (stderrenc: Text.Encoding) =
    trace "Running as daemon."
    let pname = pname |> Option.defaultValue defaultDaemonName
    let paddr = pipeaddr pname
    trace "FVR server address is '%s'" paddr

    while true do
      runSync <| task {
        let svrpipe =
            new NamedPipeServerStream(pname, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                                      PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
        do! svrpipe.WaitForConnectionAsync()
        serve nvim stderrenc svrpipe
      }
    0

let fvrConnect (stdin: Stream) (stdout: Stream) (verb: FVimRemoteVerb) =
  let payload = 
    verb
    |> Json.serialize
    |> Text.Encoding.UTF8.GetBytes
  let len = fromInt32LE payload.Length
  stdin.Write(FVR_MAGIC, 0, FVR_MAGIC.Length)
  stdin.Write(len, 0, len.Length)
  stdin.Write(payload, 0, payload.Length)

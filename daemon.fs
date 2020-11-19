module FVim.daemon

open System
open System.IO.Pipes
open System.Runtime.InteropServices
open System.Diagnostics
open System.Threading.Tasks

open FSharp.Span.Utils
open FSharp.Json

open log
open common
open getopt
open System.Security.Principal
open System.IO

type Session =
  {
    id: int
    // None=Free to connect
    // Some=Exclusively connected
    server: NamedPipeServerStream option
    proc: Process
    exitHandle: IDisposable
  }

let private sessions = hashmap []
let mutable private sessionId = 0
let FVR_MAGIC = [| 0x46uy ; 0x56uy ; 0x49uy ; 0x4Duy |]

let inline private trace x = trace "daemon" x

let pipeaddrUnix x = "/tmp/CoreFxPipe_" + x
let pipeaddr x =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    then @"\\.\pipe\" + x
    else pipeaddrUnix x

let pipename = sprintf "fvr_%s"

let defaultDaemonName = pipename "main"

let attachSession id svrpipe =
  match sessions.TryGetValue id with
  | true, ({ server = None } as s) -> 
    let ns = {s with server = Some svrpipe}
    sessions.[id] <- ns
    Ok ns
  | _ -> Error -2

let newSession nvim stderrenc args svrpipe = 
  let myid = sessionId

  let pname = pipename (string myid)
  let paddr = pipeaddr pname
  let args = "--headless" :: "--listen" :: paddr :: args
  let proc = newProcess nvim args stderrenc
  let session = 
    { 
      id = myid 
      server = Some svrpipe
      proc = proc
      exitHandle =  proc.Exited |> Observable.subscribe(fun _ -> 
        // remove the session
        trace "Session %d terminated" myid
        sessions.[myid].exitHandle.Dispose()
        sessions.Remove(myid) |> ignore
        proc.Dispose()
        )
    }

  sessionId <- sessionId + 1
  sessions.[myid] <- session
  proc.Start() |> ignore
  Ok session


let attachFirstSession svrpipe =
  sessions |> Seq.tryFind (fun kv -> kv.Value.server.IsNone)
  >>= (fun kv ->
    let ns = {kv.Value with server = Some svrpipe}
    sessions.[kv.Key] <- ns
    Some ns)
  |> function | Some ns -> Ok ns | None -> Error -1

let serveSession (session: Session) =
  async {
    let pname = pipename (string session.id)
    use client = new NamedPipeClientStream(".", pname, IO.Pipes.PipeDirection.InOut, IO.Pipes.PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation)
    do! Async.AwaitTask(client.ConnectAsync())
    let fromNvim = client.CopyToAsync(session.server.Value)
    let toNvim = session.server.Value.CopyToAsync(client)
    let! _ = Async.AwaitTask(Task.WhenAny [| fromNvim; toNvim |])
    // Something is completed, let's investigate why
    if not session.proc.HasExited then
      // the NeoVim server is still up and running
      sessions.[session.id] <- { session with server = None }
      trace "Session %d detached" session.id
    return ()
  }

let serve nvim stderrenc (pipe: NamedPipeServerStream) = 
  async {
    try
      let rbuf = Array.zeroCreate 8192
      let rmem = rbuf.AsMemory()
      // read protocol header
      // [magic header FVIM] 4B
      // [payload len] 4B, little-endian
      do! read pipe rmem.[0..7]
      if rbuf.[0..3] <> FVR_MAGIC then 
        trace "Incorrect handshake magic. Got: %A" rbuf.[0..3]
        return()
      let len = rbuf.[4..7] |> toInt32LE
      if len >= rbuf.Length || len <= 0 then 
        trace "Invalid payload length %d" len
        return()
      do! read pipe rmem.[0..len-1]

      let request: FVimRemoteVerb = 
        (rbuf, 0, len)
        |> Text.Encoding.UTF8.GetString
        |> Json.deserialize
      trace "Payload=%A" request
      let session = 
        match request with
        | NewSession args -> newSession nvim stderrenc args pipe
        | AttachTo id -> attachSession id pipe
        | AttachFirst _ -> attachFirstSession pipe

      match session with
      | Error errno -> 
        trace "Session unavailable for request %A, errno=%d" request errno
        do! fromInt32LE errno |> readonlymemory |> write pipe
        return()
      | Ok session -> 
        trace "Request %A is attaching to session %d" request session.id
        do! fromInt32LE session.id |> readonlymemory |> write pipe
        do! serveSession session
    finally
      try
        pipe.Dispose()
      with ex -> trace "%O" ex
  }

let daemon (pname: string option) (nvim: string) (stderrenc: Text.Encoding) =
    trace "Running as daemon."
    let pname = pname |> Option.defaultValue defaultDaemonName
    let paddr = pipeaddr pname
    trace "FVR server address is '%s'" paddr

    Async.RunSynchronously <| async {
      while true do
        let svrpipe =
            new NamedPipeServerStream(pname, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                                      PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
        do! Async.AwaitTask(svrpipe.WaitForConnectionAsync())
        trace "Incoming connection."
        Async.Start <| serve nvim stderrenc svrpipe
      return ()
    }
    0

let fvrConnect (stdin: Stream) (stdout: Stream) (verb: FVimRemoteVerb) =
  let payload = 
    verb
    |> Json.serialize
    |> Text.Encoding.UTF8.GetBytes
  let intbuf = fromInt32LE payload.Length
  try
    stdin.Write(FVR_MAGIC, 0, FVR_MAGIC.Length)
    stdin.Write(intbuf, 0, intbuf.Length)
    stdin.Write(payload, 0, payload.Length)
    stdin.Flush()
    Async.StartAsTask(read stdout (intbuf.AsMemory())).Wait()
    toInt32LE intbuf
  with ex ->
    trace "%O" ex
    -10

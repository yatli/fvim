module FVim.msgpack

open common
open System.Buffers
open MessagePack
open MessagePack.Resolvers
open MessagePack.Formatters

type MsgPackMatchState =
| Init = 0
| WaitDict = 1
| GetBinaryData = 2

type MsgPackFormatter(resolver: IFormatterResolver) = 
  let m_formatter = resolver.GetFormatter<obj>()
  let mutable m_state = MsgPackMatchState.Init
  interface IMessagePackFormatter<obj> with
    member this.Serialize(writer: byref<MessagePackWriter>, value: obj, options: MessagePackSerializerOptions): unit = 
      m_formatter.Serialize(&writer, value, options)
    member this.Deserialize(reader: byref<MessagePackReader>, options: MessagePackSerializerOptions): obj = 
      match reader.NextMessagePackType with
      | MessagePackType.Extension ->
        let result = reader.ReadExtensionFormat()
        let mutable data = result.Data
        MessagePackSerializer.Deserialize(&data, options)
      | MessagePackType.Map ->
        options.Security.DepthStep(&reader)
        try
            let header = reader.ReadMapHeader()
            options.Security.DepthStep(&reader)
            this.DeserializeMap(&reader, header, options)
        finally
            m_state <- MsgPackMatchState.Init
            reader.Depth <- reader.Depth - 1
      | MessagePackType.String ->
        match m_state with
        | MsgPackMatchState.Init ->
            let str = reader.ReadString()
            if str = "GuiWidgetPut" then
                m_state <- MsgPackMatchState.WaitDict
            box str
        | MsgPackMatchState.GetBinaryData ->
            m_state <- MsgPackMatchState.Init
            let bin = reader.ReadStringSequence().Value
            box <| bin.ToArray()
        | _ -> 
            let str = reader.ReadString()
            box str
      | _ -> 
        m_formatter.Deserialize(&reader, options)
  member this.DeserializeMap(reader: byref<MessagePackReader>, length: int, options: MessagePackSerializerOptions) =
    let dict = System.Collections.Generic.Dictionary<obj, obj>()
    let formatter = this :> IMessagePackFormatter<obj>
    for i = 1 to length do
        let key = formatter.Deserialize(&reader, options)
        let key = match key, m_state with
                  | String("data"), MsgPackMatchState.WaitDict -> 
                    m_state <- MsgPackMatchState.GetBinaryData
                    key
                  | _ -> key
        let value = formatter.Deserialize(&reader, options)
        dict.Add(key, value)
    box dict

let private standardResolver = MessagePack.Resolvers.StandardResolver.Instance
let private myFormatter = box(MsgPackFormatter(standardResolver))

type MsgPackResolver() =
  interface IFormatterResolver with
    member x.GetFormatter<'a>() =
      if typeof<'a> = typeof<obj> then
        myFormatter :?> IMessagePackFormatter<'a>
      else
        standardResolver.GetFormatter<'a>()

let msgpackResolver = MsgPackResolver()
let msgpackOpts = MessagePack
                    .MessagePackSerializerOptions.Standard
                    .WithResolver(msgpackResolver)
                    .WithAllowAssemblyVersionMismatch(true)
                    .WithOmitAssemblyVersion(true)

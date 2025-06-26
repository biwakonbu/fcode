module FCode.UnixDomainSocketManager

open System
open System.Buffers
open System.IO
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FCode.Logger

// ===============================================
// メッセージエンベロープ・フレーミング
// ===============================================

/// バージョン管理対応のメッセージエンベロープ
type Envelope<'T> =
    { Version: int
      MessageId: string
      Timestamp: DateTime
      Data: 'T }

/// JSON シリアライゼーション設定
let jsonOptions = JsonSerializerOptions()
jsonOptions.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
jsonOptions.WriteIndented <- false

/// エンベロープ作成ヘルパー
let createEnvelope<'T> (data: 'T) =
    { Version = 1
      MessageId = Guid.NewGuid().ToString()
      Timestamp = DateTime.UtcNow
      Data = data }

// ===============================================
// フレーミングプロトコル (4-byte length prefix)
// ===============================================

/// メッセージを4-byte big-endian length prefix + UTF-8 JSON形式にシリアライズ
let serializeMessage<'T> (envelope: Envelope<'T>) : byte[] =
    try
        let json = JsonSerializer.Serialize(envelope, jsonOptions)
        let jsonBytes = Encoding.UTF8.GetBytes(json)
        let length = jsonBytes.Length

        // 4-byte big-endian length prefix
        let lengthBytes = BitConverter.GetBytes(length)

        if BitConverter.IsLittleEndian then
            Array.Reverse(lengthBytes)

        // length prefix + JSON payload
        let result = Array.zeroCreate<byte> (4 + length)
        Array.Copy(lengthBytes, 0, result, 0, 4)
        Array.Copy(jsonBytes, 0, result, 4, length)

        logDebug "UDS" $"Serialized message: {envelope.MessageId}, length: {length}"
        result

    with ex ->
        logException "UDS" $"Failed to serialize message: {envelope.MessageId}" ex
        raise ex

/// ストリームから4-byte length prefixを読み取る
let rec readLengthPrefix (stream: Stream) (cancellationToken: CancellationToken) : Task<int> =
    task {
        let lengthBuffer = Array.zeroCreate<byte> (4)
        let mutable totalRead = 0

        while totalRead < 4 do
            let! bytesRead = stream.ReadAsync(lengthBuffer, totalRead, 4 - totalRead, cancellationToken)

            if bytesRead = 0 then
                raise (EndOfStreamException("Connection closed while reading length prefix"))

            totalRead <- totalRead + bytesRead

        // big-endian to host byte order
        if BitConverter.IsLittleEndian then
            Array.Reverse(lengthBuffer)

        let length = BitConverter.ToInt32(lengthBuffer, 0)

        // サニティチェック: 最大10MB
        if length <= 0 || length > 10_485_760 then
            raise (InvalidDataException($"Invalid message length: {length}"))

        logDebug "UDS" $"Read length prefix: {length}"
        return length
    }

/// ストリームから指定長のペイロードを読み取る
let rec readPayload (stream: Stream) (length: int) (cancellationToken: CancellationToken) : Task<byte[]> =
    task {
        let buffer = Array.zeroCreate<byte> (length)
        let mutable totalRead = 0

        while totalRead < length do
            let! bytesRead = stream.ReadAsync(buffer, totalRead, length - totalRead, cancellationToken)

            if bytesRead = 0 then
                raise (EndOfStreamException("Connection closed while reading payload"))

            totalRead <- totalRead + bytesRead

        logDebug "UDS" $"Read payload: {length} bytes"
        return buffer
    }

/// フレーミングされたメッセージをデシリアライズ
let deserializeMessage<'T> (messageBytes: byte[]) : Envelope<'T> =
    try
        let json = Encoding.UTF8.GetString(messageBytes)
        let envelope = JsonSerializer.Deserialize<Envelope<'T>>(json, jsonOptions)

        logDebug "UDS" $"Deserialized message: {envelope.MessageId}"
        envelope

    with ex ->
        logException "UDS" "Failed to deserialize message" ex
        raise ex

// ===============================================
// Unix Domain Socket 管理
// ===============================================

/// UDS接続の設定
type UdsConfig =
    { SocketPath: string
      ConnectionTimeoutMs: int
      ReceiveTimeoutMs: int
      SendTimeoutMs: int
      BufferSize: int }

let defaultUdsConfig socketPath =
    { SocketPath = socketPath
      ConnectionTimeoutMs = 5000
      ReceiveTimeoutMs = 30000
      SendTimeoutMs = 10000
      BufferSize = 65536 }

/// UDS接続ラッパー
type UdsConnection(socket: Socket, config: UdsConfig) =
    let networkStream = new NetworkStream(socket)
    let mutable disposed = false

    /// メッセージを非同期送信
    member _.SendAsync<'T>(envelope: Envelope<'T>, ?cancellationToken: CancellationToken) : Task =
        task {
            let token = defaultArg cancellationToken CancellationToken.None

            try
                let messageBytes = serializeMessage envelope
                do! networkStream.WriteAsync(messageBytes, 0, messageBytes.Length, token)
                do! networkStream.FlushAsync(token)

                logDebug "UDS" $"Sent message: {envelope.MessageId}, {messageBytes.Length} bytes"

            with ex ->
                logException "UDS" $"Failed to send message: {envelope.MessageId}" ex
                return! failwith ex.Message
        }

    /// メッセージを非同期受信
    member _.ReceiveAsync<'T>(?cancellationToken: CancellationToken) : Task<Envelope<'T>> =
        task {
            let token = defaultArg cancellationToken CancellationToken.None

            try
                // 1. length prefixを読み取り
                let! length = readLengthPrefix networkStream token

                // 2. ペイロードを読み取り
                let! payload = readPayload networkStream length token

                // 3. デシリアライズ
                let envelope = deserializeMessage<'T> payload

                logDebug "UDS" $"Received message: {envelope.MessageId}"
                return envelope

            with ex ->
                logException "UDS" "Failed to receive message" ex
                return! failwith ex.Message
        }

    /// 接続が生きているかチェック
    member _.IsConnected = not disposed && socket.Connected

    /// 接続を閉じる
    member _.Close() =
        if not disposed then
            try
                networkStream.Close()
                socket.Close()
                disposed <- true
                logDebug "UDS" "Connection closed"
            with ex ->
                logException "UDS" "Error closing connection" ex

    interface IDisposable with
        member this.Dispose() = this.Close()

// ===============================================
// UDS サーバー
// ===============================================

/// UDS サーバー
type UdsServer(config: UdsConfig) as self =
    let mutable serverSocket: Socket option = None
    let mutable isListening = false
    let cancellationTokenSource = new CancellationTokenSource()

    /// 新しい接続を処理するコールバック
    member val OnClientConnected: (UdsConnection -> unit) option = None with get, set

    /// サーバーを開始
    member _.StartAsync() : Task =
        task {
            if isListening then
                raise (InvalidOperationException("Server is already listening"))

            try
                // 既存のソケットファイルを削除
                if File.Exists(config.SocketPath) then
                    File.Delete(config.SocketPath)

                let socket =
                    new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)

                let endpoint = UnixDomainSocketEndPoint(config.SocketPath)

                socket.Bind(endpoint)
                socket.Listen(10) // バックログサイズ

                serverSocket <- Some socket
                isListening <- true

                logInfo "UDS" $"Server listening on: {config.SocketPath}"

                // 接続受付ループ
                let! _ =
                    Task.Run(fun () ->
                        while isListening && not cancellationTokenSource.Token.IsCancellationRequested do
                            try
                                let clientSocket = socket.Accept()
                                let connection = new UdsConnection(clientSocket, config)

                                logInfo "UDS" "Client connected"

                                // コールバックで処理
                                match self.OnClientConnected with
                                | Some handler -> handler connection
                                | None -> logWarning "UDS" "No client connection handler set"

                            with ex ->
                                if isListening then
                                    logException "UDS" "Error accepting client connection" ex)

                return ()

            with ex ->
                logException "UDS" "Failed to start server" ex
                return! failwith ex.Message
        }

    /// サーバーを停止
    member _.Stop() =
        if isListening then
            isListening <- false
            cancellationTokenSource.Cancel()

            match serverSocket with
            | Some socket ->
                try
                    socket.Close()

                    if File.Exists(config.SocketPath) then
                        File.Delete(config.SocketPath)

                    logInfo "UDS" "Server stopped"
                with ex ->
                    logException "UDS" "Error stopping server" ex

                serverSocket <- None
            | None -> ()

    interface IDisposable with
        member this.Dispose() =
            this.Stop()
            cancellationTokenSource.Dispose()

// ===============================================
// UDS クライアント
// ===============================================

/// UDS クライアント
type UdsClient(config: UdsConfig) =

    /// サーバーに接続
    member _.ConnectAsync(?cancellationToken: CancellationToken) : Task<UdsConnection> =
        task {
            let token = defaultArg cancellationToken CancellationToken.None

            try
                let socket =
                    new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)

                let endpoint = UnixDomainSocketEndPoint(config.SocketPath)

                // タイムアウト設定
                socket.ReceiveTimeout <- config.ReceiveTimeoutMs
                socket.SendTimeout <- config.SendTimeoutMs

                do! socket.ConnectAsync(endpoint)

                let connection = new UdsConnection(socket, config)
                logInfo "UDS" $"Connected to server: {config.SocketPath}"

                return connection

            with ex ->
                logException "UDS" $"Failed to connect to server: {config.SocketPath}" ex
                return! failwith ex.Message
        }

// ===============================================
// 便利な関数
// ===============================================

/// UDS サーバーを作成・開始
let createServer socketPath onClientConnected =
    let config = defaultUdsConfig socketPath
    let server = new UdsServer(config)
    server.OnClientConnected <- Some onClientConnected
    server, server.StartAsync()

/// UDS クライアントで接続
let connectToServer socketPath =
    let config = defaultUdsConfig socketPath
    let client = new UdsClient(config)
    client.ConnectAsync()

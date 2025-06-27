module FCode.IPCChannel

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open FCode.Logger
open FCode.UnixDomainSocketManager

// ===============================================
// セッションコマンド定義
// ===============================================

/// 各ペインセッションに対するコマンド
type SessionCommand =
    | StartSession of PaneId: string * WorkingDir: string
    | StopSession of PaneId: string
    | SendInput of PaneId: string * Input: string
    | RequestOutput of PaneId: string
    | HealthCheck of PaneId: string
    | RestartSession of PaneId: string

/// セッションレスポンス
type SessionResponse =
    | SessionStarted of PaneId: string * SessionId: string * ProcessId: int
    | SessionStopped of PaneId: string
    | InputProcessed of PaneId: string
    | OutputData of PaneId: string * Data: string
    | HealthStatus of PaneId: string * IsHealthy: bool * Details: string
    | SessionRestarted of PaneId: string * NewSessionId: string
    | Error of PaneId: string * ErrorMessage: string

// ===============================================
// IPC チャネル設定
// ===============================================

/// IPCチャネルの設定
type IPCChannelConfig =
    { MaxConcurrentRequests: int
      ChannelCapacity: int
      RequestTimeoutMs: int
      BackpressureThreshold: int
      BackpressurePolicy: BackpressurePolicy
      BatchProcessingMs: int
      MaxRetryAttempts: int
      RetryDelayMs: int
      ConnectionTimeoutMs: int
      HeartbeatIntervalMs: int }

and BackpressurePolicy =
    | DropOldest
    | DropNewest
    | BlockUntilSpace
    | ThrowException

let defaultIPCConfig =
    { MaxConcurrentRequests = 100
      ChannelCapacity = 1000
      RequestTimeoutMs = 30000
      BackpressureThreshold = 800
      BackpressurePolicy = DropOldest
      BatchProcessingMs = 16
      MaxRetryAttempts = 3
      RetryDelayMs = 1000
      ConnectionTimeoutMs = 5000
      HeartbeatIntervalMs = 30000 }

// ===============================================
// メッセージ順序制御・バックプレッシャ
// ===============================================

/// 内部処理用のリクエスト構造
type internal IPCRequest =
    { RequestId: string
      Command: SessionCommand
      ResponseChannel: Channel<SessionResponse>
      Timestamp: DateTime
      CancellationToken: CancellationToken }

/// メトリクス情報
type IPCMetrics =
    { QueueLength: int
      ProcessedRequests: int64
      DroppedRequests: int64
      AverageLatencyMs: float
      ErrorCount: int64 }

/// IPC チャネル実装（単一コンシューマ + 複数プロデューサ）
type IPCChannel(config: IPCChannelConfig) =

    // 単一コンシューマ用のチャネル
    let requestChannel =
        Channel.CreateBounded<IPCRequest>(
            BoundedChannelOptions(
                config.ChannelCapacity,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            )
        )

    // アクティブな接続管理
    let activeConnections = ConcurrentDictionary<string, UdsConnection>()

    // メトリクス
    let mutable processedRequests = 0L
    let mutable droppedRequests = 0L
    let mutable totalLatencyMs = 0L
    let mutable errorCount = 0L
    let metricsLock = obj ()

    // キャンセレーション
    let cancellationTokenSource = new CancellationTokenSource()
    let processingTask = ref None

    // バックプレッシャ制御
    let handleBackpressure (request: IPCRequest) =
        let currentLength = requestChannel.Reader.Count

        if currentLength >= config.BackpressureThreshold then
            logWarning "IPC" $"Backpressure threshold reached: {currentLength}/{config.ChannelCapacity}"

            match config.BackpressurePolicy with
            | DropOldest ->
                // 古いリクエストを1つドロップしてスペースを作る
                let mutable dropped = false

                while not dropped do
                    match requestChannel.Reader.TryRead() with
                    | true, _ ->
                        lock metricsLock (fun () -> droppedRequests <- droppedRequests + 1L)
                        dropped <- true
                    | false, _ -> dropped <- true

                logWarning "IPC" "Dropped oldest request due to backpressure"
                true

            | DropNewest ->
                lock metricsLock (fun () -> droppedRequests <- droppedRequests + 1L)
                logWarning "IPC" "Dropping newest request due to backpressure"
                false

            | BlockUntilSpace ->
                logInfo "IPC" "Blocking until channel space available"
                true

            | ThrowException -> raise (InvalidOperationException($"Channel capacity exceeded: {currentLength}"))
        else
            true

    // 単一コンシューマによる処理ループ
    let rec processRequests () =
        task {
            logInfo "IPC" "Starting IPC request processing loop"

            try
                while not cancellationTokenSource.Token.IsCancellationRequested do
                    try
                        // リクエストをバッチで読み取り
                        let! request = requestChannel.Reader.ReadAsync(cancellationTokenSource.Token)

                        let startTime = DateTime.UtcNow

                        // リクエスト処理
                        let! response = processRequest request.Command

                        // レスポンス送信
                        if not (request.ResponseChannel.Writer.TryWrite(response)) then
                            logWarning "IPC" $"Failed to write response for request: {request.RequestId}"

                        // メトリクス更新
                        let latency = (DateTime.UtcNow - startTime).TotalMilliseconds

                        lock metricsLock (fun () ->
                            processedRequests <- processedRequests + 1L
                            totalLatencyMs <- totalLatencyMs + int64 latency)

                        logDebug "IPC" $"Processed request {request.RequestId} in {latency:F2}ms"

                    with
                    | :? OperationCanceledException -> ()
                    | ex ->
                        lock metricsLock (fun () -> errorCount <- errorCount + 1L)
                        logException "IPC" "Error in request processing loop" ex

            finally
                logInfo "IPC" "IPC request processing loop ended"
        }

    // 個別リクエスト処理
    and processRequest (command: SessionCommand) : Task<SessionResponse> =
        task {
            try
                match command with
                | StartSession(paneId, workingDir) ->
                    logInfo "IPC" $"Starting session for pane: {paneId}, workingDir: {workingDir}"

                    // セッション開始ロジック（プロセス起動等）
                    let sessionId = Guid.NewGuid().ToString()
                    let processId = 12345 // プレースホルダー

                    return SessionStarted(paneId, sessionId, processId)

                | StopSession paneId ->
                    logInfo "IPC" $"Stopping session for pane: {paneId}"

                    // セッション停止ロジック
                    match activeConnections.TryRemove(paneId) with
                    | true, connection ->
                        connection.Close()
                        return SessionStopped paneId
                    | false, _ -> return Error(paneId, "Session not found")

                | SendInput(paneId, input) ->
                    logDebug "IPC" $"Sending input to pane {paneId}: {input.Substring(0, min 50 input.Length)}..."

                    // 入力送信ロジック
                    return InputProcessed paneId

                | RequestOutput paneId ->
                    logDebug "IPC" $"Requesting output from pane: {paneId}"

                    // 出力取得ロジック
                    let timeStr = DateTime.Now.ToString("HH:mm:ss")
                    let sampleOutput = $"Sample output from {paneId} at {timeStr}"
                    return OutputData(paneId, sampleOutput)

                | HealthCheck paneId ->
                    logDebug "IPC" $"Health check for pane: {paneId}"

                    // ヘルスチェックロジック
                    let isHealthy = activeConnections.ContainsKey(paneId)
                    let details = if isHealthy then "Running" else "Not found"
                    return HealthStatus(paneId, isHealthy, details)

                | RestartSession paneId ->
                    logInfo "IPC" $"Restarting session for pane: {paneId}"

                    // セッション再起動ロジック
                    let newSessionId = Guid.NewGuid().ToString()
                    return SessionRestarted(paneId, newSessionId)

            with ex ->
                let paneId =
                    match command with
                    | StartSession(id, _)
                    | StopSession id
                    | SendInput(id, _)
                    | RequestOutput id
                    | HealthCheck id
                    | RestartSession id -> id

                logException "IPC" $"Error processing command for pane: {paneId}" ex
                return Error(paneId, ex.Message)
        }

    // パブリックメソッド
    member _.StartAsync() =
        if processingTask.Value.IsSome then
            raise (InvalidOperationException("IPC Channel is already running"))

        let task = processRequests ()
        processingTask := Some task
        logInfo "IPC" "IPC Channel started"
        task

    member _.SendCommandAsync(command: SessionCommand, ?cancellationToken: CancellationToken) : Task<SessionResponse> =
        task {
            let token = defaultArg cancellationToken CancellationToken.None
            let requestId = Guid.NewGuid().ToString()

            // レスポンス用チャネル作成
            let responseChannel = Channel.CreateUnbounded<SessionResponse>()

            let request =
                { RequestId = requestId
                  Command = command
                  ResponseChannel = responseChannel
                  Timestamp = DateTime.UtcNow
                  CancellationToken = token }

            // バックプレッシャチェック
            if not (handleBackpressure request) then
                return Error("", "Request dropped due to backpressure")
            else
                // リクエスト送信
                do! requestChannel.Writer.WriteAsync(request, token)

                // レスポンス待機（タイムアウトあり）
                use timeoutCts = new CancellationTokenSource(config.RequestTimeoutMs)

                use linkedCts =
                    CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token)

                try
                    let! response = responseChannel.Reader.ReadAsync(linkedCts.Token)
                    return response

                with
                | :? OperationCanceledException when timeoutCts.Token.IsCancellationRequested ->
                    return Error("", $"Request timeout after {config.RequestTimeoutMs}ms")
                | ex -> return Error("", ex.Message)
        }

    member _.GetMetrics() : IPCMetrics =
        lock metricsLock (fun () ->
            let avgLatency =
                if processedRequests > 0L then
                    float totalLatencyMs / float processedRequests
                else
                    0.0

            { QueueLength = requestChannel.Reader.Count
              ProcessedRequests = processedRequests
              DroppedRequests = droppedRequests
              AverageLatencyMs = avgLatency
              ErrorCount = errorCount })

    member _.Stop() =
        logInfo "IPC" "Stopping IPC Channel"
        cancellationTokenSource.Cancel()
        requestChannel.Writer.Complete()

        // アクティブ接続をクリーンアップ
        for kvp in activeConnections do
            kvp.Value.Close()

        activeConnections.Clear()

    // 再接続機能付きコマンド送信
    member this.SendCommandWithRetryAsync
        (command: SessionCommand, ?cancellationToken: CancellationToken)
        : Task<SessionResponse> =
        let rec tryWithRetry attempt lastEx =
            task {
                if attempt > config.MaxRetryAttempts then
                    match lastEx with
                    | Some(ex: Exception) -> return Error("", ex.Message)
                    | None -> return Error("", "All retry attempts failed")
                else
                    try
                        let token = defaultArg cancellationToken CancellationToken.None
                        let! response = this.SendCommandAsync(command, token)

                        match response with
                        | Error(_, errorMsg) when errorMsg.Contains("timeout") || errorMsg.Contains("connection") ->
                            logWarning "IPC" $"Attempt {attempt}/{config.MaxRetryAttempts} failed: {errorMsg}"

                            if attempt < config.MaxRetryAttempts then
                                let delayMs = config.RetryDelayMs * attempt // 指数バックオフ
                                logInfo "IPC" $"Retrying in {delayMs}ms..."
                                let token = defaultArg cancellationToken CancellationToken.None
                                do! Task.Delay(delayMs, token)
                                return! tryWithRetry (attempt + 1) (Some(Exception(errorMsg)))
                            else
                                return response
                        | _ -> return response

                    with ex ->
                        logException "IPC" $"Attempt {attempt}/{config.MaxRetryAttempts} failed" ex

                        if attempt < config.MaxRetryAttempts then
                            let delayMs = config.RetryDelayMs * attempt
                            logInfo "IPC" $"Retrying in {delayMs}ms..."
                            let token = defaultArg cancellationToken CancellationToken.None
                            do! Task.Delay(delayMs, token)
                            return! tryWithRetry (attempt + 1) (Some ex)
                        else
                            return Error("", ex.Message)
            }

        tryWithRetry 1 None

    // 接続健全性チェック
    member this.CheckConnectionHealth() : Task<bool> =
        task {
            try
                let! response = this.SendCommandAsync(HealthCheck("system"), CancellationToken.None)

                match response with
                | HealthStatus(_, isHealthy, _) -> return isHealthy
                | Error _ -> return false
                | _ -> return true
            with _ ->
                return false
        }

    interface IDisposable with
        member this.Dispose() =
            this.Stop()
            cancellationTokenSource.Dispose()

// ===============================================
// 便利な関数
// ===============================================

/// デフォルト設定でIPCチャネルを作成
let createIPCChannel () = new IPCChannel(defaultIPCConfig)

/// IPCチャネルを作成して開始
let createAndStartIPCChannel () =
    let channel = createIPCChannel ()
    let startTask = channel.StartAsync()
    channel, startTask

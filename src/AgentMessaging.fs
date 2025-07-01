module FCode.AgentMessaging

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open FCode.Logger
open FCode.AgentCLI

// ===============================================
// メッセージ優先度・種別定義
// ===============================================

/// メッセージ優先度定義
type MessagePriority =
    | Critical // 緊急停止・エラー通知
    | High // タスク割り当て・品質課題
    | Normal // 進捗報告・状況更新
    | Low // 情報共有・ログ

/// メッセージ種別定義
type MessageType =
    | TaskAssignment // タスク割り当て・指示
    | Progress // 進捗報告・状況更新
    | QualityReview // 品質レビュー・改善提案
    | Escalation // エスカレーション・問題報告
    | StateUpdate // 状態変更通知
    | ResourceRequest // リソース要求・権限要求
    | Collaboration // 協調作業・相談
    | Notification // 一般通知・情報共有

// ===============================================
// エージェントメッセージ定義
// ===============================================

/// エージェント間通信メッセージ
type AgentMessage =
    { MessageId: string // 一意メッセージID
      FromAgent: string // 送信元エージェント
      ToAgent: string option // 送信先エージェント (None=ブロードキャスト)
      MessageType: MessageType // メッセージ種別
      Priority: MessagePriority // 優先度
      Content: string // メッセージ内容
      Metadata: Map<string, string> // 追加メタデータ
      Timestamp: DateTime // 送信タイムスタンプ
      ExpiresAt: DateTime option // 有効期限
      CorrelationId: string option } // 関連メッセージID

/// メッセージルーティング設定
type RoutingConfig =
    { MaxRetries: int // 最大リトライ回数
      RetryDelay: TimeSpan // リトライ間隔
      MessageTTL: TimeSpan // メッセージ生存時間
      BufferSize: int // メッセージバッファサイズ
      EnablePersistence: bool // 永続化有効フラグ
      PersistenceFile: string option } // 永続化ファイルパス

// ===============================================
// メッセージ配信・受信インターフェース
// ===============================================

/// メッセージ受信ハンドラー
type IMessageHandler =
    /// メッセージ処理
    abstract member HandleMessage: AgentMessage -> Async<bool>

    /// エージェント名
    abstract member AgentName: string

    /// 処理可能メッセージ種別
    abstract member SupportedMessageTypes: MessageType list

/// メッセージルーター
type IMessageRouter =
    /// エージェント登録
    abstract member RegisterAgent: string * IMessageHandler -> unit

    /// エージェント登録解除
    abstract member UnregisterAgent: string -> unit

    /// メッセージ送信
    abstract member SendMessage: AgentMessage -> Async<bool>

    /// ブロードキャストメッセージ送信
    abstract member BroadcastMessage: AgentMessage -> Async<int>

// ===============================================
// メッセージルーター実装
// ===============================================

/// マルチエージェントメッセージルーター
type MultiAgentMessageRouter(config: RoutingConfig) =
    let handlers = ConcurrentDictionary<string, IMessageHandler>()
    let messageQueue = ConcurrentQueue<AgentMessage>()
    let cts = new CancellationTokenSource()
    let mutable isRunning = false

    /// ユニークメッセージID生成
    let generateMessageId () =
        let timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss")
        let guidPart = Guid.NewGuid().ToString("N")[..7]
        $"{timestamp}-{guidPart}"

    /// メッセージの有効性チェック
    let isMessageValid (message: AgentMessage) =
        match message.ExpiresAt with
        | Some expiry -> DateTime.Now <= expiry
        | None -> true

    /// メッセージ配信処理
    let deliverMessage (handler: IMessageHandler) (message: AgentMessage) =
        async {
            try
                if handler.SupportedMessageTypes |> List.contains message.MessageType then
                    logDebug $"MessageRouter-{handler.AgentName}" $"Processing message: {message.MessageId}"
                    let! success = handler.HandleMessage(message)

                    if success then
                        logInfo
                            "MessageRouter"
                            $"Message delivered successfully: {message.MessageId} -> {handler.AgentName}"
                    else
                        logWarning
                            "MessageRouter"
                            $"Message handling failed: {message.MessageId} -> {handler.AgentName}"

                    return success
                else
                    logDebug
                        "MessageRouter"
                        $"Message type not supported by agent {handler.AgentName}: {message.MessageType}"

                    return false
            with ex ->
                logException "MessageRouter" $"Message delivery failed: {message.MessageId} -> {handler.AgentName}" ex
                return false
        }

    /// メッセージ処理ワーカー
    let messageProcessingWorker () =
        async {
            while not cts.Token.IsCancellationRequested do
                try
                    match messageQueue.TryDequeue() with
                    | (true, message) when isMessageValid message ->
                        match message.ToAgent with
                        | Some targetAgent ->
                            // ユニキャスト配信
                            match handlers.TryGetValue(targetAgent) with
                            | (true, handler) ->
                                let! _ = deliverMessage handler message
                                ()
                            | _ ->
                                logWarning
                                    "MessageRouter"
                                    $"Target agent not found: {targetAgent} for message {message.MessageId}"

                        | None ->
                            // ブロードキャスト配信
                            let! deliveryTasks =
                                handlers.Values
                                |> Seq.map (fun handler -> deliverMessage handler message)
                                |> Async.Parallel

                            let successCount = deliveryTasks |> Array.filter id |> Array.length

                            logInfo
                                "MessageRouter"
                                $"Broadcast message {message.MessageId} delivered to {successCount}/{handlers.Count} agents"

                    | (true, message) ->
                        // 期限切れメッセージ
                        logWarning "MessageRouter" $"Expired message discarded: {message.MessageId}"

                    | (false, _) ->
                        // キューが空の場合は少し待機
                        do! Async.Sleep(100)

                with ex ->
                    logException "MessageRouter" "Message processing worker error" ex
                    do! Async.Sleep(1000) // エラー時は長めに待機
        }

    /// メッセージルーター開始
    member this.Start() =
        if not isRunning then
            isRunning <- true
            Async.Start(messageProcessingWorker (), cts.Token)
            logInfo "MessageRouter" "Multi-agent message router started"

    /// メッセージルーター停止
    member this.Stop() =
        if isRunning then
            cts.Cancel()
            isRunning <- false
            logInfo "MessageRouter" "Multi-agent message router stopped"

    interface IMessageRouter with
        member _.RegisterAgent(agentName: string, handler: IMessageHandler) =
            handlers.TryAdd(agentName, handler) |> ignore
            logInfo "MessageRouter" $"Agent registered: {agentName} (supports: {handler.SupportedMessageTypes})"

        member _.UnregisterAgent(agentName: string) =
            handlers.TryRemove(agentName) |> ignore
            logInfo "MessageRouter" $"Agent unregistered: {agentName}"

        member _.SendMessage(message: AgentMessage) =
            async {
                if isMessageValid message then
                    messageQueue.Enqueue(message)
                    logDebug "MessageRouter" $"Message queued: {message.MessageId} ({message.MessageType})"
                    return true
                else
                    logWarning "MessageRouter" $"Invalid message rejected: {message.MessageId}"
                    return false
            }

        member this.BroadcastMessage(message: AgentMessage) =
            async {
                let broadcastMessage = { message with ToAgent = None }
                let! success = (this :> IMessageRouter).SendMessage(broadcastMessage)
                return if success then handlers.Count else 0
            }

    interface IDisposable with
        member this.Dispose() =
            this.Stop()
            cts.Dispose()

// ===============================================
// メッセージビルダー・ユーティリティ
// ===============================================

/// メッセージビルダー
type MessageBuilder() =
    let mutable fromAgent = ""
    let mutable toAgent = None
    let mutable messageType = Progress
    let mutable priority = Normal
    let mutable content = ""
    let mutable metadata = Map.empty
    let mutable expiresAt = None
    let mutable correlationId = None

    /// 送信元エージェント設定
    member this.From(agent: string) =
        fromAgent <- agent
        this

    /// 送信先エージェント設定
    member this.To(agent: string) =
        toAgent <- Some agent
        this

    /// ブロードキャスト設定
    member this.Broadcast() =
        toAgent <- None
        this

    /// メッセージ種別設定
    member this.OfType(msgType: MessageType) =
        messageType <- msgType
        this

    /// 優先度設定
    member this.WithPriority(prio: MessagePriority) =
        priority <- prio
        this

    /// メッセージ内容設定
    member this.WithContent(text: string) =
        content <- text
        this

    /// メタデータ追加
    member this.WithMetadata(key: string, value: string) =
        metadata <- metadata.Add(key, value)
        this

    /// 有効期限設定
    member this.ExpiresIn(duration: TimeSpan) =
        expiresAt <- Some(DateTime.Now.Add(duration))
        this

    /// 関連メッセージID設定
    member this.CorrelatedWith(messageId: string) =
        correlationId <- Some messageId
        this

    /// メッセージ構築
    member _.Build() =
        let timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss")
        let guidPart = Guid.NewGuid().ToString("N")[..7]

        { MessageId = $"{timestamp}-{guidPart}"
          FromAgent = fromAgent
          ToAgent = toAgent
          MessageType = messageType
          Priority = priority
          Content = content
          Metadata = metadata
          Timestamp = DateTime.Now
          ExpiresAt = expiresAt
          CorrelationId = correlationId }

/// メッセージユーティリティ
module MessageUtils =

    /// タスク割り当てメッセージ作成
    let createTaskAssignment (fromAgent: string) (toAgent: string) (taskDescription: string) (taskId: string) =
        MessageBuilder()
            .From(fromAgent)
            .To(toAgent)
            .OfType(TaskAssignment)
            .WithPriority(High)
            .WithContent(taskDescription)
            .WithMetadata("task_id", taskId)
            .WithMetadata("assigned_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .ExpiresIn(TimeSpan.FromHours(24.0))
            .Build()

    /// 進捗報告メッセージ作成
    let createProgressReport (fromAgent: string) (taskId: string) (progress: int) (status: string) =
        MessageBuilder()
            .From(fromAgent)
            .Broadcast()
            .OfType(Progress)
            .WithPriority(Normal)
            .WithContent($"Task progress: {progress}%% - {status}")
            .WithMetadata("task_id", taskId)
            .WithMetadata("progress_percentage", progress.ToString())
            .WithMetadata("status", status)
            .Build()

    /// 品質レビューメッセージ作成
    let createQualityReview (fromAgent: string) (toAgent: string) (reviewContent: string) (issueCount: int) =
        let priority = if issueCount > 0 then High else Normal

        MessageBuilder()
            .From(fromAgent)
            .To(toAgent)
            .OfType(QualityReview)
            .WithPriority(priority)
            .WithContent(reviewContent)
            .WithMetadata("issue_count", issueCount.ToString())
            .WithMetadata("review_timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .ExpiresIn(TimeSpan.FromHours(12.0))
            .Build()

    /// エスカレーションメッセージ作成
    let createEscalation (fromAgent: string) (issue: string) (severity: string) =
        MessageBuilder()
            .From(fromAgent)
            .Broadcast()
            .OfType(Escalation)
            .WithPriority(Critical)
            .WithContent($"Escalation required: {issue}")
            .WithMetadata("severity", severity)
            .WithMetadata("escalated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .ExpiresIn(TimeSpan.FromHours(2.0))
            .Build()

// ===============================================
// グローバルメッセージルーターインスタンス
// ===============================================

/// グローバルメッセージルーター設定
let defaultRoutingConfig =
    { MaxRetries = 3
      RetryDelay = TimeSpan.FromSeconds(5.0)
      MessageTTL = TimeSpan.FromHours(24.0)
      BufferSize = 1000
      EnablePersistence = true
      PersistenceFile = Some "/tmp/fcode-messages.log" }

/// グローバルメッセージルーターインスタンス
let globalMessageRouter = new MultiAgentMessageRouter(defaultRoutingConfig)

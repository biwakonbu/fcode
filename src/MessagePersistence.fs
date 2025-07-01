module FCode.MessagePersistence

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open System.Collections.Concurrent
open FCode.Logger
open FCode.AgentMessaging

// ===============================================
// JSON シリアライゼーション設定
// ===============================================

/// メッセージ永続化用JSON設定
let private jsonOptions =
    let options = JsonSerializerOptions()
    options.WriteIndented <- false
    options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
    options.Converters.Add(JsonFSharpConverter())
    options

// ===============================================
// 永続化メッセージ定義
// ===============================================

/// 永続化用メッセージデータ
type PersistedMessage =
    { MessageId: string
      FromAgent: string
      ToAgent: string option
      MessageType: string // MessageTypeの文字列表現
      Priority: string // MessagePriorityの文字列表現
      Content: string
      Metadata: Map<string, string>
      Timestamp: DateTime
      ExpiresAt: DateTime option
      CorrelationId: string option
      ProcessedAt: DateTime option // 処理完了時刻
      DeliveryAttempts: int // 配信試行回数
      LastError: string option } // 最後のエラーメッセージ

/// 永続化統計情報
type PersistenceStats =
    { TotalMessages: int64
      SuccessfulDeliveries: int64
      FailedDeliveries: int64
      ExpiredMessages: int64
      AverageDeliveryTime: TimeSpan
      LastUpdateTime: DateTime }

// ===============================================
// メッセージ永続化インターフェース
// ===============================================

/// メッセージ永続化インターフェース
type IMessagePersistence =
    /// メッセージ保存
    abstract member SaveMessage: AgentMessage -> Async<bool>

    /// メッセージ配信完了マーク
    abstract member MarkAsDelivered: string -> Async<bool>

    /// メッセージ配信失敗マーク
    abstract member MarkAsFailed: string -> string -> Async<bool>

    /// 未配信メッセージ取得
    abstract member GetUndeliveredMessages: unit -> Async<PersistedMessage list>

    /// 期限切れメッセージクリーンアップ
    abstract member CleanupExpiredMessages: unit -> Async<int>

    /// 永続化統計取得
    abstract member GetStats: unit -> Async<PersistenceStats>

// ===============================================
// ファイルベース永続化実装
// ===============================================

/// ファイルベースメッセージ永続化
type FileMessagePersistence(filePath: string) =
    let lockObject = obj ()
    let messageCache = ConcurrentDictionary<string, PersistedMessage>()

    /// AgentMessageをPersistedMessageに変換
    let toPersistedMessage (message: AgentMessage) =
        { MessageId = message.MessageId
          FromAgent = message.FromAgent
          ToAgent = message.ToAgent
          MessageType = message.MessageType.ToString()
          Priority = message.Priority.ToString()
          Content = message.Content
          Metadata = message.Metadata
          Timestamp = message.Timestamp
          ExpiresAt = message.ExpiresAt
          CorrelationId = message.CorrelationId
          ProcessedAt = None
          DeliveryAttempts = 0
          LastError = None }

    /// ファイルからメッセージ読み込み
    let loadMessagesFromFile () =
        async {
            try
                if File.Exists(filePath) then
                    let! content = File.ReadAllTextAsync(filePath) |> Async.AwaitTask
                    let lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)

                    for line in lines do
                        try
                            let message = JsonSerializer.Deserialize<PersistedMessage>(line, jsonOptions)
                            messageCache.TryAdd(message.MessageId, message) |> ignore
                        with ex ->
                            logWarning "MessagePersistence" $"Failed to parse message line: {ex.Message}"

                    logInfo "MessagePersistence" $"Loaded {messageCache.Count} messages from {filePath}"
                else
                    logInfo "MessagePersistence" $"Persistence file not found, starting fresh: {filePath}"
            with ex ->
                logException "MessagePersistence" $"Failed to load messages from file: {filePath}" ex
        }

    /// ファイルにメッセージ保存
    let saveMessageToFile (message: PersistedMessage) =
        async {
            try
                lock lockObject (fun () ->
                    let json = JsonSerializer.Serialize(message, jsonOptions)
                    File.AppendAllText(filePath, json + "\n"))

                return true
            with ex ->
                logException "MessagePersistence" $"Failed to save message to file: {filePath}" ex
                return false
        }

    /// 永続化ファイル再構築
    let rebuildPersistenceFile () =
        async {
            try
                lock lockObject (fun () ->
                    let backupPath = filePath + ".backup"

                    if File.Exists(filePath) then
                        File.Move(filePath, backupPath)

                    use writer = new StreamWriter(filePath)

                    for kvp in messageCache do
                        let json = JsonSerializer.Serialize(kvp.Value, jsonOptions)
                        writer.WriteLine(json))

                logInfo "MessagePersistence" $"Rebuilt persistence file: {filePath}"
                return true
            with ex ->
                logException "MessagePersistence" $"Failed to rebuild persistence file: {filePath}" ex
                return false
        }

    /// 初期化
    do
        // ディレクトリ作成
        let directory = Path.GetDirectoryName(filePath)

        if not (String.IsNullOrEmpty(directory)) && not (Directory.Exists(directory)) then
            Directory.CreateDirectory(directory) |> ignore

        // 既存メッセージ読み込み
        loadMessagesFromFile () |> Async.RunSynchronously

    interface IMessagePersistence with
        member _.SaveMessage(message: AgentMessage) =
            async {
                let persistedMessage = toPersistedMessage message
                messageCache.TryAdd(message.MessageId, persistedMessage) |> ignore
                return! saveMessageToFile persistedMessage
            }

        member _.MarkAsDelivered(messageId: string) =
            async {
                match messageCache.TryGetValue(messageId) with
                | (true, message) ->
                    let updatedMessage =
                        { message with
                            ProcessedAt = Some DateTime.Now
                            DeliveryAttempts = message.DeliveryAttempts + 1 }

                    messageCache.TryUpdate(messageId, updatedMessage, message) |> ignore
                    logDebug "MessagePersistence" $"Message marked as delivered: {messageId}"
                    return true
                | _ ->
                    logWarning "MessagePersistence" $"Message not found for delivery marking: {messageId}"
                    return false
            }

        member _.MarkAsFailed (messageId: string) (error: string) =
            async {
                match messageCache.TryGetValue(messageId) with
                | (true, message) ->
                    let updatedMessage =
                        { message with
                            DeliveryAttempts = message.DeliveryAttempts + 1
                            LastError = Some error }

                    messageCache.TryUpdate(messageId, updatedMessage, message) |> ignore
                    logDebug "MessagePersistence" $"Message marked as failed: {messageId} - {error}"
                    return true
                | _ ->
                    logWarning "MessagePersistence" $"Message not found for failure marking: {messageId}"
                    return false
            }

        member _.GetUndeliveredMessages() =
            async {
                let undelivered =
                    messageCache.Values
                    |> Seq.filter (fun msg -> msg.ProcessedAt.IsNone)
                    |> Seq.filter (fun msg ->
                        match msg.ExpiresAt with
                        | Some expiry -> DateTime.Now <= expiry
                        | None -> true)
                    |> Seq.sortBy (fun msg -> msg.Timestamp)
                    |> Seq.toList

                logDebug "MessagePersistence" $"Found {undelivered.Length} undelivered messages"
                return undelivered
            }

        member _.CleanupExpiredMessages() =
            async {
                let expiredKeys =
                    messageCache.Values
                    |> Seq.filter (fun msg ->
                        match msg.ExpiresAt with
                        | Some expiry -> DateTime.Now > expiry
                        | None -> false)
                    |> Seq.map (fun msg -> msg.MessageId)
                    |> Seq.toList

                let cleanedCount =
                    expiredKeys
                    |> List.fold
                        (fun count key ->
                            if messageCache.TryRemove(key) |> fst then
                                count + 1
                            else
                                count)
                        0

                if cleanedCount > 0 then
                    let! _ = rebuildPersistenceFile ()
                    logInfo "MessagePersistence" $"Cleaned up {cleanedCount} expired messages"

                return cleanedCount
            }

        member _.GetStats() =
            async {
                let messages = messageCache.Values |> Seq.toList
                let totalMessages = int64 messages.Length

                let successfulDeliveries =
                    messages
                    |> List.filter (fun msg -> msg.ProcessedAt.IsSome)
                    |> List.length
                    |> int64

                let failedDeliveries =
                    messages
                    |> List.filter (fun msg -> msg.LastError.IsSome)
                    |> List.length
                    |> int64

                let expiredMessages =
                    messages
                    |> List.filter (fun msg ->
                        match msg.ExpiresAt with
                        | Some expiry -> DateTime.Now > expiry
                        | None -> false)
                    |> List.length
                    |> int64

                let averageDeliveryTime =
                    let deliveredMessages = messages |> List.filter (fun msg -> msg.ProcessedAt.IsSome)

                    if deliveredMessages.Length > 0 then
                        let totalTime =
                            deliveredMessages
                            |> List.sumBy (fun msg ->
                                match msg.ProcessedAt with
                                | Some processed -> (processed - msg.Timestamp).TotalMilliseconds
                                | None -> 0.0)

                        TimeSpan.FromMilliseconds(totalTime / (float deliveredMessages.Length))
                    else
                        TimeSpan.Zero

                return
                    { TotalMessages = totalMessages
                      SuccessfulDeliveries = successfulDeliveries
                      FailedDeliveries = failedDeliveries
                      ExpiredMessages = expiredMessages
                      AverageDeliveryTime = averageDeliveryTime
                      LastUpdateTime = DateTime.Now }
            }

// ===============================================
// メッセージアーカイブ・クリーンアップ
// ===============================================

/// メッセージアーカイブ管理
type MessageArchiver(persistence: IMessagePersistence, archiveThreshold: TimeSpan) =

    /// 古いメッセージをアーカイブ
    member _.ArchiveOldMessages() =
        async {
            let! stats = persistence.GetStats()

            logInfo
                "MessageArchiver"
                $"Current stats - Total: {stats.TotalMessages}, Delivered: {stats.SuccessfulDeliveries}, Failed: {stats.FailedDeliveries}"

            let! expiredCount = persistence.CleanupExpiredMessages()

            if expiredCount > 0 then
                logInfo "MessageArchiver" $"Archived {expiredCount} expired messages"

            return expiredCount
        }

    /// 定期アーカイブタスク実行
    member this.StartPeriodicArchiving(interval: TimeSpan) =
        async {
            while true do
                try
                    let! _ = this.ArchiveOldMessages()
                    do! Async.Sleep(int interval.TotalMilliseconds)
                with ex ->
                    logException "MessageArchiver" "Periodic archiving failed" ex
                    do! Async.Sleep(60000) // エラー時は1分待機
        }

// ===============================================
// グローバル永続化インスタンス
// ===============================================

/// グローバルメッセージ永続化インスタンス
let globalMessagePersistence =
    let persistenceDir = "/tmp/fcode-messages"
    let persistenceFile = Path.Combine(persistenceDir, "messages.jsonl")
    new FileMessagePersistence(persistenceFile) :> IMessagePersistence

/// グローバルメッセージアーカイバーインスタンス
let globalMessageArchiver =
    new MessageArchiver(globalMessagePersistence, TimeSpan.FromDays(7.0))

namespace FCode.SessionManagement

open System
open System.IO
open System.Text.Json
open System.Threading
open System.Collections.Concurrent
open System.Threading.Tasks
open FCode

/// 通知・アラートエンジン
module NotificationEngine =

    /// 通知の重要度
    type NotificationSeverity =
        | Info = 1
        | Warning = 2
        | Error = 3
        | Critical = 4

    /// 通知の種類
    type NotificationType =
        | TaskCompleted
        | TaskFailed
        | SessionDetached
        | SessionAttached
        | ResourceThreshold
        | SystemAlert
        | UserAction
        | InterSessionMessage

    /// 通知配信チャンネル
    type NotificationChannel =
        | UI // UI内通知
        | Log // ログファイル
        | File // ファイル出力
        | External // 外部システム（将来拡張）

    /// 通知メッセージ
    type NotificationMessage =
        { MessageId: string
          SessionId: string option
          PaneId: string option
          NotificationType: NotificationType
          Severity: NotificationSeverity
          Title: string
          Content: string
          Details: Map<string, string>
          CreatedAt: DateTime
          ExpiresAt: DateTime option
          IsRead: bool
          Channels: NotificationChannel list
          Tags: string list }

    /// 通知フィルタ
    type NotificationFilter =
        { SessionId: string option
          Severity: NotificationSeverity option
          NotificationType: NotificationType option
          FromDate: DateTime option
          ToDate: DateTime option
          OnlyUnread: bool
          Tags: string list }

    /// 通知設定
    type NotificationConfig =
        { EnabledChannels: NotificationChannel list
          MaxRetainedNotifications: int
          AutoCleanupDays: int
          UINotificationTimeout: TimeSpan
          SeverityThresholds: Map<NotificationType, NotificationSeverity>
          StorageDirectory: string
          BatchNotificationEnabled: bool
          BatchSize: int
          BatchInterval: TimeSpan }

    /// デフォルト設定
    let defaultNotificationConfig =
        { EnabledChannels = [ UI; Log; File ]
          MaxRetainedNotifications = 1000
          AutoCleanupDays = 30
          UINotificationTimeout = TimeSpan.FromSeconds(10.0)
          SeverityThresholds =
            Map
                [ (TaskCompleted, NotificationSeverity.Info)
                  (TaskFailed, NotificationSeverity.Warning)
                  (SessionDetached, NotificationSeverity.Info)
                  (SessionAttached, NotificationSeverity.Info)
                  (ResourceThreshold, NotificationSeverity.Warning)
                  (SystemAlert, NotificationSeverity.Error)
                  (UserAction, NotificationSeverity.Info)
                  (InterSessionMessage, NotificationSeverity.Info) ]
          StorageDirectory =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "fcode",
                "notifications"
            )
          BatchNotificationEnabled = true
          BatchSize = 10
          BatchInterval = TimeSpan.FromSeconds(5.0) }

    /// 通知管理
    let private notifications = ConcurrentQueue<NotificationMessage>()

    let private notificationSubscribers =
        ConcurrentDictionary<string, (NotificationMessage -> unit)>()

    let private batchNotifications = ResizeArray<NotificationMessage>()
    let private batchLock = new obj ()

    /// 通知ストレージの初期化
    let initializeNotificationStorage (config: NotificationConfig) =
        try
            Directory.CreateDirectory(config.StorageDirectory) |> ignore

            Directory.CreateDirectory(Path.Combine(config.StorageDirectory, "active"))
            |> ignore

            Directory.CreateDirectory(Path.Combine(config.StorageDirectory, "archive"))
            |> ignore

            Logger.logInfo "NotificationEngine" "通知ストレージ初期化完了"
            true
        with ex ->
            Logger.logError "NotificationEngine" $"通知ストレージ初期化失敗: {ex.Message}"
            false

    /// 通知メッセージの作成
    let createNotification
        (sessionId: string option)
        (paneId: string option)
        (notificationType: NotificationType)
        (severity: NotificationSeverity)
        (title: string)
        (content: string)
        (details: Map<string, string>)
        (channels: NotificationChannel list)
        (tags: string list)
        (expiresAt: DateTime option)
        =
        { MessageId = Guid.NewGuid().ToString()
          SessionId = sessionId
          PaneId = paneId
          NotificationType = notificationType
          Severity = severity
          Title = title
          Content = content
          Details = details
          CreatedAt = DateTime.Now
          ExpiresAt = expiresAt
          IsRead = false
          Channels = channels
          Tags = tags }

    /// 通知の送信
    let sendNotification (config: NotificationConfig) (notification: NotificationMessage) =
        async {
            try
                Logger.logDebug "NotificationEngine" $"通知送信開始: {notification.Title} (重要度: {notification.Severity})"

                // 重要度チェック
                let thresholdSeverity =
                    config.SeverityThresholds
                    |> Map.tryFind notification.NotificationType
                    |> Option.defaultValue NotificationSeverity.Info

                if notification.Severity < thresholdSeverity then
                    Logger.logDebug "NotificationEngine" $"通知が重要度しきい値を下回るためスキップ: {notification.Title}"
                    return false
                else

                    // 通知をキューに追加
                    notifications.Enqueue(notification)

                    // チャンネル別配信
                    for channel in notification.Channels do
                        match channel with
                        | UI ->
                            // UI通知（購読者への配信）
                            for subscriber in notificationSubscribers.Values do
                                try
                                    subscriber notification
                                with ex ->
                                    Logger.logWarning "NotificationEngine" $"UI通知配信失敗: {ex.Message}"

                        | Log ->
                            // ログ出力
                            match notification.Severity with
                            | NotificationSeverity.Info ->
                                Logger.logInfo
                                    "NotificationEngine"
                                    $"[{notification.NotificationType}] {notification.Title}: {notification.Content}"
                            | NotificationSeverity.Warning ->
                                Logger.logWarning
                                    "NotificationEngine"
                                    $"[{notification.NotificationType}] {notification.Title}: {notification.Content}"
                            | NotificationSeverity.Error ->
                                Logger.logError
                                    "NotificationEngine"
                                    $"[{notification.NotificationType}] {notification.Title}: {notification.Content}"
                            | NotificationSeverity.Critical ->
                                Logger.logError
                                    "NotificationEngine"
                                    $"[CRITICAL] [{notification.NotificationType}] {notification.Title}: {notification.Content}"
                            | _ ->
                                Logger.logInfo
                                    "NotificationEngine"
                                    $"[{notification.NotificationType}] {notification.Title}: {notification.Content}"

                        | File ->
                            // ファイル出力
                            let notificationFile =
                                Path.Combine(config.StorageDirectory, "active", $"{notification.MessageId}.json")

                            let json =
                                JsonSerializer.Serialize(notification, JsonSerializerOptions(WriteIndented = true))

                            File.WriteAllText(notificationFile, json)

                        | External ->
                            // 外部システム通知（将来実装）
                            Logger.logDebug "NotificationEngine" "外部システム通知は将来実装予定"

                    // バッチ通知の処理
                    if config.BatchNotificationEnabled then
                        lock batchLock (fun () ->
                            batchNotifications.Add(notification)

                            if batchNotifications.Count >= config.BatchSize then
                                let batchList = batchNotifications.ToArray()
                                batchNotifications.Clear()
                                Logger.logInfo "NotificationEngine" $"バッチ通知送信: {batchList.Length}件")

                    Logger.logDebug "NotificationEngine" $"通知送信完了: {notification.MessageId}"
                    return true

            with ex ->
                Logger.logError "NotificationEngine" $"通知送信失敗 ({notification.MessageId}): {ex.Message}"
                return false
        }

    /// 便利関数: タスク完了通知
    let notifyTaskCompleted
        (config: NotificationConfig)
        (sessionId: string)
        (paneId: string)
        (taskId: string)
        (taskDescription: string)
        (outputs: string list)
        =
        let details =
            Map
                [ ("taskId", taskId)
                  ("outputCount", outputs.Length.ToString())
                  ("outputs", String.Join("; ", outputs |> List.take (min 3 outputs.Length))) ]

        let notification =
            createNotification
                (Some sessionId)
                (Some paneId)
                TaskCompleted
                NotificationSeverity.Info
                $"タスク完了: {taskDescription}"
                $"タスク '{taskDescription}' が正常に完了しました。"
                details
                config.EnabledChannels
                [ "task"; "completion" ]
                None

        sendNotification config notification

    /// 便利関数: タスク失敗通知
    let notifyTaskFailed
        (config: NotificationConfig)
        (sessionId: string)
        (paneId: string)
        (taskId: string)
        (taskDescription: string)
        (errorMessage: string)
        =
        let details = Map [ ("taskId", taskId); ("errorMessage", errorMessage) ]

        let notification =
            createNotification
                (Some sessionId)
                (Some paneId)
                TaskFailed
                NotificationSeverity.Warning
                $"タスク失敗: {taskDescription}"
                $"タスク '{taskDescription}' が失敗しました: {errorMessage}"
                details
                config.EnabledChannels
                [ "task"; "failure" ]
                None

        sendNotification config notification

    /// 便利関数: セッションデタッチ通知
    let notifySessionDetached (config: NotificationConfig) (sessionId: string) =
        let notification =
            createNotification
                (Some sessionId)
                None
                SessionDetached
                NotificationSeverity.Info
                "セッションデタッチ"
                $"セッション '{sessionId}' がデタッチされました。バックグラウンドで継続実行中です。"
                Map.empty
                config.EnabledChannels
                [ "session"; "detach" ]
                None

        sendNotification config notification

    /// 便利関数: セッションアタッチ通知
    let notifySessionAttached (config: NotificationConfig) (sessionId: string) =
        let notification =
            createNotification
                (Some sessionId)
                None
                SessionAttached
                NotificationSeverity.Info
                "セッションアタッチ"
                $"セッション '{sessionId}' にアタッチしました。"
                Map.empty
                config.EnabledChannels
                [ "session"; "attach" ]
                None

        sendNotification config notification

    /// 便利関数: リソースしきい値通知
    let notifyResourceThreshold
        (config: NotificationConfig)
        (resourceType: string)
        (currentValue: int64)
        (threshold: int64)
        =
        let details =
            Map
                [ ("resourceType", resourceType)
                  ("currentValue", currentValue.ToString())
                  ("threshold", threshold.ToString())
                  ("utilizationPercent", ((float currentValue / float threshold) * 100.0).ToString("F1")) ]

        let notification =
            createNotification
                None
                None
                ResourceThreshold
                NotificationSeverity.Warning
                $"リソースしきい値警告: {resourceType}"
                $"{resourceType}の使用量がしきい値を超えました ({currentValue:N0} / {threshold:N0})"
                details
                config.EnabledChannels
                [ "resource"; "threshold" ]
                None

        sendNotification config notification

    /// 通知購読者の登録
    let subscribeToNotifications (subscriberId: string) (callback: NotificationMessage -> unit) =
        notificationSubscribers.TryAdd(subscriberId, callback) |> ignore
        Logger.logDebug "NotificationEngine" $"通知購読者登録: {subscriberId}"

    /// 通知購読者の登録解除
    let unsubscribeFromNotifications (subscriberId: string) =
        match notificationSubscribers.TryRemove(subscriberId) with
        | true, _ ->
            Logger.logDebug "NotificationEngine" $"通知購読者登録解除: {subscriberId}"
            true
        | false, _ ->
            Logger.logWarning "NotificationEngine" $"購読者が見つかりません: {subscriberId}"
            false

    /// 通知の取得（フィルタ付き）
    let getNotifications (config: NotificationConfig) (filter: NotificationFilter) =
        async {
            try
                let allNotifications = notifications |> Seq.toList |> List.rev // 新しい順に

                let filteredNotifications =
                    allNotifications
                    |> List.filter (fun n ->
                        // セッションフィルタ
                        (filter.SessionId.IsNone || n.SessionId = filter.SessionId)
                        &&
                        // 重要度フィルタ
                        (filter.Severity.IsNone || n.Severity >= filter.Severity.Value)
                        &&
                        // 通知タイプフィルタ
                        (filter.NotificationType.IsNone
                         || n.NotificationType = filter.NotificationType.Value)
                        &&
                        // 日付フィルタ
                        (filter.FromDate.IsNone || n.CreatedAt >= filter.FromDate.Value)
                        && (filter.ToDate.IsNone || n.CreatedAt <= filter.ToDate.Value)
                        &&
                        // 未読フィルタ
                        (not filter.OnlyUnread || not n.IsRead)
                        &&
                        // タグフィルタ
                        (filter.Tags.IsEmpty
                         || filter.Tags |> List.exists (fun tag -> n.Tags |> List.contains tag))
                        &&
                        // 期限切れ除外
                        (n.ExpiresAt.IsNone || n.ExpiresAt.Value > DateTime.Now))

                return filteredNotifications

            with ex ->
                Logger.logError "NotificationEngine" $"通知取得失敗: {ex.Message}"
                return []
        }

    /// 通知の既読マーク
    let markNotificationAsRead (config: NotificationConfig) (messageId: string) =
        async {
            try
                // メモリ内通知の更新（ConcurrentQueueは直接更新できないため、新しいアプローチを取る）
                Logger.logDebug "NotificationEngine" $"通知既読マーク: {messageId}"

                // ファイル内通知の更新
                let notificationFile =
                    Path.Combine(config.StorageDirectory, "active", $"{messageId}.json")

                if File.Exists(notificationFile) then
                    let json = File.ReadAllText(notificationFile)
                    let notification = JsonSerializer.Deserialize<NotificationMessage>(json)
                    let updatedNotification = { notification with IsRead = true }

                    let updatedJson =
                        JsonSerializer.Serialize(updatedNotification, JsonSerializerOptions(WriteIndented = true))

                    File.WriteAllText(notificationFile, updatedJson)

                return true

            with ex ->
                Logger.logError "NotificationEngine" $"通知既読マーク失敗 ({messageId}): {ex.Message}"
                return false
        }

    /// 古い通知のクリーンアップ
    let cleanupOldNotifications (config: NotificationConfig) =
        async {
            try
                let cutoffDate = DateTime.Now.AddDays(-float config.AutoCleanupDays)
                let activeDir = Path.Combine(config.StorageDirectory, "active")
                let archiveDir = Path.Combine(config.StorageDirectory, "archive")

                let mutable cleanedCount = 0
                let mutable archivedCount = 0

                if Directory.Exists(activeDir) then
                    let notificationFiles = Directory.GetFiles(activeDir, "*.json")

                    for file in notificationFiles do
                        try
                            let json = File.ReadAllText(file)
                            let notification = JsonSerializer.Deserialize<NotificationMessage>(json)

                            if notification.CreatedAt < cutoffDate then
                                // アーカイブに移動
                                let archiveFile = Path.Combine(archiveDir, Path.GetFileName(file))
                                File.Move(file, archiveFile)
                                archivedCount <- archivedCount + 1
                                Logger.logDebug "NotificationEngine" $"通知アーカイブ: {notification.MessageId}"

                        with ex ->
                            Logger.logWarning "NotificationEngine" $"通知ファイル処理失敗: {file} - {ex.Message}"

                // アーカイブからの完全削除（設定期間を超えたもの）
                let archiveCutoffDate = DateTime.Now.AddDays(-float(config.AutoCleanupDays * 2))

                if Directory.Exists(archiveDir) then
                    let archiveFiles = Directory.GetFiles(archiveDir, "*.json")

                    for file in archiveFiles do
                        try
                            let fileInfo = FileInfo(file)

                            if fileInfo.CreationTime < archiveCutoffDate then
                                File.Delete(file)
                                cleanedCount <- cleanedCount + 1
                                Logger.logDebug "NotificationEngine" $"古い通知削除: {file}"

                        with ex ->
                            Logger.logWarning "NotificationEngine" $"アーカイブファイル削除失敗: {file} - {ex.Message}"

                Logger.logInfo "NotificationEngine" $"通知クリーンアップ完了: {archivedCount}件アーカイブ, {cleanedCount}件削除"
                return (archivedCount, cleanedCount)

            with ex ->
                Logger.logError "NotificationEngine" $"通知クリーンアップ失敗: {ex.Message}"
                return (0, 0)
        }

    /// 通知統計の取得
    let getNotificationStatistics (config: NotificationConfig) =
        async {
            try
                let! allNotifications =
                    getNotifications
                        config
                        { SessionId = None
                          Severity = None
                          NotificationType = None
                          FromDate = None
                          ToDate = None
                          OnlyUnread = false
                          Tags = [] }

                let totalCount = allNotifications.Length

                let unreadCount =
                    allNotifications |> List.filter (fun n -> not n.IsRead) |> List.length

                let severityStats =
                    allNotifications
                    |> List.groupBy (fun n -> n.Severity)
                    |> List.map (fun (severity, notifications) -> (severity, notifications.Length))
                    |> Map.ofList

                let typeStats =
                    allNotifications
                    |> List.groupBy (fun n -> n.NotificationType)
                    |> List.map (fun (notType, notifications) -> (notType, notifications.Length))
                    |> Map.ofList

                let statistics =
                    {| TotalNotifications = totalCount
                       UnreadNotifications = unreadCount
                       ReadNotifications = totalCount - unreadCount
                       SeverityBreakdown = severityStats
                       TypeBreakdown = typeStats
                       EnabledChannels = config.EnabledChannels
                       SubscriberCount = notificationSubscribers.Count
                       Timestamp = DateTime.Now |}

                return Some statistics

            with ex ->
                Logger.logError "NotificationEngine" $"通知統計取得失敗: {ex.Message}"
                return None
        }

namespace FCode.SessionManagement

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open System.Threading
open System.Collections.Concurrent
open FCode.SessionPersistenceManager
open FCode.DetachAttachManager
open FCode.Collaboration.TaskStorageManager
open FCode

/// 高度なセッション永続化エンジン
module SessionPersistenceEngine =

    /// セッション操作の結果
    type SessionEngineResult<'T> =
        | Success of 'T
        | Error of string

    /// 高度な永続化設定
    type AdvancedPersistenceConfig =
        { BasePersistenceConfig: PersistenceConfig
          MaxConcurrentSessions: int
          AutoSnapshotInterval: TimeSpan
          IncrementalSaveEnabled: bool
          BackupRetentionDays: int
          CompressionLevel: int
          EncryptionEnabled: bool
          MaxSnapshotSize: int64 }

    /// セッション状態の拡張情報
    type ExtendedSessionState =
        { BaseSnapshot: SessionSnapshot
          BackgroundTasks: Map<string, string>
          NotificationQueue: string list
          ResourceUsage: Map<string, int64>
          Dependencies: string list
          Priority: int
          Tags: string list }

    /// 増分変更記録
    type IncrementalChange =
        { Timestamp: DateTime
          PaneId: string
          ChangeType: string
          OldValue: string option
          NewValue: string option
          Size: int64 }

    /// セッション履歴
    type SessionHistory =
        { SessionId: string
          Changes: IncrementalChange list
          CreatedAt: DateTime
          LastUpdate: DateTime }

    /// デフォルト高度設定
    let defaultAdvancedConfig =
        { BasePersistenceConfig = defaultConfig
          MaxConcurrentSessions = 20
          AutoSnapshotInterval = TimeSpan.FromMinutes(2.0)
          IncrementalSaveEnabled = true
          BackupRetentionDays = 30
          CompressionLevel = 6
          EncryptionEnabled = false
          MaxSnapshotSize = 100L * 1024L * 1024L } // 100MB

    /// セッション操作の同期プリミティブ
    let private sessionLocks = ConcurrentDictionary<string, SemaphoreSlim>()

    /// セッション専用のロック取得
    let private getSessionLock (sessionId: string) =
        sessionLocks.GetOrAdd(sessionId, fun _ -> new SemaphoreSlim(1, 1))

    /// セッションロックの解放
    let private releaseSessionLock (sessionId: string) =
        match sessionLocks.TryGetValue(sessionId) with
        | true, semaphore -> semaphore.Release() |> ignore
        | false, _ -> ()

    /// 增分変更の記録
    let recordIncrementalChange
        (config: AdvancedPersistenceConfig)
        (sessionId: string)
        (paneId: string)
        (changeType: string)
        (oldValue: string option)
        (newValue: string option)
        =
        async {
            try
                let change =
                    { Timestamp = DateTime.Now
                      PaneId = paneId
                      ChangeType = changeType
                      OldValue = oldValue
                      NewValue = newValue
                      Size = (defaultArg newValue "").Length |> int64 }

                let historyDir =
                    Path.Combine(config.BasePersistenceConfig.StorageDirectory, "history", sessionId)

                Directory.CreateDirectory(historyDir) |> ignore

                let historyFile = Path.Combine(historyDir, $"{DateTime.Now:yyyyMMdd}.json")

                // 既存履歴の読み込み
                let existingHistory =
                    if File.Exists(historyFile) then
                        let json = File.ReadAllText(historyFile)
                        JsonSerializer.Deserialize<IncrementalChange list>(json)
                    else
                        []

                // 新しい変更を追加
                let updatedHistory = change :: existingHistory

                // 履歴の保存
                let updatedJson =
                    JsonSerializer.Serialize(updatedHistory, JsonSerializerOptions(WriteIndented = true))

                File.WriteAllText(historyFile, updatedJson)

                Logger.logDebug "SessionPersistenceEngine" $"增分変更記録: {sessionId}/{paneId} - {changeType}"
                return Success()

            with ex ->
                Logger.logError "SessionPersistenceEngine" $"增分変更記録失敗: {ex.Message}"
                return Error $"增分変更記録失敗: {ex.Message}"
        }

    /// 拡張セッション状態の保存
    let saveExtendedSession (config: AdvancedPersistenceConfig) (extendedState: ExtendedSessionState) =
        async {
            let sessionLock = getSessionLock extendedState.BaseSnapshot.SessionId
            let! _ = sessionLock.WaitAsync() |> Async.AwaitTask

            let! result =
                async {
                    try
                        Logger.logInfo "SessionPersistenceEngine" $"拡張セッション保存開始: {extendedState.BaseSnapshot.SessionId}"

                        // ベースセッションの保存
                        let baseResult = saveSession config.BasePersistenceConfig extendedState.BaseSnapshot

                        match baseResult with
                        | SessionPersistenceManager.Success _ ->
                            // 拡張情報の保存
                            let extendedDir =
                                Path.Combine(
                                    config.BasePersistenceConfig.StorageDirectory,
                                    "extended",
                                    extendedState.BaseSnapshot.SessionId
                                )

                            Directory.CreateDirectory(extendedDir) |> ignore

                            // バックグラウンドタスク情報
                            let tasksJson =
                                JsonSerializer.Serialize(
                                    extendedState.BackgroundTasks,
                                    JsonSerializerOptions(WriteIndented = true)
                                )

                            let tasksFile = Path.Combine(extendedDir, "background-tasks.json")
                            File.WriteAllText(tasksFile, tasksJson)

                            // 通知キュー
                            let notificationsJson =
                                JsonSerializer.Serialize(
                                    extendedState.NotificationQueue,
                                    JsonSerializerOptions(WriteIndented = true)
                                )

                            let notificationsFile = Path.Combine(extendedDir, "notifications.json")
                            File.WriteAllText(notificationsFile, notificationsJson)

                            // リソース使用状況
                            let resourceJson =
                                JsonSerializer.Serialize(
                                    extendedState.ResourceUsage,
                                    JsonSerializerOptions(WriteIndented = true)
                                )

                            let resourceFile = Path.Combine(extendedDir, "resource-usage.json")
                            File.WriteAllText(resourceFile, resourceJson)

                            // メタデータ
                            let metadata =
                                {| Priority = extendedState.Priority
                                   Tags = extendedState.Tags
                                   Dependencies = extendedState.Dependencies
                                   SavedAt = DateTime.Now |}

                            let metadataJson =
                                JsonSerializer.Serialize(metadata, JsonSerializerOptions(WriteIndented = true))

                            let metadataFile = Path.Combine(extendedDir, "extended-metadata.json")
                            File.WriteAllText(metadataFile, metadataJson)

                            Logger.logInfo
                                "SessionPersistenceEngine"
                                $"拡張セッション保存完了: {extendedState.BaseSnapshot.SessionId}"

                            return Success()

                        | SessionPersistenceManager.Error msg -> return Error $"ベースセッション保存失敗: {msg}"

                    with ex ->
                        Logger.logError "SessionPersistenceEngine" $"拡張セッション保存失敗: {ex.Message}"
                        return Error $"拡張セッション保存失敗: {ex.Message}"
                }

            releaseSessionLock extendedState.BaseSnapshot.SessionId
            return result
        }

    /// 拡張セッション状態の読み込み
    let loadExtendedSession (config: AdvancedPersistenceConfig) (sessionId: string) =
        async {
            let sessionLock = getSessionLock sessionId
            let! _ = sessionLock.WaitAsync() |> Async.AwaitTask

            let! result =
                async {
                    try
                        Logger.logInfo "SessionPersistenceEngine" $"拡張セッション読み込み開始: {sessionId}"

                        // ベースセッションの読み込み
                        let baseResult = loadSession config.BasePersistenceConfig sessionId

                        match baseResult with
                        | SessionPersistenceManager.Success baseSnapshot ->
                            // 拡張情報の読み込み
                            let extendedDir =
                                Path.Combine(config.BasePersistenceConfig.StorageDirectory, "extended", sessionId)

                            if not (Directory.Exists(extendedDir)) then
                                // 拡張情報がない場合はベース情報のみで拡張状態を作成
                                let defaultExtended =
                                    { BaseSnapshot = baseSnapshot
                                      BackgroundTasks = Map.empty
                                      NotificationQueue = []
                                      ResourceUsage = Map.empty
                                      Dependencies = []
                                      Priority = 1
                                      Tags = [] }

                                return Success defaultExtended
                            else
                                // 拡張情報の読み込み
                                let backgroundTasks =
                                    let tasksFile = Path.Combine(extendedDir, "background-tasks.json")

                                    if File.Exists(tasksFile) then
                                        let json = File.ReadAllText(tasksFile)
                                        JsonSerializer.Deserialize<Map<string, string>>(json)
                                    else
                                        Map.empty

                                let notifications =
                                    let notificationsFile = Path.Combine(extendedDir, "notifications.json")

                                    if File.Exists(notificationsFile) then
                                        let json = File.ReadAllText(notificationsFile)
                                        JsonSerializer.Deserialize<string list>(json)
                                    else
                                        []

                                let resourceUsage =
                                    let resourceFile = Path.Combine(extendedDir, "resource-usage.json")

                                    if File.Exists(resourceFile) then
                                        let json = File.ReadAllText(resourceFile)
                                        JsonSerializer.Deserialize<Map<string, int64>>(json)
                                    else
                                        Map.empty

                                let (priority, tags, dependencies) =
                                    let metadataFile = Path.Combine(extendedDir, "extended-metadata.json")

                                    if File.Exists(metadataFile) then
                                        let json = File.ReadAllText(metadataFile)

                                        let metadata =
                                            JsonSerializer.Deserialize<
                                                {| Priority: int
                                                   Tags: string list
                                                   Dependencies: string list |}
                                             >(
                                                json
                                            )

                                        (metadata.Priority, metadata.Tags, metadata.Dependencies)
                                    else
                                        (1, [], [])

                                let extendedState =
                                    { BaseSnapshot = baseSnapshot
                                      BackgroundTasks = backgroundTasks
                                      NotificationQueue = notifications
                                      ResourceUsage = resourceUsage
                                      Dependencies = dependencies
                                      Priority = priority
                                      Tags = tags }

                                Logger.logInfo "SessionPersistenceEngine" $"拡張セッション読み込み完了: {sessionId}"
                                return Success extendedState

                        | SessionPersistenceManager.Error msg -> return Error $"ベースセッション読み込み失敗: {msg}"

                    with ex ->
                        Logger.logError "SessionPersistenceEngine" $"拡張セッション読み込み失敗: {ex.Message}"
                        return Error $"拡張セッション読み込み失敗: {ex.Message}"
                }

            releaseSessionLock sessionId
            return result
        }

    /// 自動スナップショット機能
    let startAutoSnapshot (config: AdvancedPersistenceConfig) =
        let cancellationToken = new CancellationTokenSource()

        let snapshotTask =
            async {
                while not cancellationToken.Token.IsCancellationRequested do
                    try
                        // アクティブセッション一覧の取得
                        let sessions = listSessions config.BasePersistenceConfig

                        for session in sessions do
                            try
                                // 最後のスナップショットから一定時間が経過しているかチェック
                                let timeSinceLastUpdate = DateTime.Now - session.LastActivity

                                if timeSinceLastUpdate > config.AutoSnapshotInterval then
                                    Logger.logDebug "SessionPersistenceEngine" $"自動スナップショット実行: {session.SessionId}"

                                    // 簡易的な現在状態の保存（実際の実装では現在の状態を取得）
                                    let! loadResult = loadExtendedSession config session.SessionId

                                    match loadResult with
                                    | Success extendedState ->
                                        let updatedExtended =
                                            { extendedState with
                                                BaseSnapshot =
                                                    { extendedState.BaseSnapshot with
                                                        LastSavedAt = DateTime.Now } }

                                        let! saveResult = saveExtendedSession config updatedExtended

                                        match saveResult with
                                        | Success _ ->
                                            Logger.logDebug
                                                "SessionPersistenceEngine"
                                                $"自動スナップショット完了: {session.SessionId}"
                                        | Error msg ->
                                            Logger.logWarning
                                                "SessionPersistenceEngine"
                                                $"自動スナップショット失敗: {session.SessionId} - {msg}"
                                    | Error msg ->
                                        Logger.logWarning
                                            "SessionPersistenceEngine"
                                            $"自動スナップショット読み込み失敗: {session.SessionId} - {msg}"

                            with ex ->
                                Logger.logWarning
                                    "SessionPersistenceEngine"
                                    $"自動スナップショットエラー ({session.SessionId}): {ex.Message}"

                        // 間隔待機
                        do! Async.Sleep(int config.AutoSnapshotInterval.TotalMilliseconds)

                    with ex ->
                        Logger.logError "SessionPersistenceEngine" $"自動スナップショットタスクエラー: {ex.Message}"
                        do! Async.Sleep(60000) // エラー時は1分待機
            }

        Async.Start(snapshotTask, cancellationToken.Token)
        Logger.logInfo "SessionPersistenceEngine" "自動スナップショット機能開始"

        cancellationToken

    /// セッション履歴の取得
    let getSessionHistory (config: AdvancedPersistenceConfig) (sessionId: string) (fromDate: DateTime option) =
        async {
            try
                let historyDir =
                    Path.Combine(config.BasePersistenceConfig.StorageDirectory, "history", sessionId)

                if not (Directory.Exists(historyDir)) then
                    return Success []
                else
                    let historyFiles = Directory.GetFiles(historyDir, "*.json")
                    let startDate = defaultArg fromDate (DateTime.Now.AddDays(-7.0))

                    let allChanges =
                        historyFiles
                        |> Array.collect (fun file ->
                            try
                                let json = File.ReadAllText(file)
                                let changes = JsonSerializer.Deserialize<IncrementalChange list>(json)
                                changes |> List.filter (fun c -> c.Timestamp >= startDate) |> List.toArray
                            with ex ->
                                Logger.logWarning "SessionPersistenceEngine" $"履歴ファイル読み込み失敗: {file} - {ex.Message}"
                                [||])
                        |> Array.sortBy (fun c -> c.Timestamp)
                        |> Array.toList

                    return Success allChanges

            with ex ->
                Logger.logError "SessionPersistenceEngine" $"セッション履歴取得失敗: {ex.Message}"
                return Error $"セッション履歴取得失敗: {ex.Message}"
        }

    /// 統計情報の取得
    let getSessionStatistics (config: AdvancedPersistenceConfig) =
        async {
            try
                let sessions = listSessions config.BasePersistenceConfig
                let totalSessions = sessions.Length
                let totalSize = sessions |> List.sumBy (fun s -> s.SizeBytes)

                let avgLastActivity =
                    if totalSessions > 0 then
                        let totalTicks = sessions |> List.sumBy (fun s -> s.LastActivity.Ticks)
                        DateTime(totalTicks / int64 totalSessions)
                    else
                        DateTime.MinValue

                let statistics =
                    {| TotalSessions = totalSessions
                       TotalSizeBytes = totalSize
                       AverageLastActivity = avgLastActivity
                       StorageDirectory = config.BasePersistenceConfig.StorageDirectory
                       ConfiguredMaxSessions = config.MaxConcurrentSessions |}

                return Success statistics

            with ex ->
                Logger.logError "SessionPersistenceEngine" $"統計情報取得失敗: {ex.Message}"
                return Error $"統計情報取得失敗: {ex.Message}"
        }

    /// 古い履歴データのクリーンアップ
    let cleanupOldHistory (config: AdvancedPersistenceConfig) =
        async {
            try
                let historyBaseDir =
                    Path.Combine(config.BasePersistenceConfig.StorageDirectory, "history")

                let cutoffDate = DateTime.Now.AddDays(-float config.BackupRetentionDays)
                let mutable cleanedFiles = 0

                if Directory.Exists(historyBaseDir) then
                    let sessionDirs = Directory.GetDirectories(historyBaseDir)

                    for sessionDir in sessionDirs do
                        let historyFiles = Directory.GetFiles(sessionDir, "*.json")

                        for historyFile in historyFiles do
                            let fileInfo = FileInfo(historyFile)

                            if fileInfo.CreationTime < cutoffDate then
                                File.Delete(historyFile)
                                cleanedFiles <- cleanedFiles + 1
                                Logger.logDebug "SessionPersistenceEngine" $"古い履歴ファイル削除: {historyFile}"

                Logger.logInfo "SessionPersistenceEngine" $"履歴クリーンアップ完了: {cleanedFiles}ファイル削除"
                return Success cleanedFiles

            with ex ->
                Logger.logError "SessionPersistenceEngine" $"履歴クリーンアップ失敗: {ex.Message}"
                return Error $"履歴クリーンアップ失敗: {ex.Message}"
        }

namespace FCode

open System
open System.IO
open System.Collections.Concurrent
open System.Text.Json
open System.Security.Cryptography

/// セッション状態完全分離管理モジュール
module SessionStateManager =

    // 型定義
    type Message =
        { Id: string
          Content: string
          Timestamp: DateTime
          Role: string // user/assistant
          PaneId: string }

    type IsolatedSessionState =
        { PaneId: string
          SessionId: string
          WorkingDirectory: string
          ConversationHistory: Message list
          Environment: Map<string, string>
          FileHandles: Map<string, FileLockManager.FileLock>
          LastActivity: DateTime
          StateChecksum: string // データ整合性検証用
          CreatedAt: DateTime
          SavedAt: DateTime }

    type SessionConfig =
        { StatesBaseDir: string // ~/.local/share/fcode/sessions/
          MaxHistoryLength: int // 最大会話履歴数
          AutoSaveIntervalSeconds: int // 自動保存間隔
          StateRetentionDays: int // 状態保持期間
          CompressionEnabled: bool } // 履歴圧縮有効

    type SessionOperation =
        | LoadSession of PaneId: string
        | SaveSession of State: IsolatedSessionState
        | UpdateActivity of PaneId: string
        | AddMessage of PaneId: string * Message: Message
        | CleanupExpired
        | BackupStates

    type SessionOperationResult =
        | OperationSuccess
        | SessionNotFound of PaneId: string
        | StateCorrupted of PaneId: string * Reason: string
        | ChecksumMismatch of PaneId: string
        | OperationError of Reason: string

    // スレッドセーフなセッション状態管理
    type private SessionRegistry() =
        let activeSessions = ConcurrentDictionary<string, IsolatedSessionState>()
        let lastAccessTimes = ConcurrentDictionary<string, DateTime>()

        member _.GetSession(paneId: string) =
            match activeSessions.TryGetValue(paneId) with
            | true, session ->
                lastAccessTimes.[paneId] <- DateTime.UtcNow
                Some session
            | false, _ -> None

        member _.UpdateSession(session: IsolatedSessionState) =
            activeSessions.[session.PaneId] <- session
            lastAccessTimes.[session.PaneId] <- DateTime.UtcNow

        member _.RemoveSession(paneId: string) =
            activeSessions.TryRemove(paneId) |> ignore
            lastAccessTimes.TryRemove(paneId) |> ignore

        member _.GetAllSessions() = activeSessions.Values |> Seq.toArray

        member _.GetLastAccess(paneId: string) =
            match lastAccessTimes.TryGetValue(paneId) with
            | true, time -> Some time
            | false, _ -> None

    // グローバルセッションレジストリ
    let private sessionRegistry = SessionRegistry()

    // デフォルト設定
    let defaultConfig =
        { StatesBaseDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fcode", "sessions")
          MaxHistoryLength = 1000
          AutoSaveIntervalSeconds = 30
          StateRetentionDays = 30
          CompressionEnabled = true }

    // チェックサム計算
    let private calculateStateChecksum (state: IsolatedSessionState) =
        try
            let dateString = state.LastActivity.ToString("yyyy-MM-dd HH:mm:ss")

            let dataToHash =
                $"{state.PaneId}|{state.SessionId}|{state.ConversationHistory.Length}|{state.Environment.Count}|{dateString}"

            use sha256 = SHA256.Create()
            let hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(dataToHash))
            let hexString = Convert.ToHexString(hashBytes)
            hexString.[..15] // 16文字のチェックサム
        with ex ->
            Logger.logError "SessionStateManager" $"チェックサム計算エラー: {state.PaneId}" ex
            "CHECKSUM_ERROR"

    // メッセージID生成
    let private generateMessageId (paneId: string) =
        let timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss")
        let random = Random().Next(1000, 9999)
        $"msg-{paneId}-{timestamp}-{random}"

    // 新規セッション状態作成
    let createNewSessionState
        (config: SessionConfig)
        (paneId: string)
        (workingDir: string)
        (environment: Map<string, string>)
        =
        try
            let sessionId = $"session-{paneId}-{DateTime.UtcNow:yyyyMMddHHmmss}"

            let state =
                { PaneId = paneId
                  SessionId = sessionId
                  WorkingDirectory = workingDir
                  ConversationHistory = []
                  Environment = environment
                  FileHandles = Map.empty
                  LastActivity = DateTime.UtcNow
                  StateChecksum = ""
                  CreatedAt = DateTime.UtcNow
                  SavedAt = DateTime.UtcNow }

            let stateWithChecksum =
                { state with
                    StateChecksum = calculateStateChecksum state }

            sessionRegistry.UpdateSession(stateWithChecksum)

            Logger.logInfo "SessionStateManager" $"新規セッション状態作成: {paneId} [ID: {sessionId}]"
            Ok stateWithChecksum
        with ex ->
            Logger.logError "SessionStateManager" $"セッション状態作成エラー: {paneId}" ex
            Error $"作成エラー: {ex.Message}"

    // セッション状態ファイルパス取得
    let private getSessionStatePath (config: SessionConfig) (paneId: string) =
        let sessionDir = Path.Combine(config.StatesBaseDir, paneId)

        if not (Directory.Exists(sessionDir)) then
            Directory.CreateDirectory(sessionDir) |> ignore

        Path.Combine(sessionDir, "session.state")

    // 会話履歴ファイルパス取得
    let private getConversationHistoryPath (config: SessionConfig) (paneId: string) =
        let sessionDir = Path.Combine(config.StatesBaseDir, paneId)
        Path.Combine(sessionDir, "conversation.json")

    // セッション状態保存
    let saveSessionState (config: SessionConfig) (state: IsolatedSessionState) =
        try
            let stateWithUpdatedChecksum =
                { state with
                    StateChecksum = calculateStateChecksum state
                    SavedAt = DateTime.UtcNow }

            // メイン状態ファイル保存
            let statePath = getSessionStatePath config state.PaneId

            let stateJson =
                JsonSerializer.Serialize(stateWithUpdatedChecksum, JsonSerializerOptions(WriteIndented = true))

            File.WriteAllText(statePath, stateJson)

            // 会話履歴別ファイル保存（サイズ最適化）
            if state.ConversationHistory.Length > 0 then
                let historyPath = getConversationHistoryPath config state.PaneId

                let historyJson =
                    JsonSerializer.Serialize(state.ConversationHistory, JsonSerializerOptions(WriteIndented = false))

                File.WriteAllText(historyPath, historyJson)

            sessionRegistry.UpdateSession(stateWithUpdatedChecksum)

            Logger.logInfo "SessionStateManager" $"セッション状態保存: {state.PaneId} - {state.ConversationHistory.Length}メッセージ"
            Ok stateWithUpdatedChecksum
        with ex ->
            Logger.logError "SessionStateManager" $"セッション状態保存エラー: {state.PaneId}" ex
            Error $"保存エラー: {ex.Message}"

    // セッション状態読み込み
    let loadSessionState (config: SessionConfig) (paneId: string) =
        try
            let statePath = getSessionStatePath config paneId

            if File.Exists(statePath) then
                let stateJson = File.ReadAllText(statePath)
                let mutable state = JsonSerializer.Deserialize<IsolatedSessionState>(stateJson)

                // 会話履歴別ファイルから読み込み
                let historyPath = getConversationHistoryPath config paneId

                if File.Exists(historyPath) then
                    let historyJson = File.ReadAllText(historyPath)
                    let history = JsonSerializer.Deserialize<Message list>(historyJson)

                    state <-
                        { state with
                            ConversationHistory = history }

                // チェックサム検証
                let currentChecksum = calculateStateChecksum state

                if state.StateChecksum <> currentChecksum then
                    Logger.logWarn
                        "SessionStateManager"
                        $"チェックサム不一致: {paneId} - 保存時:{state.StateChecksum} 現在:{currentChecksum}"
                    // チェックサム更新して続行
                    state <-
                        { state with
                            StateChecksum = currentChecksum }

                sessionRegistry.UpdateSession(state)

                Logger.logInfo "SessionStateManager" $"セッション状態読み込み: {paneId} - {state.ConversationHistory.Length}メッセージ"
                Ok(Some state)
            else
                Logger.logInfo "SessionStateManager" $"セッション状態ファイル未存在: {paneId}"
                Ok None
        with ex ->
            Logger.logError "SessionStateManager" $"セッション状態読み込みエラー: {paneId}" ex
            Error $"読み込みエラー: {ex.Message}"

    // セッション初期化または復元
    let initializeOrRestoreSession
        (config: SessionConfig)
        (paneId: string)
        (workingDir: string)
        (environment: Map<string, string>)
        =
        try
            // まずメモリから確認
            match sessionRegistry.GetSession(paneId) with
            | Some existingSession ->
                Logger.logInfo "SessionStateManager" $"アクティブセッション取得: {paneId}"
                Ok existingSession
            | None ->
                // ファイルから復元試行
                match loadSessionState config paneId with
                | Ok(Some restoredSession) ->
                    Logger.logInfo "SessionStateManager" $"セッション復元: {paneId}"
                    Ok restoredSession
                | Ok None ->
                    // 新規作成
                    Logger.logInfo "SessionStateManager" $"新規セッション作成: {paneId}"
                    createNewSessionState config paneId workingDir environment
                | Error e -> Error e
        with ex ->
            Logger.logError "SessionStateManager" $"セッション初期化エラー: {paneId}" ex
            Error $"初期化エラー: {ex.Message}"

    // メッセージ追加
    let addMessage (config: SessionConfig) (paneId: string) (role: string) (content: string) =
        try
            match sessionRegistry.GetSession(paneId) with
            | Some session ->
                let message =
                    { Id = generateMessageId paneId
                      Content = content
                      Timestamp = DateTime.UtcNow
                      Role = role
                      PaneId = paneId }

                let updatedHistory =
                    if session.ConversationHistory.Length >= config.MaxHistoryLength then
                        // 古いメッセージを削除（FIFO）
                        message
                        :: (session.ConversationHistory |> List.take (config.MaxHistoryLength - 1))
                    else
                        message :: session.ConversationHistory

                let updatedSession =
                    { session with
                        ConversationHistory = updatedHistory
                        LastActivity = DateTime.UtcNow }

                sessionRegistry.UpdateSession(updatedSession)

                Logger.logDebug "SessionStateManager" $"メッセージ追加: {paneId} ({role}) - {content.Length}文字"
                Ok updatedSession
            | None ->
                Logger.logWarn "SessionStateManager" $"メッセージ追加失敗 - セッション未存在: {paneId}"
                Error $"セッション未存在: {paneId}"
        with ex ->
            Logger.logError "SessionStateManager" $"メッセージ追加エラー: {paneId}" ex
            Error $"メッセージ追加例外: {ex.Message}"

    // ファイルハンドル管理
    let updateFileHandles (paneId: string) (handles: Map<string, FileLockManager.FileLock>) =
        try
            match sessionRegistry.GetSession(paneId) with
            | Some session ->
                let updatedSession =
                    { session with
                        FileHandles = handles
                        LastActivity = DateTime.UtcNow }

                sessionRegistry.UpdateSession(updatedSession)

                Logger.logDebug "SessionStateManager" $"ファイルハンドル更新: {paneId} - {handles.Count}個"
                Ok updatedSession
            | None ->
                Logger.logWarn "SessionStateManager" $"ファイルハンドル更新失敗 - セッション未存在: {paneId}"
                Error $"セッション未存在: {paneId}"
        with ex ->
            Logger.logError "SessionStateManager" $"ファイルハンドル更新エラー: {paneId}" ex
            Error $"更新例外: {ex.Message}"

    // 環境変数更新
    let updateEnvironment (paneId: string) (environment: Map<string, string>) =
        try
            match sessionRegistry.GetSession(paneId) with
            | Some session ->
                let updatedSession =
                    { session with
                        Environment = environment
                        LastActivity = DateTime.UtcNow }

                sessionRegistry.UpdateSession(updatedSession)

                Logger.logDebug "SessionStateManager" $"環境変数更新: {paneId} - {environment.Count}個"
                Ok updatedSession
            | None ->
                Logger.logWarn "SessionStateManager" $"環境変数更新失敗 - セッション未存在: {paneId}"
                Error $"セッション未存在: {paneId}"
        with ex ->
            Logger.logError "SessionStateManager" $"環境変数更新エラー: {paneId}" ex
            Error $"更新例外: {ex.Message}"

    // 期限切れセッション削除
    let cleanupExpiredSessions (config: SessionConfig) =
        try
            let cutoffTime = DateTime.UtcNow.AddDays(-float config.StateRetentionDays)
            let allSessions = sessionRegistry.GetAllSessions()
            let mutable cleanupCount = 0

            for session in allSessions do
                if session.LastActivity < cutoffTime then
                    // ファイル削除
                    let sessionDir = Path.Combine(config.StatesBaseDir, session.PaneId)

                    if Directory.Exists(sessionDir) then
                        Directory.Delete(sessionDir, true)

                    sessionRegistry.RemoveSession(session.PaneId)
                    cleanupCount <- cleanupCount + 1
                    Logger.logDebug "SessionStateManager" $"期限切れセッション削除: {session.PaneId}"

            Logger.logInfo "SessionStateManager" $"期限切れセッションクリーンアップ完了: {cleanupCount}個削除"
            Ok cleanupCount
        with ex ->
            Logger.logError "SessionStateManager" $"クリーンアップエラー" ex
            Error $"クリーンアップ例外: {ex.Message}"

    // 全セッション保存
    let saveAllActiveSessions (config: SessionConfig) =
        try
            let allSessions = sessionRegistry.GetAllSessions()
            let mutable savedCount = 0
            let mutable errorCount = 0

            for session in allSessions do
                match saveSessionState config session with
                | Ok _ -> savedCount <- savedCount + 1
                | Error e ->
                    errorCount <- errorCount + 1
                    Logger.logWarn "SessionStateManager" $"セッション保存失敗: {session.PaneId} - {e}"

            Logger.logInfo "SessionStateManager" $"全セッション保存完了: {savedCount}個成功, {errorCount}個失敗"
            Ok(savedCount, errorCount)
        with ex ->
            Logger.logError "SessionStateManager" $"全セッション保存エラー" ex
            Error $"保存例外: {ex.Message}"

    // セッション統計取得
    let getSessionStatistics (config: SessionConfig) =
        try
            let allSessions = sessionRegistry.GetAllSessions()
            let now = DateTime.UtcNow

            let stats =
                {| TotalActiveSessions = allSessions.Length
                   TotalMessages = allSessions |> Array.sumBy (fun s -> s.ConversationHistory.Length)
                   SessionsByPane =
                    allSessions
                    |> Array.groupBy (fun s -> s.PaneId)
                    |> Array.map (fun (pane, sessions) -> (pane, sessions.Length))
                    |> Map.ofArray
                   OldestSession = allSessions |> Array.map (fun s -> s.CreatedAt) |> Array.tryMin
                   NewestActivity = allSessions |> Array.map (fun s -> s.LastActivity) |> Array.tryMax
                   AverageMessagesPerSession =
                    if allSessions.Length > 0 then
                        float (allSessions |> Array.sumBy (fun s -> s.ConversationHistory.Length))
                        / float allSessions.Length
                    else
                        0.0 |}

            Logger.logDebug "SessionStateManager" $"セッション統計取得完了: {stats.TotalActiveSessions}個のアクティブセッション"
            Ok stats
        with ex ->
            Logger.logError "SessionStateManager" $"統計取得エラー" ex
            Error $"統計例外: {ex.Message}"

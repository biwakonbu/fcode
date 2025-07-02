namespace FCode

open System
open System.IO
open System.Diagnostics
open System.Threading.Tasks
open SessionPersistenceManager
open FCode.SecurityUtils

/// デタッチ・アタッチ機能を提供するモジュール
module DetachAttachManager =

    /// デタッチモード
    type DetachMode =
        | GracefulDetach // プロセス維持・バックグラウンド実行
        | ForceDetach // 強制終了・状態保存
        | BackgroundMode // デーモンモード移行

    /// アタッチ結果
    type AttachResult =
        | AttachSuccess of SessionSnapshot
        | SessionNotFound of SessionId: string
        | AttachConflict of ActivePid: int
        | AttachError of Reason: string

    /// デタッチ結果
    type DetachResult =
        | DetachSuccess of SessionId: string
        | DetachError of Reason: string

    /// プロセス情報
    type ProcessInfo =
        { ProcessId: int
          SessionId: string
          StartTime: DateTime
          IsDetached: bool }

    /// デタッチ・アタッチ設定
    type DetachAttachConfig =
        { PersistenceConfig: PersistenceConfig
          BackgroundProcessTimeout: TimeSpan
          ProcessCheckInterval: TimeSpan
          MaxDetachedSessions: int }

    /// デフォルト設定
    let defaultDetachAttachConfig =
        { PersistenceConfig = defaultConfig
          BackgroundProcessTimeout = TimeSpan.FromHours(24.0)
          ProcessCheckInterval = TimeSpan.FromMinutes(1.0)
          MaxDetachedSessions = 5 }

    /// プロセスロックファイルのパス（セキュリティ強化版）
    let getProcessLockFile (config: DetachAttachConfig) (sessionId: string) =
        let safeSessionId = sanitizeSessionId sessionId
        Path.Combine(config.PersistenceConfig.StorageDirectory, "locks", $"{safeSessionId}.lock")

    /// プロセスロック情報の保存（セキュリティ強化版）
    let saveProcessLock (config: DetachAttachConfig) (sessionId: string) (processId: int) =
        try
            // セキュリティ検証: セッションIDのサニタイズ
            match sanitizeSessionId sessionId with
            | Result.Error msg ->
                Logger.logError "DetachAttach" $"セッションIDサニタイズエラー: {msg}"
                false
            | Result.Ok safeSessionId ->

                let lockDir = Path.Combine(config.PersistenceConfig.StorageDirectory, "locks")

                // パスインジェクション防止: ベースディレクトリ外へのアクセスを拒否
                let normalizedLockDir = Path.GetFullPath(lockDir)
                let normalizedBaseDir = Path.GetFullPath(config.PersistenceConfig.StorageDirectory)

                if not (normalizedLockDir.StartsWith(normalizedBaseDir)) then
                    Logger.logError "DetachAttach" "セキュリティ検証失敗: パスインジェクション攻撃を検出"
                    false
                else
                    Directory.CreateDirectory(lockDir) |> ignore

                    let lockInfo =
                        { ProcessId = processId
                          SessionId = safeSessionId
                          StartTime = DateTime.Now
                          IsDetached = true }

                    let lockFile = getProcessLockFile config safeSessionId
                    let json = System.Text.Json.JsonSerializer.Serialize(lockInfo)
                    File.WriteAllText(lockFile, json)

                    let sanitizedLogMessage =
                        sanitizeLogMessage $"プロセスロック保存: {safeSessionId} (PID: {processId})"

                    Logger.logInfo "DetachAttach" sanitizedLogMessage
                    true
        with ex ->
            let sanitizedMessage = sanitizeLogMessage ex.Message
            Logger.logError "DetachAttach" $"プロセスロック保存失敗: {sanitizedMessage}"
            false

    /// プロセスロック情報の読み込み（セキュリティ強化版）
    let loadProcessLock (config: DetachAttachConfig) (sessionId: string) =
        try
            // セキュリティ検証: セッションIDのサニタイズ
            match sanitizeSessionId sessionId with
            | Result.Error msg ->
                Logger.logWarning "DetachAttach" $"セッションIDサニタイズエラー: {msg}"
                None
            | Result.Ok safeSessionId ->
                let lockFile = getProcessLockFile config safeSessionId

                // パスインジェクション防止: ファイルパスの検証
                let normalizedLockFile = Path.GetFullPath(lockFile)
                let normalizedBaseDir = Path.GetFullPath(config.PersistenceConfig.StorageDirectory)

                if not (normalizedLockFile.StartsWith(normalizedBaseDir)) then
                    Logger.logWarning "DetachAttach" "セキュリティ検証失敗: パスインジェクション攻撃を検出"
                    None
                elif File.Exists(lockFile) then
                    try
                        let json = File.ReadAllText(lockFile)

                        // 空のファイルや無効なJSONをチェック
                        if String.IsNullOrWhiteSpace(json) then
                            Logger.logWarning "DetachAttach" "プロセスロックファイルが空です"
                            None
                        else
                            let lockInfo = System.Text.Json.JsonSerializer.Deserialize<ProcessInfo>(json)

                            // デシリアライズされたデータの妥当性チェック
                            if String.IsNullOrEmpty(lockInfo.SessionId) || lockInfo.ProcessId <= 0 then
                                Logger.logWarning "DetachAttach" "プロセスロックファイルのデータが無効です"
                                None
                            else
                                Some lockInfo
                    with jsonEx ->
                        let sanitizedJsonMessage = sanitizeLogMessage jsonEx.Message
                        Logger.logWarning "DetachAttach" $"プロセスロックファイルのJSON解析失敗: {sanitizedJsonMessage}"
                        None
                else
                    None
        with ex ->
            let sanitizedMessage = sanitizeLogMessage ex.Message
            Logger.logWarning "DetachAttach" $"プロセスロック読み込み失敗: {sanitizedMessage}"
            None

    /// プロセスロックの削除
    let removeProcessLock (config: DetachAttachConfig) (sessionId: string) =
        try
            let lockFile = getProcessLockFile config sessionId

            if File.Exists(lockFile) then
                File.Delete(lockFile)
                Logger.logInfo "DetachAttach" $"プロセスロック削除: {sessionId}"
        with ex ->
            Logger.logWarning "DetachAttach" $"プロセスロック削除失敗: {ex.Message}"

    /// プロセスの生存確認
    let isProcessAlive (processId: int) =
        try
            let proc = Process.GetProcessById(processId)
            not proc.HasExited
        with
        | :? ArgumentException -> false
        | :? InvalidOperationException -> false
        | ex ->
            Logger.logWarning "DetachAttach" $"プロセス生存確認失敗 (PID: {processId}): {ex.Message}"
            false

    /// デタッチ済みセッション一覧の取得
    let listDetachedSessions (config: DetachAttachConfig) =
        try
            let lockDir = Path.Combine(config.PersistenceConfig.StorageDirectory, "locks")

            if not (Directory.Exists(lockDir)) then
                []
            else
                Directory.GetFiles(lockDir, "*.lock")
                |> Array.choose (fun lockFile ->
                    try
                        let sessionId = Path.GetFileNameWithoutExtension(lockFile)
                        loadProcessLock config sessionId
                    with ex ->
                        Logger.logWarning "DetachAttach" $"ロックファイル読み込み失敗: {ex.Message}"
                        None)
                |> Array.filter (fun info -> isProcessAlive info.ProcessId)
                |> Array.sortByDescending (fun info -> info.StartTime)
                |> Array.toList
        with ex ->
            Logger.logError "DetachAttach" $"デタッチセッション一覧取得失敗: {ex.Message}"
            []

    /// セッションのデタッチ実行
    let detachSession (config: DetachAttachConfig) (sessionManager: obj) (sessionId: string) (mode: DetachMode) =
        async {
            try
                Logger.logInfo "DetachAttach" $"セッションデタッチ開始: {sessionId} (モード: {mode})"

                // 現在のプロセスIDを取得
                let currentPid = Process.GetCurrentProcess().Id

                match mode with
                | GracefulDetach ->
                    // グレースフルデタッチ: セッション状態保存 + プロセス継続

                    // TODO: SessionManagerから現在の状態を取得
                    // let! currentSnapshot = sessionManager.CreateSnapshot(sessionId)

                    // 仮の実装: 基本的なセッション情報作成
                    let snapshot =
                        { SessionId = sessionId
                          PaneStates = Map.empty // TODO: 実際のペイン状態を取得
                          CreatedAt = DateTime.Now.AddHours(-1.0)
                          LastSavedAt = DateTime.Now
                          TotalSize = 0L
                          Version = "1.0" }

                    match saveSession config.PersistenceConfig snapshot with
                    | Success _ ->
                        if saveProcessLock config sessionId currentPid then
                            Logger.logInfo "DetachAttach" $"セッションデタッチ完了: {sessionId}"
                            return DetachSuccess sessionId
                        else
                            return DetachError "プロセスロック保存失敗"
                    | Error msg -> return DetachError $"セッション状態保存失敗: {msg}"

                | ForceDetach ->
                    // 強制デタッチ: セッション状態保存 + プロセス終了準備

                    // 仮の実装
                    let snapshot =
                        { SessionId = sessionId
                          PaneStates = Map.empty
                          CreatedAt = DateTime.Now.AddHours(-1.0)
                          LastSavedAt = DateTime.Now
                          TotalSize = 0L
                          Version = "1.0" }

                    match saveSession config.PersistenceConfig snapshot with
                    | Success _ ->
                        Logger.logInfo "DetachAttach" $"強制デタッチ完了: {sessionId}"
                        return DetachSuccess sessionId
                    | Error msg -> return DetachError $"強制デタッチ失敗: {msg}"

                | BackgroundMode ->
                    // バックグラウンドモード: デーモン化処理
                    Logger.logInfo "DetachAttach" "バックグラウンドモード移行"
                    return DetachError "バックグラウンドモード未実装"

            with ex ->
                Logger.logError "DetachAttach" $"デタッチ処理失敗: {ex.Message}"
                return DetachError $"デタッチ処理失敗: {ex.Message}"
        }

    /// セッションのアタッチ実行
    let attachSession (config: DetachAttachConfig) (sessionId: string) =
        async {
            try
                Logger.logInfo "DetachAttach" $"セッションアタッチ開始: {sessionId}"

                // プロセスロック情報の確認
                let lockInfo = loadProcessLock config sessionId

                match lockInfo with
                | Some info when isProcessAlive info.ProcessId ->
                    Logger.logWarning "DetachAttach" $"セッションが既にアクティブです: {sessionId} (PID: {info.ProcessId})"
                    return AttachConflict info.ProcessId

                | Some info ->
                    // プロセスが終了している場合、ロックを削除
                    Logger.logInfo "DetachAttach" $"古いプロセスロックを削除: {sessionId} (PID: {info.ProcessId})"
                    removeProcessLock config sessionId

                    // セッション状態の読み込み
                    match loadSession config.PersistenceConfig sessionId with
                    | Success snapshot ->
                        Logger.logInfo "DetachAttach" $"セッションアタッチ完了: {sessionId}"
                        return AttachSuccess snapshot
                    | Error msg -> return AttachError $"セッション読み込み失敗: {msg}"

                | None ->
                    // プロセスロックがない場合、セッション状態の直接読み込み
                    match loadSession config.PersistenceConfig sessionId with
                    | Success snapshot ->
                        Logger.logInfo "DetachAttach" $"セッションアタッチ完了: {sessionId}"
                        return AttachSuccess snapshot
                    | Error msg -> return SessionNotFound sessionId

            with ex ->
                Logger.logError "DetachAttach" $"アタッチ処理失敗: {ex.Message}"
                return AttachError $"アタッチ処理失敗: {ex.Message}"
        }

    /// 孤立プロセスロックの清理
    let cleanupOrphanedLocks (config: DetachAttachConfig) =
        async {
            try
                Logger.logInfo "DetachAttach" "孤立プロセスロック清理開始"

                let lockDir = Path.Combine(config.PersistenceConfig.StorageDirectory, "locks")
                let mutable cleanedCount = 0

                if Directory.Exists(lockDir) then
                    let lockFiles = Directory.GetFiles(lockDir, "*.lock")

                    for lockFile in lockFiles do
                        try
                            let sessionId = Path.GetFileNameWithoutExtension(lockFile)

                            match loadProcessLock config sessionId with
                            | Some lockInfo when not (isProcessAlive lockInfo.ProcessId) ->
                                removeProcessLock config sessionId
                                cleanedCount <- cleanedCount + 1
                                Logger.logInfo "DetachAttach" $"孤立ロック削除: {sessionId} (PID: {lockInfo.ProcessId})"
                            | _ -> ()
                        with ex ->
                            Logger.logWarning "DetachAttach" $"ロックファイル処理失敗: {ex.Message}"

                Logger.logInfo "DetachAttach" $"孤立プロセスロック清理完了: {cleanedCount}件削除"
                return cleanedCount

            with ex ->
                Logger.logError "DetachAttach" $"孤立ロック清理失敗: {ex.Message}"
                return 0
        }

    /// バックグラウンドでの定期清理タスク
    let startPeriodicCleanup (config: DetachAttachConfig) =
        let cleanupTask =
            async {
                while true do
                    try
                        let! _ = cleanupOrphanedLocks config
                        do! Async.Sleep(int config.ProcessCheckInterval.TotalMilliseconds)
                    with ex ->
                        Logger.logError "DetachAttach" $"定期清理エラー: {ex.Message}"
                        do! Async.Sleep 60000 // エラー時は1分待機
            }

        Async.Start cleanupTask
        Logger.logInfo "DetachAttach" "定期清理タスク開始"

    /// セッション復旧候補の検索
    let findRecoverableSessions (config: DetachAttachConfig) =
        try
            let allSessions = listSessions config.PersistenceConfig

            let detachedSessions =
                listDetachedSessions config
                |> List.map (fun info -> info.SessionId)
                |> Set.ofList

            // 最近活動があり、デタッチされていないセッション
            let recentTime = DateTime.Now.AddHours(-2.0)

            let recoverableSessions =
                allSessions
                |> List.filter (fun s -> s.LastActivity > recentTime && not (detachedSessions.Contains s.SessionId))
                |> List.sortByDescending (fun s -> s.LastActivity)

            recoverableSessions
        with ex ->
            Logger.logError "DetachAttach" $"復旧候補検索失敗: {ex.Message}"
            []

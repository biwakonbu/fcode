namespace FCode

open System
open System.IO
open System.Diagnostics
open System.Threading.Tasks
open SessionPersistenceManager

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

    /// プロセスロックファイルのパス
    let getProcessLockFile (config: DetachAttachConfig) (sessionId: string) =
        Path.Combine(config.PersistenceConfig.StorageDirectory, "locks", $"{sessionId}.lock")

    /// プロセスロック情報の保存
    let saveProcessLock (config: DetachAttachConfig) (sessionId: string) (processId: int) =
        try
            let lockDir = Path.Combine(config.PersistenceConfig.StorageDirectory, "locks")
            Directory.CreateDirectory(lockDir) |> ignore

            let lockInfo =
                { ProcessId = processId
                  SessionId = sessionId
                  StartTime = DateTime.Now
                  IsDetached = true }

            let lockFile = getProcessLockFile config sessionId
            let json = System.Text.Json.JsonSerializer.Serialize(lockInfo)
            File.WriteAllText(lockFile, json)

            Logger.logInfo "DetachAttach" $"プロセスロック保存: {sessionId} (PID: {processId})"
            true
        with ex ->
            Logger.logError "DetachAttach" $"プロセスロック保存失敗: {ex.Message}"
            false

    /// プロセスロック情報の読み込み
    let loadProcessLock (config: DetachAttachConfig) (sessionId: string) =
        try
            let lockFile = getProcessLockFile config sessionId

            if File.Exists(lockFile) then
                let json = File.ReadAllText(lockFile)
                let lockInfo = System.Text.Json.JsonSerializer.Deserialize<ProcessInfo>(json)
                Some lockInfo
            else
                None
        with ex ->
            Logger.logWarning "DetachAttach" $"プロセスロック読み込み失敗: {ex.Message}"
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

                let detachedSessions = listDetachedSessions config
                let mutable cleanedCount = 0

                for session in detachedSessions do
                    if not (isProcessAlive session.ProcessId) then
                        removeProcessLock config session.SessionId
                        cleanedCount <- cleanedCount + 1
                        Logger.logInfo "DetachAttach" $"孤立ロック削除: {session.SessionId} (PID: {session.ProcessId})"

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

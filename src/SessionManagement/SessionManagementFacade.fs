namespace FCode.SessionManagement

open System
open System.Threading.Tasks
open FCode.SessionPersistenceManager
open FCode.DetachAttachManager
open FCode.SessionManagement.SessionPersistenceEngine
open FCode.SessionManagement.BackgroundTaskManager
open FCode.SessionManagement.MultiSessionCoordinator
open FCode.SessionManagement.NotificationEngine
open FCode

/// セッション管理統合ファサード
module SessionManagementFacade =

    /// 統合セッション管理設定
    type IntegratedSessionConfig =
        { AdvancedPersistenceConfig: AdvancedPersistenceConfig
          BackgroundTaskConfig: BackgroundTaskConfig
          MultiSessionConfig: MultiSessionConfig
          NotificationConfig: NotificationConfig
          DetachAttachConfig: DetachAttachConfig
          EnableAutoSnapshot: bool
          EnableAutoCleanup: bool
          AutoCleanupInterval: TimeSpan }

    /// セッション操作結果
    type SessionOperationResult<'T> =
        | Success of 'T
        | Error of string

    /// セッション管理ファサード
    type SessionManagementFacade(config: IntegratedSessionConfig) =

        // 自動処理タスクのキャンセレーショントークン
        let mutable autoSnapshotCancellation: System.Threading.CancellationTokenSource option =
            None

        let mutable autoCleanupTask: Task option = None

        /// 初期化
        member this.Initialize() =
            async {
                try
                    Logger.logInfo "SessionManagementFacade" "セッション管理ファサード初期化開始"

                    // 各コンポーネントのストレージ初期化
                    let persistenceInit =
                        initializeStorage config.AdvancedPersistenceConfig.BasePersistenceConfig

                    let taskStorageInit = initializeTaskStorage config.BackgroundTaskConfig
                    let coordinatorInit = initializeCoordinatorStorage config.MultiSessionConfig
                    let notificationInit = initializeNotificationStorage config.NotificationConfig

                    match persistenceInit with
                    | SessionPersistenceManager.Error msg -> return Error $"永続化ストレージ初期化失敗: {msg}"
                    | SessionPersistenceManager.Success _ ->

                        if not taskStorageInit then
                            return Error "バックグラウンドタスクストレージ初期化失敗"
                        elif not coordinatorInit then
                            return Error "セッション協調ストレージ初期化失敗"
                        elif not notificationInit then
                            return Error "通知ストレージ初期化失敗"
                        else

                            // 既存のアクティブタスクの復元
                            let! _ = loadActiveTasks config.BackgroundTaskConfig

                            // 自動スナップショット機能の開始
                            if config.EnableAutoSnapshot then
                                autoSnapshotCancellation <- Some(startAutoSnapshot config.AdvancedPersistenceConfig)
                                Logger.logInfo "SessionManagementFacade" "自動スナップショット機能開始"

                            // 自動クリーンアップタスクの開始
                            if config.EnableAutoCleanup then
                                this.StartAutoCleanupTask()

                            Logger.logInfo "SessionManagementFacade" "セッション管理ファサード初期化完了"
                            return Success()

                with ex ->
                    Logger.logError "SessionManagementFacade" $"初期化失敗: {ex.Message}"
                    return Error $"初期化失敗: {ex.Message}"
            }

        /// 自動クリーンアップタスクの開始
        member private this.StartAutoCleanupTask() =
            let cleanupTask =
                Task.Run(
                    System.Func<Task>(fun () ->
                        async {
                            while true do
                                try
                                    // 各種クリーンアップの実行
                                    let! _ = cleanupCompletedTasks config.BackgroundTaskConfig
                                    let! _ = cleanupInactiveSessions config.MultiSessionConfig
                                    let! _ = cleanupOldNotifications config.NotificationConfig
                                    let! _ = cleanupOldHistory config.AdvancedPersistenceConfig

                                    Logger.logDebug "SessionManagementFacade" "定期クリーンアップ実行完了"

                                    // 間隔待機
                                    do! Async.Sleep(int config.AutoCleanupInterval.TotalMilliseconds)

                                with ex ->
                                    Logger.logError "SessionManagementFacade" $"自動クリーンアップエラー: {ex.Message}"
                                    do! Async.Sleep(60000) // エラー時は1分待機
                        }
                        |> Async.StartAsTask
                        :> Task)
                )

            autoCleanupTask <- Some cleanupTask
            Logger.logInfo "SessionManagementFacade" "自動クリーンアップタスク開始"

        /// 新しいセッションの作成
        member this.CreateSession(sessionId: string, coordinationMode: SessionCoordinationMode) =
            async {
                try
                    Logger.logInfo "SessionManagementFacade" $"新規セッション作成開始: {sessionId}"

                    // セッションIDの重複チェック
                    let existingSessions = getActiveSessions ()

                    if existingSessions |> List.exists (fun s -> s.SessionId = sessionId) then
                        return Error $"セッションID重複: {sessionId}"
                    else

                        // 基本セッションスナップショットの作成
                        let baseSnapshot =
                            { SessionId = sessionId
                              PaneStates = Map.empty
                              CreatedAt = DateTime.Now
                              LastSavedAt = DateTime.Now
                              TotalSize = 0L
                              Version = "2.0" }

                        // 拡張セッション状態の作成
                        let extendedState =
                            { BaseSnapshot = baseSnapshot
                              BackgroundTasks = Map.empty
                              NotificationQueue = []
                              ResourceUsage = Map.empty
                              Dependencies = []
                              Priority = 1
                              Tags = [ "new-session" ] }

                        // 拡張セッションの保存
                        let! saveResult = saveExtendedSession config.AdvancedPersistenceConfig extendedState

                        match saveResult with
                        | SessionPersistenceEngine.Success _ ->
                            // マルチセッション協調機能への登録
                            let! registerResult =
                                registerSession config.MultiSessionConfig sessionId extendedState coordinationMode

                            if registerResult then
                                // セッション作成通知
                                let notification =
                                    createNotification
                                        (Some sessionId)
                                        None
                                        UserAction
                                        NotificationSeverity.Info
                                        "新規セッション作成"
                                        $"セッション '{sessionId}' が正常に作成されました"
                                        Map.empty
                                        config.NotificationConfig.EnabledChannels
                                        [ "session"; "create" ]
                                        None

                                let! _ = sendNotification config.NotificationConfig notification

                                Logger.logInfo "SessionManagementFacade" $"新規セッション作成完了: {sessionId}"
                                return Success extendedState
                            else
                                return Error "マルチセッション登録失敗"

                        | SessionPersistenceEngine.Error msg -> return Error $"セッション保存失敗: {msg}"

                with ex ->
                    Logger.logError "SessionManagementFacade" $"セッション作成失敗 ({sessionId}): {ex.Message}"
                    return Error $"セッション作成失敗: {ex.Message}"
            }

        /// セッションの読み込み
        member this.LoadSession(sessionId: string) =
            async {
                try
                    Logger.logInfo "SessionManagementFacade" $"セッション読み込み開始: {sessionId}"

                    let! loadResult = loadExtendedSession config.AdvancedPersistenceConfig sessionId

                    match loadResult with
                    | SessionPersistenceEngine.Success extendedState ->
                        Logger.logInfo "SessionManagementFacade" $"セッション読み込み完了: {sessionId}"
                        return Success extendedState

                    | SessionPersistenceEngine.Error msg ->
                        Logger.logError "SessionManagementFacade" $"セッション読み込み失敗: {sessionId} - {msg}"
                        return Error $"セッション読み込み失敗: {msg}"

                with ex ->
                    Logger.logError "SessionManagementFacade" $"セッション読み込みエラー ({sessionId}): {ex.Message}"
                    return Error $"セッション読み込みエラー: {ex.Message}"
            }

        /// セッションの保存
        member this.SaveSession(extendedState: ExtendedSessionState) =
            async {
                try
                    Logger.logDebug "SessionManagementFacade" $"セッション保存開始: {extendedState.BaseSnapshot.SessionId}"

                    let! saveResult = saveExtendedSession config.AdvancedPersistenceConfig extendedState

                    match saveResult with
                    | SessionPersistenceEngine.Success _ ->
                        Logger.logDebug "SessionManagementFacade" $"セッション保存完了: {extendedState.BaseSnapshot.SessionId}"
                        return Success()

                    | SessionPersistenceEngine.Error msg -> return Error $"セッション保存失敗: {msg}"

                with ex ->
                    Logger.logError
                        "SessionManagementFacade"
                        $"セッション保存エラー ({extendedState.BaseSnapshot.SessionId}): {ex.Message}"

                    return Error $"セッション保存エラー: {ex.Message}"
            }

        /// セッションのデタッチ
        member this.DetachSession(sessionId: string, mode: DetachMode) =
            async {
                try
                    Logger.logInfo "SessionManagementFacade" $"セッションデタッチ開始: {sessionId}"

                    // デタッチ処理の実行
                    let! detachResult = detachSession config.DetachAttachConfig null sessionId mode

                    match detachResult with
                    | DetachSuccess _ ->
                        // デタッチ通知の送信
                        let! _ = notifySessionDetached config.NotificationConfig sessionId

                        Logger.logInfo "SessionManagementFacade" $"セッションデタッチ完了: {sessionId}"
                        return Success()

                    | DetachError msg -> return Error $"デタッチ失敗: {msg}"

                with ex ->
                    Logger.logError "SessionManagementFacade" $"セッションデタッチエラー ({sessionId}): {ex.Message}"
                    return Error $"セッションデタッチエラー: {ex.Message}"
            }

        /// セッションのアタッチ
        member this.AttachSession(sessionId: string) =
            async {
                try
                    Logger.logInfo "SessionManagementFacade" $"セッションアタッチ開始: {sessionId}"

                    let! attachResult = attachSession config.DetachAttachConfig sessionId

                    match attachResult with
                    | AttachSuccess snapshot ->
                        // アタッチ通知の送信
                        let! _ = notifySessionAttached config.NotificationConfig sessionId

                        Logger.logInfo "SessionManagementFacade" $"セッションアタッチ完了: {sessionId}"
                        return Success snapshot

                    | SessionNotFound _ -> return Error $"セッションが見つかりません: {sessionId}"

                    | AttachConflict activePid -> return Error $"セッションが既にアクティブです (PID: {activePid})"

                    | AttachError reason -> return Error $"アタッチ失敗: {reason}"

                with ex ->
                    Logger.logError "SessionManagementFacade" $"セッションアタッチエラー ({sessionId}): {ex.Message}"
                    return Error $"セッションアタッチエラー: {ex.Message}"
            }

        /// セッション切り替え
        member this.SwitchSession(targetSessionId: string, strategy: SessionSwitchStrategy) =
            async {
                try
                    Logger.logInfo "SessionManagementFacade" $"セッション切り替え開始: {targetSessionId}"

                    let! switchResult =
                        switchToSession
                            config.MultiSessionConfig
                            config.AdvancedPersistenceConfig
                            targetSessionId
                            strategy

                    match switchResult with
                    | Ok extendedState ->
                        Logger.logInfo "SessionManagementFacade" $"セッション切り替え完了: {targetSessionId}"
                        return Success extendedState

                    | Result.Error msg -> return Error $"セッション切り替え失敗: {msg}"

                with ex ->
                    Logger.logError "SessionManagementFacade" $"セッション切り替えエラー ({targetSessionId}): {ex.Message}"
                    return Error $"セッション切り替えエラー: {ex.Message}"
            }

        /// バックグラウンドタスクの作成・実行
        member this.CreateAndExecuteBackgroundTask
            (
                sessionId: string,
                paneId: string,
                taskType: string,
                description: string,
                command: string,
                arguments: string list,
                workingDir: string,
                env: Map<string, string>,
                priority: TaskPriority
            ) =
            async {
                try
                    Logger.logInfo "SessionManagementFacade" $"バックグラウンドタスク作成: {description} (セッション: {sessionId})"

                    // タスクの作成
                    let task =
                        createTask sessionId paneId taskType description command arguments workingDir env priority

                    // タスクのスケジューリング
                    let! scheduleResult = scheduleTask config.BackgroundTaskConfig task

                    if scheduleResult then
                        Logger.logInfo "SessionManagementFacade" $"バックグラウンドタスク開始: {task.TaskId}"
                        return Success task.TaskId
                    else
                        return Error "タスクスケジューリング失敗"

                with ex ->
                    Logger.logError "SessionManagementFacade" $"バックグラウンドタスク作成エラー: {ex.Message}"
                    return Error $"バックグラウンドタスク作成エラー: {ex.Message}"
            }

        /// 通知の送信
        member this.SendNotification
            (
                sessionId: string option,
                paneId: string option,
                notificationType: NotificationType,
                severity: NotificationSeverity,
                title: string,
                content: string,
                details: Map<string, string>
            ) =
            async {
                try
                    let notification =
                        createNotification
                            sessionId
                            paneId
                            notificationType
                            severity
                            title
                            content
                            details
                            config.NotificationConfig.EnabledChannels
                            []
                            None

                    let! result = sendNotification config.NotificationConfig notification

                    if result then
                        return Success notification.MessageId
                    else
                        return Error "通知送信失敗"

                with ex ->
                    Logger.logError "SessionManagementFacade" $"通知送信エラー: {ex.Message}"
                    return Error $"通知送信エラー: {ex.Message}"
            }

        /// セッション一覧の取得
        member this.GetSessions() =
            try
                let sessions = getActiveSessions ()
                Success sessions
            with ex ->
                Logger.logError "SessionManagementFacade" $"セッション一覧取得エラー: {ex.Message}"
                Error $"セッション一覧取得エラー: {ex.Message}"

        /// 統計情報の取得
        member this.GetStatistics() =
            async {
                try
                    let! sessionStats = getSessionStatistics config.MultiSessionConfig
                    let! notificationStats = getNotificationStatistics config.NotificationConfig

                    let! persistenceStats =
                        SessionPersistenceEngine.getSessionStatistics config.AdvancedPersistenceConfig

                    let combinedStats =
                        {| SessionManagement = sessionStats
                           Notifications = notificationStats
                           Persistence = persistenceStats
                           BackgroundTasks = getActiveTasks None |> List.length
                           Timestamp = DateTime.Now |}

                    return Success combinedStats

                with ex ->
                    Logger.logError "SessionManagementFacade" $"統計情報取得エラー: {ex.Message}"
                    return Error $"統計情報取得エラー: {ex.Message}"
            }

        /// リソースのクリーンアップ
        member this.Dispose() =
            async {
                try
                    Logger.logInfo "SessionManagementFacade" "セッション管理ファサード終了処理開始"

                    // 自動スナップショットの停止
                    match autoSnapshotCancellation with
                    | Some cancellation ->
                        cancellation.Cancel()
                        autoSnapshotCancellation <- None
                        Logger.logDebug "SessionManagementFacade" "自動スナップショット停止"
                    | None -> ()

                    // 自動クリーンアップタスクの停止
                    match autoCleanupTask with
                    | Some task ->
                        task.Dispose()
                        autoCleanupTask <- None
                        Logger.logDebug "SessionManagementFacade" "自動クリーンアップタスク停止"
                    | None -> ()

                    // アクティブタスクのクリーンアップ
                    let! _ = cleanupCompletedTasks config.BackgroundTaskConfig

                    Logger.logInfo "SessionManagementFacade" "セッション管理ファサード終了処理完了"

                with ex ->
                    Logger.logError "SessionManagementFacade" $"終了処理エラー: {ex.Message}"
            }

        interface IDisposable with
            member this.Dispose() =
                this.Dispose() |> Async.RunSynchronously

    /// デフォルト統合設定の作成
    let createDefaultIntegratedConfig () =
        { AdvancedPersistenceConfig = defaultAdvancedConfig
          BackgroundTaskConfig = defaultBackgroundTaskConfig
          MultiSessionConfig = defaultMultiSessionConfig
          NotificationConfig = defaultNotificationConfig
          DetachAttachConfig = defaultDetachAttachConfig
          EnableAutoSnapshot = true
          EnableAutoCleanup = true
          AutoCleanupInterval = TimeSpan.FromMinutes(10.0) }

    /// ファサードインスタンスの作成
    let createSessionManagementFacade (config: IntegratedSessionConfig option) =
        let finalConfig = defaultArg config (createDefaultIntegratedConfig ())
        new SessionManagementFacade(finalConfig)

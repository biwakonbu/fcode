namespace FCode.SessionManagement

open System
open System.IO
open System.Text.Json
open System.Threading
open System.Collections.Concurrent
open FCode.SessionPersistenceManager
open FCode.DetachAttachManager
open FCode.SessionManagement.SessionPersistenceEngine
open FCode.SessionManagement.BackgroundTaskManager
open FCode

/// 複数セッション管理・協調機能
module MultiSessionCoordinator =

    /// セッション切り替え戦略
    type SessionSwitchStrategy =
        | Immediate // 即座に切り替え
        | Graceful // グレースフルな切り替え（現在の作業を保存）
        | Background // バックグラウンドに移行

    /// セッション協調モード
    type SessionCoordinationMode =
        | Independent // 独立動作
        | SharedResources // リソース共有
        | Collaborative // 協調作業

    /// セッション間通信メッセージ
    type InterSessionMessage =
        { MessageId: string
          FromSessionId: string
          ToSessionId: string
          MessageType: string
          Content: string
          Priority: int
          SentAt: DateTime
          ExpiresAt: DateTime option }

    /// セッション状態サマリー
    type SessionStateSummary =
        { SessionId: string
          PaneCount: int
          ActiveTaskCount: int
          LastActivity: DateTime
          IsDetached: bool
          ResourceUsage: int64
          Priority: int
          CoordinationMode: SessionCoordinationMode }

    /// マルチセッション設定
    type MultiSessionConfig =
        { MaxConcurrentSessions: int
          DefaultSwitchStrategy: SessionSwitchStrategy
          ResourceSharingEnabled: bool
          InterSessionMessagingEnabled: bool
          SessionTimeoutMinutes: int
          AutoCleanupEnabled: bool
          CoordinatorStorageDirectory: string }

    /// デフォルト設定
    let defaultMultiSessionConfig =
        { MaxConcurrentSessions = 10
          DefaultSwitchStrategy = Graceful
          ResourceSharingEnabled = true
          InterSessionMessagingEnabled = true
          SessionTimeoutMinutes = 30
          AutoCleanupEnabled = true
          CoordinatorStorageDirectory =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "fcode",
                "multi-session"
            ) }

    /// アクティブセッション管理
    let private activeSessions = ConcurrentDictionary<string, SessionStateSummary>()
    let private sessionMessages = ConcurrentQueue<InterSessionMessage>()
    let private currentActiveSession = ref None
    let private sessionSwitchLock = new SemaphoreSlim(1, 1)

    /// セッション協調ストレージの初期化
    let initializeCoordinatorStorage (config: MultiSessionConfig) =
        try
            Directory.CreateDirectory(config.CoordinatorStorageDirectory) |> ignore

            Directory.CreateDirectory(Path.Combine(config.CoordinatorStorageDirectory, "messages"))
            |> ignore

            Directory.CreateDirectory(Path.Combine(config.CoordinatorStorageDirectory, "state"))
            |> ignore

            Logger.logInfo "MultiSessionCoordinator" "協調ストレージ初期化完了"
            true
        with ex ->
            Logger.logError "MultiSessionCoordinator" $"協調ストレージ初期化失敗: {ex.Message}"
            false

    /// セッション状態サマリーの作成
    let createSessionSummary
        (sessionId: string)
        (extendedState: ExtendedSessionState)
        (coordinationMode: SessionCoordinationMode)
        =
        let activeTaskCount =
            getActiveTasks (Some sessionId)
            |> List.filter (fun t -> t.Status = Running || t.Status = Pending)
            |> List.length

        { SessionId = sessionId
          PaneCount = extendedState.BaseSnapshot.PaneStates.Count
          ActiveTaskCount = activeTaskCount
          LastActivity = extendedState.BaseSnapshot.LastSavedAt
          IsDetached = false // DetachAttachManagerと連携して取得
          ResourceUsage = extendedState.BaseSnapshot.TotalSize
          Priority = extendedState.Priority
          CoordinationMode = coordinationMode }

    /// セッションの登録
    let registerSession
        (config: MultiSessionConfig)
        (sessionId: string)
        (extendedState: ExtendedSessionState)
        (coordinationMode: SessionCoordinationMode)
        =
        async {
            try
                let summary = createSessionSummary sessionId extendedState coordinationMode

                if activeSessions.Count >= config.MaxConcurrentSessions then
                    // 最も古いセッションを特定
                    let oldestSession =
                        activeSessions.Values |> Seq.sortBy (fun s -> s.LastActivity) |> Seq.head

                    Logger.logInfo "MultiSessionCoordinator" $"最大セッション数に達したため古いセッションを非アクティブ化: {oldestSession.SessionId}"
                    activeSessions.TryRemove(oldestSession.SessionId) |> ignore

                activeSessions.TryAdd(sessionId, summary) |> ignore

                // 状態の永続化
                let stateFile =
                    Path.Combine(config.CoordinatorStorageDirectory, "state", $"{sessionId}.json")

                let json =
                    JsonSerializer.Serialize(summary, JsonSerializerOptions(WriteIndented = true))

                File.WriteAllText(stateFile, json)

                Logger.logInfo "MultiSessionCoordinator" $"セッション登録完了: {sessionId} (協調モード: {coordinationMode})"
                return true

            with ex ->
                Logger.logError "MultiSessionCoordinator" $"セッション登録失敗 ({sessionId}): {ex.Message}"
                return false
        }

    /// セッションの登録解除
    let unregisterSession (config: MultiSessionConfig) (sessionId: string) =
        async {
            try
                match activeSessions.TryRemove(sessionId) with
                | true, _ ->
                    // 状態ファイルの削除
                    let stateFile =
                        Path.Combine(config.CoordinatorStorageDirectory, "state", $"{sessionId}.json")

                    if File.Exists(stateFile) then
                        File.Delete(stateFile)

                    // 現在のアクティブセッションの場合は解除
                    if !currentActiveSession = Some sessionId then
                        currentActiveSession := None

                    Logger.logInfo "MultiSessionCoordinator" $"セッション登録解除完了: {sessionId}"
                    return true
                | false, _ ->
                    Logger.logWarning "MultiSessionCoordinator" $"登録解除対象セッションが見つかりません: {sessionId}"
                    return false

            with ex ->
                Logger.logError "MultiSessionCoordinator" $"セッション登録解除失敗 ({sessionId}): {ex.Message}"
                return false
        }

    /// アクティブセッション一覧の取得
    let getActiveSessions () =
        activeSessions.Values
        |> Seq.sortByDescending (fun s -> s.LastActivity)
        |> Seq.toList

    /// セッション切り替えの実行
    let switchToSession
        (config: MultiSessionConfig)
        (advancedConfig: AdvancedPersistenceConfig)
        (targetSessionId: string)
        (strategy: SessionSwitchStrategy)
        =
        async {
            let! _ = sessionSwitchLock.WaitAsync() |> Async.AwaitTask

            let! result =
                async {
                    try
                        Logger.logInfo "MultiSessionCoordinator" $"セッション切り替え開始: {targetSessionId} (戦略: {strategy})"

                        // 現在のアクティブセッションの処理
                        match !currentActiveSession with
                        | Some currentSessionId when currentSessionId <> targetSessionId ->
                            Logger.logInfo "MultiSessionCoordinator" $"現在のセッションを非アクティブ化: {currentSessionId}"

                            match strategy with
                            | Graceful ->
                                // 現在のセッション状態を保存
                                let! currentState = loadExtendedSession advancedConfig currentSessionId

                                match currentState with
                                | SessionPersistenceEngine.Success state ->
                                    let! saveResult = saveExtendedSession advancedConfig state

                                    match saveResult with
                                    | SessionPersistenceEngine.Success _ ->
                                        Logger.logDebug "MultiSessionCoordinator" $"現在のセッション状態保存完了: {currentSessionId}"
                                    | SessionPersistenceEngine.Error msg ->
                                        Logger.logWarning
                                            "MultiSessionCoordinator"
                                            $"現在のセッション状態保存失敗: {currentSessionId} - {msg}"
                                | SessionPersistenceEngine.Error msg ->
                                    Logger.logWarning
                                        "MultiSessionCoordinator"
                                        $"現在のセッション状態取得失敗: {currentSessionId} - {msg}"

                            | Background ->
                                // バックグラウンドモードに移行
                                Logger.logInfo "MultiSessionCoordinator" $"現在のセッションをバックグラウンドに移行: {currentSessionId}"

                            | Immediate ->
                                // 即座に切り替え（状態保存なし）
                                Logger.logDebug
                                    "MultiSessionCoordinator"
                                    $"即座に切り替え: {currentSessionId} -> {targetSessionId}"

                        | _ -> () // アクティブセッションがない、または同じセッション

                        // 対象セッションの存在確認
                        match activeSessions.TryGetValue(targetSessionId) with
                        | true, sessionSummary ->
                            // セッション状態の読み込み
                            let! targetState = loadExtendedSession advancedConfig targetSessionId

                            match targetState with
                            | SessionPersistenceEngine.Success state ->
                                // アクティブセッションとして設定
                                currentActiveSession := Some targetSessionId

                                // セッション状態の更新
                                let updatedSummary =
                                    { sessionSummary with
                                        LastActivity = DateTime.Now }

                                activeSessions.TryUpdate(targetSessionId, updatedSummary, sessionSummary)
                                |> ignore

                                Logger.logInfo "MultiSessionCoordinator" $"セッション切り替え完了: {targetSessionId}"
                                return Ok state

                            | SessionPersistenceEngine.Error msg ->
                                Logger.logError "MultiSessionCoordinator" $"対象セッション読み込み失敗: {targetSessionId} - {msg}"
                                return Result.Error $"対象セッション読み込み失敗: {msg}"

                        | false, _ ->
                            Logger.logError "MultiSessionCoordinator" $"対象セッションが見つかりません: {targetSessionId}"
                            return Result.Error $"対象セッションが見つかりません: {targetSessionId}"

                    with ex ->
                        Logger.logError "MultiSessionCoordinator" $"セッション切り替えエラー ({targetSessionId}): {ex.Message}"
                        return Result.Error $"セッション切り替えエラー: {ex.Message}"
                }

            sessionSwitchLock.Release() |> ignore
            return result
        }

    /// セッション間メッセージの送信
    let sendInterSessionMessage
        (config: MultiSessionConfig)
        (fromSessionId: string)
        (toSessionId: string)
        (messageType: string)
        (content: string)
        (priority: int)
        =
        async {
            try
                if not config.InterSessionMessagingEnabled then
                    return false
                else

                    let messageId = Guid.NewGuid().ToString()

                    let message =
                        { MessageId = messageId
                          FromSessionId = fromSessionId
                          ToSessionId = toSessionId
                          MessageType = messageType
                          Content = content
                          Priority = priority
                          SentAt = DateTime.Now
                          ExpiresAt = Some(DateTime.Now.AddMinutes(30.0)) }

                    sessionMessages.Enqueue(message)

                    // メッセージの永続化
                    let messageFile =
                        Path.Combine(config.CoordinatorStorageDirectory, "messages", $"{messageId}.json")

                    let json =
                        JsonSerializer.Serialize(message, JsonSerializerOptions(WriteIndented = true))

                    File.WriteAllText(messageFile, json)

                    Logger.logDebug
                        "MultiSessionCoordinator"
                        $"セッション間メッセージ送信: {fromSessionId} -> {toSessionId} ({messageType})"

                    return true

            with ex ->
                Logger.logError "MultiSessionCoordinator" $"セッション間メッセージ送信失敗: {ex.Message}"
                return false
        }

    /// セッション間メッセージの受信
    let receiveInterSessionMessages (sessionId: string) =
        let messages = ResizeArray<InterSessionMessage>()
        let mutable continueProcessing = true

        while continueProcessing do
            match sessionMessages.TryDequeue() with
            | true, message ->
                if
                    message.ToSessionId = sessionId
                    && (message.ExpiresAt.IsNone || message.ExpiresAt.Value > DateTime.Now)
                then
                    messages.Add(message)
            | false, _ -> continueProcessing <- false

        messages.ToArray() |> Array.toList

    /// リソース使用状況の監視
    let monitorResourceUsage (config: MultiSessionConfig) =
        async {
            try
                let totalResourceUsage =
                    activeSessions.Values |> Seq.sumBy (fun s -> s.ResourceUsage)

                let sessionCount = activeSessions.Count

                let resourceStats =
                    {| TotalSessions = sessionCount
                       TotalResourceUsage = totalResourceUsage
                       AverageResourcePerSession =
                        if sessionCount > 0 then
                            totalResourceUsage / int64 sessionCount
                        else
                            0L
                       MaxConcurrentSessions = config.MaxConcurrentSessions
                       Timestamp = DateTime.Now |}

                Logger.logDebug "MultiSessionCoordinator" $"リソース監視: {sessionCount}セッション, {totalResourceUsage}バイト"
                return Some resourceStats

            with ex ->
                Logger.logError "MultiSessionCoordinator" $"リソース監視エラー: {ex.Message}"
                return None
        }

    /// 非アクティブセッションのクリーンアップ
    let cleanupInactiveSessions (config: MultiSessionConfig) =
        async {
            try
                let timeoutThreshold = DateTime.Now.AddMinutes(-float config.SessionTimeoutMinutes)

                let inactiveSessions =
                    activeSessions.Values
                    |> Seq.filter (fun s -> s.LastActivity < timeoutThreshold)
                    |> Seq.toList

                let mutable cleanedCount = 0

                for session in inactiveSessions do
                    Logger.logInfo "MultiSessionCoordinator" $"非アクティブセッションをクリーンアップ: {session.SessionId}"
                    let! unregistered = unregisterSession config session.SessionId

                    if unregistered then
                        cleanedCount <- cleanedCount + 1

                Logger.logInfo "MultiSessionCoordinator" $"非アクティブセッションクリーンアップ完了: {cleanedCount}件"
                return cleanedCount

            with ex ->
                Logger.logError "MultiSessionCoordinator" $"非アクティブセッションクリーンアップ失敗: {ex.Message}"
                return 0
        }

    /// セッション依存関係の解析
    let analyzeSessionDependencies () =
        async {
            try
                let sessions = getActiveSessions ()

                let dependencies =
                    ResizeArray<
                        {| Source: string
                           Target: string
                           Type: string |}
                     >()

                // 各セッションの依存関係を分析
                for session in sessions do
                    // バックグラウンドタスクの依存関係
                    let activeTasks = getActiveTasks (Some session.SessionId)

                    for task in activeTasks do
                        for depTaskId in task.DependsOn do
                            // 依存タスクがどのセッションに属するかを調べる
                            let depSessions =
                                sessions
                                |> List.filter (fun s ->
                                    getActiveTasks (Some s.SessionId) |> List.exists (fun t -> t.TaskId = depTaskId))

                            for depSession in depSessions do
                                if depSession.SessionId <> session.SessionId then
                                    dependencies.Add(
                                        {| Source = session.SessionId
                                           Target = depSession.SessionId
                                           Type = "task-dependency" |}
                                    )

                    // リソース共有による依存関係
                    if session.CoordinationMode = SharedResources then
                        let otherSharedSessions =
                            sessions
                            |> List.filter (fun s ->
                                s.CoordinationMode = SharedResources && s.SessionId <> session.SessionId)

                        for otherSession in otherSharedSessions do
                            dependencies.Add(
                                {| Source = session.SessionId
                                   Target = otherSession.SessionId
                                   Type = "resource-sharing" |}
                            )

                let dependencyResult = dependencies.ToArray() |> Array.toList
                Logger.logDebug "MultiSessionCoordinator" $"セッション依存関係分析完了: {dependencyResult.Length}件"
                return dependencyResult

            with ex ->
                Logger.logError "MultiSessionCoordinator" $"セッション依存関係分析失敗: {ex.Message}"
                return []
        }

    /// 現在のアクティブセッションの取得
    let getCurrentActiveSession () = !currentActiveSession

    /// セッション統計情報の取得
    let getSessionStatistics (config: MultiSessionConfig) =
        async {
            try
                let sessions = getActiveSessions ()
                let totalSessions = sessions.Length

                let detachedSessions =
                    sessions |> List.filter (fun s -> s.IsDetached) |> List.length

                let totalTasks = sessions |> List.sumBy (fun s -> s.ActiveTaskCount)
                let totalResourceUsage = sessions |> List.sumBy (fun s -> s.ResourceUsage)

                let statistics =
                    {| TotalSessions = totalSessions
                       DetachedSessions = detachedSessions
                       ActiveSessions = totalSessions - detachedSessions
                       TotalActiveTasks = totalTasks
                       TotalResourceUsage = totalResourceUsage
                       MaxConcurrentSessions = config.MaxConcurrentSessions
                       SessionUtilization = float totalSessions / float config.MaxConcurrentSessions
                       CurrentActiveSession = getCurrentActiveSession ()
                       Timestamp = DateTime.Now |}

                return Some statistics

            with ex ->
                Logger.logError "MultiSessionCoordinator" $"セッション統計取得失敗: {ex.Message}"
                return None
        }

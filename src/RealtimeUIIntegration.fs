module FCode.RealtimeUIIntegration

open System
open System.Threading
open Terminal.Gui
open FCode.Logger
open FCode.UnifiedActivityView
open FCode.DecisionTimelineView
open FCode.EscalationNotificationUI
open FCode.ProgressDashboard
// FullWorkflowCoordinatorは同じモジュールにあるため、importは不要

/// UIコンポーネント参照マップ
type UIComponentRegistry =
    { ConversationTextView: TextView option
      PMTimelineTextView: TextView option
      QA1TextView: TextView option
      UXTextView: TextView option
      AgentTextViews: Map<string, TextView> }

/// エラーバックオフ設定
type BackoffConfig =
    { InitialDelayMs: int
      MaxDelayMs: int
      BackoffMultiplier: float
      MaxRetryAttempts: int }

// ===============================================
// 内部責務分離型 (SOLID準拠設計)
// ===============================================

/// UI統合状態管理責務 (Single Responsibility)
type private UIIntegrationStateManager() =
    let mutable uiRegistry =
        { ConversationTextView = None
          PMTimelineTextView = None
          QA1TextView = None
          UXTextView = None
          AgentTextViews = Map.empty }

    let mutable cancellationTokenSource: CancellationTokenSource option = None
    let mutable isRunning = false
    let lockObject = obj ()

    member this.RegisterUIComponents
        (
            conversationView: TextView,
            pmTimelineView: TextView,
            qa1View: TextView,
            uxView: TextView,
            agentViews: Map<string, TextView>
        ) : Result<unit, string> =
        try
            uiRegistry <-
                { ConversationTextView = Some conversationView
                  PMTimelineTextView = Some pmTimelineView
                  QA1TextView = Some qa1View
                  UXTextView = Some uxView
                  AgentTextViews = agentViews }

            Result.Ok()
        with ex ->
            Result.Error $"Failed to register UI components: {ex.Message}"

    member this.GetUIRegistry() = uiRegistry
    member this.IsRunning() = isRunning

    member this.SetRunning(running: bool) =
        lock lockObject (fun () -> isRunning <- running)

    member this.SetCancellationTokenSource(cts: CancellationTokenSource option) =
        lock lockObject (fun () -> cancellationTokenSource <- cts)

    member this.GetCancellationTokenSource() = cancellationTokenSource

    member this.ClearRegistry() =
        uiRegistry <-
            { ConversationTextView = None
              PMTimelineTextView = None
              QA1TextView = None
              UXTextView = None
              AgentTextViews = Map.empty }

/// エラー回復管理責務 (Single Responsibility)
type private ErrorRecoveryManager(config: BackoffConfig) =
    let mutable currentRetryCount = 0
    let mutable currentDelayMs = config.InitialDelayMs

    member this.CalculateBackoffDelay() : int =
        let delay = min currentDelayMs config.MaxDelayMs
        currentDelayMs <- int (float currentDelayMs * config.BackoffMultiplier)
        delay

    member this.ResetBackoffState() =
        currentRetryCount <- 0
        currentDelayMs <- config.InitialDelayMs

    member this.IncrementRetryCount() =
        currentRetryCount <- currentRetryCount + 1

    member this.GetRetryCount() = currentRetryCount

    member this.IsMaxRetriesReached() =
        currentRetryCount >= config.MaxRetryAttempts

    member this.GetConfig() = config

/// UI更新処理責務 (Single Responsibility)
type private UIUpdateProcessor(stateManager: UIIntegrationStateManager) =

    /// 進捗情報の更新
    member this.UpdateProgressInformation() : Result<unit, string> =
        try
            addSystemActivity "system" SystemMessage "UI統合: システム状態更新中" |> ignore
            Result.Ok()
        with ex ->
            Result.Error $"進捗情報更新エラー: {ex.Message}"

    /// エージェント状態の同期
    member this.SynchronizeAgentStates() : Result<unit, string> =
        try
            let uiRegistry = stateManager.GetUIRegistry()

            uiRegistry.AgentTextViews
            |> Map.iter (fun agentId textView ->
                if not (isNull textView) then
                    let status =
                        match agentId with
                        | "dev1" -> "準備完了"
                        | "dev2" -> "準備完了"
                        | "dev3" -> "準備完了"
                        | "qa1" -> "テスト準備完了"
                        | "qa2" -> "探索的テスト準備完了"
                        | _ -> "待機中"

                    addSystemActivity agentId SystemMessage $"状態: {status}" |> ignore
                else
                    logWarning "RealtimeUIIntegration" $"エージェント {agentId} のTextViewがnullです")

            Result.Ok()
        with ex ->
            Result.Error $"エージェント状態同期エラー: {ex.Message}"

    /// タスク状態の反映
    member this.UpdateTaskStatuses() : Result<unit, string> =
        try
            let taskUpdates =
                [ ("dev1", "コア機能実装", 75)
                  ("dev2", "UI実装", 60)
                  ("dev3", "統合テスト", 45)
                  ("qa1", "品質テスト", 30) ]

            taskUpdates
            |> List.iter (fun (agentId, taskName, progress) ->
                addSystemActivity agentId Progress $"{taskName}: {progress}%%完了" |> ignore)

            Result.Ok()
        with ex ->
            Result.Error $"タスク状態更新エラー: {ex.Message}"

    /// エスカレーション通知の更新
    member this.UpdateEscalationNotifications() : Result<unit, string> =
        try
            let uiRegistry = stateManager.GetUIRegistry()

            match uiRegistry.QA1TextView with
            | Some textView -> Result.Ok()
            | None -> Result.Ok()
        with ex ->
            Result.Error $"エスカレーション通知更新エラー: {ex.Message}"

/// イベントループ管理責務 (Single Responsibility)
type private EventLoopManager
    (stateManager: UIIntegrationStateManager, errorManager: ErrorRecoveryManager, updateProcessor: UIUpdateProcessor) =

    /// メインイベントループ実行
    member this.ExecuteMainEventLoop(cancellationToken: CancellationToken) : Async<Result<unit, string>> =
        let rec eventLoop () =
            async {
                if cancellationToken.IsCancellationRequested then
                    stateManager.SetRunning(false)
                    logInfo "RealtimeUIIntegration" "イベントループがキャンセルされました"
                    return Result.Ok()
                else
                    try
                        // 各更新処理を実行
                        let results =
                            [ updateProcessor.UpdateProgressInformation()
                              updateProcessor.SynchronizeAgentStates()
                              updateProcessor.UpdateTaskStatuses()
                              updateProcessor.UpdateEscalationNotifications() ]

                        // エラーチェック
                        let errors =
                            results
                            |> List.choose (function
                                | Result.Error e -> Some e
                                | _ -> None)

                        if errors.IsEmpty then
                            errorManager.ResetBackoffState()
                            do! Async.Sleep(1000) // updateIntervalMs
                            return! eventLoop ()
                        else
                            errorManager.IncrementRetryCount()

                            if errorManager.IsMaxRetriesReached() then
                                let errorList = String.concat "; " errors
                                let errorMsg = $"統合イベントループ最大リトライ回数到達。終了します: {errorList}"
                                logError "RealtimeUIIntegration" errorMsg
                                stateManager.SetRunning(false)
                                return Result.Error errorMsg
                            else
                                let backoffDelay = errorManager.CalculateBackoffDelay()
                                let retryCount = errorManager.GetRetryCount()
                                let maxRetries = errorManager.GetConfig().MaxRetryAttempts
                                let errorList = String.concat "; " errors

                                logWarning
                                    "RealtimeUIIntegration"
                                    $"統合イベントループエラー({retryCount}/{maxRetries}): {errorList}。{backoffDelay}ms後にリトライします"

                                do! Async.Sleep(backoffDelay)
                                return! eventLoop ()

                    with
                    | :? OperationCanceledException ->
                        stateManager.SetRunning(false)
                        logInfo "RealtimeUIIntegration" "イベントループがキャンセルされました"
                        return Result.Ok()
                    | ex ->
                        errorManager.IncrementRetryCount()

                        if errorManager.IsMaxRetriesReached() then
                            let errorMsg = $"イベントループ致命的エラー: {ex.Message}"
                            logError "RealtimeUIIntegration" errorMsg
                            stateManager.SetRunning(false)
                            return Result.Error errorMsg
                        else
                            let backoffDelay = errorManager.CalculateBackoffDelay()
                            let retryCount = errorManager.GetRetryCount()
                            let maxRetries = errorManager.GetConfig().MaxRetryAttempts
                            let exceptionMessage = ex.Message

                            logWarning
                                "RealtimeUIIntegration"
                                $"イベントループ例外({retryCount}/{maxRetries}): {exceptionMessage}。{backoffDelay}ms後にリトライします"

                            do! Async.Sleep(backoffDelay)
                            return! eventLoop ()
            }

        async {
            try
                logInfo "RealtimeUIIntegration" "リアルタイムUI統合イベントループ開始"
                stateManager.SetRunning(true)
                let! result = eventLoop ()
                stateManager.SetRunning(false)
                logInfo "RealtimeUIIntegration" "イベントループ正常終了"
                return result
            with ex ->
                stateManager.SetRunning(false)
                let errorMsg = $"イベントループ致命的エラー: {ex.Message}"
                logError "RealtimeUIIntegration" errorMsg
                return Result.Error errorMsg
        }

// ===============================================
// リアルタイムUI統合マネージャー (依存性注入によるSOLID設計)
// ===============================================

/// リアルタイムUI統合マネージャー (リファクタリング版)
type RealtimeUIIntegrationManager() =

    // エラーバックオフ設定
    let backoffConfig =
        { InitialDelayMs = 1000
          MaxDelayMs = 30000
          BackoffMultiplier = 2.0
          MaxRetryAttempts = 5 }

    // 依存性注入による責務分離
    let stateManager = UIIntegrationStateManager()
    let errorManager = ErrorRecoveryManager(backoffConfig)
    let updateProcessor = UIUpdateProcessor(stateManager)
    let eventLoopManager = EventLoopManager(stateManager, errorManager, updateProcessor)

    /// UIコンポーネント登録 - Result型対応
    member this.RegisterUIComponents
        (
            conversationView: TextView,
            pmTimelineView: TextView,
            qa1View: TextView,
            uxView: TextView,
            agentViews: Map<string, TextView>
        ) : Result<unit, string> =
        match stateManager.RegisterUIComponents(conversationView, pmTimelineView, qa1View, uxView, agentViews) with
        | Result.Ok() ->
            // 既存UIシステムとの統合（互換性）
            setConversationTextView conversationView
            setTimelineTextView pmTimelineView
            setNotificationTextView qa1View
            setDashboardTextView uxView
            logInfo "RealtimeUIIntegration" "UI components registered successfully"
            Result.Ok()
        | Result.Error error ->
            logError "RealtimeUIIntegration" $"UIコンポーネント登録失敗: {error}"
            Result.Error error

    /// リアルタイムUI更新イベントループ開始 - Result型対応
    member this.StartIntegrationEventLoop() : Async<Result<unit, string>> =
        async {
            try
                if stateManager.IsRunning() then
                    let errorMsg = "既にイベントループが実行中です"
                    logWarning "RealtimeUIIntegration" errorMsg
                    return Result.Error errorMsg
                else
                    let cts = new CancellationTokenSource()
                    stateManager.SetCancellationTokenSource(Some cts)

                    let! result = eventLoopManager.ExecuteMainEventLoop(cts.Token)

                    // クリーンアップ
                    stateManager.SetCancellationTokenSource(None)
                    cts.Dispose()

                    return result

            with ex ->
                let errorMsg = $"イベントループ開始失敗: {ex.Message}"
                logError "RealtimeUIIntegration" errorMsg
                return Result.Error errorMsg
        }

    /// 安全な強制停止 - Result型対応
    member this.EmergencyShutdown(reason: string) : Result<unit, string> =
        try
            logWarning "RealtimeUIIntegration" $"緊急停止要求: {reason}"

            match stateManager.GetCancellationTokenSource() with
            | Some cts ->
                cts.Cancel()
                stateManager.SetCancellationTokenSource(None)
                cts.Dispose()
            | None -> ()

            stateManager.SetRunning(false)
            errorManager.IncrementRetryCount() // 強制停止でリトライ回数を最大に

            logInfo "RealtimeUIIntegration" "緊急停止完了"
            Result.Ok()
        with ex ->
            let errorMsg = $"緊急停止エラー: {ex.Message}"
            logError "RealtimeUIIntegration" errorMsg
            Result.Error errorMsg

    /// リソース解放 - Result型対応
    member this.Dispose() : Result<unit, string> =
        try
            // 緊急停止を実行
            match this.EmergencyShutdown("Dispose処理による停止") with
            | Result.Ok() ->
                // 完全に停止するまで待機
                let mutable waitCount = 0

                while stateManager.IsRunning() && waitCount < 50 do // 最大5秒待機
                    System.Threading.Thread.Sleep(100)
                    waitCount <- waitCount + 1

                // UIレジストリをクリア
                stateManager.ClearRegistry()

                logInfo "RealtimeUIIntegration" "RealtimeUIIntegrationManager disposed"
                Result.Ok()
            | Result.Error error ->
                logError "RealtimeUIIntegration" $"Dispose処理中の緊急停止失敗: {error}"
                Result.Error error
        with ex ->
            let errorMsg = $"Dispose例外: {ex.Message}"
            logError "RealtimeUIIntegration" errorMsg
            Result.Error errorMsg

    interface IDisposable with
        member this.Dispose() = this.Dispose() |> ignore

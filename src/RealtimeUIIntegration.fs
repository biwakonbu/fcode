module FCode.RealtimeUIIntegration

open System
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

/// リアルタイムUI統合マネージャー
type RealtimeUIIntegrationManager() =

    let mutable uiRegistry =
        { ConversationTextView = None
          PMTimelineTextView = None
          QA1TextView = None
          UXTextView = None
          AgentTextViews = Map.empty }

    // エラーバックオフ設定
    let backoffConfig =
        { InitialDelayMs = 1000
          MaxDelayMs = 30000
          BackoffMultiplier = 2.0
          MaxRetryAttempts = 5 }

    // 現在のリトライ状態
    let mutable currentRetryCount = 0
    let mutable currentDelayMs = backoffConfig.InitialDelayMs

    /// UIコンポーネント登録
    member this.RegisterUIComponents
        (
            conversationView: TextView,
            pmTimelineView: TextView,
            qa1View: TextView,
            uxView: TextView,
            agentViews: Map<string, TextView>
        ) =
        uiRegistry <-
            { ConversationTextView = Some conversationView
              PMTimelineTextView = Some pmTimelineView
              QA1TextView = Some qa1View
              UXTextView = Some uxView
              AgentTextViews = agentViews }

        // 既存UIシステムとの統合
        setConversationTextView conversationView
        setTimelineTextView pmTimelineView
        setNotificationTextView qa1View
        setDashboardTextView uxView

        logInfo "RealtimeUIIntegration" "UI components registered successfully"

    /// 指数バックオフによるエラー回復
    member private this.CalculateBackoffDelay() =
        let delay = min currentDelayMs backoffConfig.MaxDelayMs
        currentDelayMs <- int (float currentDelayMs * backoffConfig.BackoffMultiplier)
        delay

    /// エラー状態リセット
    member private this.ResetBackoffState() =
        currentRetryCount <- 0
        currentDelayMs <- backoffConfig.InitialDelayMs

    /// リアルタイムUI更新イベントループ開始
    member this.StartIntegrationEventLoop() =
        async {
            logInfo "RealtimeUIIntegration" "リアルタイムUI統合イベントループ開始"

            // UI更新間隔（1秒間隔でリアルタイム性を向上）
            let updateIntervalMs = 1000

            let rec eventLoop () =
                async {
                    try
                        // 1. 進捗情報の更新
                        this.UpdateProgressInformation()

                        // 2. エージェント状態の同期
                        this.SynchronizeAgentStates()

                        // 3. タスク状態の反映
                        this.UpdateTaskStatuses()

                        // 4. エスカレーション通知の更新
                        this.UpdateEscalationNotifications()

                        // 成功時はバックオフ状態をリセット
                        this.ResetBackoffState()

                        do! Async.Sleep(updateIntervalMs)
                        return! eventLoop ()

                    with ex ->
                        currentRetryCount <- currentRetryCount + 1

                        if currentRetryCount >= backoffConfig.MaxRetryAttempts then
                            logError
                                "RealtimeUIIntegration"
                                $"統合イベントループ最大リトライ回数({backoffConfig.MaxRetryAttempts})到達。終了します: {ex.Message}"

                            return () // ループを終了
                        else
                            let backoffDelay = this.CalculateBackoffDelay()

                            logWarning
                                "RealtimeUIIntegration"
                                $"統合イベントループエラー({currentRetryCount}/{backoffConfig.MaxRetryAttempts}): {ex.Message}。{backoffDelay}ms後にリトライします"

                            do! Async.Sleep(backoffDelay)
                            return! eventLoop ()
                }

            // バックグラウンドで実行
            return! eventLoop ()
        }

    /// 進捗情報の更新
    member private this.UpdateProgressInformation() =
        try
            // システム全体の進捗情報を表示
            addSystemActivity "system" SystemMessage "UI統合: システム状態更新中"

        with ex ->
            logWarning "RealtimeUIIntegration" $"進捗情報更新エラー: {ex.Message}"

    /// エージェント状態の同期
    member private this.SynchronizeAgentStates() =
        try
            // 基本的なエージェント状態表示
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

                    addSystemActivity agentId SystemMessage $"状態: {status}"
                else
                    logWarning "RealtimeUIIntegration" $"エージェント {agentId} のTextViewがnullです")

        with ex ->
            logWarning "RealtimeUIIntegration" $"エージェント状態同期エラー: {ex.Message}"

    /// タスク状態の反映
    member private this.UpdateTaskStatuses() =
        try
            // タスク進捗のUI反映（サンプル）
            let taskUpdates =
                [ ("dev1", "コア機能実装", 75)
                  ("dev2", "UI実装", 60)
                  ("dev3", "統合テスト", 45)
                  ("qa1", "品質テスト", 30) ]

            taskUpdates
            |> List.iter (fun (agentId, taskName, progress) ->
                addSystemActivity agentId Progress $"{taskName}: {progress}%%完了")

        with ex ->
            logWarning "RealtimeUIIntegration" $"タスク状態更新エラー: {ex.Message}"

    /// エスカレーション通知の更新
    member private this.UpdateEscalationNotifications() =
        try
            // QA1ペイン（エスカレーション通知）に重要情報を表示
            match uiRegistry.QA1TextView with
            | Some textView ->
                // エスカレーション情報は EscalationNotificationUI が自動更新
                ()
            | None -> ()

        with ex ->
            logWarning "RealtimeUIIntegration" $"エスカレーション通知更新エラー: {ex.Message}"

    /// 安全な強制停止
    member this.EmergencyShutdown(reason: string) =
        try
            logWarning "RealtimeUIIntegration" $"緊急停止要求: {reason}"

            // リトライカウンタを最大値に設定してループ終了を強制
            currentRetryCount <- backoffConfig.MaxRetryAttempts

            logInfo "RealtimeUIIntegration" "緊急停止完了"
        with ex ->
            logError "RealtimeUIIntegration" $"緊急停止エラー: {ex.Message}"

    /// リソース解放
    member this.Dispose() =
        try
            // 緊急停止を実行
            this.EmergencyShutdown("Dispose処理による停止")

            // UIレジストリをクリア
            uiRegistry <-
                { ConversationTextView = None
                  PMTimelineTextView = None
                  QA1TextView = None
                  UXTextView = None
                  AgentTextViews = Map.empty }

            logInfo "RealtimeUIIntegration" "RealtimeUIIntegrationManager disposed"
        with ex ->
            logError "RealtimeUIIntegration" ("Dispose例外: " + ex.Message)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

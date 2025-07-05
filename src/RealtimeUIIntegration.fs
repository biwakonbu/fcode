module FCode.RealtimeUIIntegration

open System
open Terminal.Gui
open FCode.Logger
open FCode.UnifiedActivityView
open FCode.DecisionTimelineView
open FCode.EscalationNotificationUI
open FCode.ProgressDashboard

/// UIコンポーネント参照マップ
type UIComponentRegistry =
    { ConversationTextView: TextView option
      PMTimelineTextView: TextView option
      QA1TextView: TextView option
      UXTextView: TextView option
      AgentTextViews: Map<string, TextView> }

/// リアルタイムUI統合マネージャー（簡易版）
type RealtimeUIIntegrationManager() =

    let mutable uiRegistry =
        { ConversationTextView = None
          PMTimelineTextView = None
          QA1TextView = None
          UXTextView = None
          AgentTextViews = Map.empty }

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

    /// システム統合イベントループ開始
    member this.StartIntegrationEventLoop() =
        async {
            logInfo "RealtimeUIIntegration" "統合イベントループ開始"

            // 基本的な統合処理を30秒間隔で実行
            let rec eventLoop () =
                async {
                    try
                        // 基本的なシステム状態更新
                        addSystemActivity "system" SystemMessage "システム統合正常動作中"

                        do! Async.Sleep(30000) // 30秒間隔
                        return! eventLoop ()

                    with ex ->
                        logError "RealtimeUIIntegration" ("統合イベントループエラー: " + ex.Message)
                        do! Async.Sleep(60000) // エラー時は1分待機
                        return! eventLoop ()
                }

            // バックグラウンドで実行
            return! eventLoop ()
        }

    /// リソース解放
    member this.Dispose() =
        try
            logInfo "RealtimeUIIntegration" "RealtimeUIIntegrationManager disposing"
        with ex ->
            logError "RealtimeUIIntegration" ("Dispose例外: " + ex.Message)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

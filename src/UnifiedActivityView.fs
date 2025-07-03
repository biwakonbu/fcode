module FCode.UnifiedActivityView

open System
open System.Collections.Concurrent
open Terminal.Gui
open NStack
open FCode.Logger
open FCode.AgentMessaging
open FCode.ColorSchemes

// ===============================================
// アクティビティ統合表示型定義
// ===============================================

/// エージェント活動種別
type ActivityType =
    | CodeGeneration // コード生成・実装
    | Testing // テスト実行・検証
    | QualityReview // 品質レビュー・コードレビュー
    | Documentation // ドキュメント作成・更新
    | TaskAssignment // タスク割り当て・指示
    | Progress // 進捗報告・状況更新
    | Escalation // エスカレーション・問題報告
    | Decision // 意思決定・判断要求
    | SystemMessage // システムメッセージ・通知

/// 統合活動データ
type UnifiedActivity =
    { ActivityId: string // 活動一意ID
      AgentId: string // エージェントID
      ActivityType: ActivityType // 活動種別
      Message: string // 活動内容
      Timestamp: DateTime // 発生タイムスタンプ
      Priority: MessagePriority // 優先度
      Metadata: Map<string, string> // 追加メタデータ
      RelatedTaskId: string option // 関連タスクID
      Status: string } // 状態 (processing, completed, failed等)

// ===============================================
// 統合活動表示管理
// ===============================================

/// 統合活動表示管理クラス
type UnifiedActivityManager() =
    let activities = ConcurrentQueue<UnifiedActivity>()
    let maxActivities = 1000 // 最大保持活動数
    let mutable conversationTextView: TextView option = None

    /// 活動一意ID生成
    let generateActivityId () =
        let timestamp = DateTime.Now.ToString("HHmmss")
        let guidPart = Guid.NewGuid().ToString("N")[..3]
        $"act-{timestamp}-{guidPart}"

    /// 会話ペインTextView設定
    member this.SetConversationTextView(textView: TextView) =
        conversationTextView <- Some textView
        logInfo "UnifiedActivityView" "Conversation TextView set for unified activity display"

    /// AgentMessageから統合活動データ作成
    member private this.CreateActivityFromMessage(message: AgentMessage) =
        let activityType =
            match message.MessageType with
            | MessageType.TaskAssignment -> ActivityType.TaskAssignment
            | MessageType.Progress -> ActivityType.Progress
            | MessageType.QualityReview -> ActivityType.QualityReview
            | MessageType.Escalation -> ActivityType.Escalation
            | MessageType.StateUpdate -> ActivityType.SystemMessage
            | MessageType.ResourceRequest -> ActivityType.SystemMessage
            | MessageType.Collaboration -> ActivityType.Decision
            | MessageType.Notification -> ActivityType.SystemMessage

        { ActivityId = generateActivityId ()
          AgentId = message.FromAgent
          ActivityType = activityType
          Message = message.Content
          Timestamp = message.Timestamp
          Priority = message.Priority
          Metadata = message.Metadata
          RelatedTaskId = message.Metadata.TryFind("task_id")
          Status = "received" }

    /// エージェント活動追加 (AgentMessage経由)
    member this.AddActivityFromMessage(message: AgentMessage) =
        let activity = this.CreateActivityFromMessage(message)
        this.AddActivity(activity)

    /// エージェント活動追加 (直接)
    member this.AddActivity(activity: UnifiedActivity) =
        activities.Enqueue(activity)

        // 最大数超過時の古い活動削除
        while activities.Count > maxActivities do
            activities.TryDequeue() |> ignore

        // 会話ペイン更新
        this.UpdateConversationDisplay()

        let messagePreview =
            if activity.Message.Length > 50 then
                activity.Message.[..50] + "..."
            else
                activity.Message

        logDebug
            "UnifiedActivityView"
            $"Activity added: {activity.AgentId} - {activity.ActivityType} - {messagePreview}"

    /// カスタム活動追加 (システムメッセージ等)
    member this.AddSystemActivity
        (
            agentId: string,
            activityType: ActivityType,
            message: string,
            ?priority: MessagePriority,
            ?metadata: Map<string, string>
        ) =
        let priority = defaultArg priority Normal
        let metadata = defaultArg metadata Map.empty

        let activity =
            { ActivityId = generateActivityId ()
              AgentId = agentId
              ActivityType = activityType
              Message = message
              Timestamp = DateTime.Now
              Priority = priority
              Metadata = metadata
              RelatedTaskId = None
              Status = "system" }

        this.AddActivity(activity)

    /// 会話ペイン表示更新
    member private this.UpdateConversationDisplay() =
        match conversationTextView with
        | Some textView ->
            try
                // 最新10件の活動を取得・フォーマット
                let allActivities = activities.ToArray()

                let recentActivities =
                    allActivities
                    |> Array.sortByDescending (fun a -> a.Timestamp)
                    |> Array.take (min 10 allActivities.Length)

                let displayText = this.FormatActivitiesForDisplay(recentActivities)

                // UI更新はメインスレッドで実行
                Application.MainLoop.Invoke(fun () ->
                    textView.Text <- ustring.Make(displayText: string)
                    textView.SetNeedsDisplay())

                logDebug
                    "UnifiedActivityView"
                    $"Conversation display updated with {recentActivities.Length} recent activities"

            with ex ->
                logException "UnifiedActivityView" "Failed to update conversation display" ex
        | None -> logWarning "UnifiedActivityView" "Conversation TextView not set - cannot update display"

    /// 活動表示フォーマット
    member private this.FormatActivitiesForDisplay(activities: UnifiedActivity[]) =
        let header = "=== 統合エージェント活動ログ ===\n\n"

        let activityLines =
            activities
            |> Array.map (fun activity ->
                let timeStr = activity.Timestamp.ToString("HH:mm:ss")
                let agentStr = activity.AgentId.PadRight(6)
                let typeStr = this.GetActivityTypeDisplay(activity.ActivityType)
                let priorityStr = this.GetPriorityDisplay(activity.Priority)

                let messagePreview =
                    if activity.Message.Length > 60 then
                        activity.Message.[..57] + "..."
                    else
                        activity.Message

                $"[{timeStr}] {agentStr} {typeStr} {priorityStr} {messagePreview}")
            |> String.concat "\n"

        let totalCount = this.GetActivityCount()

        let footer =
            $"\n\n--- 最新{activities.Length}件 / 総活動数: {totalCount} ---\nキーバインド: ESC(終了) Ctrl+X(コマンド) Ctrl+Tab(ペイン切替)"

        header + activityLines + footer

    /// 活動種別表示文字列取得
    member private this.GetActivityTypeDisplay(activityType: ActivityType) =
        match activityType with
        | CodeGeneration -> "🔧 CODE"
        | Testing -> "🧪 TEST"
        | QualityReview -> "📋 QA  "
        | Documentation -> "📝 DOC "
        | TaskAssignment -> "📌 TASK"
        | Progress -> "📊 PROG"
        | Escalation -> "🚨 ESC "
        | Decision -> "💭 DEC "
        | SystemMessage -> "⚙️ SYS "

    /// 優先度表示文字列取得
    member private this.GetPriorityDisplay(priority: MessagePriority) =
        match priority with
        | Critical -> "[🔴]"
        | High -> "[🟡]"
        | Normal -> "[🟢]"
        | Low -> "[⚪]"

    /// 指定エージェントの最新活動取得
    member this.GetLatestActivitiesByAgent(agentId: string, count: int) =
        let filteredActivities =
            activities.ToArray()
            |> Array.filter (fun a -> a.AgentId = agentId)
            |> Array.sortByDescending (fun a -> a.Timestamp)

        filteredActivities |> Array.take (min count filteredActivities.Length)

    /// 指定活動種別の最新活動取得
    member this.GetLatestActivitiesByType(activityType: ActivityType, count: int) =
        let filteredActivities =
            activities.ToArray()
            |> Array.filter (fun a -> a.ActivityType = activityType)
            |> Array.sortByDescending (fun a -> a.Timestamp)

        filteredActivities |> Array.take (min count filteredActivities.Length)

    /// 全活動取得
    member this.GetAllActivities() = activities.ToArray()

    /// 活動数取得
    member this.GetActivityCount() = activities.Count

    /// 活動クリア
    member this.ClearActivities() =
        activities.Clear()
        this.UpdateConversationDisplay()
        logInfo "UnifiedActivityView" "All activities cleared"

// ===============================================
// グローバル統合活動管理インスタンス
// ===============================================

/// グローバル統合活動管理インスタンス
let globalUnifiedActivityManager = UnifiedActivityManager()

/// AgentMessageから統合活動追加 (グローバル関数)
let addActivityFromMessage (message: AgentMessage) =
    globalUnifiedActivityManager.AddActivityFromMessage(message)

/// システム活動追加 (グローバル関数)
let addSystemActivity (agentId: string) (activityType: ActivityType) (message: string) =
    globalUnifiedActivityManager.AddSystemActivity(agentId, activityType, message)

/// 会話ペインTextView設定 (グローバル関数)
let setConversationTextView (textView: TextView) =
    globalUnifiedActivityManager.SetConversationTextView(textView)

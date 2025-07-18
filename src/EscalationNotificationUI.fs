module FCode.EscalationNotificationUI

open System
open System.Collections.Concurrent
open Terminal.Gui
open NStack
open FCode.Logger
open FCode.AgentMessaging
open FCode.ColorSchemes
open FCode.DecisionTimelineView

// ===============================================
// エスカレーション通知システム型定義
// ===============================================

/// エスカレーション緊急度
type EscalationUrgency =
    | Immediate // 即座に対応必要
    | Urgent // 緊急（数時間以内）
    | Normal // 通常（1日以内）
    | Low // 低優先度（数日以内）

/// エスカレーション通知タイプ
type EscalationNotificationType =
    | TechnicalDecision // 技術的判断要求
    | ResourceRequest // リソース割り当て要求
    | QualityGate // 品質判断要求
    | TimelineExtension // 期限延長要求
    | ExternalDependency // 外部依存関係解決要求
    | BusinessDecision // ビジネス判断要求

/// エスカレーション通知状態
type EscalationNotificationStatus =
    | Pending // 待機中
    | Acknowledged // 確認済み
    | Resolved // 解決済み
    | Expired // 期限切れ
    | MoreInfoRequested // 追加情報要求
    | EscalatedHigher // 上位エスカレーション
    | Rejected // 却下

/// エスカレーション通知エントリ
type EscalationNotification =
    { NotificationId: string // 通知一意ID
      Title: string // 通知タイトル
      Description: string // 詳細説明
      NotificationType: EscalationNotificationType // 通知タイプ
      Urgency: EscalationUrgency // 緊急度
      RequestingAgent: string // 要求元エージェント
      TargetRole: string // 対象ロール（PO、PM等）
      CreatedAt: DateTime // 作成日時
      RequiredResponseBy: DateTime // 回答期限
      RelatedTaskIds: string list // 関連タスクID
      RelatedDecisionId: string option // 関連意思決定ID
      Metadata: Map<string, string> // 追加メタデータ
      Status: EscalationNotificationStatus // 状態
      ResponseContent: string option // 回答内容
      ResponseAt: DateTime option } // 回答日時

/// PO通知アクション
type PONotificationAction =
    | Acknowledge // 確認のみ
    | ApproveWithComment of string // コメント付き承認
    | RequestMoreInfo of string // 追加情報要求
    | EscalateToHigher of string // 上位エスカレーション
    | Reject of string // 却下

// ===============================================
// エスカレーション通知管理
// ===============================================

/// エスカレーション通知管理クラス - スレッドセーフ実装
type EscalationNotificationManager() =
    let notifications = ConcurrentDictionary<string, EscalationNotification>()
    let maxNotificationHistory = 200 // 最大通知履歴保持数
    let notificationSpinLock = ref 0
    let mutable notificationTextView: TextView option = None

    /// 通知一意ID生成
    let generateNotificationId () =
        let timestamp = DateTime.Now.ToString("yyMMdd-HHmmss")
        let guidPart = Guid.NewGuid().ToString("N")[..3]
        $"esc-{timestamp}-{guidPart}"

    /// 通知表示用TextView設定
    member this.SetNotificationTextView(textView: TextView) =
        notificationTextView <- Some textView
        logInfo "EscalationNotificationUI" "Notification TextView set for PO escalation display"

    /// 新規エスカレーション通知作成
    member this.CreateEscalationNotification
        (
            title: string,
            description: string,
            notificationType: EscalationNotificationType,
            urgency: EscalationUrgency,
            requestingAgent: string,
            targetRole: string,
            relatedTaskIds: string list,
            relatedDecisionId: string option
        ) =
        let notificationId = generateNotificationId ()

        let responseDeadline =
            match urgency with
            | Immediate -> DateTime.Now.AddHours(1.0)
            | Urgent -> DateTime.Now.AddHours(4.0)
            | Normal -> DateTime.Now.AddDays(1.0)
            | Low -> DateTime.Now.AddDays(3.0)

        let notification =
            { NotificationId = notificationId
              Title = title
              Description = description
              NotificationType = notificationType
              Urgency = urgency
              RequestingAgent = requestingAgent
              TargetRole = targetRole
              CreatedAt = DateTime.Now
              RequiredResponseBy = responseDeadline
              RelatedTaskIds = relatedTaskIds
              RelatedDecisionId = relatedDecisionId
              Metadata = Map.empty
              Status = Pending
              ResponseContent = None
              ResponseAt = None }

        notifications.[notificationId] <- notification

        // UI更新
        this.UpdateNotificationDisplay()

        // 関連意思決定があれば連携
        match relatedDecisionId with
        | Some decisionId ->
            updateDecisionStage decisionId Options requestingAgent $"PO判断要求: {title}"
            |> ignore
        | None -> ()

        logInfo "EscalationNotificationUI" $"Escalation notification created: {notificationId} - {title} ({urgency})"
        notificationId

    /// エスカレーション通知への応答処理
    member this.RespondToNotification(notificationId: string, action: PONotificationAction, responder: string) =
        match notifications.TryGetValue(notificationId) with
        | true, notification ->
            let (status, responseContent) =
                match action with
                | Acknowledge -> (Acknowledged, Some "確認済み")
                | ApproveWithComment comment -> (Resolved, Some $"承認: {comment}")
                | RequestMoreInfo info -> (MoreInfoRequested, Some $"追加情報要求: {info}")
                | EscalateToHigher reason -> (EscalatedHigher, Some $"上位エスカレーション: {reason}")
                | Reject reason -> (Rejected, Some $"却下: {reason}")

            let updatedNotification =
                { notification with
                    Status = status
                    ResponseContent = responseContent
                    ResponseAt = Some DateTime.Now }

            notifications.[notificationId] <- updatedNotification

            // UI更新
            this.UpdateNotificationDisplay()

            // 関連意思決定があれば更新
            match notification.RelatedDecisionId with
            | Some decisionId ->
                let newStage =
                    match action with
                    | ApproveWithComment _ -> Decision
                    | Reject _ -> Review
                    | _ -> Evaluation

                updateDecisionStage decisionId newStage responder responseContent.Value
                |> ignore
            | None -> ()

            logInfo
                "EscalationNotificationUI"
                $"Escalation notification responded: {notificationId} - {status} by {responder}"

            true
        | false, _ ->
            logWarning "EscalationNotificationUI" $"Escalation notification not found: {notificationId}"
            false

    /// AgentMessageからエスカレーション通知自動作成
    member this.ProcessEscalationMessage(message: AgentMessage) =
        if message.MessageType = MessageType.Escalation then
            let title =
                message.Metadata.TryFind("escalation_title")
                |> Option.defaultValue message.Content.[.. min 40 (message.Content.Length - 1)]

            let notificationType =
                match message.Metadata.TryFind("escalation_type") with
                | Some "technical" -> TechnicalDecision
                | Some "resource" -> ResourceRequest
                | Some "quality" -> QualityGate
                | Some "timeline" -> TimelineExtension
                | Some "external" -> ExternalDependency
                | Some "business" -> BusinessDecision
                | _ -> TechnicalDecision

            let urgency =
                match message.Priority with
                | MessagePriority.Critical -> Immediate
                | MessagePriority.High -> Urgent
                | MessagePriority.Normal -> EscalationUrgency.Normal
                | MessagePriority.Low -> EscalationUrgency.Low

            let targetRole = message.Metadata.TryFind("target_role") |> Option.defaultValue "PO"

            let relatedTaskIds =
                message.Metadata.TryFind("related_tasks")
                |> Option.map (fun tasks -> tasks.Split(',') |> Array.toList)
                |> Option.defaultValue []

            let relatedDecisionId = message.Metadata.TryFind("decision_id")

            this.CreateEscalationNotification(
                title,
                message.Content,
                notificationType,
                urgency,
                message.FromAgent,
                targetRole,
                relatedTaskIds,
                relatedDecisionId
            )
            |> ignore

            logInfo "EscalationNotificationUI" $"Auto-created escalation notification from message: {message.MessageId}"

    /// 期限切れ通知の自動処理
    member this.ProcessExpiredNotifications() =
        let now = DateTime.Now

        let expiredNotifications =
            notifications.Values
            |> Seq.filter (fun n -> n.Status = Pending && n.RequiredResponseBy < now)
            |> Seq.toArray

        for notification in expiredNotifications do
            let expiredNotification =
                { notification with
                    Status = Expired
                    ResponseContent = Some "期限切れ - 自動処理"
                    ResponseAt = Some now }

            notifications.[notification.NotificationId] <- expiredNotification

            logWarning "EscalationNotificationUI" $"Escalation notification expired: {notification.NotificationId}"

        if expiredNotifications.Length > 0 then
            this.UpdateNotificationDisplay()

        expiredNotifications.Length

    /// 通知表示更新
    member private this.UpdateNotificationDisplay() =
        match notificationTextView with
        | Some textView ->
            try
                // アクティブ通知と最新履歴を取得・フォーマット
                let filteredActive =
                    notifications.Values
                    |> Seq.filter (fun n -> n.Status = Pending || n.Status = MoreInfoRequested)
                    |> Seq.sortByDescending (fun n -> n.CreatedAt)

                let activeNotifications =
                    let activeCount = Seq.length filteredActive

                    if activeCount > 0 then
                        filteredActive |> Seq.take (min 3 activeCount) |> Seq.toArray
                    else
                        [||]

                let filteredResolved =
                    notifications.Values
                    |> Seq.filter (fun n -> n.Status <> Pending && n.Status <> MoreInfoRequested)
                    |> Seq.sortByDescending (fun n -> n.ResponseAt |> Option.defaultValue n.CreatedAt)

                let recentResolved =
                    let filteredCount = Seq.length filteredResolved

                    if filteredCount > 0 then
                        filteredResolved |> Seq.take (min 5 filteredCount) |> Seq.toArray
                    else
                        [||]

                let displayText =
                    this.FormatNotificationForDisplay(activeNotifications, recentResolved)

                // UI更新はメインスレッドで実行・CI環境では安全にスキップ
                let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

                if not isCI then
                    try
                        // Application.MainLoopの安全性チェック
                        if not (isNull Application.MainLoop) then
                            Application.MainLoop.Invoke(fun () ->
                                try
                                    if not (isNull textView) then
                                        textView.Text <- ustring.Make(displayText: string)
                                        textView.SetNeedsDisplay()
                                    else
                                        logWarning "EscalationNotificationUI" "TextView is null during UI update"
                                with ex ->
                                    logException "EscalationNotificationUI" "UI thread update failed" ex)
                        else
                            // MainLoopが利用できない場合は直接更新を試行
                            try
                                if not (isNull textView) then
                                    textView.Text <- ustring.Make(displayText: string)
                                    textView.SetNeedsDisplay()
                                else
                                    logWarning "EscalationNotificationUI" "TextView is null during direct UI update"
                            with ex ->
                                logWarning "EscalationNotificationUI" $"Direct UI update failed: {ex.Message}"
                    with ex ->
                        logException "EscalationNotificationUI" "MainLoop.Invoke failed" ex
                else
                    logDebug "EscalationNotificationUI" "CI environment detected - skipping UI update"

                logDebug "EscalationNotificationUI"
                <| $"Notification display updated with {activeNotifications.Length} active and {recentResolved.Length} resolved notifications"

            with ex ->
                logException "EscalationNotificationUI" "Failed to update notification display" ex
        | None -> logWarning "EscalationNotificationUI" "Notification TextView not set - cannot update display"

    /// 通知表示フォーマット
    member private this.FormatNotificationForDisplay
        (activeNotifications: EscalationNotification[], recentResolved: EscalationNotification[])
        =
        let header = "=== PO エスカレーション通知 ===\n\n"

        // アクティブ通知セクション
        let activeSection =
            if activeNotifications.Length > 0 then
                let activeLines =
                    activeNotifications
                    |> Array.map (fun notification ->
                        let timeStr = notification.CreatedAt.ToString("MM/dd HH:mm")
                        let urgencyStr = this.GetUrgencyDisplay(notification.Urgency)
                        let typeStr = this.GetNotificationTypeDisplay(notification.NotificationType)
                        let deadlineStr = notification.RequiredResponseBy.ToString("HH:mm")
                        let agentStr = notification.RequestingAgent.PadRight(6)

                        let titlePreview =
                            if notification.Title.Length > 20 then
                                notification.Title.[..17] + "..."
                            else
                                notification.Title.PadRight(20)

                        $"[{timeStr}] {urgencyStr} {typeStr} {agentStr} {titlePreview} (~{deadlineStr})")
                    |> String.concat "\n"

                $"🚨 要対応通知 ({activeNotifications.Length}件)\n{activeLines}\n\n"
            else
                "✅ 要対応通知なし\n\n"

        // 最新処理済みセクション
        let resolvedSection =
            if recentResolved.Length > 0 then
                let resolvedLines =
                    recentResolved
                    |> Array.map (fun notification ->
                        let timeStr =
                            notification.ResponseAt
                            |> Option.map (fun t -> t.ToString("MM/dd HH:mm"))
                            |> Option.defaultValue "未回答"

                        let statusStr = this.GetStatusDisplay(notification.Status)
                        let typeStr = this.GetNotificationTypeDisplay(notification.NotificationType)
                        let agentStr = notification.RequestingAgent.PadRight(6)

                        let titlePreview =
                            if notification.Title.Length > 20 then
                                notification.Title.[..17] + "..."
                            else
                                notification.Title

                        $"[{timeStr}] {statusStr} {typeStr} {agentStr} {titlePreview}")
                    |> String.concat "\n"

                $"📋 最新処理済み ({recentResolved.Length}件)\n{resolvedLines}\n\n"
            else
                "📋 処理済み通知なし\n\n"

        let totalNotifications = notifications.Count

        let pendingCount =
            notifications.Values |> Seq.filter (fun n -> n.Status = Pending) |> Seq.length

        let footer =
            $"--- 総通知数: {totalNotifications} | 要対応: {pendingCount} ---\nキーバインド: Ctrl+R(応答) Ctrl+A(確認) ESC(終了)"

        header + activeSection + resolvedSection + footer

    /// 緊急度表示文字列取得
    member private this.GetUrgencyDisplay(urgency: EscalationUrgency) =
        match urgency with
        | Immediate -> "🔴即座"
        | Urgent -> "🟡緊急"
        | Normal -> "🟢通常"
        | Low -> "⚪低優"

    /// 通知タイプ表示文字列取得
    member private this.GetNotificationTypeDisplay(notificationType: EscalationNotificationType) =
        match notificationType with
        | TechnicalDecision -> "🔧技術"
        | ResourceRequest -> "💰資源"
        | QualityGate -> "✅品質"
        | TimelineExtension -> "⏰期限"
        | ExternalDependency -> "🔗外部"
        | BusinessDecision -> "💼事業"

    /// 状態表示文字列取得
    member private this.GetStatusDisplay(status: EscalationNotificationStatus) =
        match status with
        | Acknowledged -> "👁️確認"
        | Resolved -> "✅解決"
        | MoreInfoRequested -> "❓追加"
        | EscalatedHigher -> "⬆️上位"
        | Rejected -> "❌却下"
        | Expired -> "⏰期限"
        | Pending -> "⏳待機"

    /// アクティブ通知取得
    member this.GetActiveNotifications() =
        notifications.Values
        |> Seq.filter (fun n -> n.Status = Pending || n.Status = MoreInfoRequested)
        |> Seq.toArray

    /// 指定通知詳細取得
    member this.GetNotificationDetail(notificationId: string) =
        notifications.TryGetValue(notificationId)

    /// 全通知取得
    member this.GetAllNotifications() = notifications.Values |> Seq.toArray

    /// 通知数取得
    member this.GetNotificationCount() = notifications.Count

    /// 履歴クリア
    member this.ClearNotificationHistory() =
        notifications.Clear()
        this.UpdateNotificationDisplay()
        logInfo "EscalationNotificationUI" "Notification history cleared"

    /// リソース解放
    member this.Dispose() =
        notifications.Clear()
        notificationTextView <- None
        GC.SuppressFinalize(this)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

// ===============================================
// 依存性注入対応グローバル管理インスタンス
// ===============================================

/// 依存性注入対応エスカレーション通知管理インスタンス（遅延初期化）
let mutable private escalationManagerInstance: EscalationNotificationManager option =
    None

/// エスカレーション通知管理インスタンス取得または作成
let private getOrCreateEscalationManager () =
    match escalationManagerInstance with
    | Some manager -> manager
    | None ->
        let manager = new EscalationNotificationManager()
        escalationManagerInstance <- Some manager
        manager

/// 新規エスカレーション通知作成 (グローバル関数)
let createEscalationNotification
    (title: string)
    (description: string)
    (notificationType: EscalationNotificationType)
    (urgency: EscalationUrgency)
    (requestingAgent: string)
    (targetRole: string)
    (relatedTaskIds: string list)
    (relatedDecisionId: string option)
    =
    (getOrCreateEscalationManager ())
        .CreateEscalationNotification(
            title,
            description,
            notificationType,
            urgency,
            requestingAgent,
            targetRole,
            relatedTaskIds,
            relatedDecisionId
        )

/// エスカレーション通知応答 (グローバル関数)
let respondToNotification (notificationId: string) (action: PONotificationAction) (responder: string) =
    (getOrCreateEscalationManager ()).RespondToNotification(notificationId, action, responder)

/// 通知表示用TextView設定 (グローバル関数)
let setNotificationTextView (textView: TextView) =
    (getOrCreateEscalationManager ()).SetNotificationTextView(textView)

/// AgentMessageからエスカレーション通知処理 (グローバル関数)
let processEscalationMessage (message: AgentMessage) =
    (getOrCreateEscalationManager ()).ProcessEscalationMessage(message)

/// 期限切れ通知処理 (グローバル関数)
let processExpiredNotifications () =
    (getOrCreateEscalationManager ()).ProcessExpiredNotifications()

/// 依存性注入: 既存のインスタンスを置き換え（テスト用）
let injectEscalationManager (manager: EscalationNotificationManager) =
    escalationManagerInstance <- Some manager

module FCode.EscalationPODecisionUI

open System
open System.Collections.Generic
open System.Text
open Terminal.Gui
open FCode.EscalationNotificationUI
open FCode.QualityGateManager
open FCode.TaskAssignmentManager
open FCode.Logger
open FCode.ColorSchemes

/// PO判断結果
type PODecisionResult =
    | Approved of string // 承認（理由付き）
    | Rejected of string // 却下（理由付き）
    | ModificationRequired of string // 修正要求（指示付き）
    | EscalatedHigher of string // 上位エスカレーション（理由付き）
    | MoreInfoRequested of string // 追加情報要求（質問内容付き）

/// PO判断UI状態
type PODecisionUIState =
    | Idle
    | AwaitingDecision
    | DecisionProcessing
    | DecisionCompleted

/// PO判断UI統合情報
type PODecisionUIInfo =
    { NotificationId: string
      TaskTitle: string
      NotificationType: EscalationNotificationType
      Urgency: EscalationUrgency
      CreatedAt: DateTime
      RequiredResponseBy: DateTime
      Description: string
      RelatedTaskIds: string list
      PODecisionResult: PODecisionResult option
      UIState: PODecisionUIState
      LastUpdated: DateTime }

/// エスカレーションPO判断UI管理クラス
type EscalationPODecisionUIManager(escalationNotificationManager: EscalationNotificationManager) =

    let mutable currentDecisionInfo: PODecisionUIInfo option = None
    let mutable decisionHistory: PODecisionUIInfo list = []
    let maxHistorySize = 50

    /// エスカレーション通知をPO判断UI表示用にフォーマット
    member this.FormatEscalationForPODecision(notification: EscalationNotification) : string =
        let sb = StringBuilder()

        // 緊急度とタイプのアイコン
        let urgencyIcon =
            match notification.Urgency with
            | EscalationUrgency.Immediate -> "🚨"
            | EscalationUrgency.Urgent -> "⚠️"
            | EscalationUrgency.Normal -> "📋"
            | EscalationUrgency.Low -> "📝"

        let typeIcon =
            match notification.NotificationType with
            | EscalationNotificationType.TechnicalDecision -> "🔧"
            | EscalationNotificationType.QualityGate -> "🎯"
            | EscalationNotificationType.BusinessDecision -> "💼"
            | EscalationNotificationType.ResourceRequest -> "📈"
            | EscalationNotificationType.TimelineExtension -> "⏰"
            | EscalationNotificationType.ExternalDependency -> "🔗"

        sb.AppendFormat("🔴 PO判断要求 - {0}\n\n", notification.Title) |> ignore

        sb.AppendFormat(
            "{0} 緊急度: {1} | {2} 種別: {3}\n\n",
            urgencyIcon,
            notification.Urgency,
            typeIcon,
            notification.NotificationType
        )
        |> ignore

        // 時間情報
        let timeRemaining = notification.RequiredResponseBy - DateTime.UtcNow

        let timeRemainingStr =
            if timeRemaining.TotalMinutes > 0 then
                sprintf
                    "残り時間: %s"
                    (if timeRemaining.TotalDays >= 1.0 then
                         sprintf "%.1f日" timeRemaining.TotalDays
                     elif timeRemaining.TotalHours >= 1.0 then
                         sprintf "%.1f時間" timeRemaining.TotalHours
                     else
                         sprintf "%.0f分" timeRemaining.TotalMinutes)
            else
                "⏰ 期限切れ"

        sb.AppendFormat("📅 要求日時: {0:HH:mm:ss}\n", notification.CreatedAt) |> ignore

        sb.AppendFormat("⏱️ 期限: {0:HH:mm:ss} ({1})\n\n", notification.RequiredResponseBy, timeRemainingStr)
        |> ignore

        // 要求内容
        sb.AppendFormat("📝 判断要求内容:\n{0}\n\n", notification.Description) |> ignore

        // 関連タスク情報
        if notification.RelatedTaskIds.Length > 0 then
            sb.AppendLine("🔗 関連タスク:") |> ignore

            for taskId in notification.RelatedTaskIds do
                sb.AppendFormat("  • {0}\n", taskId) |> ignore

            sb.AppendLine() |> ignore

        // 要求元情報
        sb.AppendFormat("👤 要求元: {0}\n", notification.RequestingAgent) |> ignore
        sb.AppendFormat("📍 対象: {0}\n\n", notification.TargetRole) |> ignore

        // 判断選択肢
        sb.AppendLine("🎯 判断選択肢:") |> ignore
        sb.AppendLine("  1. ✅ 承認（理由付き）") |> ignore
        sb.AppendLine("  2. ❌ 却下（理由付き）") |> ignore
        sb.AppendLine("  3. 🔄 修正要求（指示付き）") |> ignore
        sb.AppendLine("  4. ⬆️ 上位エスカレーション（理由付き）") |> ignore
        sb.AppendLine("  5. ❓ 追加情報要求（質問内容付き）") |> ignore

        sb.ToString()

    /// PO判断結果をUI表示用にフォーマット
    member this.FormatPODecisionResult(decision: PODecisionResult) : string =
        match decision with
        | Approved reason -> sprintf "✅ 承認: %s" reason
        | Rejected reason -> sprintf "❌ 却下: %s" reason
        | ModificationRequired instruction -> sprintf "🔄 修正要求: %s" instruction
        | EscalatedHigher reason -> sprintf "⬆️ 上位エスカレーション: %s" reason
        | MoreInfoRequested question -> sprintf "❓ 追加情報要求: %s" question

    /// エスカレーション通知をPO判断UI表示に設定
    member this.DisplayEscalationForPODecision(notification: EscalationNotification, targetView: TextView) : unit =
        try
            let decisionInfo =
                { NotificationId = notification.NotificationId
                  TaskTitle = notification.Title
                  NotificationType = notification.NotificationType
                  Urgency = notification.Urgency
                  CreatedAt = notification.CreatedAt
                  RequiredResponseBy = notification.RequiredResponseBy
                  Description = notification.Description
                  RelatedTaskIds = notification.RelatedTaskIds
                  PODecisionResult = None
                  UIState = AwaitingDecision
                  LastUpdated = DateTime.UtcNow }

            currentDecisionInfo <- Some decisionInfo

            // 判断待機UI表示
            let displayText = this.FormatEscalationForPODecision(notification)
            targetView.Text <- NStack.ustring.Make(displayText)
            targetView.SetNeedsDisplay()

            logInfo "EscalationPODecisionUI" (sprintf "PO判断UI表示: %s" notification.NotificationId)

        with ex ->
            logError "EscalationPODecisionUI" (sprintf "PO判断UI表示エラー: %s" ex.Message)

    /// PO判断実行処理
    member this.ProcessPODecision(notificationId: string, decision: PODecisionResult, responder: string) : bool =
        try
            match currentDecisionInfo with
            | Some info when info.NotificationId = notificationId ->
                // 判断処理中状態に設定
                let processingInfo =
                    { info with
                        UIState = DecisionProcessing
                        LastUpdated = DateTime.UtcNow }

                currentDecisionInfo <- Some processingInfo

                // エスカレーション通知管理システムに判断結果を送信
                let poAction =
                    match decision with
                    | Approved reason -> ApproveWithComment reason
                    | Rejected reason -> Reject reason
                    | ModificationRequired instruction -> RequestMoreInfo instruction
                    | EscalatedHigher reason -> EscalateToHigher reason
                    | MoreInfoRequested question -> RequestMoreInfo question

                let success =
                    escalationNotificationManager.RespondToNotification(notificationId, poAction, responder)

                if success then
                    // 判断完了状態に設定
                    let completedInfo =
                        { processingInfo with
                            PODecisionResult = Some decision
                            UIState = DecisionCompleted
                            LastUpdated = DateTime.UtcNow }

                    currentDecisionInfo <- Some completedInfo

                    // 履歴に追加
                    decisionHistory <- completedInfo :: decisionHistory

                    if decisionHistory.Length > maxHistorySize then
                        decisionHistory <- decisionHistory |> List.take maxHistorySize

                    logInfo
                        "EscalationPODecisionUI"
                        (sprintf "PO判断完了: %s -> %s" notificationId (this.FormatPODecisionResult(decision)))

                    true
                else
                    logError "EscalationPODecisionUI" (sprintf "PO判断処理失敗: %s" notificationId)
                    false

            | Some info ->
                logWarning
                    "EscalationPODecisionUI"
                    (sprintf "PO判断対象不一致: 現在=%s, 要求=%s" info.NotificationId notificationId)

                false
            | None ->
                logWarning "EscalationPODecisionUI" (sprintf "PO判断対象なし: %s" notificationId)
                false

        with ex ->
            logError "EscalationPODecisionUI" (sprintf "PO判断処理例外: %s - %s" notificationId ex.Message)
            false

    /// PO判断結果をUI表示に反映
    member this.UpdatePODecisionResultDisplay(targetView: TextView) : unit =
        try
            match currentDecisionInfo with
            | Some info ->
                let sb = StringBuilder()

                sb.AppendFormat("📊 PO判断結果\n\n") |> ignore
                sb.AppendFormat("通知ID: {0}\n", info.NotificationId) |> ignore
                sb.AppendFormat("タスク: {0}\n", info.TaskTitle) |> ignore
                sb.AppendFormat("判断時刻: {0:HH:mm:ss}\n\n", info.LastUpdated) |> ignore

                match info.PODecisionResult with
                | Some decision ->
                    sb.AppendFormat("判断結果: {0}\n\n", this.FormatPODecisionResult(decision))
                    |> ignore

                    // 次のアクション提案
                    sb.AppendLine("🚀 次のアクション:") |> ignore

                    match decision with
                    | Approved _ -> sb.AppendLine("✅ 承認済み - 実装継続") |> ignore
                    | Rejected _ -> sb.AppendLine("❌ 却下 - 要件再検討") |> ignore
                    | ModificationRequired _ -> sb.AppendLine("🔄 修正要求 - 指示に従って修正") |> ignore
                    | EscalatedHigher _ -> sb.AppendLine("⬆️ 上位エスカレーション - 上位判断待ち") |> ignore
                    | MoreInfoRequested _ -> sb.AppendLine("❓ 追加情報要求 - 情報提供待ち") |> ignore

                | None -> sb.AppendLine("判断結果: 処理中...") |> ignore

                sb.AppendFormat("状態: {0}\n", info.UIState) |> ignore

                targetView.Text <- NStack.ustring.Make(sb.ToString())
                targetView.SetNeedsDisplay()

            | None ->
                targetView.Text <- NStack.ustring.Make("PO判断待機\n\nエスカレーション通知を受信するとここに表示されます。")
                targetView.SetNeedsDisplay()

        with ex ->
            logError "EscalationPODecisionUI" (sprintf "PO判断結果表示更新エラー: %s" ex.Message)

    /// 現在のPO判断UI状態を取得
    member this.GetCurrentPODecisionState() : PODecisionUIState option =
        currentDecisionInfo |> Option.map (fun info -> info.UIState)

    /// 現在のPO判断UI情報を取得
    member this.GetCurrentPODecisionInfo() : PODecisionUIInfo option = currentDecisionInfo

    /// PO判断履歴を取得
    member this.GetPODecisionHistory() : PODecisionUIInfo list = decisionHistory

    /// PO判断UI表示をクリア
    member this.ClearPODecisionDisplay(targetView: TextView) : unit =
        currentDecisionInfo <- None
        targetView.Text <- NStack.ustring.Make("PO判断待機\n\nエスカレーション通知を受信するとここに表示されます。")
        targetView.SetNeedsDisplay()
        logInfo "EscalationPODecisionUI" "PO判断UI表示クリア完了"

    /// PO判断統計情報を生成
    member this.GeneratePODecisionStatistics() : string =
        let sb = StringBuilder()

        sb.AppendFormat("📊 PO判断統計\n\n") |> ignore
        sb.AppendFormat("現在時刻: {0:HH:mm:ss}\n", DateTime.UtcNow) |> ignore

        match currentDecisionInfo with
        | Some info ->
            sb.AppendFormat("アクティブ通知: {0}\n", info.TaskTitle) |> ignore
            sb.AppendFormat("緊急度: {0}\n", info.Urgency) |> ignore
            sb.AppendFormat("状態: {0}\n", info.UIState) |> ignore
            sb.AppendFormat("最終更新: {0:HH:mm:ss}\n", info.LastUpdated) |> ignore

            let timeRemaining = info.RequiredResponseBy - DateTime.UtcNow

            if timeRemaining.TotalMinutes > 0 then
                sb.AppendFormat("残り時間: %.0f分\n", timeRemaining.TotalMinutes) |> ignore
            else
                sb.AppendLine("⏰ 期限切れ") |> ignore
        | None -> sb.AppendLine("アクティブ通知: なし") |> ignore

        sb.AppendFormat("判断履歴: {0}件\n", decisionHistory.Length) |> ignore

        if decisionHistory.Length > 0 then
            sb.AppendLine("\n最近の判断:") |> ignore

            for i, decision in decisionHistory |> List.take (min 3 decisionHistory.Length) |> List.indexed do
                match decision.PODecisionResult with
                | Some result ->
                    sb.AppendFormat(
                        "  {0}. {1} ({2:HH:mm:ss})\n",
                        i + 1,
                        this.FormatPODecisionResult(result),
                        decision.LastUpdated
                    )
                    |> ignore
                | None -> ()

        sb.ToString()

    /// PO判断UIの緊急度ベースの色設定
    member this.GetUrgencyColorScheme(urgency: EscalationUrgency) : ColorScheme =
        match urgency with
        | EscalationUrgency.Immediate -> defaultScheme
        | EscalationUrgency.Urgent -> defaultScheme
        | EscalationUrgency.Normal -> defaultScheme
        | EscalationUrgency.Low -> defaultScheme

module FCode.QualityGateUIIntegration

open System
open System.Threading.Tasks
open Terminal.Gui
open FCode.QualityGateUI
open FCode.QualityGateManager
open FCode.EscalationNotificationUI
open FCode.TaskAssignmentManager
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// 品質ゲート統合実行結果
type QualityGateIntegrationResult =
    { TaskId: string
      Approved: bool
      RequiresEscalation: bool
      EscalationNotification: EscalationNotification option
      QualityReport: string
      ExecutionTime: TimeSpan }

/// 品質ゲート統合管理クラス
type QualityGateIntegrationManager() =

    // 品質ゲート管理コンポーネント
    let evaluationEngine = QualityEvaluationEngine()
    let reviewer = UpstreamDownstreamReviewer()
    let proposalGenerator = AlternativeProposalGenerator()

    let qualityGateManager =
        QualityGateManager(evaluationEngine, reviewer, proposalGenerator)

    let qualityGateUIManager = new QualityGateUIManager(qualityGateManager)
    let escalationNotificationManager = new EscalationNotificationManager()

    /// 品質ゲート統合実行
    member this.ExecuteQualityGateIntegration
        (task: ParsedTask, targetView: TextView)
        : Async<QualityGateIntegrationResult> =
        async {
            let startTime = DateTime.UtcNow

            try
                logInfo "QualityGateIntegration" (sprintf "品質ゲート統合実行開始: %s" task.TaskId)

                // 品質ゲートUI実行
                qualityGateUIManager.ExecuteQualityGateWithUI(task, targetView)

                // 少し待機してUI更新を確認
                do! Async.Sleep(1000)

                // 品質ゲート実行結果を取得
                match qualityGateManager.ExecuteQualityGate(task) with
                | Result.Ok(reviewResult, alternatives) ->
                    // エスカレーション必要性判定
                    let requiresEscalation =
                        this.DetermineEscalationRequirement(reviewResult, alternatives)

                    // エスカレーション通知作成
                    let escalationNotification =
                        if requiresEscalation then
                            Some(this.CreateEscalationNotification(task, reviewResult, alternatives))
                        else
                            None

                    // 品質レポート生成
                    let qualityReport =
                        qualityGateManager.GenerateQualityReport(task, reviewResult, alternatives)

                    let executionTime = DateTime.UtcNow - startTime

                    let result =
                        { TaskId = task.TaskId
                          Approved = reviewResult.Approved
                          RequiresEscalation = requiresEscalation
                          EscalationNotification = escalationNotification
                          QualityReport = qualityReport
                          ExecutionTime = executionTime }

                    // エスカレーション通知が必要な場合は通知管理に追加
                    match escalationNotification with
                    | Some notification ->
                        let notificationId =
                            escalationNotificationManager.CreateEscalationNotification(
                                notification.Title,
                                notification.Description,
                                notification.NotificationType,
                                notification.Urgency,
                                notification.RequestingAgent,
                                notification.TargetRole,
                                notification.RelatedTaskIds,
                                notification.RelatedDecisionId
                            )

                        logInfo "QualityGateIntegration" (sprintf "エスカレーション通知作成: %s" notificationId)
                    | None -> ()

                    logInfo
                        "QualityGateIntegration"
                        (sprintf
                            "品質ゲート統合実行完了: %s (承認: %b, エスカレーション: %b)"
                            task.TaskId
                            result.Approved
                            result.RequiresEscalation)

                    return result

                | Result.Error errorMsg ->
                    let executionTime = DateTime.UtcNow - startTime

                    let errorResult =
                        { TaskId = task.TaskId
                          Approved = false
                          RequiresEscalation = true
                          EscalationNotification = Some(this.CreateErrorEscalationNotification(task, errorMsg))
                          QualityReport = sprintf "品質ゲート実行エラー: %s" errorMsg
                          ExecutionTime = executionTime }

                    logError "QualityGateIntegration" (sprintf "品質ゲート統合実行エラー: %s - %s" task.TaskId errorMsg)

                    return errorResult

            with ex ->
                let executionTime = DateTime.UtcNow - startTime
                let errorMsg = sprintf "品質ゲート統合実行例外: %s" ex.Message

                let errorResult =
                    { TaskId = task.TaskId
                      Approved = false
                      RequiresEscalation = true
                      EscalationNotification = Some(this.CreateErrorEscalationNotification(task, errorMsg))
                      QualityReport = errorMsg
                      ExecutionTime = executionTime }

                logError "QualityGateIntegration" (sprintf "品質ゲート統合実行例外: %s - %s" task.TaskId ex.Message)

                return errorResult
        }

    /// エスカレーション必要性判定
    member this.DetermineEscalationRequirement
        (reviewResult: ReviewResult, alternatives: AlternativeProposal list option)
        : bool =
        // 複数の条件でエスカレーション必要性を判定
        let baseEscalationConditions =
            [ not reviewResult.Approved // 承認されていない
              reviewResult.ConsensusScore < 0.5 // 総合スコアが低い
              reviewResult.RequiredImprovements.Length > 3 // 改善事項が多い
              reviewResult.Comments
              |> List.exists (fun c -> c.Priority = TaskPriority.Critical) ] // 重要な問題がある

        let alternativeEscalationConditions =
            match alternatives with
            | Some alts ->
                [ alts.Length > 2 // 代替案が多い
                  alts |> List.exists (fun alt -> alt.DifficultyScore > 0.8) // 高難易度の代替案がある
                  alts |> List.exists (fun alt -> alt.FeasibilityScore < 0.5) ] // 実現可能性が低い代替案がある
            | None -> []

        let allConditions = baseEscalationConditions @ alternativeEscalationConditions

        // 条件の半分以上が満たされた場合にエスカレーション
        let trueConditions = allConditions |> List.filter id |> List.length
        let totalConditions = allConditions.Length

        float trueConditions / float totalConditions >= 0.5

    /// エスカレーション通知作成
    member this.CreateEscalationNotification
        (task: ParsedTask, reviewResult: ReviewResult, alternatives: AlternativeProposal list option)
        : EscalationNotification =
        let urgency =
            if reviewResult.ConsensusScore < 0.3 then
                EscalationUrgency.Immediate
            elif reviewResult.ConsensusScore < 0.5 then
                EscalationUrgency.Urgent
            else
                EscalationUrgency.Normal

        let notificationType =
            if
                reviewResult.Comments
                |> List.exists (fun c -> c.Dimension = QualityDimension.UserExperience)
            then
                EscalationNotificationType.BusinessDecision
            elif
                reviewResult.Comments
                |> List.exists (fun c -> c.Dimension = QualityDimension.TechnicalCompleteness)
            then
                EscalationNotificationType.TechnicalDecision
            else
                EscalationNotificationType.QualityGate

        let description =
            let sb = System.Text.StringBuilder()
            sb.AppendFormat("タスク「{0}」の品質ゲート評価でエスカレーションが必要です。\n\n", task.Title) |> ignore
            sb.AppendFormat("総合スコア: {0:F2}\n", reviewResult.ConsensusScore) |> ignore

            sb.AppendFormat("承認状況: {0}\n", if reviewResult.Approved then "承認" else "要改善")
            |> ignore

            sb.AppendFormat("改善事項: {0}件\n\n", reviewResult.RequiredImprovements.Length)
            |> ignore

            if reviewResult.RequiredImprovements.Length > 0 then
                sb.AppendLine("主な改善事項:") |> ignore

                for i, improvement in reviewResult.RequiredImprovements |> List.indexed do
                    sb.AppendFormat("  {0}. {1}\n", i + 1, improvement) |> ignore

            match alternatives with
            | Some alts when alts.Length > 0 -> sb.AppendFormat("\n{0}件の代替案が提案されています。\n", alts.Length) |> ignore
            | _ -> ()

            sb.ToString()

        let responseTime =
            match urgency with
            | EscalationUrgency.Immediate -> TimeSpan.FromMinutes(30.0)
            | EscalationUrgency.Urgent -> TimeSpan.FromHours(2.0)
            | EscalationUrgency.Normal -> TimeSpan.FromHours(8.0)
            | EscalationUrgency.Low -> TimeSpan.FromDays(1.0)

        { NotificationId = sprintf "qg-esc-%s-%s" task.TaskId (DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"))
          Title = sprintf "品質ゲート要判断: %s" task.Title
          Description = description
          NotificationType = notificationType
          Urgency = urgency
          RequestingAgent = "quality_gate_manager"
          TargetRole = "po"
          CreatedAt = DateTime.UtcNow
          RequiredResponseBy = DateTime.UtcNow.Add(responseTime)
          RelatedTaskIds = [ task.TaskId ]
          RelatedDecisionId = None
          Metadata = Map.empty
          Status = EscalationNotificationStatus.Pending
          ResponseContent = None
          ResponseAt = None }

    /// エラー時エスカレーション通知作成
    member this.CreateErrorEscalationNotification(task: ParsedTask, errorMsg: string) : EscalationNotification =
        { NotificationId = sprintf "qg-error-%s-%s" task.TaskId (DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"))
          Title = sprintf "品質ゲートエラー: %s" task.Title
          Description = sprintf "タスク「%s」の品質ゲート実行中にエラーが発生しました。\n\nエラー内容: %s\n\n手動による確認・判断が必要です。" task.Title errorMsg
          NotificationType = EscalationNotificationType.TechnicalDecision
          Urgency = EscalationUrgency.Urgent
          RequestingAgent = "quality_gate_manager"
          TargetRole = "po"
          CreatedAt = DateTime.UtcNow
          RequiredResponseBy = DateTime.UtcNow.Add(TimeSpan.FromHours(1.0))
          RelatedTaskIds = [ task.TaskId ]
          RelatedDecisionId = None
          Metadata = Map.empty
          Status = EscalationNotificationStatus.Pending
          ResponseContent = None
          ResponseAt = None }

    /// 品質ゲート統合統計情報生成
    member this.GenerateIntegrationStatistics() : string =
        let currentInfo = qualityGateUIManager.GetCurrentQualityGateInfo()
        let escalationStatus = escalationNotificationManager.GetNotificationCount()

        let sb = System.Text.StringBuilder()
        sb.AppendFormat("📊 品質ゲート統合統計\n\n") |> ignore
        sb.AppendFormat("現在時刻: {0:HH:mm:ss}\n", DateTime.UtcNow) |> ignore

        match currentInfo with
        | Some info ->
            sb.AppendFormat("アクティブタスク: {0}\n", info.TaskTitle) |> ignore
            sb.AppendFormat("表示状態: {0}\n", info.DisplayState) |> ignore
            sb.AppendFormat("最終更新: {0:HH:mm:ss}\n", info.LastUpdated) |> ignore
        | None -> sb.AppendLine("アクティブタスク: なし") |> ignore

        sb.AppendFormat("保留中エスカレーション: {0}件\n", escalationStatus) |> ignore

        sb.ToString()

/// 品質ゲート統合実行エントリーポイント
let executeQualityGateEvaluation (task: ParsedTask) : Async<QualityGateIntegrationResult> =
    async {
        let integrationManager = QualityGateIntegrationManager()

        // 適切なターゲットビューを特定（実際の実装では globalPaneTextViews から取得）
        use targetView = new TextView()

        return! integrationManager.ExecuteQualityGateIntegration(task, targetView)
    }

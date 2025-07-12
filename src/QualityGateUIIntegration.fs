module FCode.QualityGateUIIntegration

open System
open System.Collections.Concurrent
open Terminal.Gui
open NStack
open FCode.Logger
open FCode.QualityGateManager
open FCode.EscalationNotificationUI
open FCode.Collaboration.EscalationManager
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.AgentStateManager
open FCode.Collaboration.TaskDependencyGraph
open FCode.Collaboration.ProgressAggregator
open FCode.Collaboration.CollaborationCoordinator
open FCode.TaskAssignmentManager

// ===============================================
// 品質ゲート UI 統合システム型定義
// ===============================================

/// 品質ゲート表示状態
type QualityGateDisplayStatus =
    | Pending // 評価待機中
    | InProgress // 評価実行中
    | Passed // 品質ゲート通過
    | Failed // 品質ゲート失敗
    | RequiresPOApproval // PO承認要求
    | EscalationTriggered // エスカレーション発生

/// 品質ゲート評価エントリ
type QualityGateEvaluationEntry =
    { TaskId: string
      TaskTitle: string
      EvaluatedAt: DateTime
      QualityResult: QualityEvaluationResult option
      ReviewResult: ReviewResult option
      AlternativeProposals: AlternativeProposal list option
      DisplayStatus: QualityGateDisplayStatus
      POApprovalRequired: bool
      EscalationId: string option
      LastUpdated: DateTime }

/// PO判断アクション
type POApprovalAction =
    | Approve of string // 承認とコメント
    | Reject of string // 却下と理由
    | RequestRevision of string list // 修正要求と具体的指示
    | EscalateHigher of string // 上位エスカレーション

// ===============================================
// 品質ゲート UI 統合管理
// ===============================================

/// 品質ゲートUI統合管理クラス - QAペイン表示制御
type QualityGateUIIntegrationManager(qualityGateManager: QualityGateManager, escalationManager: EscalationManager) =

    let evaluationEntries = ConcurrentDictionary<string, QualityGateEvaluationEntry>()
    let maxEvaluationHistory = 50 // 最大評価履歴保持数
    let mutable qaTextView: TextView option = None
    let mutable qa2TextView: TextView option = None

    /// QAペイン用TextView設定
    member this.SetQATextViews(qa1TextViewParam: TextView, qa2TextViewParam: TextView) =
        qaTextView <- Some qa1TextViewParam
        qa2TextView <- Some qa2TextViewParam
        logInfo "QualityGateUIIntegration" "QA pane TextViews configured for quality gate display"

    /// タスクの品質ゲート評価実行
    member this.ExecuteQualityGateEvaluation(task: ParsedTask) =
        async {
            try
                logInfo "QualityGateUIIntegration" $"品質ゲート評価開始: {task.TaskId} - {task.Title}"

                // 評価エントリ初期化
                let initialEntry =
                    { TaskId = task.TaskId
                      TaskTitle = task.Title
                      EvaluatedAt = DateTime.UtcNow
                      QualityResult = None
                      ReviewResult = None
                      AlternativeProposals = None
                      DisplayStatus = InProgress
                      POApprovalRequired = false
                      EscalationId = None
                      LastUpdated = DateTime.UtcNow }

                evaluationEntries.[task.TaskId] <- initialEntry
                this.UpdateQADisplay()

                // QualityGateManager実行
                match qualityGateManager.ExecuteQualityGate(task) with
                | Result.Ok(reviewResult, alternatives) ->
                    // 品質評価結果の分析
                    let qualityLevel = this.DetermineQualityLevel(reviewResult)
                    let poApprovalRequired = this.RequiresPOApproval(reviewResult, alternatives)

                    // エスカレーション判定
                    let escalationId =
                        if not reviewResult.Approved && reviewResult.ConsensusScore < 0.5 then
                            // エスカレーション必要時のID生成（簡易版）
                            Some(sprintf "ESC-QG-%s-%s" task.TaskId (System.DateTime.Now.ToString("yyyyMMddHHmmss")))
                        else
                            None

                    // 評価エントリ更新
                    let updatedEntry =
                        { initialEntry with
                            QualityResult = None // QualityEvaluationResultは別途取得必要
                            ReviewResult = Some reviewResult
                            AlternativeProposals = alternatives
                            DisplayStatus = qualityLevel
                            POApprovalRequired = poApprovalRequired
                            EscalationId = escalationId
                            LastUpdated = DateTime.UtcNow }

                    evaluationEntries.[task.TaskId] <- updatedEntry
                    this.UpdateQADisplay()

                    // エスカレーション通知作成
                    match escalationId with
                    | Some escId ->
                        createEscalationNotification
                            $"品質ゲート評価: {task.Title}"
                            $"品質スコア {reviewResult.ConsensusScore:F2} - 改善要求 {reviewResult.RequiredImprovements.Length}件"
                            TechnicalDecision
                            (if reviewResult.ConsensusScore < 0.4 then Urgent else Normal)
                            "quality_gate"
                            "PO"
                            [ task.TaskId ]
                            (Some escId)
                        |> ignore
                    | None -> ()

                    logInfo "QualityGateUIIntegration" $"品質ゲート評価完了: {task.TaskId} - 状態: {qualityLevel}"
                    return Result.Ok updatedEntry

                | Result.Error error ->
                    let errorEntry =
                        { initialEntry with
                            DisplayStatus = Failed
                            LastUpdated = DateTime.UtcNow }

                    evaluationEntries.[task.TaskId] <- errorEntry
                    this.UpdateQADisplay()

                    logError "QualityGateUIIntegration" $"品質ゲート評価失敗: {task.TaskId} - {error}"
                    return Result.Error error

            with ex ->
                let errorMsg = $"品質ゲート評価例外: {ex.Message}"
                logError "QualityGateUIIntegration" errorMsg
                return Result.Error errorMsg
        }

    /// 品質レベル判定
    member private this.DetermineQualityLevel(reviewResult: ReviewResult) : QualityGateDisplayStatus =
        if reviewResult.Approved && reviewResult.ConsensusScore >= 0.8 then
            Passed
        elif reviewResult.Approved && reviewResult.ConsensusScore >= 0.65 then
            RequiresPOApproval
        elif
            reviewResult.RequiredImprovements.Length > 5
            || reviewResult.ConsensusScore < 0.4
        then
            EscalationTriggered
        else
            Failed

    /// PO承認要求判定
    member private this.RequiresPOApproval
        (reviewResult: ReviewResult, alternatives: AlternativeProposal list option)
        : bool =
        // 中程度の品質スコアまたは代替案が存在する場合はPO承認要求
        (reviewResult.ConsensusScore >= 0.5 && reviewResult.ConsensusScore < 0.8)
        || alternatives.IsSome

    /// PO承認処理
    member this.ProcessPOApproval(taskId: string, action: POApprovalAction, approver: string) =
        async {
            try
                match evaluationEntries.TryGetValue(taskId) with
                | true, entry ->
                    logInfo "QualityGateUIIntegration" $"PO承認処理開始: {taskId} - {action}"

                    let (newStatus, actionDescription) =
                        match action with
                        | Approve comment -> (Passed, $"PO承認: {comment}")
                        | Reject reason -> (Failed, $"PO却下: {reason}")
                        | RequestRevision revisions -> (Failed, sprintf "修正要求: %s" (String.concat "; " revisions))
                        | EscalateHigher reason -> (EscalationTriggered, $"上位エスカレーション: {reason}")

                    // エントリ更新
                    let updatedEntry =
                        { entry with
                            DisplayStatus = newStatus
                            POApprovalRequired = false
                            LastUpdated = DateTime.UtcNow }

                    evaluationEntries.[taskId] <- updatedEntry
                    this.UpdateQADisplay()

                    // エスカレーション処理
                    match entry.EscalationId with
                    | Some escId ->
                        let approved =
                            match action with
                            | Approve _ -> true
                            | _ -> false

                        let! poDecisionResult = escalationManager.ProcessPODecision(escId, approved, actionDescription)

                        match poDecisionResult with
                        | Result.Ok _ -> logInfo "QualityGateUIIntegration" $"エスカレーション解決: {escId}"
                        | Result.Error err -> logError "QualityGateUIIntegration" $"エスカレーション処理失敗: {err}"
                    | None -> ()

                    logInfo "QualityGateUIIntegration" $"PO承認処理完了: {taskId} - {actionDescription}"
                    return Result.Ok updatedEntry

                | false, _ ->
                    let errorMsg = $"評価エントリが見つかりません: {taskId}"
                    logError "QualityGateUIIntegration" errorMsg
                    return Result.Error errorMsg

            with ex ->
                let errorMsg = $"PO承認処理例外: {ex.Message}"
                logError "QualityGateUIIntegration" errorMsg
                return Result.Error errorMsg
        }

    /// QA表示更新
    member private this.UpdateQADisplay() =
        match qaTextView, qa2TextView with
        | Some qa1View, Some qa2View ->
            try
                // QA1: アクティブ評価表示
                let activeEvaluations =
                    evaluationEntries.Values
                    |> Seq.filter (fun e -> e.DisplayStatus = InProgress || e.DisplayStatus = RequiresPOApproval)
                    |> Seq.sortByDescending (fun e -> e.EvaluatedAt)
                    |> Seq.take 5
                    |> Seq.toArray

                let qa1Text = this.FormatActiveEvaluationsDisplay(activeEvaluations)

                // QA2: 品質レポート・履歴表示
                let recentEvaluations =
                    evaluationEntries.Values
                    |> Seq.filter (fun e -> e.DisplayStatus = Passed || e.DisplayStatus = Failed)
                    |> Seq.sortByDescending (fun e -> e.LastUpdated)
                    |> Seq.take 8
                    |> Seq.toArray

                let qa2Text = this.FormatQualityReportDisplay(recentEvaluations)

                // UI更新（CI環境では安全にスキップ）
                let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

                if not isCI then
                    try
                        if not (isNull Application.MainLoop) then
                            Application.MainLoop.Invoke(fun () ->
                                try
                                    qa1View.Text <- ustring.Make(qa1Text: string)
                                    qa1View.SetNeedsDisplay()
                                    qa2View.Text <- ustring.Make(qa2Text: string)
                                    qa2View.SetNeedsDisplay()
                                with ex ->
                                    logException "QualityGateUIIntegration" "QA UI update failed" ex)
                        else
                            qa1View.Text <- ustring.Make(qa1Text: string)
                            qa1View.SetNeedsDisplay()
                            qa2View.Text <- ustring.Make(qa2Text: string)
                            qa2View.SetNeedsDisplay()
                    with ex ->
                        logException "QualityGateUIIntegration" "QA display update failed" ex

                logDebug
                    "QualityGateUIIntegration"
                    $"QA表示更新完了: アクティブ {activeEvaluations.Length}件, 履歴 {recentEvaluations.Length}件"

            with ex ->
                logException "QualityGateUIIntegration" "Failed to update QA display" ex
        | _ -> logWarning "QualityGateUIIntegration" "QA TextViews not configured - cannot update display"

    /// アクティブ評価表示フォーマット
    member private this.FormatActiveEvaluationsDisplay(activeEvaluations: QualityGateEvaluationEntry[]) =
        let header = "=== QA1: 品質ゲート評価状況 ===\n\n"

        let activeSection =
            if activeEvaluations.Length > 0 then
                let activeLines =
                    activeEvaluations
                    |> Array.map (fun entry ->
                        let timeStr = entry.EvaluatedAt.ToString("MM/dd HH:mm")
                        let statusStr = this.GetStatusDisplay(entry.DisplayStatus)

                        let titlePreview =
                            if entry.TaskTitle.Length > 25 then
                                entry.TaskTitle.[..22] + "..."
                            else
                                entry.TaskTitle.PadRight(25)

                        let scoreStr =
                            match entry.ReviewResult with
                            | Some result -> $"{result.ConsensusScore:F2}"
                            | None -> "-.--"

                        let improvementCount =
                            match entry.ReviewResult with
                            | Some result -> result.RequiredImprovements.Length
                            | None -> 0

                        $"[{timeStr}] {statusStr} {titlePreview} スコア:{scoreStr} 改善:{improvementCount}件")
                    |> String.concat "\n"

                $"🔍 評価中・承認待ち ({activeEvaluations.Length}件)\n{activeLines}\n\n"
            else
                "✅ 評価待ちタスクなし\n\n"

        let poApprovalSection =
            let poApprovalTasks =
                activeEvaluations
                |> Array.filter (fun e -> e.POApprovalRequired)
                |> Array.length

            if poApprovalTasks > 0 then
                $"📋 PO承認要求: {poApprovalTasks}件\n\n"
            else
                ""

        let footer =
            "キーバインド: Ctrl+Q(品質詳細) Ctrl+A(承認) Ctrl+R(却下)\n--- 品質基準: スコア0.65以上で合格 ---"

        header + activeSection + poApprovalSection + footer

    /// 品質レポート表示フォーマット
    member private this.FormatQualityReportDisplay(recentEvaluations: QualityGateEvaluationEntry[]) =
        let header = "=== QA2: 品質レポート・履歴 ===\n\n"

        let recentSection =
            if recentEvaluations.Length > 0 then
                let recentLines =
                    recentEvaluations
                    |> Array.map (fun entry ->
                        let timeStr = entry.LastUpdated.ToString("MM/dd HH:mm")
                        let statusStr = this.GetStatusDisplay(entry.DisplayStatus)

                        let titlePreview =
                            if entry.TaskTitle.Length > 20 then
                                entry.TaskTitle.[..17] + "..."
                            else
                                entry.TaskTitle

                        let scoreStr =
                            match entry.ReviewResult with
                            | Some result -> $"{result.ConsensusScore:F2}"
                            | None -> "-.--"

                        $"[{timeStr}] {statusStr} {titlePreview} ({scoreStr})")
                    |> String.concat "\n"

                $"📊 最新評価結果 ({recentEvaluations.Length}件)\n{recentLines}\n\n"
            else
                "📊 評価履歴なし\n\n"

        // 品質統計の計算
        let allEvaluations = evaluationEntries.Values |> Seq.toArray
        let totalEvaluations = allEvaluations.Length

        let passedCount =
            allEvaluations
            |> Array.filter (fun e -> e.DisplayStatus = Passed)
            |> Array.length

        let failedCount =
            allEvaluations
            |> Array.filter (fun e -> e.DisplayStatus = Failed)
            |> Array.length

        let escalationCount =
            allEvaluations
            |> Array.filter (fun e -> e.DisplayStatus = EscalationTriggered)
            |> Array.length

        let passRate =
            if totalEvaluations > 0 then
                (float passedCount / float totalEvaluations) * 100.0
            else
                0.0

        let statisticsSection =
            "📈 品質統計\n"
            + sprintf "総評価: %d件 | 合格: %d件 (%.1f%%)\n" totalEvaluations passedCount passRate
            + sprintf "失敗: %d件 | エスカレ: %d件\n\n" failedCount escalationCount

        let footer = "--- 品質ゲート連携: QualityGateManager + EscalationManager ---"

        header + recentSection + statisticsSection + footer

    /// ステータス表示文字列取得
    member private this.GetStatusDisplay(status: QualityGateDisplayStatus) =
        match status with
        | Pending -> "⏳待機"
        | InProgress -> "🔍評価"
        | Passed -> "✅合格"
        | Failed -> "❌失敗"
        | RequiresPOApproval -> "📋承認要求"
        | EscalationTriggered -> "🚨エスカレ"

    /// タスク評価エントリ取得
    member this.GetEvaluationEntry(taskId: string) = evaluationEntries.TryGetValue(taskId)

    /// 全評価エントリ取得
    member this.GetAllEvaluationEntries() = evaluationEntries.Values |> Seq.toArray

    /// 評価履歴クリア
    member this.ClearEvaluationHistory() =
        evaluationEntries.Clear()
        this.UpdateQADisplay()
        logInfo "QualityGateUIIntegration" "評価履歴をクリアしました"

    /// リソース解放
    member this.Dispose() =
        evaluationEntries.Clear()
        qaTextView <- None
        qa2TextView <- None
        GC.SuppressFinalize(this)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

// ===============================================
// グローバル管理インスタンス
// ===============================================

/// 品質ゲートUI統合管理インスタンス（遅延初期化）
let mutable private qualityGateUIManagerInstance: QualityGateUIIntegrationManager option =
    None

/// 品質ゲートUI統合管理インスタンス取得または作成
let private getOrCreateQualityGateUIManager () =
    match qualityGateUIManagerInstance with
    | Some manager -> manager
    | None ->
        // デフォルト依存関係で初期化（実際の依存性注入は後で対応）
        let evaluationEngine = QualityEvaluationEngine()
        let reviewer = UpstreamDownstreamReviewer()
        let proposalGenerator = AlternativeProposalGenerator()

        let qualityGateManager =
            QualityGateManager(evaluationEngine, reviewer, proposalGenerator)

        // EscalationManagerの初期化（簡易版）
        let config =
            { MaxConcurrentAgents = 10
              TaskTimeoutMinutes = 30
              StaleAgentThreshold = TimeSpan.FromMinutes(5.0)
              MaxRetryAttempts = 3
              DatabasePath = "~/.fcode/tasks.db"
              ConnectionPoolSize = 5
              WALModeEnabled = true
              AutoVacuumEnabled = false
              MaxHistoryRetentionDays = 30
              BackupEnabled = false
              BackupIntervalHours = 24
              EscalationEnabled = true
              AutoRecoveryMaxAttempts = 3
              PONotificationThreshold = EscalationSeverity.Important
              CriticalEscalationTimeoutMinutes = 60
              DataProtectionModeEnabled = false
              EmergencyShutdownEnabled = false }

        // 実際の依存関係で初期化
        let agentStateManager = new AgentStateManager(config)
        let taskDependencyGraph = new TaskDependencyGraph(config)

        let progressAggregator =
            new ProgressAggregator(agentStateManager, taskDependencyGraph, config)

        let collaborationCoordinator =
            new CollaborationCoordinator(agentStateManager, taskDependencyGraph, config)

        let escalationManager =
            new EscalationManager(
                agentStateManager,
                taskDependencyGraph,
                progressAggregator,
                collaborationCoordinator,
                config
            )

        let manager =
            new QualityGateUIIntegrationManager(qualityGateManager, escalationManager)

        qualityGateUIManagerInstance <- Some manager
        manager

/// QAペイン用TextView設定（グローバル関数）
let setQATextViews (qa1TextView: TextView) (qa2TextView: TextView) =
    (getOrCreateQualityGateUIManager ()).SetQATextViews(qa1TextView, qa2TextView)

/// タスク品質ゲート評価実行（グローバル関数）
let executeQualityGateEvaluation (task: ParsedTask) =
    (getOrCreateQualityGateUIManager ()).ExecuteQualityGateEvaluation(task)

/// PO承認処理（グローバル関数）
let processPOApproval (taskId: string) (action: POApprovalAction) (approver: string) =
    (getOrCreateQualityGateUIManager ()).ProcessPOApproval(taskId, action, approver)

/// 依存性注入: 既存のインスタンスを置き換え（テスト用）
let injectQualityGateUIManager (manager: QualityGateUIIntegrationManager) =
    qualityGateUIManagerInstance <- Some manager

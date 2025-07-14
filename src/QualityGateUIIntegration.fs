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

    /// 評価エントリの初期化
    member private this.InitializeEvaluationEntry(task: ParsedTask) =
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

    /// エスカレーション判定とID生成
    member private this.DetermineEscalationId(task: ParsedTask, reviewResult: ReviewResult) =
        if not reviewResult.Approved && reviewResult.ConsensusScore < 0.5 then
            Some(sprintf "ESC-QG-%s-%s" task.TaskId (DateTime.UtcNow.ToString("yyyyMMddHHmmss")))
        else
            None

    /// エスカレーション通知作成
    member private this.CreateEscalationNotification
        (task: ParsedTask, reviewResult: ReviewResult, escalationId: string)
        =
        createEscalationNotification
            (sprintf "品質ゲート評価: %s" task.Title)
            (sprintf "品質スコア %.2f - 改善要求 %d件" reviewResult.ConsensusScore reviewResult.RequiredImprovements.Length)
            TechnicalDecision
            (if reviewResult.ConsensusScore < 0.4 then Urgent else Normal)
            "quality_gate"
            "PO"
            [ task.TaskId ]
            (Some escalationId)
        |> ignore

    /// 成功結果の処理
    member private this.ProcessSuccessfulEvaluation
        (
            task: ParsedTask,
            initialEntry: QualityGateEvaluationEntry,
            reviewResult: ReviewResult,
            alternatives: AlternativeProposal list option
        ) =
        let qualityLevel = this.DetermineQualityLevel(reviewResult)
        let poApprovalRequired = this.RequiresPOApproval(reviewResult, alternatives)
        let escalationId = this.DetermineEscalationId(task, reviewResult)

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
        | Some escId -> this.CreateEscalationNotification(task, reviewResult, escId)
        | None -> ()

        logInfo "QualityGateUIIntegration" (sprintf "品質ゲート評価完了: %s - 状態: %A" task.TaskId qualityLevel)
        Result.Ok updatedEntry

    /// エラー結果の処理
    member private this.ProcessErrorEvaluation
        (task: ParsedTask, initialEntry: QualityGateEvaluationEntry, error: string)
        =
        let errorEntry =
            { initialEntry with
                DisplayStatus = Failed
                LastUpdated = DateTime.UtcNow }

        evaluationEntries.[task.TaskId] <- errorEntry
        this.UpdateQADisplay()

        logError "QualityGateUIIntegration" (sprintf "品質ゲート評価失敗: %s - %s" task.TaskId error)
        Result.Error error

    /// タスクの品質ゲート評価実行
    member this.ExecuteQualityGateEvaluation(task: ParsedTask) =
        async {
            try
                logInfo "QualityGateUIIntegration" (sprintf "品質ゲート評価開始: %s - %s" task.TaskId task.Title)

                let initialEntry = this.InitializeEvaluationEntry(task)
                evaluationEntries.[task.TaskId] <- initialEntry
                this.UpdateQADisplay()

                // QualityGateManager実行
                match qualityGateManager.ExecuteQualityGate(task) with
                | Result.Ok(reviewResult, alternatives) ->
                    return this.ProcessSuccessfulEvaluation(task, initialEntry, reviewResult, alternatives)
                | Result.Error error -> return this.ProcessErrorEvaluation(task, initialEntry, error)

            with ex ->
                let errorMsg = sprintf "品質ゲート評価例外: %s" ex.Message
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
                    logInfo "QualityGateUIIntegration" (sprintf "PO承認処理開始: %s - %A" taskId action)

                    let (newStatus, actionDescription) =
                        match action with
                        | Approve comment -> (Passed, sprintf "PO承認: %s" comment)
                        | Reject reason -> (Failed, sprintf "PO却下: %s" reason)
                        | RequestRevision revisions -> (Failed, sprintf "修正要求: %s" (String.concat "; " revisions))
                        | EscalateHigher reason -> (EscalationTriggered, sprintf "上位エスカレーション: %s" reason)

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
                        | Result.Ok _ -> logInfo "QualityGateUIIntegration" (sprintf "エスカレーション解決: %s" escId)
                        | Result.Error err -> logError "QualityGateUIIntegration" (sprintf "エスカレーション処理失敗: %A" err)
                    | None -> ()

                    logInfo "QualityGateUIIntegration" (sprintf "PO承認処理完了: %s - %s" taskId actionDescription)
                    return Result.Ok updatedEntry

                | false, _ ->
                    let errorMsg = sprintf "評価エントリが見つかりません: %s" taskId
                    logError "QualityGateUIIntegration" errorMsg
                    return Result.Error errorMsg

            with ex ->
                let errorMsg = sprintf "PO承認処理例外: %s" ex.Message
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
                    (sprintf "QA表示更新完了: アクティブ %d件, 履歴 %d件" activeEvaluations.Length recentEvaluations.Length)

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
                            | Some result -> sprintf "%.2f" result.ConsensusScore
                            | None -> "-.--"

                        let improvementCount =
                            match entry.ReviewResult with
                            | Some result -> result.RequiredImprovements.Length
                            | None -> 0

                        sprintf "[%s] %s %s スコア:%s 改善:%d件" timeStr statusStr titlePreview scoreStr improvementCount)
                    |> String.concat "\n"

                sprintf "🔍 評価中・承認待ち (%d件)\n%s\n\n" activeEvaluations.Length activeLines
            else
                "✅ 評価待ちタスクなし\n\n"

        let poApprovalSection =
            let poApprovalTasks =
                activeEvaluations
                |> Array.filter (fun e -> e.POApprovalRequired)
                |> Array.length

            if poApprovalTasks > 0 then
                sprintf "📋 PO承認要求: %d件\n\n" poApprovalTasks
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
                            | Some result -> sprintf "%.2f" result.ConsensusScore
                            | None -> "-.--"

                        sprintf "[%s] %s %s (%s)" timeStr statusStr titlePreview scoreStr)
                    |> String.concat "\n"

                sprintf "📊 最新評価結果 (%d件)\n%s\n\n" recentEvaluations.Length recentLines
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

/// PO判断待ち状態の視覚的表示制御（SC-1-4用）
let updatePOWaitingDisplay (isWaiting: bool) =
    try
        let manager = getOrCreateQualityGateUIManager ()
        let waitingIndicator = if isWaiting then "⏳ PO判断待ち" else "✅ 判断完了"
        let timestamp = DateTime.Now.ToString("HH:mm:ss")

        // QA TextViewsの状態表示（SC-1-4 PO判断待ち状態管理）
        let statusMessage =
            if isWaiting then
                $"🔶 {timestamp} - PO判断待ち状態\n"
                + "Ctrl+Q A で承認、Ctrl+Q R で却下してください\n"
                + "代替作業: ブロックされていないタスクを継続可能\n"
                + "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n"
            else
                $"✅ {timestamp} - PO判断完了\n"
                + "作業を継続します\n"
                + "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n"

        logInfo
            "QualityGateUI"
            $"PO status message prepared: {statusMessage.Substring(0, min 50 statusMessage.Length)}..."

        logInfo "QualityGateUI" $"PO waiting display updated: {waitingIndicator}"
    with ex ->
        logError "QualityGateUI" $"Error updating PO waiting display: {ex.Message}"

/// PO判断処理（SC-1-4用統合関数）
let processPODecision (action: POApprovalAction) =
    try
        logInfo "QualityGateUI" $"Processing PO decision: {action}"

        // 現在PO判断待ちのタスクを検索
        let manager = getOrCreateQualityGateUIManager ()

        let pendingTasks =
            manager.GetAllEvaluationEntries()
            |> Array.filter (fun entry -> entry.POApprovalRequired && entry.DisplayStatus = RequiresPOApproval)
            |> Array.toList

        match pendingTasks with
        | [] ->
            logWarning "QualityGateUI" "No pending PO approval tasks found"
            false
        | latestTask :: _ ->
            // 最新のPO判断待ちタスクに対してアクションを適用（非同期）
            async {
                let! approvalResult = manager.ProcessPOApproval(latestTask.TaskId, action, "PO")

                match approvalResult with
                | Result.Ok _ ->
                    // 判断完了後、待機中表示を更新
                    updatePOWaitingDisplay false
                    logInfo "QualityGateUI" $"PO decision processed for task: {latestTask.TaskId}"
                    return true
                | Result.Error err ->
                    logError "QualityGateUI" $"Failed to process PO decision for task: {latestTask.TaskId} - {err}"
                    return false
            }
            |> Async.RunSynchronously
    with ex ->
        logError "QualityGateUI" $"Error processing PO decision: {ex.Message}"
        false

/// PO判断要求の開始（SC-1-4用）
let requestPOApproval (taskId: string) (taskTitle: string) =
    try
        logInfo "QualityGateUI" $"Requesting PO approval for task: {taskId} - {taskTitle}"

        // 判断待ち状態表示を開始
        updatePOWaitingDisplay true

        // エスカレーション通知も作成
        FCode.EscalationNotificationUI.createEscalationNotification
            $"PO判断要求: {taskTitle}"
            $"品質ゲート評価完了。PO判断をお待ちしています。\nCtrl+Q A (承認) または Ctrl+Q R (却下) で判断してください。"
            FCode.EscalationNotificationUI.QualityGate
            FCode.EscalationNotificationUI.Urgent
            taskId
            "PO"
            [ taskId ]
            None
        |> ignore

        logInfo "QualityGateUI" $"PO approval request created for task: {taskId}"
        true
    with ex ->
        logError "QualityGateUI" $"Error requesting PO approval: {ex.Message}"
        false

/// 依存性注入: 既存のインスタンスを置き換え（テスト用）
let injectQualityGateUIManager (manager: QualityGateUIIntegrationManager) =
    qualityGateUIManagerInstance <- Some manager

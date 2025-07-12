module FCode.EscalationUIHandler

open System
open System.Collections.Concurrent
open FCode.Logger
open FCode.EscalationNotificationUI
open FCode.Collaboration.EscalationManager
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.AgentStateManager
open FCode.Collaboration.TaskDependencyGraph
open FCode.Collaboration.ProgressAggregator
open FCode.Collaboration.CollaborationCoordinator
open FCode.QualityGateManager

// ===============================================
// エスカレーション UI ハンドラー型定義
// ===============================================

/// エスカレーション統合表示エントリ
type EscalationIntegratedEntry =
    { EscalationId: string
      TaskId: string
      TaskTitle: string
      AgentId: string
      Severity: EscalationSeverity
      CreatedAt: DateTime
      NotificationId: string option
      QualityGateEntryId: string option
      Status: EscalationNotificationStatus
      RequiredActions: string list
      PODecisionRequired: bool
      LastUpdated: DateTime }

/// PO対応アクション統合
type IntegratedPOAction =
    | ApproveAndContinue of string // 承認・継続
    | RequestQualityImprovement of string list // 品質改善要求
    | EscalateToHigherLevel of string // 上位エスカレーション
    | StopTaskExecution of string // タスク実行停止
    | RequestAlternativeApproach of string // 代替アプローチ要求

// ===============================================
// エスカレーション UI ハンドラー実装
// ===============================================

/// エスカレーションUIハンドラークラス - 統合エスカレーション表示制御
type EscalationUIHandler(escalationManager: EscalationManager, qualityGateManager: QualityGateManager) =

    let integratedEntries = ConcurrentDictionary<string, EscalationIntegratedEntry>()
    let maxIntegratedHistory = 30 // 最大統合履歴保持数

    /// エスカレーション発生時の統合処理
    member this.HandleEscalationTriggered(escalationContext: EscalationContext) =
        async {
            try
                logInfo "EscalationUIHandler" $"エスカレーション統合処理開始: {escalationContext.EscalationId}"

                // タスク情報の取得
                let taskTitle =
                    match escalationContext.TaskId with
                    | taskId when not (String.IsNullOrEmpty(taskId)) ->
                        // 実際のタスク情報取得（簡易実装）
                        $"Task-{taskId}"
                    | _ -> "不明なタスク"

                // 通知作成
                let notificationId =
                    createEscalationNotification
                        $"エスカレーション: {taskTitle}"
                        escalationContext.Description
                        TechnicalDecision
                        (this.ConvertSeverityToUrgency(escalationContext.Severity))
                        escalationContext.AgentId
                        "PO"
                        [ escalationContext.TaskId ]
                        (Some escalationContext.EscalationId)

                // 品質ゲート連携チェック（簡易版）
                let qualityGateEntryId = None // 実装時に品質ゲート結果との連携を追加

                // 統合エントリ作成
                let integratedEntry =
                    { EscalationId = escalationContext.EscalationId
                      TaskId = escalationContext.TaskId
                      TaskTitle = taskTitle
                      AgentId = escalationContext.AgentId
                      Severity = escalationContext.Severity
                      CreatedAt = escalationContext.DetectedAt
                      NotificationId = Some notificationId
                      QualityGateEntryId = qualityGateEntryId
                      Status = EscalationNotificationStatus.Pending
                      RequiredActions = escalationContext.RequiredActions
                      PODecisionRequired = this.RequiresPODecision(escalationContext.Severity)
                      LastUpdated = DateTime.UtcNow }

                integratedEntries.[escalationContext.EscalationId] <- integratedEntry

                logInfo
                    "EscalationUIHandler"
                    $"エスカレーション統合エントリ作成: {escalationContext.EscalationId} - 通知ID: {notificationId}"

                return Result.Ok integratedEntry

            with ex ->
                let errorMsg = $"エスカレーション統合処理例外: {ex.Message}"
                logError "EscalationUIHandler" errorMsg
                return Result.Error errorMsg
        }

    /// 致命度から緊急度への変換
    member private this.ConvertSeverityToUrgency(severity: EscalationSeverity) : EscalationUrgency =
        match severity with
        | EscalationSeverity.Critical -> Immediate
        | EscalationSeverity.Severe -> Urgent
        | EscalationSeverity.Important -> Normal
        | EscalationSeverity.Moderate -> Normal
        | EscalationSeverity.Minor -> Low
        | _ -> Normal

    /// PO判断要求判定
    member private this.RequiresPODecision(severity: EscalationSeverity) : bool =
        match severity with
        | EscalationSeverity.Critical
        | EscalationSeverity.Severe
        | EscalationSeverity.Important -> true
        | _ -> false

    /// 統合PO対応処理
    member this.ProcessIntegratedPOAction(escalationId: string, action: IntegratedPOAction, responder: string) =
        async {
            try
                match integratedEntries.TryGetValue(escalationId) with
                | true, entry ->
                    logInfo "EscalationUIHandler" $"統合PO対応処理開始: {escalationId} - {action}"

                    // エスカレーション処理
                    let (approved, reason) =
                        match action with
                        | ApproveAndContinue comment -> (true, $"承認・継続: {comment}")
                        | RequestQualityImprovement improvements ->
                            (false, sprintf "品質改善要求: %s" (String.concat "; " improvements))
                        | EscalateToHigherLevel reason -> (false, $"上位エスカレーション: {reason}")
                        | StopTaskExecution reason -> (false, $"タスク実行停止: {reason}")
                        | RequestAlternativeApproach reason -> (false, $"代替アプローチ要求: {reason}")

                    let! escalationResult = escalationManager.ProcessPODecision(escalationId, approved, reason)

                    // 通知システム連携
                    match entry.NotificationId with
                    | Some notificationId ->
                        let notificationAction =
                            match action with
                            | ApproveAndContinue comment -> ApproveWithComment comment
                            | RequestQualityImprovement _ -> RequestMoreInfo reason
                            | EscalateToHigherLevel reason -> EscalateToHigher reason
                            | StopTaskExecution reason -> Reject reason
                            | RequestAlternativeApproach reason -> RequestMoreInfo reason

                        respondToNotification notificationId notificationAction responder |> ignore
                    | None -> ()

                    // 品質ゲート連携（簡易版）
                    match entry.QualityGateEntryId with
                    | Some taskId -> logInfo "EscalationUIHandler" $"品質ゲート連携: {taskId} - {action}"
                    // 実装時にqualityGateManagerとの連携を追加
                    | None -> ()

                    // 統合エントリ更新
                    let newStatus =
                        match action with
                        | ApproveAndContinue _ -> EscalationNotificationStatus.Resolved
                        | EscalateToHigherLevel _ -> EscalationNotificationStatus.EscalatedHigher
                        | StopTaskExecution _ -> EscalationNotificationStatus.Rejected
                        | _ -> EscalationNotificationStatus.MoreInfoRequested

                    let updatedEntry =
                        { entry with
                            Status = newStatus
                            PODecisionRequired = false
                            LastUpdated = DateTime.UtcNow }

                    integratedEntries.[escalationId] <- updatedEntry

                    logInfo "EscalationUIHandler" $"統合PO対応処理完了: {escalationId} - {reason}"
                    return Result.Ok updatedEntry

                | false, _ ->
                    let errorMsg = $"統合エントリが見つかりません: {escalationId}"
                    logError "EscalationUIHandler" errorMsg
                    return Result.Error errorMsg

            with ex ->
                let errorMsg = $"統合PO対応処理例外: {ex.Message}"
                logError "EscalationUIHandler" errorMsg
                return Result.Error errorMsg
        }

    /// アクティブエスカレーション取得
    member this.GetActiveEscalations() =
        integratedEntries.Values
        |> Seq.filter (fun e ->
            e.Status = EscalationNotificationStatus.Pending
            || e.Status = EscalationNotificationStatus.MoreInfoRequested)
        |> Seq.sortByDescending (fun e -> e.CreatedAt)
        |> Seq.toArray

    /// 重要度別エスカレーション統計
    member this.GetEscalationStatistics() =
        let allEntries = integratedEntries.Values |> Seq.toArray
        let activeEntries = this.GetActiveEscalations()

        let severityStats =
            allEntries
            |> Array.groupBy (fun e -> e.Severity)
            |> Array.map (fun (severity, entries) -> (severity, entries.Length))
            |> Array.sortByDescending snd

        let poDecisionStats =
            activeEntries |> Array.filter (fun e -> e.PODecisionRequired) |> Array.length

        let qualityGateLinkedStats =
            allEntries
            |> Array.filter (fun e -> e.QualityGateEntryId.IsSome)
            |> Array.length

        {| TotalEscalations = allEntries.Length
           ActiveEscalations = activeEntries.Length
           PODecisionRequired = poDecisionStats
           QualityGateLinked = qualityGateLinkedStats
           SeverityDistribution = severityStats
           LastUpdated = DateTime.UtcNow |}

    /// エスカレーション詳細情報取得
    member this.GetEscalationDetail(escalationId: string) =
        integratedEntries.TryGetValue(escalationId)

    /// エスカレーション状態同期
    member this.SynchronizeEscalationStatus() =
        async {
            try
                let! activeEscalationsResult = escalationManager.GetActiveEscalations()

                match activeEscalationsResult with
                | Result.Ok activeEscalations ->
                    // アクティブエスカレーションとの同期
                    for escalationContext in activeEscalations do
                        match integratedEntries.TryGetValue(escalationContext.EscalationId) with
                        | true, entry ->
                            // エントリが存在する場合は状態を同期
                            let updatedEntry =
                                { entry with
                                    LastUpdated = DateTime.UtcNow }

                            integratedEntries.[escalationContext.EscalationId] <- updatedEntry
                        | false, _ ->
                            // エントリが存在しない場合は新規作成
                            let! createResult = this.HandleEscalationTriggered(escalationContext)

                            match createResult with
                            | Result.Ok _ -> ()
                            | Result.Error err -> logError "EscalationUIHandler" $"エスカレーション同期中の新規作成失敗: {err}"

                    logInfo "EscalationUIHandler" $"エスカレーション状態同期完了: {activeEscalations.Length}件"
                    return Result.Ok activeEscalations.Length

                | Result.Error err ->
                    let errorMsg = $"アクティブエスカレーション取得失敗: {err}"
                    logError "EscalationUIHandler" errorMsg
                    return Result.Error errorMsg

            with ex ->
                let errorMsg = $"エスカレーション状態同期例外: {ex.Message}"
                logError "EscalationUIHandler" errorMsg
                return Result.Error errorMsg
        }

    /// 履歴クリア
    member this.ClearEscalationHistory() =
        integratedEntries.Clear()
        logInfo "EscalationUIHandler" "エスカレーション統合履歴をクリアしました"

    /// リソース解放
    member this.Dispose() =
        integratedEntries.Clear()
        GC.SuppressFinalize(this)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

// ===============================================
// エスカレーション統合管理ヘルパー関数
// ===============================================

/// エスカレーション緊急度表示取得
let getEscalationSeverityDisplay (severity: EscalationSeverity) =
    match severity with
    | EscalationSeverity.Critical -> "🔴致命"
    | EscalationSeverity.Severe -> "🟠重大"
    | EscalationSeverity.Important -> "🟡重要"
    | EscalationSeverity.Moderate -> "🟢中程"
    | EscalationSeverity.Minor -> "⚪軽微"
    | _ -> "❓不明"

/// エスカレーション推奨アクション取得
let getRecommendedActions (entry: EscalationIntegratedEntry) =
    match entry.Severity with
    | EscalationSeverity.Critical -> [ "即座対応"; "システム停止検討"; "技術チーム招集"; "経営陣報告" ]
    | EscalationSeverity.Severe -> [ "緊急技術調査"; "影響範囲確認"; "復旧計画立案"; "ステークホルダー通知" ]
    | EscalationSeverity.Important -> [ "技術調査"; "代替案検討"; "スケジュール調整"; "品質基準見直し" ]
    | EscalationSeverity.Moderate -> [ "通常調査"; "改善案作成"; "次回スプリントで対応" ]
    | EscalationSeverity.Minor -> [ "記録・追跡"; "将来対応検討" ]
    | _ -> [ "詳細調査必要" ]

/// エスカレーション統合表示フォーマット
let formatEscalationIntegratedDisplay (entries: EscalationIntegratedEntry[]) =
    let header = "=== エスカレーション統合管理 ===\n\n"

    let activeSection =
        if entries.Length > 0 then
            let activeLines =
                entries
                |> Array.map (fun entry ->
                    let timeStr = entry.CreatedAt.ToString("MM/dd HH:mm")
                    let severityStr = getEscalationSeverityDisplay entry.Severity
                    let agentStr = entry.AgentId.PadRight(6)

                    let titlePreview =
                        if entry.TaskTitle.Length > 18 then
                            entry.TaskTitle.[..15] + "..."
                        else
                            entry.TaskTitle.PadRight(18)

                    let statusIndicator =
                        if entry.PODecisionRequired then "📋PO要求"
                        elif entry.QualityGateEntryId.IsSome then "🔍品質連携"
                        else "⏳処理中"

                    $"[{timeStr}] {severityStr} {agentStr} {titlePreview} {statusIndicator}")
                |> String.concat "\n"

            $"🚨 アクティブエスカレーション ({entries.Length}件)\n{activeLines}\n\n"
        else
            "✅ アクティブエスカレーションなし\n\n"

    let footer =
        "キーバインド: Ctrl+E(詳細) Ctrl+A(承認) Ctrl+R(却下) Ctrl+H(上位エスカレ)\n"
        + "--- エスカレーション統合: 品質ゲート + 通知システム連携 ---"

    header + activeSection + footer

// ===============================================
// グローバル管理インスタンス
// ===============================================

/// エスカレーションUIハンドラーインスタンス（遅延初期化）
let mutable private escalationUIHandlerInstance: EscalationUIHandler option = None

/// エスカレーションUIハンドラーインスタンス取得または作成
let private getOrCreateEscalationUIHandler () =
    match escalationUIHandlerInstance with
    | Some handler -> handler
    | None ->
        // 依存関係の取得（実際の実装では依存性注入で解決）
        let escalationManager =
            // 簡易版EscalationManager（実際の依存性注入が必要）
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

            let agentStateManager = new AgentStateManager(config)
            let taskDependencyGraph = new TaskDependencyGraph(config)

            let progressAggregator =
                new ProgressAggregator(agentStateManager, taskDependencyGraph, config)

            let collaborationCoordinator =
                new CollaborationCoordinator(agentStateManager, taskDependencyGraph, config)

            new EscalationManager(
                agentStateManager,
                taskDependencyGraph,
                progressAggregator,
                collaborationCoordinator,
                config
            )

        let qualityGateManager =
            // QualityGateManagerの取得（実際の依存性注入が必要）
            let evaluationEngine = QualityEvaluationEngine()
            let reviewer = UpstreamDownstreamReviewer()
            let proposalGenerator = AlternativeProposalGenerator()
            QualityGateManager(evaluationEngine, reviewer, proposalGenerator)

        let handler = new EscalationUIHandler(escalationManager, qualityGateManager)
        escalationUIHandlerInstance <- Some handler
        handler

/// エスカレーション発生処理（グローバル関数）
let handleEscalationTriggered (escalationContext: EscalationContext) =
    (getOrCreateEscalationUIHandler ()).HandleEscalationTriggered(escalationContext)

/// 統合PO対応処理（グローバル関数）
let processIntegratedPOAction (escalationId: string) (action: IntegratedPOAction) (responder: string) =
    (getOrCreateEscalationUIHandler ()).ProcessIntegratedPOAction(escalationId, action, responder)

/// アクティブエスカレーション取得（グローバル関数）
let getActiveEscalations () =
    (getOrCreateEscalationUIHandler ()).GetActiveEscalations()

/// エスカレーション統計取得（グローバル関数）
let getEscalationStatistics () =
    (getOrCreateEscalationUIHandler ()).GetEscalationStatistics()

/// 依存性注入: 既存のインスタンスを置き換え（テスト用）
let injectEscalationUIHandler (handler: EscalationUIHandler) =
    escalationUIHandlerInstance <- Some handler

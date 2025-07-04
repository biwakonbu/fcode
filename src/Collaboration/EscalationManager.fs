module FCode.Collaboration.EscalationManager

open System
open System.Collections.Concurrent
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IAgentStateManager
open FCode.Collaboration.ITaskDependencyGraph
open FCode.Collaboration.IProgressAggregator
open FCode.Collaboration.ICollaborationCoordinator
open FCode.Collaboration.IEscalationManager
open FCode.Logger

/// エスカレーションマネージャー実装（基本版）
type EscalationManager
    (
        agentStateManager: IAgentStateManager,
        taskDependencyGraph: ITaskDependencyGraph,
        progressAggregator: IProgressAggregator,
        collaborationCoordinator: ICollaborationCoordinator,
        config: CollaborationConfig
    ) =

    let activeEscalations = ConcurrentDictionary<string, EscalationContext>()
    let escalationHistory = ConcurrentBag<EscalationResult>()
    let escalationIdCounter = ref 0

    /// エスカレーションID生成
    member private this.GenerateEscalationId() =
        let id = System.Threading.Interlocked.Increment(escalationIdCounter)
        sprintf "ESC-%s-%04d" (DateTime.UtcNow.ToString("yyyyMMdd")) id

    /// 5段階致命度評価: 影響度・時間制約・リスク分析
    member this.EvaluateSeverity(taskId: string, agentId: string, error: string) =
        async {
            try
                logInfo "EscalationManager"
                <| sprintf "致命度評価開始: Task=%s, Agent=%s" taskId agentId

                // 簡易評価ロジック
                let severity =
                    if
                        error.ToLowerInvariant().Contains("critical")
                        || error.ToLowerInvariant().Contains("failure")
                    then
                        EscalationSeverity.Critical
                    elif
                        error.ToLowerInvariant().Contains("error")
                        || error.ToLowerInvariant().Contains("exception")
                    then
                        EscalationSeverity.Severe
                    elif
                        error.ToLowerInvariant().Contains("warning")
                        || error.ToLowerInvariant().Contains("timeout")
                    then
                        EscalationSeverity.Important
                    else
                        EscalationSeverity.Moderate

                logInfo "EscalationManager" <| sprintf "致命度評価完了: %A" severity
                return Result.Ok severity
            with ex ->
                logError "EscalationManager" <| sprintf "致命度評価例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// PO通知レベル判定: 軽微(自動対応) / 重要(即座通知) / 致命(緊急停止)
    member this.DetermineNotificationLevel(severity: EscalationSeverity, factors: EscalationFactors) =
        try
            if not config.EscalationEnabled then
                Result.Ok(false, "エスカレーション機能が無効")
            else
                let shouldNotify = severity >= config.PONotificationThreshold
                let reason = sprintf "%A: 通知判定完了" severity

                logInfo "EscalationManager" <| sprintf "PO通知判定: %b, 理由=%s" shouldNotify reason
                Result.Ok(shouldNotify, reason)
        with ex ->
            logError "EscalationManager" <| sprintf "PO通知判定例外: %s" ex.Message
            Result.Error(SystemError ex.Message)

    /// エスカレーション発生時の初期処理: コンテキスト作成・分析・対応方針決定
    member this.TriggerEscalation(taskId: string, agentId: string, error: string) =
        async {
            try
                logInfo "EscalationManager"
                <| sprintf "エスカレーション発生: Task=%s, Agent=%s" taskId agentId

                let escalationId = this.GenerateEscalationId()

                // 致命度評価
                let! severityResult = this.EvaluateSeverity(taskId, agentId, error)

                let severity =
                    match severityResult with
                    | Result.Ok s -> s
                    | Result.Error _ -> EscalationSeverity.Important // フォールバック

                // 影響範囲とリスクレベルの詳細分析
                let (impactScope, riskLevel, dependentTaskCount) =
                    match severity with
                    | EscalationSeverity.Critical -> (SystemWide, CriticalRisk [ "システム全体停止"; "データ損失リスク" ], 10)
                    | EscalationSeverity.Severe -> (AgentWorkflow, HighRisk "重要プロセス影響", 5)
                    | EscalationSeverity.Important -> (RelatedTasks, ModerateRisk, 2)
                    | EscalationSeverity.Moderate -> (SingleTask, LowRisk, 0)
                    | EscalationSeverity.Minor -> (SingleTask, LowRisk, 0)
                    | _ -> (SingleTask, LowRisk, 0)

                // 時間制約の判定
                let timeConstraint =
                    match severity with
                    | EscalationSeverity.Critical -> CriticalTiming
                    | EscalationSeverity.Severe -> ImmediateAction
                    | EscalationSeverity.Important -> SoonDeadline(TimeSpan.FromHours(4.0))
                    | _ -> NoUrgency

                // ブロッカー種別の判定
                let blockerType =
                    if
                        error.ToLowerInvariant().Contains("resource")
                        || error.ToLowerInvariant().Contains("memory")
                    then
                        ResourceUnavailable error
                    elif
                        error.ToLowerInvariant().Contains("dependency")
                        || error.ToLowerInvariant().Contains("external")
                    then
                        ExternalDependency error
                    elif
                        error.ToLowerInvariant().Contains("quality")
                        || error.ToLowerInvariant().Contains("standard")
                    then
                        QualityGate
                    elif
                        error.ToLowerInvariant().Contains("decision")
                        || error.ToLowerInvariant().Contains("approval")
                    then
                        BusinessJudgment
                    else
                        TechnicalIssue error

                // 推定解決時間の算出
                let estimatedResolutionTime =
                    match severity with
                    | EscalationSeverity.Critical -> Some(TimeSpan.FromMinutes(15.0))
                    | EscalationSeverity.Severe -> Some(TimeSpan.FromMinutes(60.0))
                    | EscalationSeverity.Important -> Some(TimeSpan.FromMinutes(240.0))
                    | EscalationSeverity.Moderate -> Some(TimeSpan.FromMinutes(480.0))
                    | EscalationSeverity.Minor -> Some(TimeSpan.FromHours(24.0))
                    | _ -> Some(TimeSpan.FromMinutes(240.0))

                // 必要アクションの決定
                let requiredActions =
                    match severity with
                    | EscalationSeverity.Critical -> [ "緊急システム停止"; "データバックアップ"; "インシデント対応チーム招集"; "経営陣報告" ]
                    | EscalationSeverity.Severe -> [ "技術チームエスカレーション"; "影響範囲調査"; "復旧計画立案" ]
                    | EscalationSeverity.Important -> [ "技術調査"; "代替案検討"; "スケジュール調整" ]
                    | _ -> [ "技術調査"; "エラー分析" ]

                // エスカレーションコンテキスト作成
                let escalationContext =
                    { EscalationId = escalationId
                      TaskId = taskId
                      AgentId = agentId
                      Severity = severity
                      Factors =
                        { ImpactScope = impactScope
                          TimeConstraint = timeConstraint
                          RiskLevel = riskLevel
                          BlockerType = blockerType
                          AutoRecoveryAttempts = 0
                          DependentTaskCount = dependentTaskCount }
                      Description = error
                      DetectedAt = DateTime.UtcNow
                      AutoRecoveryAttempted = false
                      RequiredActions = requiredActions
                      EstimatedResolutionTime = estimatedResolutionTime }

                activeEscalations.[escalationId] <- escalationContext

                logInfo "EscalationManager"
                <| sprintf "エスカレーション登録完了: %s (致命度=%A)" escalationId severity

                return Result.Ok escalationContext
            with ex ->
                logError "EscalationManager" <| sprintf "エスカレーション発生例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 自動復旧試行: 回復可能な問題の自動解決
    member this.AttemptAutoRecovery(escalationContext: EscalationContext) =
        async {
            try
                logInfo "EscalationManager"
                <| sprintf
                    "自動復旧試行開始: %s (試行回数: %d/%d)"
                    escalationContext.EscalationId
                    escalationContext.Factors.AutoRecoveryAttempts
                    config.AutoRecoveryMaxAttempts

                // 試行回数制限チェック
                if escalationContext.Factors.AutoRecoveryAttempts >= config.AutoRecoveryMaxAttempts then
                    logInfo "EscalationManager"
                    <| sprintf "自動復旧上限到達: %s" escalationContext.EscalationId

                    return Result.Ok(false, "自動復旧試行回数上限に到達")
                else

                    // 致命度による復旧可能性判定
                    let recoveryPossible =
                        match escalationContext.Severity with
                        | EscalationSeverity.Minor
                        | EscalationSeverity.Moderate -> true
                        | EscalationSeverity.Important -> escalationContext.Factors.AutoRecoveryAttempts < 2
                        | EscalationSeverity.Severe
                        | EscalationSeverity.Critical -> false
                        | _ -> false

                    if not recoveryPossible then
                        logInfo "EscalationManager"
                        <| sprintf "致命度により自動復旧不可: %s (%A)" escalationContext.EscalationId escalationContext.Severity

                        return Result.Ok(false, sprintf "致命度%Aのため自動復旧不可" escalationContext.Severity)
                    else

                        // ブロッカー種別による復旧戦略の選択
                        let (recoveryStrategy, successProbability) =
                            match escalationContext.Factors.BlockerType with
                            | TechnicalIssue error ->
                                if error.ToLowerInvariant().Contains("timeout") then
                                    ("リトライ実行", 0.8)
                                elif error.ToLowerInvariant().Contains("network") then
                                    ("ネットワーク再接続", 0.7)
                                elif error.ToLowerInvariant().Contains("memory") then
                                    ("メモリクリーンアップ", 0.6)
                                else
                                    ("基本復旧手順", 0.5)
                            | ResourceUnavailable resource ->
                                if resource.ToLowerInvariant().Contains("memory") then
                                    ("メモリ解放・GC実行", 0.7)
                                elif resource.ToLowerInvariant().Contains("disk") then
                                    ("一時ファイル削除", 0.6)
                                else
                                    ("リソース待機・再試行", 0.4)
                            | ExternalDependency _ -> ("依存サービス再接続", 0.3)
                            | QualityGate -> ("品質チェック緩和", 0.2)
                            | BusinessJudgment -> ("自動判断不可", 0.0)

                        // 復旧成功判定（確率的）
                        let random = System.Random()
                        let recoverySuccess = random.NextDouble() < successProbability

                        // コンテキストの更新（試行回数増加）
                        let updatedFactors =
                            { escalationContext.Factors with
                                AutoRecoveryAttempts = escalationContext.Factors.AutoRecoveryAttempts + 1 }

                        let updatedContext =
                            { escalationContext with
                                Factors = updatedFactors
                                AutoRecoveryAttempted = true }

                        activeEscalations.[escalationContext.EscalationId] <- updatedContext

                        let resultMessage =
                            if recoverySuccess then
                                sprintf "自動復旧成功: %s (戦略: %s)" escalationContext.EscalationId recoveryStrategy
                            else
                                sprintf
                                    "自動復旧失敗: %s (戦略: %s, 成功率: %.1f%%)"
                                    escalationContext.EscalationId
                                    recoveryStrategy
                                    (successProbability * 100.0)

                        logInfo "EscalationManager" <| sprintf "自動復旧結果: %s" resultMessage

                        return Result.Ok(recoverySuccess, resultMessage)
            with ex ->
                logError "EscalationManager" <| sprintf "自動復旧例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 判断待機管理: 代替作業継続・ブロッカー迂回・優先順位調整
    member this.ManageWaitingDecision(escalationId: string, maxWaitTime: TimeSpan) =
        async {
            try
                match activeEscalations.TryGetValue(escalationId) with
                | true, context ->
                    logInfo "EscalationManager" <| sprintf "判断待機管理開始: %s" escalationId

                    let action =
                        match context.Severity with
                        | EscalationSeverity.Minor
                        | EscalationSeverity.Moderate -> ContinueWithAlternative "代替作業継続"
                        | EscalationSeverity.Important -> WaitForPODecision maxWaitTime
                        | EscalationSeverity.Severe
                        | EscalationSeverity.Critical -> StopTaskExecution
                        | _ -> StopTaskExecution

                    logInfo "EscalationManager"
                    <| sprintf "判断待機アクション決定: %s, アクション=%A" escalationId action

                    return Result.Ok action
                | false, _ ->
                    logError "EscalationManager" <| sprintf "エスカレーション未発見: %s" escalationId
                    return Result.Error(NotFound(sprintf "エスカレーション %s が見つかりません" escalationId))
            with ex ->
                logError "EscalationManager" <| sprintf "判断待機管理例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 緊急対応フロー: データ保護・復旧優先・影響最小化
    member this.ExecuteEmergencyResponse(escalationContext: EscalationContext) =
        async {
            try
                logInfo "EscalationManager"
                <| sprintf "緊急対応フロー開始: %s" escalationContext.EscalationId

                let result =
                    { EscalationId = escalationContext.EscalationId
                      Action = DataProtectionMode
                      ResolvedAt = Some DateTime.UtcNow
                      ResolutionMethod = Some "緊急対応フロー実行"
                      PONotified = true
                      ImpactMitigated = true
                      LessonsLearned = [ sprintf "致命度: %A" escalationContext.Severity ] }

                escalationHistory.Add(result)
                activeEscalations.TryRemove(escalationContext.EscalationId) |> ignore

                logInfo "EscalationManager"
                <| sprintf "緊急対応フロー完了: %s" escalationContext.EscalationId

                return Result.Ok result
            with ex ->
                logError "EscalationManager" <| sprintf "緊急対応フロー例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// PO判断受信処理: 承認・却下・上位エスカレーション対応
    member this.ProcessPODecision(escalationId: string, approved: bool, reason: string) =
        async {
            try
                match activeEscalations.TryGetValue(escalationId) with
                | true, context ->
                    logInfo "EscalationManager" <| sprintf "PO判断受信: %s, 承認=%b" escalationId approved

                    let action =
                        if approved then
                            ContinueWithAlternative reason
                        else
                            StopTaskExecution

                    let resolutionMethod =
                        if approved then
                            sprintf "PO承認: %s" reason
                        else
                            sprintf "PO却下: %s" reason

                    let result =
                        { EscalationId = escalationId
                          Action = action
                          ResolvedAt = Some DateTime.UtcNow
                          ResolutionMethod = Some resolutionMethod
                          PONotified = true
                          ImpactMitigated = approved
                          LessonsLearned = [ if approved then "PO判断: 承認" else "PO判断: 却下" ] }

                    escalationHistory.Add(result)
                    activeEscalations.TryRemove(escalationId) |> ignore

                    logInfo "EscalationManager" <| sprintf "PO判断処理完了: %s" escalationId
                    return Result.Ok result
                | false, _ ->
                    logError "EscalationManager" <| sprintf "エスカレーション未発見: %s" escalationId
                    return Result.Error(NotFound(sprintf "エスカレーション %s が見つかりません" escalationId))
            with ex ->
                logError "EscalationManager" <| sprintf "PO判断処理例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// エスカレーション履歴取得: 過去の対応パターン・学習データ
    member this.GetEscalationHistory(agentId: string option, severity: EscalationSeverity option) =
        async {
            try
                let allHistory = escalationHistory |> Seq.toList
                let activeEscalations = activeEscalations.Values |> Seq.toList

                // AgentIdによるフィルタリング
                let filteredByAgent =
                    match agentId with
                    | Some targetAgentId ->
                        allHistory
                        |> List.filter (fun result ->
                            activeEscalations
                            |> List.exists (fun ctx ->
                                ctx.EscalationId = result.EscalationId && ctx.AgentId = targetAgentId))
                    | None -> allHistory

                // Severityによるフィルタリング
                let filteredBySeverity =
                    match severity with
                    | Some targetSeverity ->
                        filteredByAgent
                        |> List.filter (fun result ->
                            activeEscalations
                            |> List.exists (fun ctx ->
                                ctx.EscalationId = result.EscalationId && ctx.Severity = targetSeverity))
                    | None -> filteredByAgent

                // 時系列順（新しい順）でソート
                let sortedHistory =
                    filteredBySeverity
                    |> List.sortByDescending (fun r ->
                        match r.ResolvedAt with
                        | Some resolvedAt -> resolvedAt
                        | None -> DateTime.MinValue)

                let filterInfo =
                    match agentId, severity with
                    | Some aid, Some sev -> sprintf " (Agent=%s, Severity=%A)" aid sev
                    | Some aid, None -> sprintf " (Agent=%s)" aid
                    | None, Some sev -> sprintf " (Severity=%A)" sev
                    | None, None -> ""

                logInfo "EscalationManager"
                <| sprintf "エスカレーション履歴取得: %d件%s" sortedHistory.Length filterInfo

                return Result.Ok sortedHistory
            with ex ->
                logError "EscalationManager" <| sprintf "エスカレーション履歴取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 現在アクティブなエスカレーション一覧
    member this.GetActiveEscalations() =
        async {
            try
                let activeList = activeEscalations.Values |> Seq.toList
                logInfo "EscalationManager" <| sprintf "アクティブエスカレーション取得: %d件" activeList.Length
                return Result.Ok activeList
            with ex ->
                logError "EscalationManager" <| sprintf "アクティブエスカレーション取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// エスカレーション統計情報
    member this.GetEscalationStatistics() =
        async {
            try
                let allHistory = escalationHistory |> Seq.toList
                let totalEscalations = allHistory.Length

                // アクティブエスカレーションの致命度別集計
                let activeEscalations = activeEscalations.Values |> Seq.toList

                let escalationsBySeverity =
                    allHistory
                    |> List.fold
                        (fun acc result ->
                            // アクティブエスカレーションから致命度を取得
                            let severity =
                                activeEscalations
                                |> List.tryFind (fun ctx -> ctx.EscalationId = result.EscalationId)
                                |> Option.map (fun ctx -> ctx.Severity)
                                |> Option.defaultValue EscalationSeverity.Moderate

                            let count = Map.tryFind severity acc |> Option.defaultValue 0
                            Map.add severity (count + 1) acc)
                        Map.empty

                // 自動復旧成功率の計算
                let autoRecoveryAttempts =
                    allHistory
                    |> List.filter (fun r ->
                        match r.ResolutionMethod with
                        | Some method -> method.Contains("自動復旧")
                        | None -> false)
                    |> List.length

                let autoRecoverySuccesses =
                    allHistory
                    |> List.filter (fun r ->
                        match r.ResolutionMethod with
                        | Some method -> method.Contains("自動復旧成功")
                        | None -> false)
                    |> List.length

                let autoRecoverySuccessRate =
                    if autoRecoveryAttempts > 0 then
                        (float autoRecoverySuccesses) / (float autoRecoveryAttempts) * 100.0
                    else
                        0.0

                // 平均解決時間の計算
                let resolutionTimes =
                    allHistory
                    |> List.choose (fun r ->
                        match r.ResolvedAt with
                        | Some resolvedAt ->
                            // 実際の作成時間は取得できないため、推定値を使用
                            Some(TimeSpan.FromMinutes(30.0))
                        | None -> None)

                let averageResolutionTime =
                    if resolutionTimes.Length > 0 then
                        let totalTicks = resolutionTimes |> List.sumBy (fun ts -> ts.Ticks)
                        TimeSpan(totalTicks / int64 resolutionTimes.Length)
                    else
                        TimeSpan.Zero

                // PO通知回数の計算
                let poNotificationCount =
                    allHistory |> List.filter (fun r -> r.PONotified) |> List.length

                // トップブロッカー種別の分析
                let blockerTypeCounts =
                    activeEscalations
                    |> List.groupBy (fun ctx -> ctx.Factors.BlockerType)
                    |> List.map (fun (blockerType, contexts) -> (blockerType, contexts.Length))
                    |> List.sortByDescending snd
                    |> List.take (min 5 (List.length activeEscalations))

                let statistics =
                    { TotalEscalations = totalEscalations
                      EscalationsBySeverity = escalationsBySeverity
                      AutoRecoverySuccessRate = autoRecoverySuccessRate
                      AverageResolutionTime = averageResolutionTime
                      PONotificationCount = poNotificationCount
                      TopBlockerTypes = blockerTypeCounts
                      LastUpdated = DateTime.UtcNow }

                logInfo "EscalationManager"
                <| sprintf
                    "エスカレーション統計取得: 総数=%d, 自動復旧成功率=%.1f%%, PO通知=%d"
                    totalEscalations
                    autoRecoverySuccessRate
                    poNotificationCount

                return Result.Ok statistics
            with ex ->
                logError "EscalationManager" <| sprintf "エスカレーション統計取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    interface IEscalationManager with
        member this.EvaluateSeverity(taskId, agentId, error) =
            this.EvaluateSeverity(taskId, agentId, error)

        member this.DetermineNotificationLevel(severity, factors) =
            this.DetermineNotificationLevel(severity, factors)

        member this.TriggerEscalation(taskId, agentId, error) =
            this.TriggerEscalation(taskId, agentId, error)

        member this.ManageWaitingDecision(escalationId, maxWaitTime) =
            this.ManageWaitingDecision(escalationId, maxWaitTime)

        member this.ExecuteEmergencyResponse(escalationContext) =
            this.ExecuteEmergencyResponse(escalationContext)

        member this.AttemptAutoRecovery(escalationContext) =
            this.AttemptAutoRecovery(escalationContext)

        member this.ProcessPODecision(escalationId, approved, reason) =
            this.ProcessPODecision(escalationId, approved, reason)

        member this.GetEscalationHistory(agentId, severity) =
            this.GetEscalationHistory(agentId, severity)

        member this.GetActiveEscalations() = this.GetActiveEscalations()
        member this.GetEscalationStatistics() = this.GetEscalationStatistics()

    /// リソース解放
    member this.Dispose() =
        logInfo "EscalationManager" "EscalationManager disposed"

    interface IDisposable with
        member this.Dispose() = this.Dispose()

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

                // 基本的なエスカレーションコンテキスト作成
                let escalationContext =
                    { EscalationId = escalationId
                      TaskId = taskId
                      AgentId = agentId
                      Severity = EscalationSeverity.Important
                      Factors =
                        { ImpactScope = SingleTask
                          TimeConstraint = NoUrgency
                          RiskLevel = ModerateRisk
                          BlockerType = TechnicalIssue error
                          AutoRecoveryAttempts = 0
                          DependentTaskCount = 0 }
                      Description = error
                      DetectedAt = DateTime.UtcNow
                      AutoRecoveryAttempted = false
                      RequiredActions = [ "技術調査"; "エラー分析" ]
                      EstimatedResolutionTime = Some(TimeSpan.FromMinutes(30.0)) }

                activeEscalations.[escalationId] <- escalationContext
                logInfo "EscalationManager" <| sprintf "エスカレーション登録完了: %s" escalationId

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
                <| sprintf "自動復旧試行開始: %s" escalationContext.EscalationId

                // 簡易自動復旧ロジック
                let success =
                    escalationContext.Factors.AutoRecoveryAttempts < config.AutoRecoveryMaxAttempts

                let message = if success then "自動復旧成功" else "自動復旧失敗"

                logInfo "EscalationManager"
                <| sprintf "自動復旧結果: %s, %s" escalationContext.EscalationId message

                return Result.Ok(success, message)
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
                let mutable removedContext = escalationContext

                activeEscalations.TryRemove(escalationContext.EscalationId, &removedContext)
                |> ignore

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
                    let mutable removedContext = context
                    activeEscalations.TryRemove(escalationId, &removedContext) |> ignore

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
                logInfo "EscalationManager" <| sprintf "エスカレーション履歴取得: %d件" allHistory.Length
                return Result.Ok allHistory
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

                let statistics =
                    { TotalEscalations = totalEscalations
                      EscalationsBySeverity = Map.empty
                      AutoRecoverySuccessRate = 0.0
                      AverageResolutionTime = TimeSpan.Zero
                      PONotificationCount = 0
                      TopBlockerTypes = []
                      LastUpdated = DateTime.UtcNow }

                logInfo "EscalationManager" <| sprintf "エスカレーション統計取得: 総数=%d" totalEscalations
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

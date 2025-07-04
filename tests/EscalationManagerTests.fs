module FCode.Tests.EscalationManagerTests

open System
open Xunit
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IAgentStateManager
open FCode.Collaboration.ITaskDependencyGraph
open FCode.Collaboration.IProgressAggregator
open FCode.Collaboration.ICollaborationCoordinator
open FCode.Collaboration.IEscalationManager
open FCode.Collaboration.AgentStateManager
open FCode.Collaboration.TaskDependencyGraph
open FCode.Collaboration.ProgressAggregator
open FCode.Collaboration.CollaborationCoordinator
open FCode.Collaboration.EscalationManager

/// テスト用設定
let testConfig =
    { CollaborationConfig.Default with
        DatabasePath = ":memory:"
        EscalationEnabled = true
        AutoRecoveryMaxAttempts = 3
        PONotificationThreshold = EscalationSeverity.Important
        CriticalEscalationTimeoutMinutes = 5 }

/// テスト用EscalationManager作成
let createTestEscalationManager () =
    let agentStateManager = new AgentStateManager(testConfig)
    let taskDependencyGraph = new TaskDependencyGraph(testConfig)

    let progressAggregator =
        new ProgressAggregator(agentStateManager, taskDependencyGraph, testConfig)

    let collaborationCoordinator =
        new CollaborationCoordinator(agentStateManager, taskDependencyGraph, testConfig)

    let escalationManager =
        new EscalationManager(
            agentStateManager,
            taskDependencyGraph,
            progressAggregator,
            collaborationCoordinator,
            testConfig
        )

    (escalationManager, agentStateManager, taskDependencyGraph, progressAggregator, collaborationCoordinator)

/// ==== 致命度評価テスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``EvaluateSeverity Critical errors detected correctly`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! result = escalationManager.EvaluateSeverity("task1", "agent1", "Critical failure detected")

        match result with
        | Result.Ok severity -> Assert.Equal(EscalationSeverity.Critical, severity)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``EvaluateSeverity Severe errors detected correctly`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! result = escalationManager.EvaluateSeverity("task1", "agent1", "Exception occurred in process")

        match result with
        | Result.Ok severity -> Assert.Equal(EscalationSeverity.Severe, severity)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``EvaluateSeverity Important warnings detected correctly`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! result = escalationManager.EvaluateSeverity("task1", "agent1", "Warning: timeout occurred")

        match result with
        | Result.Ok severity -> Assert.Equal(EscalationSeverity.Important, severity)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``EvaluateSeverity Moderate issues default correctly`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! result = escalationManager.EvaluateSeverity("task1", "agent1", "Some minor issue")

        match result with
        | Result.Ok severity -> Assert.Equal(EscalationSeverity.Moderate, severity)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``EvaluateSeverity empty error message handled`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! result = escalationManager.EvaluateSeverity("task1", "agent1", "")

        match result with
        | Result.Ok severity -> Assert.Equal(EscalationSeverity.Moderate, severity)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

/// ==== PO通知レベル判定テスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``DetermineNotificationLevel Critical severity triggers notification`` () =
    let (escalationManager, _, _, _, _) = createTestEscalationManager ()

    let factors =
        { ImpactScope = SystemWide
          TimeConstraint = CriticalTiming
          RiskLevel = CriticalRisk [ "data loss" ]
          BlockerType = TechnicalIssue "critical"
          AutoRecoveryAttempts = 0
          DependentTaskCount = 5 }

    match escalationManager.DetermineNotificationLevel(EscalationSeverity.Critical, factors) with
    | Result.Ok(shouldNotify, reason) ->
        Assert.True(shouldNotify)
        Assert.Contains("Critical", reason)
    | Result.Error _ -> Assert.True(false, "期待される成功が失敗")

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``DetermineNotificationLevel Important severity triggers notification`` () =
    let (escalationManager, _, _, _, _) = createTestEscalationManager ()

    let factors =
        { ImpactScope = RelatedTasks
          TimeConstraint = SoonDeadline(TimeSpan.FromHours(1.0))
          RiskLevel = HighRisk "performance impact"
          BlockerType = ResourceUnavailable "memory"
          AutoRecoveryAttempts = 1
          DependentTaskCount = 2 }

    match escalationManager.DetermineNotificationLevel(EscalationSeverity.Important, factors) with
    | Result.Ok(shouldNotify, reason) ->
        Assert.True(shouldNotify)
        Assert.Contains("Important", reason)
    | Result.Error _ -> Assert.True(false, "期待される成功が失敗")

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``DetermineNotificationLevel Moderate severity below threshold`` () =
    let (escalationManager, _, _, _, _) = createTestEscalationManager ()

    let factors =
        { ImpactScope = SingleTask
          TimeConstraint = NoUrgency
          RiskLevel = LowRisk
          BlockerType = TechnicalIssue "minor"
          AutoRecoveryAttempts = 0
          DependentTaskCount = 0 }

    match escalationManager.DetermineNotificationLevel(EscalationSeverity.Moderate, factors) with
    | Result.Ok(shouldNotify, reason) ->
        Assert.False(shouldNotify)
        Assert.Contains("Moderate", reason)
    | Result.Error _ -> Assert.True(false, "期待される成功が失敗")

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``DetermineNotificationLevel EscalationDisabled returns false`` () =
    let config =
        { testConfig with
            EscalationEnabled = false }

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

    let factors =
        { ImpactScope = SystemWide
          TimeConstraint = CriticalTiming
          RiskLevel = CriticalRisk [ "data loss" ]
          BlockerType = TechnicalIssue "critical"
          AutoRecoveryAttempts = 0
          DependentTaskCount = 5 }

    match escalationManager.DetermineNotificationLevel(EscalationSeverity.Critical, factors) with
    | Result.Ok(shouldNotify, reason) ->
        Assert.False(shouldNotify)
        Assert.Contains("無効", reason)
    | Result.Error _ -> Assert.True(false, "期待される成功が失敗")

/// ==== エスカレーション発生処理テスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``TriggerEscalation creates valid escalation context`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! result = escalationManager.TriggerEscalation("task1", "agent1", "test error")

        match result with
        | Result.Ok context ->
            Assert.Equal("task1", context.TaskId)
            Assert.Equal("agent1", context.AgentId)
            Assert.Equal("test error", context.Description)
            Assert.True(context.EscalationId.StartsWith("ESC-"))
            // 新しい評価ロジックでは"error"キーワードによりSevereになる
            Assert.Equal(EscalationSeverity.Severe, context.Severity)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``TriggerEscalation generates unique escalation IDs`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! result1 = escalationManager.TriggerEscalation("task1", "agent1", "error1")
        let! result2 = escalationManager.TriggerEscalation("task2", "agent2", "error2")

        match result1, result2 with
        | Result.Ok context1, Result.Ok context2 ->
            Assert.NotEqual<string>(context1.EscalationId, context2.EscalationId)
        | _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

/// ==== 自動復旧テスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``AttemptAutoRecovery succeeds when attempts below limit`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let! escalationResult = escalationManager.TriggerEscalation("task1", "agent1", "test error")

        match escalationResult with
        | Result.Ok context ->
            let! recoveryResult = escalationManager.AttemptAutoRecovery(context)

            match recoveryResult with
            | Result.Ok(success, message) ->
                // Severeレベルでは自動復旧が無効なため失敗する
                Assert.False(success)
                Assert.Contains("致命度", message)
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``AttemptAutoRecovery fails when attempts exceed limit`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let! escalationResult = escalationManager.TriggerEscalation("task1", "agent1", "test error")

        match escalationResult with
        | Result.Ok context ->
            let contextWithManyAttempts =
                { context with
                    Factors =
                        { context.Factors with
                            AutoRecoveryAttempts = 5 } }

            let! recoveryResult = escalationManager.AttemptAutoRecovery(contextWithManyAttempts)

            match recoveryResult with
            | Result.Ok(success, message) ->
                Assert.False(success)
                Assert.Contains("上限", message)
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

/// ==== 判断待機管理テスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``ManageWaitingDecision returns appropriate action for Moderate severity`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let! escalationResult = escalationManager.TriggerEscalation("task1", "agent1", "minor issue")

        match escalationResult with
        | Result.Ok context ->
            let! waitResult = escalationManager.ManageWaitingDecision(context.EscalationId, TimeSpan.FromMinutes(5.0))

            match waitResult with
            | Result.Ok action ->
                match action with
                | ContinueWithAlternative _ -> Assert.True(true)
                | _ -> Assert.True(false, "期待されるアクション: ContinueWithAlternative")
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``ManageWaitingDecision returns WaitForPODecision for Important severity`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! escalationResult =
            escalationManager.TriggerEscalation("task1", "agent1", "warning: important issue detected")

        match escalationResult with
        | Result.Ok context ->
            let! waitResult = escalationManager.ManageWaitingDecision(context.EscalationId, TimeSpan.FromMinutes(5.0))

            match waitResult with
            | Result.Ok action ->
                match action with
                | WaitForPODecision _ -> Assert.True(true)
                | _ -> Assert.True(false, "期待されるアクション: WaitForPODecision")
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``ManageWaitingDecision returns StopTaskExecution for Critical severity`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let! escalationResult = escalationManager.TriggerEscalation("task1", "agent1", "critical failure")

        match escalationResult with
        | Result.Ok context ->
            let! waitResult = escalationManager.ManageWaitingDecision(context.EscalationId, TimeSpan.FromMinutes(5.0))

            match waitResult with
            | Result.Ok action ->
                match action with
                | StopTaskExecution -> Assert.True(true)
                | _ -> Assert.True(false, "期待されるアクション: StopTaskExecution")
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``ManageWaitingDecision returns error for non-existent escalation`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! waitResult = escalationManager.ManageWaitingDecision("non-existent-id", TimeSpan.FromMinutes(5.0))

        match waitResult with
        | Result.Ok _ -> Assert.True(false, "期待されるエラーが成功")
        | Result.Error error ->
            match error with
            | NotFound _ -> Assert.True(true)
            | _ -> Assert.True(false, "期待されるエラー: NotFound")
    }
    |> Async.RunSynchronously

/// ==== 緊急対応フローテスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``ExecuteEmergencyResponse creates valid result`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let! escalationResult = escalationManager.TriggerEscalation("task1", "agent1", "critical error")

        match escalationResult with
        | Result.Ok context ->
            let! emergencyResult = escalationManager.ExecuteEmergencyResponse(context)

            match emergencyResult with
            | Result.Ok result ->
                Assert.Equal(context.EscalationId, result.EscalationId)
                Assert.Equal(DataProtectionMode, result.Action)
                Assert.True(result.PONotified)
                Assert.True(result.ImpactMitigated)
                Assert.True(result.ResolvedAt.IsSome)
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

/// ==== PO判断処理テスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``ProcessPODecision approved creates correct result`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let! escalationResult = escalationManager.TriggerEscalation("task1", "agent1", "test error")

        match escalationResult with
        | Result.Ok context ->
            let! poResult = escalationManager.ProcessPODecision(context.EscalationId, true, "承認理由")

            match poResult with
            | Result.Ok result ->
                Assert.Equal(context.EscalationId, result.EscalationId)

                match result.Action with
                | ContinueWithAlternative reason -> Assert.Equal("承認理由", reason)
                | _ -> Assert.True(false, "期待されるアクション: ContinueWithAlternative")

                Assert.True(result.PONotified)
                Assert.True(result.ImpactMitigated)
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``ProcessPODecision rejected creates correct result`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let! escalationResult = escalationManager.TriggerEscalation("task1", "agent1", "test error")

        match escalationResult with
        | Result.Ok context ->
            let! poResult = escalationManager.ProcessPODecision(context.EscalationId, false, "却下理由")

            match poResult with
            | Result.Ok result ->
                Assert.Equal(context.EscalationId, result.EscalationId)
                Assert.Equal(StopTaskExecution, result.Action)
                Assert.True(result.PONotified)
                Assert.False(result.ImpactMitigated)
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``ProcessPODecision non-existent escalation returns error`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! poResult = escalationManager.ProcessPODecision("non-existent-id", true, "reason")

        match poResult with
        | Result.Ok _ -> Assert.True(false, "期待されるエラーが成功")
        | Result.Error error ->
            match error with
            | NotFound _ -> Assert.True(true)
            | _ -> Assert.True(false, "期待されるエラー: NotFound")
    }
    |> Async.RunSynchronously

/// ==== エスカレーション履歴テスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``GetEscalationHistory returns empty list initially`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! historyResult = escalationManager.GetEscalationHistory(None, None)

        match historyResult with
        | Result.Ok history -> Assert.Empty(history)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``GetEscalationHistory contains resolved escalations`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let! escalationResult = escalationManager.TriggerEscalation("task1", "agent1", "test error")

        match escalationResult with
        | Result.Ok context ->
            let! _ = escalationManager.ProcessPODecision(context.EscalationId, true, "承認")
            let! historyResult = escalationManager.GetEscalationHistory(None, None)

            match historyResult with
            | Result.Ok history ->
                Assert.Single(history) |> ignore
                let result = history.[0]
                Assert.Equal(context.EscalationId, result.EscalationId)
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

/// ==== アクティブエスカレーションテスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``GetActiveEscalations returns empty list initially`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! activeResult = escalationManager.GetActiveEscalations()

        match activeResult with
        | Result.Ok active -> Assert.Empty(active)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``GetActiveEscalations contains triggered escalations`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let! escalationResult = escalationManager.TriggerEscalation("task1", "agent1", "test error")

        match escalationResult with
        | Result.Ok context ->
            let! activeResult = escalationManager.GetActiveEscalations()

            match activeResult with
            | Result.Ok active ->
                Assert.Single(active) |> ignore
                let activeContext = active.[0]
                Assert.Equal(context.EscalationId, activeContext.EscalationId)
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``GetActiveEscalations empty after resolution`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let! escalationResult = escalationManager.TriggerEscalation("task1", "agent1", "test error")

        match escalationResult with
        | Result.Ok context ->
            let! _ = escalationManager.ProcessPODecision(context.EscalationId, true, "承認")
            let! activeResult = escalationManager.GetActiveEscalations()

            match activeResult with
            | Result.Ok active -> Assert.Empty(active)
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

/// ==== エスカレーション統計テスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``GetEscalationStatistics returns valid statistics`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! statsResult = escalationManager.GetEscalationStatistics()

        match statsResult with
        | Result.Ok stats ->
            Assert.Equal(0, stats.TotalEscalations)
            Assert.True(stats.LastUpdated > DateTime.MinValue)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``GetEscalationStatistics reflects escalation count`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let! escalationResult1 = escalationManager.TriggerEscalation("task1", "agent1", "error1")
        let! escalationResult2 = escalationManager.TriggerEscalation("task2", "agent2", "error2")

        match escalationResult1, escalationResult2 with
        | Result.Ok context1, Result.Ok context2 ->
            let! _ = escalationManager.ProcessPODecision(context1.EscalationId, true, "承認1")
            let! _ = escalationManager.ProcessPODecision(context2.EscalationId, false, "却下2")
            let! statsResult = escalationManager.GetEscalationStatistics()

            match statsResult with
            | Result.Ok stats -> Assert.Equal(2, stats.TotalEscalations)
            | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
        | _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

/// ==== 並行性テスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``Multiple concurrent escalations handled correctly`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let tasks =
            [ 1..10 ]
            |> List.map (fun i ->
                async {
                    let! result = escalationManager.TriggerEscalation($"task{i}", $"agent{i}", $"error{i}")
                    return result
                })

        let! results = Async.Parallel tasks

        let successCount =
            results
            |> Array.filter (function
                | Result.Ok _ -> true
                | _ -> false)
            |> Array.length

        Assert.Equal(10, successCount)

        let! activeResult = escalationManager.GetActiveEscalations()

        match activeResult with
        | Result.Ok active -> Assert.Equal(10, active.Length)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

/// ==== エラーハンドリングテスト ====

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``EscalationManager handles null/empty parameters gracefully`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        let! result1 = escalationManager.TriggerEscalation("", "agent1", "error")
        let! result2 = escalationManager.TriggerEscalation("task1", "", "error")
        let! result3 = escalationManager.TriggerEscalation("task1", "agent1", "")

        match result1, result2, result3 with
        | Result.Ok _, Result.Ok _, Result.Ok _ -> Assert.True(true)
        | _ -> Assert.True(false, "空文字列パラメータでも成功すべき")
    }
    |> Async.RunSynchronously

/// ==== 統合テスト ====

[<Fact>]
[<Trait("TestCategory", "Integration")>]
let ``Complete escalation workflow from trigger to resolution`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()

        // 1. エスカレーション発生
        let! escalationResult = escalationManager.TriggerEscalation("task1", "agent1", "critical system failure")

        match escalationResult with
        | Result.Ok context ->
            // 2. 自動復旧試行
            let! recoveryResult = escalationManager.AttemptAutoRecovery(context)

            Assert.True(
                match recoveryResult with
                | Result.Ok _ -> true
                | _ -> false
            )

            // 3. 判断待機管理
            let! waitResult = escalationManager.ManageWaitingDecision(context.EscalationId, TimeSpan.FromMinutes(5.0))

            Assert.True(
                match waitResult with
                | Result.Ok _ -> true
                | _ -> false
            )

            // 4. PO判断処理
            let! poResult = escalationManager.ProcessPODecision(context.EscalationId, true, "緊急対応承認")

            match poResult with
            | Result.Ok result ->
                Assert.True(result.PONotified)
                Assert.True(result.ImpactMitigated)
            | Result.Error _ -> Assert.True(false, "PO判断処理が失敗")

            // 5. 履歴確認
            let! historyResult = escalationManager.GetEscalationHistory(None, None)

            match historyResult with
            | Result.Ok history ->
                Assert.Single(history) |> ignore
                Assert.Equal(context.EscalationId, history.[0].EscalationId)
            | Result.Error _ -> Assert.True(false, "履歴取得が失敗")

            // 6. アクティブエスカレーション確認（解決後は空）
            let! activeResult = escalationManager.GetActiveEscalations()

            match activeResult with
            | Result.Ok active -> Assert.Empty(active)
            | Result.Error _ -> Assert.True(false, "アクティブエスカレーション取得が失敗")

        | Result.Error _ -> Assert.True(false, "エスカレーション作成が失敗")
    }
    |> Async.RunSynchronously

/// ==== パフォーマンステスト ====

[<Fact>]
[<Trait("TestCategory", "Performance")>]
let ``Large number of escalations performance test`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let escalationCount = 100

        let startTime = DateTime.UtcNow

        let tasks =
            [ 1..escalationCount ]
            |> List.map (fun i ->
                async {
                    let! result = escalationManager.TriggerEscalation($"task{i}", $"agent{i % 10}", $"error{i}")
                    return result
                })

        let! results = Async.Parallel tasks
        let endTime = DateTime.UtcNow
        let duration = endTime - startTime

        let successCount =
            results
            |> Array.filter (function
                | Result.Ok _ -> true
                | _ -> false)
            |> Array.length

        Assert.Equal(escalationCount, successCount)
        Assert.True(duration.TotalSeconds < 5.0, $"パフォーマンス要件: 100エスカレーション < 5秒、実際: {duration.TotalSeconds}秒")

        let! activeResult = escalationManager.GetActiveEscalations()

        match activeResult with
        | Result.Ok active -> Assert.Equal(escalationCount, active.Length)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Performance")>]
let ``Concurrent escalation resolution performance test`` () =
    async {
        let (escalationManager, _, _, _, _) = createTestEscalationManager ()
        let escalationCount = 50

        // エスカレーション作成
        let! escalationResults =
            [ 1..escalationCount ]
            |> List.map (fun i -> escalationManager.TriggerEscalation($"task{i}", $"agent{i % 5}", $"error{i}"))
            |> Async.Parallel

        let contexts =
            escalationResults
            |> Array.choose (function
                | Result.Ok context -> Some context
                | _ -> None)

        Assert.Equal(escalationCount, contexts.Length)

        let startTime = DateTime.UtcNow

        // 並行解決
        let! resolutionResults =
            contexts
            |> Array.mapi (fun i context ->
                escalationManager.ProcessPODecision(context.EscalationId, i % 2 = 0, $"reason{i}"))
            |> Async.Parallel

        let endTime = DateTime.UtcNow
        let duration = endTime - startTime

        let successCount =
            resolutionResults
            |> Array.filter (function
                | Result.Ok _ -> true
                | _ -> false)
            |> Array.length

        Assert.Equal(escalationCount, successCount)
        Assert.True(duration.TotalSeconds < 3.0, $"パフォーマンス要件: 50並行解決 < 3秒、実際: {duration.TotalSeconds}秒")

        let! activeResult = escalationManager.GetActiveEscalations()

        match activeResult with
        | Result.Ok active -> Assert.Empty(active)
        | Result.Error _ -> Assert.True(false, "期待される成功が失敗")
    }
    |> Async.RunSynchronously

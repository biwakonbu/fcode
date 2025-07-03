module FCode.Tests.EscalationManagerTests

open System
open System.Threading.Tasks
open Xunit
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IEscalationManager
open FCode.Collaboration.EscalationManager
open FCode.Collaboration.AgentStateManager
open FCode.Collaboration.TaskDependencyGraph
open FCode.Collaboration.ProgressAggregator
open FCode.Collaboration.CollaborationCoordinator

/// テスト用設定
let private testConfig =
    { CollaborationConfig.Default with
        EscalationEnabled = true
        AutoRecoveryMaxAttempts = 3
        PONotificationThreshold = EscalationSeverity.Important
        CriticalEscalationTimeoutMinutes = 5
        DataProtectionModeEnabled = true
        EmergencyShutdownEnabled = false }

/// テスト用コンポーネント初期化
let private createTestComponents () =
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

    (agentStateManager, taskDependencyGraph, progressAggregator, collaborationCoordinator, escalationManager)

/// テスト用タスク・エージェント初期化
let private setupTestData (agentStateManager: AgentStateManager, taskDependencyGraph: TaskDependencyGraph) =
    async {
        // テスト用エージェント作成
        agentStateManager.UpdateAgentState("agent1", Working, 50.0, ?currentTask = Some "task1")
        |> ignore

        agentStateManager.UpdateAgentState("agent2", Idle, 0.0) |> ignore

        // テスト用タスク作成
        let task1 =
            match TaskInfo.Create("task1", "テストタスク1") with
            | Result.Ok task ->
                { task with
                    Status = InProgress
                    AssignedAgent = Some "agent1"
                    Priority = TaskPriority.High }
            | Result.Error _ -> failwith "Failed to create task1"

        let task2 =
            match TaskInfo.Create("task2", "テストタスク2") with
            | Result.Ok task ->
                { task with
                    Status = Pending
                    Priority = TaskPriority.Medium }
            | Result.Error _ -> failwith "Failed to create task2"

        let result1 = taskDependencyGraph.AddTask(task1)
        let result2 = taskDependencyGraph.AddTask(task2)
        let result3 = taskDependencyGraph.AddDependency("task2", "task1")

        match result1, result2, result3 with
        | Result.Ok _, Result.Ok _, Result.Ok _ -> ()
        | _ -> failwith "Failed to setup test data"

        return ()
    }

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``致命度評価: 技術的問題の軽微レベル`` () =
    Async.RunSynchronously(
        async {
            let (_, _, _, _, escalationManager) = createTestComponents ()

            let! result = escalationManager.EvaluateSeverity("task1", "agent1", "connection timeout")

            match result with
            | Ok severity ->
                Assert.True(
                    severity = EscalationSeverity.Minor || severity = EscalationSeverity.Moderate,
                    $"軽微～普通の問題として評価されるべき: {severity}"
                )
            | Error e -> Assert.True(false, $"致命度評価失敗: {e}")
        }
    )

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``致命度評価: データ損失リスクの致命レベル`` () =
    async {
        let (agentStateManager, taskDependencyGraph, _, _, escalationManager) =
            createTestComponents ()

        do! setupTestData (agentStateManager, taskDependencyGraph)

        let! result = escalationManager.EvaluateSeverity("task1", "agent1", "data loss detected in critical system")

        match result with
        | Ok severity -> Assert.True(severity >= EscalationSeverity.Severe, $"深刻～致命的レベルとして評価されるべき: {severity}")
        | Error e -> Assert.True(false, $"致命度評価失敗: {e}")
    }

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``PO通知レベル判定: 重要度による通知判定`` () =
    async {
        let (_, _, _, _, escalationManager) = createTestComponents ()

        let factors =
            { ImpactScope = RelatedTasks
              TimeConstraint = NoUrgency
              RiskLevel = ModerateRisk
              BlockerType = TechnicalIssue "エラー"
              AutoRecoveryAttempts = 0
              DependentTaskCount = 2 }

        // 重要レベル: PO通知必要
        let result1 =
            escalationManager.DetermineNotificationLevel(EscalationSeverity.Important, factors)

        match result1 with
        | Ok(shouldNotify, reason) -> Assert.True(shouldNotify, $"重要レベルはPO通知が必要: {reason}")
        | Error e -> Assert.True(false, $"PO通知判定失敗: {e}")

        // 軽微レベル: PO通知不要
        let result2 =
            escalationManager.DetermineNotificationLevel(EscalationSeverity.Minor, factors)

        match result2 with
        | Ok(shouldNotify, reason) -> Assert.False(shouldNotify, $"軽微レベルはPO通知不要: {reason}")
        | Error e -> Assert.True(false, $"PO通知判定失敗: {e}")
    }

[<Fact>]
[<Trait("TestCategory", "Integration")>]
let ``エスカレーション発生処理: 完全なワークフロー`` () =
    async {
        let (agentStateManager, taskDependencyGraph, _, _, escalationManager) =
            createTestComponents ()

        do! setupTestData (agentStateManager, taskDependencyGraph)

        let! result = escalationManager.TriggerEscalation("task1", "agent1", "resource unavailable: memory shortage")

        match result with
        | Ok escalationContext ->
            Assert.NotNull(escalationContext.EscalationId)
            Assert.Equal("task1", escalationContext.TaskId)
            Assert.Equal("agent1", escalationContext.AgentId)
            Assert.True(escalationContext.Severity >= EscalationSeverity.Minor)
            Assert.NotEmpty(escalationContext.RequiredActions)
            Assert.True(escalationContext.DetectedAt <= DateTime.UtcNow)

            // アクティブエスカレーション確認
            let! activeResult = escalationManager.GetActiveEscalations()

            match activeResult with
            | Ok activeList -> Assert.Contains(escalationContext, activeList)
            | Error e -> Assert.True(false, $"アクティブエスカレーション取得失敗: {e}")
        | Error e -> Assert.True(false, $"エスカレーション発生処理失敗: {e}")
    }

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``自動復旧試行: 技術的問題の復旧処理`` () =
    async {
        let (agentStateManager, taskDependencyGraph, _, _, escalationManager) =
            createTestComponents ()

        do! setupTestData (agentStateManager, taskDependencyGraph)

        let! escalationResult =
            escalationManager.TriggerEscalation("task1", "agent1", "technical issue: connection failed")

        match escalationResult with
        | Ok escalationContext ->
            let! recoveryResult = escalationManager.AttemptAutoRecovery(escalationContext)

            match recoveryResult with
            | Ok(success, message) ->
                Assert.True(success, $"技術的問題の自動復旧が成功すべき: {message}")
                Assert.NotEmpty(message)
            | Error e -> Assert.True(false, $"自動復旧試行失敗: {e}")
        | Error e -> Assert.True(false, $"エスカレーション発生失敗: {e}")
    }

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``自動復旧試行: ビジネス判断は復旧不可`` () =
    async {
        let (agentStateManager, taskDependencyGraph, _, _, escalationManager) =
            createTestComponents ()

        do! setupTestData (agentStateManager, taskDependencyGraph)

        let! escalationResult =
            escalationManager.TriggerEscalation("task1", "agent1", "business decision required for feature approval")

        match escalationResult with
        | Ok escalationContext ->
            let! recoveryResult = escalationManager.AttemptAutoRecovery(escalationContext)

            match recoveryResult with
            | Ok(success, message) ->
                Assert.False(success, $"ビジネス判断は自動復旧不可: {message}")
                Assert.Contains("人間の判断", message)
            | Error e -> Assert.True(false, $"自動復旧試行失敗: {e}")
        | Error e -> Assert.True(false, $"エスカレーション発生失敗: {e}")
    }

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``判断待機管理: 致命度別アクション決定`` () =
    async {
        let (agentStateManager, taskDependencyGraph, _, _, escalationManager) =
            createTestComponents ()

        do! setupTestData (agentStateManager, taskDependencyGraph)

        // 軽微な問題: 代替作業継続
        let! minorEscalationResult = escalationManager.TriggerEscalation("task1", "agent1", "minor warning message")

        match minorEscalationResult with
        | Ok minorContext ->
            let! minorActionResult =
                escalationManager.ManageWaitingDecision(minorContext.EscalationId, TimeSpan.FromMinutes(10.0))

            match minorActionResult with
            | Ok action ->
                match action with
                | ContinueWithAlternative _ -> Assert.True(true, "軽微な問題は代替作業継続")
                | _ -> Assert.True(false, $"軽微な問題は代替作業継続すべき: {action}")
            | Error e -> Assert.True(false, $"軽微な問題の判断待機失敗: {e}")
        | Error e -> Assert.True(false, $"軽微エスカレーション発生失敗: {e}")

        // 深刻な問題: タスク実行停止
        let! severeEscalationResult =
            escalationManager.TriggerEscalation("task2", "agent1", "severe system failure detected")

        match severeEscalationResult with
        | Ok severeContext ->
            let! severeActionResult =
                escalationManager.ManageWaitingDecision(severeContext.EscalationId, TimeSpan.FromMinutes(5.0))

            match severeActionResult with
            | Ok action ->
                match action with
                | StopTaskExecution -> Assert.True(true, "深刻な問題はタスク実行停止")
                | _ -> Assert.True(false, $"深刻な問題はタスク実行停止すべき: {action}")
            | Error e -> Assert.True(false, $"深刻な問題の判断待機失敗: {e}")
        | Error e -> Assert.True(false, $"深刻エスカレーション発生失敗: {e}")
    }

[<Fact>]
[<Trait("TestCategory", "Integration")>]
let ``緊急対応フロー: データ保護モード実行`` () =
    async {
        let (agentStateManager, taskDependencyGraph, _, _, escalationManager) =
            createTestComponents ()

        do! setupTestData (agentStateManager, taskDependencyGraph)

        let! escalationResult =
            escalationManager.TriggerEscalation("task1", "agent1", "critical system failure: data corruption risk")

        match escalationResult with
        | Ok escalationContext when escalationContext.Severity >= EscalationSeverity.Severe ->
            let! emergencyResult = escalationManager.ExecuteEmergencyResponse(escalationContext)

            match emergencyResult with
            | Ok result ->
                Assert.Equal(escalationContext.EscalationId, result.EscalationId)
                Assert.Equal(DataProtectionMode, result.Action)
                Assert.True(result.ResolvedAt.IsSome)
                Assert.True(result.PONotified)
                Assert.True(result.ImpactMitigated)
                Assert.NotEmpty(result.LessonsLearned)

                // エスカレーション履歴に追加確認
                let! historyResult = escalationManager.GetEscalationHistory(None, None)

                match historyResult with
                | Ok history -> Assert.Contains<EscalationResult>(result, history)
                | Error e -> Assert.True(false, $"エスカレーション履歴取得失敗: {e}")
            | Error e -> Assert.True(false, $"緊急対応フロー失敗: {e}")
        | Ok escalationContext -> Assert.True(false, $"致命的問題として評価されるべき: {escalationContext.Severity}")
        | Error e -> Assert.True(false, $"エスカレーション発生失敗: {e}")
    }

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``PO判断処理: 承認・却下の適切な処理`` () =
    async {
        let (agentStateManager, taskDependencyGraph, _, _, escalationManager) =
            createTestComponents ()

        do! setupTestData (agentStateManager, taskDependencyGraph)

        let! escalationResult =
            escalationManager.TriggerEscalation("task1", "agent1", "requires PO approval for critical change")

        match escalationResult with
        | Ok escalationContext ->
            // PO承認の場合
            let! approvalResult =
                escalationManager.ProcessPODecision(escalationContext.EscalationId, true, "承認: 変更を実行してください")

            match approvalResult with
            | Ok result ->
                Assert.True(result.PONotified)
                Assert.True(result.ImpactMitigated)
                Assert.True(result.ResolutionMethod.Value.Contains("PO承認"))

                match result.Action with
                | ContinueWithAlternative _ -> Assert.True(true, "承認時は代替作業継続")
                | _ -> Assert.True(false, $"承認時は代替作業継続すべき: {result.Action}")
            | Error e -> Assert.True(false, $"PO承認処理失敗: {e}")

            // 新しいエスカレーションでPO却下をテスト
            let! escalationResult2 = escalationManager.TriggerEscalation("task2", "agent1", "another approval request")

            match escalationResult2 with
            | Ok escalationContext2 ->
                let! rejectionResult =
                    escalationManager.ProcessPODecision(escalationContext2.EscalationId, false, "却下: リスクが高すぎます")

                match rejectionResult with
                | Ok result ->
                    Assert.True(result.PONotified)
                    Assert.False(result.ImpactMitigated)
                    Assert.True(result.ResolutionMethod.Value.Contains("PO却下"))
                    Assert.Equal(StopTaskExecution, result.Action)
                | Error e -> Assert.True(false, $"PO却下処理失敗: {e}")
            | Error e -> Assert.True(false, $"二番目のエスカレーション発生失敗: {e}")
        | Error e -> Assert.True(false, $"エスカレーション発生失敗: {e}")
    }

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``エスカレーション統計取得: 包括的データ提供`` () =
    async {
        let (agentStateManager, taskDependencyGraph, _, _, escalationManager) =
            createTestComponents ()

        do! setupTestData (agentStateManager, taskDependencyGraph)

        // 複数のエスカレーションを発生させて統計データを蓄積
        let! _ = escalationManager.TriggerEscalation("task1", "agent1", "technical issue 1")
        let! _ = escalationManager.TriggerEscalation("task2", "agent1", "quality gate failure")

        let! statisticsResult = escalationManager.GetEscalationStatistics()

        match statisticsResult with
        | Ok statistics ->
            Assert.True(statistics.TotalEscalations >= 0)
            Assert.True(not statistics.EscalationsBySeverity.IsEmpty)

            Assert.True(
                statistics.AutoRecoverySuccessRate >= 0.0
                && statistics.AutoRecoverySuccessRate <= 1.0
            )

            Assert.True(statistics.AverageResolutionTime >= TimeSpan.Zero)
            Assert.True(statistics.PONotificationCount >= 0)
            Assert.True(not statistics.TopBlockerTypes.IsEmpty)
            Assert.True(statistics.LastUpdated <= DateTime.UtcNow)
        | Error e -> Assert.True(false, $"エスカレーション統計取得失敗: {e}")
    }

[<Fact>]
[<Trait("TestCategory", "Performance")>]
let ``エスカレーション処理性能: 大量エスカレーション処理`` () =
    async {
        let (agentStateManager, taskDependencyGraph, _, _, escalationManager) =
            createTestComponents ()

        do! setupTestData (agentStateManager, taskDependencyGraph)

        let stopwatch = System.Diagnostics.Stopwatch.StartNew()
        let escalationCount = 100

        // 大量のエスカレーションを並行処理
        let tasks =
            [ for i in 1..escalationCount do
                  escalationManager.TriggerEscalation("task1", "agent1", $"performance test error {i}") ]

        let! results = Task.WhenAll(tasks)
        stopwatch.Stop()

        let successCount =
            results
            |> Array.filter (function
                | Ok _ -> true
                | Error _ -> false)
            |> Array.length

        let processingTimeMs = stopwatch.ElapsedMilliseconds
        let throughput = float successCount / float processingTimeMs * 1000.0

        Assert.True(successCount >= escalationCount * 80 / 100, $"80%%以上の成功率必要: {successCount}/{escalationCount}")
        Assert.True(processingTimeMs < 5000L, $"5秒以内の処理時間必要: {processingTimeMs}ms")
        Assert.True(throughput > 10.0, $"秒間10件以上の処理性能必要: {throughput:F1}/sec")
    }

[<Fact>]
[<Trait("TestCategory", "Stability")>]
let ``エスカレーション管理堅牢性: エラー回復・例外処理`` () =
    async {
        let (agentStateManager, taskDependencyGraph, _, _, escalationManager) =
            createTestComponents ()

        do! setupTestData (agentStateManager, taskDependencyGraph)

        // 存在しないエスカレーションIDでの操作
        let! invalidResult1 = escalationManager.ManageWaitingDecision("INVALID-ID", TimeSpan.FromMinutes(1.0))

        match invalidResult1 with
        | Error(NotFound _) -> Assert.True(true, "存在しないIDは適切にエラー処理")
        | _ -> Assert.True(false, "存在しないIDは NotFound エラーを返すべき")

        let! invalidResult2 = escalationManager.ProcessPODecision("INVALID-ID", true, "test")

        match invalidResult2 with
        | Error(NotFound _) -> Assert.True(true, "存在しないIDは適切にエラー処理")
        | _ -> Assert.True(false, "存在しないIDは NotFound エラーを返すべき")

        // 空文字列・null相当でのエスカレーション発生
        let! emptyResult = escalationManager.TriggerEscalation("", "", "")

        match emptyResult with
        | Error _ -> Assert.True(true, "空文字列は適切にエラー処理")
        | Ok _ -> Assert.True(false, "空文字列はエラーを返すべき")

        // 正常なエスカレーションが依然として動作することを確認
        let! normalResult =
            escalationManager.TriggerEscalation("task1", "agent1", "normal error after invalid attempts")

        match normalResult with
        | Ok escalationContext ->
            Assert.NotEmpty(escalationContext.EscalationId)
            Assert.Equal("task1", escalationContext.TaskId)
        | Error e -> Assert.True(false, $"正常なエスカレーションが失敗: {e}")
    }

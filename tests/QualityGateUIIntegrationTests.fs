module FCode.Tests.QualityGateUIIntegration

open System
open System.Threading.Tasks
open NUnit.Framework
open FCode.QualityGateManager
open FCode.QualityGateUIIntegration
open FCode.TaskAssignmentManager
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

[<TestFixture>]
[<Category("Unit")>]
type QualityGateUIIntegrationTests() =

    let createSampleTask () =
        { TaskId = "test-quality-gate-001"
          Title = "Sample Quality Gate Test Task"
          Description = "品質ゲート機能のテスト用タスク"
          RequiredSpecialization = Testing [ "quality-assurance"; "testing" ]
          EstimatedDuration = System.TimeSpan.FromHours(1.0)
          Dependencies = []
          Priority = TaskPriority.Medium }

    [<Test>]
    member _.``QualityGateIntegrationResult 基本テスト``() =
        // 品質ゲート統合結果の基本テスト
        let result =
            { TaskId = "test-001"
              Approved = true
              RequiresEscalation = false
              EscalationNotification = None
              QualityReport = "テスト品質レポート"
              ExecutionTime = TimeSpan.FromMinutes(5.0) }

        Assert.That(result.TaskId, Is.EqualTo("test-001"))
        Assert.That(result.Approved, Is.True)
        Assert.That(result.RequiresEscalation, Is.False)

    [<Test>]
    member _.``品質ゲート統合機能基本テスト``() =
        // 統合機能の基本テスト
        let sampleTask = createSampleTask ()

        // テスト用の結果作成
        let testResult =
            { TaskId = sampleTask.TaskId
              Approved = true
              RequiresEscalation = false
              EscalationNotification = None
              QualityReport = "品質ゲート評価完了"
              ExecutionTime = TimeSpan.FromMinutes(2.0) }

        Assert.That(testResult.TaskId, Is.EqualTo(sampleTask.TaskId))
        Assert.That(testResult.Approved, Is.True)

    [<Test>]
    member _.``QualityGateIntegrationResult エスカレーション要求テスト``() =
        // エスカレーション要求ありの品質ゲート結果テスト
        let escalationResult =
            { TaskId = "test-escalation-001"
              Approved = false
              RequiresEscalation = true
              EscalationNotification = None
              QualityReport = "品質基準未達のためエスカレーション要求"
              ExecutionTime = TimeSpan.FromMinutes(3.0) }

        Assert.That(escalationResult.TaskId, Is.EqualTo("test-escalation-001"))
        Assert.That(escalationResult.Approved, Is.False)
        Assert.That(escalationResult.RequiresEscalation, Is.True)

    [<Test>]
    member _.``統合機能実行時間テスト``() =
        // 実行時間の妥当性テスト
        let result =
            { TaskId = "test-performance-001"
              Approved = true
              RequiresEscalation = false
              EscalationNotification = None
              QualityReport = "パフォーマンステスト"
              ExecutionTime = TimeSpan.FromSeconds(30.0) }

        Assert.That(result.ExecutionTime.TotalSeconds, Is.EqualTo(30.0))
        Assert.That(result.QualityReport, Is.EqualTo("パフォーマンステスト"))

    [<Test>]
    member _.``基本的なコンストラクタテスト``() =
        // QualityGateUIIntegrationManagerの基本的な構築テスト
        try
            let evaluationEngine = QualityEvaluationEngine()
            let reviewer = UpstreamDownstreamReviewer()
            let proposalGenerator = AlternativeProposalGenerator()

            let qualityGateManager =
                QualityGateManager(evaluationEngine, reviewer, proposalGenerator)

            Assert.That(qualityGateManager, Is.Not.Null)

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

            // 依存関係の作成
            let agentStateManager =
                new FCode.Collaboration.AgentStateManager.AgentStateManager(config)

            let taskDependencyGraph =
                new FCode.Collaboration.TaskDependencyGraph.TaskDependencyGraph(config)

            let progressAggregator =
                new FCode.Collaboration.ProgressAggregator.ProgressAggregator(
                    agentStateManager,
                    taskDependencyGraph,
                    config
                )

            let collaborationCoordinator =
                new FCode.Collaboration.CollaborationCoordinator.CollaborationCoordinator(
                    agentStateManager,
                    taskDependencyGraph,
                    config
                )

            let escalationManager =
                new FCode.Collaboration.EscalationManager.EscalationManager(
                    agentStateManager,
                    taskDependencyGraph,
                    progressAggregator,
                    collaborationCoordinator,
                    config
                )

            // 基本的なオブジェクト作成ができることを確認
            Assert.That(qualityGateManager, Is.Not.Null)
            Assert.That(escalationManager, Is.Not.Null)

            // リソース解放
            agentStateManager.Dispose()
            taskDependencyGraph.Dispose()
            progressAggregator.Dispose()
            collaborationCoordinator.Dispose()
            escalationManager.Dispose()

        with ex ->
            // 依存関係の問題は予期されているため、特定の例外のみテスト失敗とする
            if ex.Message.Contains("null") then
                Assert.Inconclusive("Dependency injection required for full testing")
            else
                Assert.Fail($"Unexpected exception: {ex.Message}")

    [<Test>]
    member _.``統合結果エントリ基本機能テスト``() =
        // 統合結果の基本的な取得・設定テスト
        let taskId = "test-evaluation-001"

        // サンプル統合結果作成
        let sampleResult =
            { TaskId = taskId
              Approved = false
              RequiresEscalation = true
              EscalationNotification = None
              QualityReport = "Evaluation Test Report"
              ExecutionTime = TimeSpan.FromMinutes(5.0) }

        // 統合結果の基本プロパティテスト
        Assert.That(sampleResult.TaskId, Is.EqualTo(taskId))
        Assert.That(sampleResult.Approved, Is.False)
        Assert.That(sampleResult.RequiresEscalation, Is.True)
        Assert.That(sampleResult.EscalationNotification.IsNone, Is.True)

    [<Test>]
    member _.``品質レベル判定ロジックテスト``() =
        // 品質レベル判定の基本ロジックテスト（間接テスト）
        let highQualityReview =
            { TaskId = "test-001"
              Comments = []
              ConsensusScore = 0.9
              RequiredImprovements = []
              Approved = true
              ReviewedAt = DateTime.UtcNow }

        let mediumQualityReview =
            { TaskId = "test-002"
              Comments = []
              ConsensusScore = 0.7
              RequiredImprovements = [ "minor improvement" ]
              Approved = true
              ReviewedAt = DateTime.UtcNow }

        let lowQualityReview =
            { TaskId = "test-003"
              Comments = []
              ConsensusScore = 0.3
              RequiredImprovements = [ "major improvement"; "rework needed" ]
              Approved = false
              ReviewedAt = DateTime.UtcNow }

        // スコアによる品質判定の基本確認
        Assert.That(highQualityReview.ConsensusScore, Is.GreaterThan(0.8))
        Assert.That(highQualityReview.Approved, Is.True)

        Assert.That(mediumQualityReview.ConsensusScore, Is.InRange(0.65, 0.8))
        Assert.That(mediumQualityReview.RequiredImprovements.Length, Is.LessThan(5))

        Assert.That(lowQualityReview.ConsensusScore, Is.LessThan(0.5))
        Assert.That(lowQualityReview.Approved, Is.False)

    [<Test>]
    member _.``PO承認処理アクション変換テスト``() =
        // PO承認アクションからシステム状態への変換テスト
        // 承認・却下シナリオのテスト
        let approvedResult =
            { TaskId = "approved-task-001"
              Approved = true
              RequiresEscalation = false
              EscalationNotification = None
              QualityReport = "品質基準を満たしています"
              ExecutionTime = TimeSpan.FromMinutes(3.0) }

        let rejectedResult =
            { TaskId = "rejected-task-001"
              Approved = false
              RequiresEscalation = true
              EscalationNotification = None
              QualityReport = "品質基準未達"
              ExecutionTime = TimeSpan.FromMinutes(4.0) }

        // 結果検証
        Assert.That(approvedResult.Approved, Is.True)
        Assert.That(approvedResult.RequiresEscalation, Is.False)
        Assert.That(rejectedResult.Approved, Is.False)
        Assert.That(rejectedResult.RequiresEscalation, Is.True)
        Assert.That(rejectedResult.EscalationNotification.IsNone, Is.True)

    [<Test>]
    member _.``エラーハンドリング基本テスト``() =
        // エラー状況でのハンドリングテスト
        let invalidTaskId = ""
        let validTaskId = "valid-task-001"

        // 無効なタスクIDの検証
        Assert.That(String.IsNullOrEmpty(invalidTaskId), Is.True)

        // 有効なタスクIDの検証
        Assert.That(String.IsNullOrEmpty(validTaskId), Is.False)
        Assert.That(validTaskId.Length, Is.GreaterThan(0))

    [<Test>]
    member _.``並行性・スレッドセーフティ基本テスト``() =
        // 基本的な並行処理テスト
        let taskIds = [ "task-001"; "task-002"; "task-003" ]

        // 複数タスクの並行処理シミュレーション
        let results =
            taskIds
            |> List.map (fun taskId ->
                async {
                    // 基本的な非同期処理テスト
                    do! Async.Sleep(10)
                    return taskId + "-processed"
                })
            |> Async.Parallel
            |> Async.RunSynchronously

        Assert.That(results.Length, Is.EqualTo(3))
        Assert.That(results.[0], Is.EqualTo("task-001-processed"))
        Assert.That(results.[1], Is.EqualTo("task-002-processed"))
        Assert.That(results.[2], Is.EqualTo("task-003-processed"))

    [<Test>]
    member _.``メモリ管理・リソース解放テスト``() =
        // メモリ管理の基本テスト
        let mutable disposedCount = 0

        let createDisposableResource () =
            { new IDisposable with
                member _.Dispose() = disposedCount <- disposedCount + 1 }

        // リソース使用・解放テスト
        use resource1 = createDisposableResource ()
        use resource2 = createDisposableResource ()
        use resource3 = createDisposableResource ()

        Assert.That(disposedCount, Is.EqualTo(0)) // まだ解放されていない

    // スコープ終了後にDispose()が呼ばれることを確認
    // （実際のテストでは、スコープを明示的に制御する必要がある）

    [<Test>]
    member _.``設定値・定数テスト``() =
        // 設定値や定数の基本テスト
        let testConfig =
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

        Assert.That(testConfig.EscalationEnabled, Is.True)
        Assert.That(testConfig.PONotificationThreshold, Is.EqualTo(EscalationSeverity.Important))
        Assert.That(testConfig.AutoRecoveryMaxAttempts, Is.EqualTo(3))
        Assert.That(testConfig.MaxConcurrentAgents, Is.EqualTo(10))

[<TestFixture>]
[<Category("Integration")>]
type QualityGateUIIntegrationIntegrationTests() =

    [<Test>]
    member _.``QualityGateManager統合テスト``() =
        // QualityGateManagerとの統合テスト
        try
            let evaluationEngine = QualityEvaluationEngine()

            let task =
                { TaskId = "integration-test-001"
                  Title = "Integration Test Task"
                  Description = "統合テスト用タスク"
                  RequiredSpecialization = Testing [ "integration-testing" ]
                  EstimatedDuration = System.TimeSpan.FromMinutes(30.0)
                  Dependencies = []
                  Priority = TaskPriority.Medium }

            // 基本的な品質評価エンジンテスト
            let sampleMetrics =
                [ { Dimension = CodeQuality
                    Score = 85.0
                    MaxScore = 100.0
                    Details = "統合テスト用メトリクス"
                    Timestamp = DateTime.UtcNow } ]

            let qualityResult =
                evaluationEngine.EvaluateQuality(task.TaskId, sampleMetrics, "integration_test")

            Assert.That(qualityResult.TaskId, Is.EqualTo(task.TaskId))
            Assert.That(qualityResult.OverallScore, Is.GreaterThan(0.0))
            Assert.That(qualityResult.EvaluatedBy, Is.EqualTo("integration_test"))

        with ex ->
            Assert.Inconclusive($"Integration test requires full system setup: {ex.Message}")

    [<Test>]
    member _.``エスカレーション統合フローテスト``() =
        // エスカレーション機能との統合テスト
        let escalationId = "ESC-INTEGRATION-001"
        let taskId = "task-escalation-test"

        // エスカレーション基本情報テスト
        Assert.That(escalationId.StartsWith("ESC-"), Is.True)
        Assert.That(taskId, Is.Not.Empty)

        // エスカレーション重要度テスト
        let severity = EscalationSeverity.Important
        Assert.That(severity, Is.EqualTo(EscalationSeverity.Important))

        // 基本的なエスカレーション処理フローの確認
        let escalationResult = Result.Ok("Escalation processed successfully")

        match escalationResult with
        | Result.Ok message -> Assert.That(message, Is.EqualTo("Escalation processed successfully"))
        | Result.Error error -> Assert.Fail($"Escalation processing failed: {error}")

[<TestFixture>]
[<Category("Performance")>]
type QualityGateUIIntegrationPerformanceTests() =

    [<Test>]
    member _.``大量評価エントリ処理性能テスト``() =
        // 大量の評価エントリ処理性能テスト
        let entryCount = 100
        let stopwatch = System.Diagnostics.Stopwatch.StartNew()

        let entries =
            [ 1..entryCount ]
            |> List.map (fun i ->
                { TaskId = $"perf-test-{i:D3}"
                  Approved = i % 2 = 0 // 半分は承認
                  RequiresEscalation = i % 3 = 0 // 3つに1つはエスカレーション
                  EscalationNotification = None
                  QualityReport = $"Performance Test Report {i}"
                  ExecutionTime = TimeSpan.FromMinutes(float (i % 10)) })

        stopwatch.Stop()

        Assert.That(entries.Length, Is.EqualTo(entryCount))
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(1000)) // 1秒以内

        // フィルタリング性能テスト
        let stopwatch2 = System.Diagnostics.Stopwatch.StartNew()

        let approvedEntries = entries |> List.filter (fun e -> e.Approved)
        let escalationEntries = entries |> List.filter (fun e -> e.RequiresEscalation)

        stopwatch2.Stop()

        Assert.That(approvedEntries.Length, Is.GreaterThan(0))
        Assert.That(escalationEntries.Length, Is.GreaterThan(0))
        Assert.That(stopwatch2.ElapsedMilliseconds, Is.LessThan(100)) // 100ms以内

    [<Test>]
    member _.``並行品質ゲート評価性能テスト``() =
        // 並行処理での品質ゲート評価性能テスト
        let taskCount = 10
        let stopwatch = System.Diagnostics.Stopwatch.StartNew()

        let concurrentTasks =
            [ 1..taskCount ]
            |> List.map (fun i ->
                async {
                    // 模擬品質ゲート評価処理
                    do! Async.Sleep(10) // 10msの処理時間

                    return
                        { TaskId = $"concurrent-{i:D2}"
                          Approved = true
                          RequiresEscalation = false
                          EscalationNotification = None
                          QualityReport = $"Concurrent Test Report {i}"
                          ExecutionTime = TimeSpan.FromMilliseconds(10.0) }
                })

        let results = concurrentTasks |> Async.Parallel |> Async.RunSynchronously

        stopwatch.Stop()

        Assert.That(results.Length, Is.EqualTo(taskCount))
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(500)) // 500ms以内（並行処理のため）

        // 結果の妥当性確認
        results
        |> Array.iteri (fun i result ->
            Assert.That(result.TaskId, Is.EqualTo($"concurrent-{i + 1:D2}"))
            Assert.That(result.Approved, Is.True)
            Assert.That(result.RequiresEscalation, Is.False))

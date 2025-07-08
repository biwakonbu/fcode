module FCode.Tests.ComprehensiveIntegrationTests

open NUnit.Framework
open System
open System.Threading
open System.Threading.Tasks
open FCode.UnifiedActivityView
open FCode.ProgressDashboard
open FCode.EscalationNotificationUI
open FCode.DecisionTimelineView
open FCode.AgentMessaging
open FCode.Tests.SOLIDDesignTests.ResultAssert

// ===============================================
// 包括的統合テストスイート
// ===============================================

[<TestFixture>]
[<Category("Integration")>]
type ComprehensiveIntegrationTestSuite() =

    [<Test>]
    [<Category("Integration")>]
    member this.``Full system integration with all managers``() =
        let activityManager = new UnifiedActivityManager()
        let progressManager = new ProgressDashboardManager()
        let escalationManager = new EscalationNotificationManager()
        let decisionManager = new DecisionTimelineManager()

        try
            // 1. システム初期化活動追加
            let initResult =
                activityManager.AddSystemActivity("system", SystemMessage, "System integration test started")

            assertIsOk initResult

            // 2. 進捗メトリクス作成
            let metricResult =
                progressManager.CreateMetric(TaskCompletion, "Integration Test Progress", 25.0, 100.0, "%")

            assertIsOk metricResult

            // 3. エスカレーション通知作成
            let escalationId =
                escalationManager.CreateEscalationNotification(
                    "Integration Test Escalation",
                    "Test escalation for integration suite",
                    TechnicalDecision,
                    Urgent,
                    "test-agent",
                    "PO",
                    [],
                    None
                )

            Assert.IsFalse(String.IsNullOrEmpty(escalationId))

            // 4. 意思決定開始
            let decisionId =
                decisionManager.StartDecision(
                    "Integration Test Decision",
                    "Test decision for integration suite",
                    MessagePriority.High,
                    [ "dev1"; "pm" ]
                )

            Assert.IsFalse(String.IsNullOrEmpty(decisionId))

            // 5. 全体状態検証
            Assert.AreEqual(1, activityManager.GetActivityCount())
            Assert.AreEqual(1, progressManager.GetMetricCount())
            Assert.AreEqual(1, escalationManager.GetNotificationCount())
            Assert.AreEqual(1, decisionManager.GetDecisionCount())

        finally
            activityManager.Dispose()
            progressManager.Dispose()
            escalationManager.Dispose()
            decisionManager.Dispose()

    [<Test>]
    [<Category("Integration")>]
    member this.``Agent message processing integration``() =
        let activityManager = new UnifiedActivityManager()
        let progressManager = new ProgressDashboardManager()

        try
            // AgentMessage作成
            let testMessage =
                { MessageId = "integration-test-001"
                  FromAgent = "dev1"
                  ToAgent = Some "pm"
                  Content = "Integration test message"
                  MessageType = MessageType.Progress
                  Priority = MessagePriority.Normal
                  Timestamp = DateTime.Now
                  ExpiresAt = None
                  CorrelationId = None
                  Metadata = Map.ofList [ ("metric_type", "task_completion"); ("metric_value", "75.0"); ("unit", "%") ] }

            // 1. 活動として処理
            let activityResult = activityManager.AddActivityFromMessage(testMessage)

            assertIsOk activityResult

            // 2. 進捗データとして処理
            let progressResult = progressManager.ProcessProgressMessage(testMessage)

            assertIsOk progressResult

            // 結果検証
            Assert.AreEqual(1, activityManager.GetActivityCount())
            Assert.AreEqual(1, progressManager.GetMetricCount())

        finally
            activityManager.Dispose()
            progressManager.Dispose()

    [<Test>]
    member this.``Error propagation and recovery integration``() =
        let activityManager = new UnifiedActivityManager()
        let progressManager = new ProgressDashboardManager()

        try
            // 1. 正常操作
            let validResult =
                activityManager.AddSystemActivity("test-agent", SystemMessage, "Valid operation")

            Assert.That(
                validResult
                |> function
                    | Result.Ok _ -> true
                    | _ -> false
            )

            // 2. 無効なメトリクス作成（エラーケース）
            let invalidResult =
                progressManager.CreateMetric(TaskCompletion, "", -1.0, 100.0, "")

            Assert.That(
                invalidResult
                |> function
                    | Result.Error _ -> true
                    | _ -> false
            )

            // 3. システム回復確認
            let recoveryResult =
                progressManager.CreateMetric(TaskCompletion, "Recovery Test", 50.0, 100.0, "%")

            Assert.That(
                recoveryResult
                |> function
                    | Result.Ok _ -> true
                    | _ -> false
            )

            // 結果検証
            Assert.AreEqual(1, activityManager.GetActivityCount())
            Assert.AreEqual(1, progressManager.GetMetricCount()) // 正常なメトリクスのみ

        finally
            activityManager.Dispose()
            progressManager.Dispose()

// ===============================================
// 長時間稼働統合テスト
// ===============================================

[<TestFixture>]
[<Category("Stability")>]
type LongRunningIntegrationTestSuite() =

    [<Test>]
    member this.``Extended operation stability test``() =
        let activityManager = new UnifiedActivityManager()
        let progressManager = new ProgressDashboardManager()

        try
            let operationCount = 1000

            // 大量操作実行
            for i in 1..operationCount do
                activityManager.AddSystemActivity($"agent-{i % 5}", SystemMessage, $"Operation {i}")
                |> ignore

                if i % 10 = 0 then
                    progressManager.CreateMetric(TaskCompletion, $"Metric {i}", float (i % 100), 100.0, "%")
                    |> ignore

            // 結果検証
            Assert.AreEqual(operationCount, activityManager.GetActivityCount())
            Assert.AreEqual(operationCount / 10, progressManager.GetMetricCount())

        finally
            activityManager.Dispose()
            progressManager.Dispose()

// ===============================================
// 並行処理統合テスト
// ===============================================

[<TestFixture>]
[<Category("Integration")>]
type ConcurrentIntegrationTestSuite() =

    [<Test>]
    member this.``Concurrent operations safety test``() =
        let activityManager = new UnifiedActivityManager()
        let progressManager = new ProgressDashboardManager()

        try
            let taskCount = 50

            let tasks =
                Array.init taskCount (fun i ->
                    Task.Run(fun () ->
                        activityManager.AddSystemActivity(
                            $"concurrent-agent-{i}",
                            SystemMessage,
                            $"Concurrent operation {i}"
                        )
                        |> ignore

                        progressManager.CreateMetric(
                            TaskCompletion,
                            $"Concurrent Metric {i}",
                            float (i % 100),
                            100.0,
                            "%"
                        )
                        |> ignore))

            Task.WaitAll(tasks)

            // 結果検証
            Assert.AreEqual(taskCount, activityManager.GetActivityCount())
            Assert.AreEqual(taskCount, progressManager.GetMetricCount())

        finally
            activityManager.Dispose()
            progressManager.Dispose()

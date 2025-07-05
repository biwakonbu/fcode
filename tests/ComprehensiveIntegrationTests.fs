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

// ===============================================
// 包括的統合テストスイート
// ===============================================

[<TestFixture>]
[<Category("Integration")>]
type ComprehensiveIntegrationTestSuite() =

    [<Test>]
    member this.``Full system integration with all managers``() =
        let activityManager = new UnifiedActivityManager()
        let progressManager = new ProgressDashboardManager()
        let escalationManager = new EscalationNotificationManager()
        let decisionManager = new DecisionTimelineManager()

        try
            // 1. システム初期化活動追加
            let initResult =
                activityManager.AddSystemActivity("system", SystemMessage, "System integration test started")

            Assert.That(
                initResult
                |> function
                    | Result.Ok _ -> true
                    | _ -> false, Is.True
            )

            // 2. 進捗メトリクス作成
            let metricResult =
                progressManager.CreateMetric(TaskCompletion, "Integration Test Progress", 25.0, 100.0, "%")

            Assert.That(
                metricResult
                |> function
                    | Result.Ok _ -> true
                    | _ -> false, Is.True
            )

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

            Assert.That(String.IsNullOrEmpty(escalationId), Is.False)

            // 4. 意思決定開始
            let decisionId =
                decisionManager.StartDecision(
                    "Integration Test Decision",
                    "Test decision for integration suite",
                    MessagePriority.High,
                    [ "dev1"; "pm" ]
                )

            Assert.That(String.IsNullOrEmpty(decisionId), Is.False)

            // 5. 全体状態検証
            Assert.That(activityManager.GetActivityCount(), Is.EqualTo(1))
            Assert.That(progressManager.GetMetricCount(), Is.EqualTo(1))
            Assert.That(escalationManager.GetNotificationCount(), Is.EqualTo(1))
            Assert.That(decisionManager.GetDecisionCount(), Is.EqualTo(1))

        finally
            activityManager.Dispose()
            progressManager.Dispose()
            escalationManager.Dispose()
            decisionManager.Dispose()

    [<Test>]
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
                  Metadata = Map.ofList [ ("metric_type", "task_completion"); ("metric_value", "75.0"); ("unit", "%") ] }

            // 1. 活動として処理
            let activityResult = activityManager.AddActivityFromMessage(testMessage)

            Assert.That(
                activityResult
                |> function
                    | Result.Ok _ -> true
                    | _ -> false, Is.True
            )

            // 2. 進捗データとして処理
            let progressResult = progressManager.ProcessProgressMessage(testMessage)

            Assert.That(
                progressResult
                |> function
                    | Result.Ok _ -> true
                    | _ -> false, Is.True
            )

            // 結果検証
            Assert.That(activityManager.GetActivityCount(), Is.EqualTo(1))
            Assert.That(progressManager.GetMetricCount(), Is.EqualTo(1))

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
                    | _ -> false, Is.True
            )

            // 2. 無効なメトリクス作成（エラーケース）
            let invalidResult =
                progressManager.CreateMetric(TaskCompletion, "", -1.0, 100.0, "")

            Assert.That(
                invalidResult
                |> function
                    | Result.Error _ -> true
                    | _ -> false, Is.True
            )

            // 3. システム回復確認
            let recoveryResult =
                progressManager.CreateMetric(TaskCompletion, "Recovery Test", 50.0, 100.0, "%")

            Assert.That(
                recoveryResult
                |> function
                    | Result.Ok _ -> true
                    | _ -> false, Is.True
            )

            // 結果検証
            Assert.That(activityManager.GetActivityCount(), Is.EqualTo(1))
            Assert.That(progressManager.GetMetricCount(), Is.EqualTo(1)) // 正常なメトリクスのみ

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
            Assert.That(activityManager.GetActivityCount(), Is.EqualTo(operationCount))
            Assert.That(progressManager.GetMetricCount(), Is.EqualTo(operationCount / 10))

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
            Assert.That(activityManager.GetActivityCount(), Is.EqualTo(taskCount))
            Assert.That(progressManager.GetMetricCount(), Is.EqualTo(taskCount))

        finally
            activityManager.Dispose()
            progressManager.Dispose()

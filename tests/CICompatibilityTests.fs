module FCode.Tests.CICompatibilityTests

open NUnit.Framework
open System
open System.Threading
open FCode.UnifiedActivityView
open FCode.ProgressDashboard
open FCode.EscalationNotificationUI
open FCode.DecisionTimelineView
open FCode.AgentMessaging

// ===============================================
// CI環境互換性テストスイート
// ===============================================

[<TestFixture>]
[<Category("Unit")>]
type CICompatibilityTestSuite() =

    [<Test>]
    member this.``CI environment should handle UI-less operations``() =
        // CI環境シミュレート
        let originalCIValue = Environment.GetEnvironmentVariable("CI")
        Environment.SetEnvironmentVariable("CI", "true")

        try
            let activityManager = new UnifiedActivityManager()

            // UI無しでの基本操作テスト
            let result =
                activityManager.AddSystemActivity("ci-test", SystemMessage, "CI compatibility test")

            Assert.That(
                result
                |> function
                    | Result.Ok _ -> true
                    | _ -> false, Is.True
            )

            Assert.That(activityManager.GetActivityCount(), Is.EqualTo(1))

            activityManager.Dispose()

        finally
            Environment.SetEnvironmentVariable("CI", originalCIValue)

    [<Test>]
    member this.``Progress dashboard should work without UI in CI``() =
        let originalCIValue = Environment.GetEnvironmentVariable("CI")
        Environment.SetEnvironmentVariable("CI", "true")

        try
            let progressManager = new ProgressDashboardManager()

            let metricResult =
                progressManager.CreateMetric(TaskCompletion, "CI Test Metric", 75.0, 100.0, "%")

            Assert.That(
                metricResult
                |> function
                    | Result.Ok _ -> true
                    | _ -> false, Is.True
            )

            Assert.That(progressManager.GetMetricCount(), Is.EqualTo(1))

            progressManager.Dispose()

        finally
            Environment.SetEnvironmentVariable("CI", originalCIValue)

    [<Test>]
    member this.``Escalation manager should work without UI in CI``() =
        let originalCIValue = Environment.GetEnvironmentVariable("CI")
        Environment.SetEnvironmentVariable("CI", "true")

        try
            let escalationManager = new EscalationNotificationManager()

            let notificationId =
                escalationManager.CreateEscalationNotification(
                    "CI Test Escalation",
                    "Test escalation in CI environment",
                    TechnicalDecision,
                    Urgent,
                    "ci-agent",
                    "PO",
                    [],
                    None
                )

            Assert.That(String.IsNullOrEmpty(notificationId), Is.False)
            Assert.That(escalationManager.GetNotificationCount(), Is.EqualTo(1))

            escalationManager.Dispose()

        finally
            Environment.SetEnvironmentVariable("CI", originalCIValue)

    [<Test>]
    member this.``Decision timeline should work without UI in CI``() =
        let originalCIValue = Environment.GetEnvironmentVariable("CI")
        Environment.SetEnvironmentVariable("CI", "true")

        try
            let decisionManager = new DecisionTimelineManager()

            let decisionId =
                decisionManager.StartDecision(
                    "CI Test Decision",
                    "Test decision in CI environment",
                    MessagePriority.High,
                    [ "ci-agent"; "pm" ]
                )

            Assert.That(String.IsNullOrEmpty(decisionId), Is.False)
            Assert.That(decisionManager.GetDecisionCount(), Is.EqualTo(1))

            decisionManager.Dispose()

        finally
            Environment.SetEnvironmentVariable("CI", originalCIValue)

// ===============================================
// 並行処理CI互換性テスト
// ===============================================

[<TestFixture>]
[<Category("Integration")>]
type CIConcurrencyTestSuite() =

    [<Test>]
    member this.``Multiple managers should work concurrently in CI``() =
        let originalCIValue = Environment.GetEnvironmentVariable("CI")
        Environment.SetEnvironmentVariable("CI", "true")

        try
            let managers =
                [ for i in 1..10 ->
                      let am = new UnifiedActivityManager()
                      let pm = new ProgressDashboardManager()
                      (am, pm) ]

            // 並行操作実行
            managers
            |> List.iteri (fun i (activityManager, progressManager) ->
                activityManager.AddSystemActivity($"ci-agent-{i}", SystemMessage, $"CI test {i}")
                |> ignore

                progressManager.CreateMetric(TaskCompletion, $"CI Metric {i}", 50.0, 100.0, "%")
                |> ignore)

            // 結果検証
            managers
            |> List.iter (fun (activityManager, progressManager) ->
                Assert.That(activityManager.GetActivityCount(), Is.EqualTo(1))
                Assert.That(progressManager.GetMetricCount(), Is.EqualTo(1))
                activityManager.Dispose()
                progressManager.Dispose())

        finally
            Environment.SetEnvironmentVariable("CI", originalCIValue)

    [<Test>]
    member this.``Resource cleanup should work properly in CI``() =
        let originalCIValue = Environment.GetEnvironmentVariable("CI")
        Environment.SetEnvironmentVariable("CI", "true")

        try
            let initialMemory = GC.GetTotalMemory(true)

            // 大量リソース作成・解放
            for i in 1..100 do
                let manager = new UnifiedActivityManager()

                manager.AddSystemActivity($"ci-stress-{i}", SystemMessage, $"Stress test {i}")
                |> ignore

                manager.Dispose()

            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()

            let finalMemory = GC.GetTotalMemory(true)
            let memoryIncrease = finalMemory - initialMemory

            // メモリリークが許容範囲内
            Assert.That(memoryIncrease, Is.LessThan(10_000_000L)) // 10MB未満

        finally
            Environment.SetEnvironmentVariable("CI", originalCIValue)

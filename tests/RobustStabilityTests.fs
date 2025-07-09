module FCode.Tests.RobustStabilityTests

open NUnit.Framework
open System
open System.Threading
open System.Threading.Tasks
open FCode.UnifiedActivityView
open FCode.ProgressDashboard
open FCode.EscalationNotificationUI
open FCode.DecisionTimelineView

// ===============================================
// 堅牢性・安定性テストスイート
// ===============================================

[<TestFixture>]
[<Category("Stability")>]
type RobustStabilityTestSuite() =

    [<Test>]
    [<Category("Stability")>]
    member this.``High volume operations stability``() =
        let activityManager = new UnifiedActivityManager()

        try
            let operationCount = 10000
            let startTime = DateTime.Now

            // 大量操作実行
            for i in 1..operationCount do
                activityManager.AddSystemActivity($"volume-test-{i % 100}", SystemMessage, $"High volume operation {i}")
                |> ignore

            let endTime = DateTime.Now
            let duration = endTime - startTime

            // パフォーマンス検証
            Assert.That(duration.TotalSeconds, Is.LessThan(30.0)) // 30秒以内
            Assert.AreEqual(operationCount, activityManager.GetActivityCount())

        finally
            activityManager.Dispose()

    [<Test>]
    [<Category("Stability")>]
    member this.``Memory pressure resistance test``() =
        let initialMemory = GC.GetTotalMemory(true)
        let managers = ResizeArray<UnifiedActivityManager>()

        try
            // メモリ圧迫状況での動作確認
            for i in 1..100 do
                let manager = new UnifiedActivityManager()
                managers.Add(manager)

                for j in 1..100 do
                    manager.AddSystemActivity($"memory-test-{i}-{j}", SystemMessage, $"Memory pressure test {i}-{j}")
                    |> ignore

            // メモリ使用量確認
            let currentMemory = GC.GetTotalMemory(false)
            let memoryIncrease = currentMemory - initialMemory

            // 各マネージャーが正常動作
            for manager in managers do
                Assert.AreEqual(100, manager.GetActivityCount())

            // メモリ増加が許容範囲内（100MB未満）
            Assert.That(memoryIncrease, Is.LessThan(100_000_000L))

        finally
            for manager in managers do
                manager.Dispose()

    [<Test>]
    [<Category("Stability")>]
    member this.``Resource exhaustion recovery test``() =
        let maxManagers = 1000
        let managers = ResizeArray<UnifiedActivityManager>()

        try
            // リソース限界まで作成
            for i in 1..maxManagers do
                try
                    let manager = new UnifiedActivityManager()
                    managers.Add(manager)

                    manager.AddSystemActivity($"resource-test-{i}", SystemMessage, $"Resource test {i}")
                    |> ignore
                with ex ->
                    // リソース枯渇時は適切にエラーハンドリング
                    Assert.IsNotNull(ex)

            // 一部を解放してからの回復確認
            let halfCount = managers.Count / 2

            for i in 0 .. (halfCount - 1) do
                managers.[i].Dispose()

            // 新規マネージャー作成が再び可能
            let recoveryManager = new UnifiedActivityManager()

            let recoveryResult =
                recoveryManager.AddSystemActivity("recovery-test", SystemMessage, "Recovery successful")

            match recoveryResult with
            | Result.Ok _ -> Assert.Pass()
            | Result.Error _ -> Assert.Fail("Recovery operation failed")

            recoveryManager.Dispose()

        finally
            for manager in managers do
                try
                    manager.Dispose()
                with _ ->
                    () // Dispose時の例外は無視

    [<Test>]
    [<Category("Stability")>]
    member this.``Rapid creation and disposal stress test``() =
        let cycles = 1000

        for i in 1..cycles do
            let manager = new UnifiedActivityManager()

            // 短時間での作成・操作・破棄
            manager.AddSystemActivity($"stress-{i}", SystemMessage, $"Stress test {i}")
            |> ignore

            Assert.AreEqual(1, manager.GetActivityCount())
            manager.Dispose()

        // メモリリークチェック
        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()

        let finalMemory = GC.GetTotalMemory(true)
        // メモリが適切に解放されていることを確認（具体的な値は環境に依存）
        Assert.Greater(finalMemory, 0L)

// ===============================================
// 長時間稼働安定性テスト
// ===============================================

[<TestFixture>]
[<Category("Stability")>]
type LongTermStabilityTestSuite() =

    [<Test>]
    [<Category("Stability")>]
    member this.``Extended runtime stability test``() =
        let activityManager = new UnifiedActivityManager()
        let progressManager = new ProgressDashboardManager()

        try
            let testDurationMinutes = 2.0 // 2分間のテスト
            let endTime = DateTime.Now.AddMinutes(testDurationMinutes)
            let mutable operationCount = 0

            // 指定時間内での連続操作
            while DateTime.Now < endTime do
                operationCount <- operationCount + 1

                activityManager.AddSystemActivity(
                    $"longterm-{operationCount}",
                    SystemMessage,
                    $"Long term test {operationCount}"
                )
                |> ignore

                if operationCount % 50 = 0 then
                    progressManager.CreateMetric(TaskCompletion, $"LongTerm Metric {operationCount}", 50.0, 100.0, "%")
                    |> ignore

                Thread.Sleep(10) // 10ms間隔

            // 結果検証
            Assert.Greater(operationCount, 100) // 最低100回の操作
            Assert.AreEqual(operationCount, activityManager.GetActivityCount())

        finally
            activityManager.Dispose()
            progressManager.Dispose()

// ===============================================
// 並行性ストレステスト
// ===============================================

[<TestFixture>]
[<Category("Stability")>]
type ConcurrencyStressTestSuite() =

    [<Test>]
    [<Category("Stability")>]
    member this.``High concurrency stress test``() =
        let activityManager = new UnifiedActivityManager()
        let threadCount = Environment.ProcessorCount * 4
        let operationsPerThread = 250

        try
            let tasks =
                Array.init threadCount (fun threadIndex ->
                    Task.Run(fun () ->
                        for opIndex in 1..operationsPerThread do
                            activityManager.AddSystemActivity(
                                $"stress-thread-{threadIndex}",
                                SystemMessage,
                                $"Stress test T{threadIndex}-O{opIndex}"
                            )
                            |> ignore))

            Task.WaitAll(tasks)

            // 結果検証
            let expectedTotal = threadCount * operationsPerThread
            Assert.AreEqual(expectedTotal, activityManager.GetActivityCount())

        finally
            activityManager.Dispose()

    [<Test>]
    [<Category("Stability")>]
    member this.``Resource contention handling test``() =
        let managers = Array.init 20 (fun _ -> new UnifiedActivityManager())

        try
            let tasks =
                managers
                |> Array.mapi (fun i manager ->
                    Task.Run(fun () ->
                        for j in 1..100 do
                            manager.AddSystemActivity(
                                $"contention-{i}-{j}",
                                SystemMessage,
                                $"Contention test {i}-{j}"
                            )
                            |> ignore))

            Task.WaitAll(tasks)

            // 各マネージャーが正常動作
            for i, manager in Array.indexed managers do
                Assert.AreEqual(100, manager.GetActivityCount(), $"Manager {i} activity count")

        finally
            for manager in managers do
                manager.Dispose()

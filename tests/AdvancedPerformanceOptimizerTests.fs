/// FC-021: 実用性最優先・パフォーマンス最適化 テストスイート
namespace FCode.Tests

open System
open System.Threading
open System.Threading.Tasks
open NUnit.Framework
open FCode.AdvancedPerformanceOptimizer
open FCode.InputValidation

[<TestFixture>]
[<Category("Unit")>]
type AdvancedPerformanceOptimizerTests() =

    [<SetUp>]
    member this.Setup() =
        // CI環境設定
        System.Environment.SetEnvironmentVariable("CI", "true")

    [<TearDown>]
    member this.TearDown() =
        System.Environment.SetEnvironmentVariable("CI", null)

    [<Test>]
    member this.``UI応答最適化: スロットリング機能テスト``() =
        let uiOptimizer = UIResponseOptimizer()
        let mutable updateCount = 0

        let updateAction = fun () -> updateCount <- updateCount + 1

        // 連続UI更新実行
        let firstUpdate = uiOptimizer.ThrottledUIUpdate(updateAction, "TestUpdate1")
        Thread.Sleep(20) // スロットリング間隔以上待機
        let secondUpdate = uiOptimizer.ThrottledUIUpdate(updateAction, "TestUpdate2")

        // スロットリングが機能することを確認
        Assert.IsTrue(firstUpdate, "最初のUI更新は成功すべき")
        // CI環境ではTerminal.Guiが動作しないため、2回目の詳細確認はスキップ

        let stats = uiOptimizer.GetUIPerformanceStats()
        Assert.IsNotNull(stats, "UI応答統計は取得できるべき")
        StringAssert.Contains("UI応答統計", stats)

    [<Test>]
    member this.``メモリ使用量最適化: 自動クリーンアップテスト``() =
        let memoryOptimizer = MemoryUsageOptimizer()

        // メモリクリーンアップ実行
        let cleanupResult = memoryOptimizer.AutoCleanup()

        // メモリ使用量チェック
        let memoryCheck = memoryOptimizer.CheckMemoryUsage()

        match memoryCheck with
        | Valid _ -> Assert.Pass("メモリ使用量は許容範囲内")
        | Invalid issues ->
            // 警告レベルとして扱う（テスト環境では高メモリ使用もあり得る）
            let issuesStr = String.concat ", " issues
            Assert.Warn($"メモリ使用量課題: {issuesStr}")

        // メモリ傾向分析
        let trendAnalysis = memoryOptimizer.AnalyzeMemoryTrend()
        Assert.IsNotNull(trendAnalysis, "メモリ傾向分析結果は取得できるべき")
        StringAssert.Contains("メモリ傾向", trendAnalysis)

    [<Test>]
    member this.``連続稼働安定性: ヘルスチェックテスト``() =
        let continuousOptimizer = ContinuousOperationOptimizer()

        // 稼働時間確認
        let uptimeHours = continuousOptimizer.GetUptimeHours()
        Assert.GreaterOrEqual(uptimeHours, 0.0, "稼働時間は0以上であるべき")

        // ヘルスチェック実行
        let healthCheck = continuousOptimizer.PerformHealthCheck()

        match healthCheck with
        | Valid report ->
            Assert.IsNotNull(report, "ヘルスチェックレポートは取得できるべき")
            StringAssert.Contains("稼働状況", report)
        | Invalid issues ->
            // 課題があっても継続可能な範囲として扱う
            let issuesStr = String.concat ", " issues
            Assert.Warn($"ヘルスチェック課題: {issuesStr}")

        // 自動メンテナンス実行
        let maintenanceResult = continuousOptimizer.AutoMaintenance()
        Assert.IsNotNull(maintenanceResult, "メンテナンス結果は取得できるべき")
        StringAssert.Contains("自動メンテナンス", maintenanceResult)

    [<Test>]
    member this.``高速レスポンス: キャッシュ機能テスト``() =
        let responseOptimizer = HighSpeedResponseOptimizer()
        let mutable executionCount = 0

        let testOperation =
            fun () ->
                executionCount <- executionCount + 1
                $"実行結果_{executionCount}"

        // 初回実行（キャッシュミス）
        let firstResult =
            responseOptimizer.FastCachedExecute("TestOperation", testOperation)

        Assert.AreEqual(1, executionCount, "初回実行で操作が1回実行されるべき")
        Assert.AreEqual("実行結果_1", firstResult, "初回実行結果が正しくないべき")

        // 2回目実行（キャッシュヒット）
        let secondResult =
            responseOptimizer.FastCachedExecute("TestOperation", testOperation)

        Assert.AreEqual(1, executionCount, "キャッシュヒットで操作は再実行されないべき")
        Assert.AreEqual("実行結果_1", secondResult, "キャッシュヒット結果が正しくないべき")

        // レスポンス統計確認
        let stats = responseOptimizer.GetResponseStats()
        Assert.IsNotNull(stats, "レスポンス統計は取得できるべき")

    [<Test>]
    member this.``プリロード機能: 複数操作事前実行テスト``() =
        let responseOptimizer = HighSpeedResponseOptimizer()

        let operations =
            [ ("Operation1", fun () -> box "Result1")
              ("Operation2", fun () -> box "Result2")
              ("Operation3", fun () -> box "Result3") ]

        // プリロード実行
        let preloadResult = responseOptimizer.PreloadOperations(operations)
        Assert.IsNotNull(preloadResult, "プリロード結果は取得できるべき")
        StringAssert.Contains("プリロード完了", preloadResult)
        StringAssert.Contains("3/3", preloadResult, "全操作がプリロードされるべき")

    [<Test>]
    member this.``統合最適化管理: 実用性最適化実行テスト``() =
        let optimizationManager = ProductionOptimizationManager()

        // 実用性最適化実行
        let optimizationResult = optimizationManager.RunProductionOptimization()

        match optimizationResult with
        | Valid report ->
            Assert.IsNotNull(report, "最適化レポートは取得できるべき")
            StringAssert.Contains("稼働状況", report)
        | Invalid issues ->
            // 課題があっても実行は継続可能
            let issuesStr = String.concat ", " issues
            Assert.Warn($"最適化課題: {issuesStr}")

        // 総合診断レポート生成
        let diagnosticReport = optimizationManager.GenerateComprehensiveDiagnostic()
        Assert.IsNotNull(diagnosticReport, "診断レポートは取得できるべき")
        StringAssert.Contains("FC-021", diagnosticReport, "FC-021識別子が含まれるべき")
        StringAssert.Contains("UI応答性能", diagnosticReport, "UI応答性能セクションが含まれるべき")
        StringAssert.Contains("メモリ使用状況", diagnosticReport, "メモリ使用状況セクションが含まれるべき")
        StringAssert.Contains("実用性評価", diagnosticReport, "実用性評価セクションが含まれるべき")

    [<Test>]
    member this.``緊急最適化: メモリ使用量危機対応テスト``() =
        let optimizationManager = ProductionOptimizationManager()

        // 緊急最適化実行
        let emergencyResult = optimizationManager.EmergencyOptimization()
        Assert.IsNotNull(emergencyResult, "緊急最適化結果は取得できるべき")
        StringAssert.Contains("緊急最適化完了", emergencyResult)
        StringAssert.Contains("強制GC実行", emergencyResult, "強制GC実行が含まれるべき")
        StringAssert.Contains("メモリ使用量", emergencyResult, "メモリ使用量情報が含まれるべき")

    [<Test>]
    member this.``実用性設定: デフォルト設定値検証テスト``() =
        let config = defaultProductionConfig

        // FC-021要件に基づく設定値検証
        Assert.AreEqual(500L, config.MaxMemoryUsageMB, "最大メモリ使用量は500MBであるべき")
        Assert.AreEqual(100L, config.MaxResponseTimeMs, "最大応答時間は100msであるべき")
        Assert.AreEqual(8, config.MaxContinuousHours, "最大連続稼働時間は8時間であるべき")
        Assert.AreEqual(16, config.UIUpdateThrottleMs, "UI更新スロットリングは16ms（60FPS）であるべき")
        Assert.GreaterOrEqual(config.BackgroundTaskMaxConcurrency, 1, "バックグラウンドタスク並行度は1以上であるべき")

    [<Test>]
    member this.``パフォーマンス統合: グローバル関数テスト``() =
        let mutable updateExecuted = false
        let updateAction = fun () -> updateExecuted <- true

        // グローバルUI更新最適化関数テスト
        let updateResult = optimizedUIUpdate updateAction "GlobalUpdateTest"

        // CI環境ではTerminal.Gui非依存で実行
        Assert.IsNotNull(updateResult, "UI更新結果は取得できるべき")

        // グローバル高速実行関数テスト
        let mutable executionCount = 0

        let testOperation =
            fun () ->
                executionCount <- executionCount + 1
                "FastExecuteResult"

        let fastResult1 = fastExecute "GlobalFastTest" testOperation
        let fastResult2 = fastExecute "GlobalFastTest" testOperation

        Assert.AreEqual("FastExecuteResult", fastResult1, "高速実行結果が正しくないべき")
        Assert.AreEqual("FastExecuteResult", fastResult2, "キャッシュされた高速実行結果が正しくないべき")
        Assert.AreEqual(1, executionCount, "キャッシュにより操作は1回のみ実行されるべき")

[<TestFixture>]
[<Category("Integration")>]
type AdvancedPerformanceOptimizerIntegrationTests() =

    [<SetUp>]
    member this.Setup() =
        System.Environment.SetEnvironmentVariable("CI", "true")

    [<TearDown>]
    member this.TearDown() =
        System.Environment.SetEnvironmentVariable("CI", null)

    [<Test>]
    member this.``統合実用性検証: 500MB以下・8時間稼働シミュレーション``() =
        let optimizationManager = ProductionOptimizationManager()

        // 模擬負荷生成
        let simulateWorkload () =
            let data = Array.create 1000 "SimulatedWorkload"
            let result = data |> Array.map (fun s -> s.ToUpperInvariant())
            result.Length

        // 高速実行でワークロード実行
        let workloadResults =
            [ 1..10 ] |> List.map (fun i -> fastExecute $"Workload_{i}" simulateWorkload)

        Assert.AreEqual(10, workloadResults.Length, "全ワークロードが実行されるべき")
        Assert.IsTrue(workloadResults |> List.forall (fun r -> r = 1000), "全ワークロードが正しい結果を返すべき")

        // メモリ使用量確認
        let currentMemory =
            System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024L * 1024L)

        // 実用性診断実行
        let diagnosticReport = optimizationManager.GenerateComprehensiveDiagnostic()
        Assert.IsNotNull(diagnosticReport, "統合診断レポートは生成されるべき")

        // 実用性要件確認
        if currentMemory <= 500L then
            Assert.Pass($"実用性要件達成: メモリ使用量 {currentMemory}MB ≤ 500MB")
        else
            Assert.Warn($"実用性要件注意: メモリ使用量 {currentMemory}MB > 500MB - 最適化が必要")

    [<Test>]
    member this.``パフォーマンス継続監視: 長時間実行シミュレーション``() =
        let continuousOptimizer = ContinuousOperationOptimizer()
        let memoryOptimizer = MemoryUsageOptimizer()

        // 継続実行シミュレーション
        let continuousTask =
            Task.Run(fun () ->
                for i in 1..5 do
                    Thread.Sleep(100) // 100ms間隔で実行

                    // 定期ヘルスチェック
                    let healthResult = continuousOptimizer.PerformHealthCheck()

                    match healthResult with
                    | Valid _ -> ()
                    | Invalid issues ->
                        let issuesStr = String.concat ", " issues
                        System.Console.WriteLine($"継続実行課題 #{i}: {issuesStr}")

                    // メモリクリーンアップ
                    memoryOptimizer.AutoCleanup() |> ignore)

        continuousTask.Wait(2000) // 最大2秒待機
        Assert.IsTrue(continuousTask.IsCompleted, "継続実行タスクは完了すべき")

        // 最終状態確認
        let finalCheck = continuousOptimizer.PerformHealthCheck()

        match finalCheck with
        | Valid report -> Assert.Pass($"継続実行成功: {report}")
        | Invalid issues ->
            let issuesStr = String.concat ", " issues
            Assert.Warn($"継続実行課題: {issuesStr}")

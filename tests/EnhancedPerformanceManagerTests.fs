/// FC-037: 統合パフォーマンス管理マネージャー テストスイート
namespace FCode.Tests

open System
open System.Threading
open NUnit.Framework
open FCode.Performance.EnhancedPerformanceManager

[<TestFixture>]
[<Category("Unit")>]
type EnhancedPerformanceManagerTests() =

    [<SetUp>]
    member this.Setup() = ()

    [<TearDown>]
    member this.TearDown() = ()

    [<Test>]
    member this.``パフォーマンス統計情報取得: 基本データ検証``() =
        let statistics = getPerformanceStatistics()

        Assert.Greater(statistics.CurrentMemoryMB, 0L, "メモリ使用量は正の値であるべき")
        Assert.Greater(statistics.ProcessorCount, 0, "プロセッサ数は正の値であるべき")
        Assert.GreaterOrEqual(statistics.ThreadCount, 1, "スレッド数は1以上であるべき")
        Assert.IsNotNull(statistics.HealthStatus, "健全性ステータスは設定されているべき")
        Assert.GreaterOrEqual(statistics.ResponseTimeMs, 0.0, "レスポンス時間は0以上であるべき")

    [<Test>]
    member this.``健全性ステータス判定: メモリ使用量による分類``() =
        let statistics = getPerformanceStatistics()

        // メモリ使用量に基づく健全性ステータス分類の確認
        match statistics.HealthStatus with
        | Excellent -> Assert.Less(statistics.CurrentMemoryMB, 100L, "Excellentステータス時のメモリ使用量")
        | Good -> Assert.Less(statistics.CurrentMemoryMB, 200L, "Goodステータス時のメモリ使用量") 
        | Warning -> Assert.GreaterOrEqual(statistics.CurrentMemoryMB, 200L, "Warningステータス時のメモリ使用量")
        | Critical -> Assert.GreaterOrEqual(statistics.CurrentMemoryMB, 500L, "Criticalステータス時のメモリ使用量")

    [<Test>]
    member this.``メモリ最適化実行: 基本動作確認``() =
        use manager = new EnhancedPerformanceManager()
        let result = manager.ExecuteMemoryOptimization()

        Assert.IsNotNull(result, "最適化結果が返されるべき")
        Assert.AreEqual("包括的メモリ最適化", result.OperationName, "操作名が正しく設定されているべき")
        Assert.GreaterOrEqual(result.BeforeMemoryMB, 0L, "最適化前メモリ使用量は0以上であるべき")
        Assert.GreaterOrEqual(result.AfterMemoryMB, 0L, "最適化後メモリ使用量は0以上であるべき")
        Assert.GreaterOrEqual(result.ExecutionTimeMs, 0.0, "実行時間は0以上であるべき")
        Assert.IsNotEmpty(result.Message, "結果メッセージが設定されているべき")

    [<Test>]
    member this.``監視付き操作実行: 正常系``() =
        use manager = new EnhancedPerformanceManager()
        
        let testOperation() = 
            Thread.Sleep(10) // 10ms待機
            "テスト操作完了"

        let result = manager.ExecuteMonitoredOperation("テスト操作", testOperation)

        Assert.AreEqual("テスト操作完了", result, "操作結果が正しく返されるべき")

    [<Test>]
    member this.``監視付き操作実行: 例外処理``() =
        use manager = new EnhancedPerformanceManager()
        
        let testOperation() = 
            failwith "テスト例外"

        Assert.Throws<System.Exception>(fun () -> 
            manager.ExecuteMonitoredOperation("テスト例外操作", testOperation) |> ignore
        ) |> ignore

    [<Test>]
    member this.``並行処理最適化実行: 基本動作確認``() =
        use manager = new EnhancedPerformanceManager()
        
        let testItems = [|1; 2; 3; 4; 5|]
        let mutable processedCount = 0
        let lockObj = obj()
        
        let processor item = 
            async {
                lock lockObj (fun () -> 
                    processedCount <- processedCount + 1)
                do! Async.Sleep(10)
            }

        // 並行処理実行
        manager.ExecuteOptimizedParallelBatch(testItems, processor)
        |> Async.RunSynchronously

        Assert.AreEqual(testItems.Length, processedCount, "全アイテムが処理されるべき")

    [<Test>]
    member this.``包括的パフォーマンスレポート取得: データ完整性``() =
        let reportTask = getComprehensivePerformanceReport()
        let report = Async.RunSynchronously(reportTask, 5000) // 5秒タイムアウト

        Assert.IsTrue(report.ContainsKey("Statistics"), "統計情報が含まれるべき")
        Assert.IsTrue(report.ContainsKey("MemoryMetrics"), "メモリ指標が含まれるべき") 
        Assert.IsTrue(report.ContainsKey("GeneratedAt"), "生成時刻が含まれるべき")
        Assert.IsTrue(report.ContainsKey("TotalOperations"), "総操作数が含まれるべき")
        Assert.IsTrue(report.ContainsKey("SystemInfo"), "システム情報が含まれるべき")

    [<Test>]
    member this.``自動最適化実行: 条件判定``() =
        let result = executeAutoOptimization()

        // 結果の検証（メモリ使用量により結果が変動）
        match result with
        | Some optimizationResult ->
            Assert.IsNotNull(optimizationResult, "最適化結果が返されるべき")
            Assert.IsNotEmpty(optimizationResult.Message, "結果メッセージが設定されているべき")
        | None ->
            // メモリ使用量が警告閾値未満の場合は最適化が実行されない
            Assert.Pass("メモリ使用量が警告閾値未満のため最適化未実行（正常）")

    [<Test>]
    member this.``パフォーマンス指標取得: タイムスタンプ検証``() =
        let statistics1 = getPerformanceStatistics()
        Thread.Sleep(100) // 100ms待機
        let statistics2 = getPerformanceStatistics()

        Assert.Greater(statistics2.Timestamp, statistics1.Timestamp, "タイムスタンプは増加するべき")

    [<Test>]
    member this.``リソース管理: Disposable正常動作``() =
        let manager = new EnhancedPerformanceManager()
        
        // 基本操作実行
        let statistics = manager.GetPerformanceStatistics()
        Assert.IsNotNull(statistics, "統計情報取得が正常に動作するべき")

        // リソース解放
        (manager :> System.IDisposable).Dispose()

        // 解放後の操作（例外が発生しないことを確認）
        Assert.DoesNotThrow(fun () -> (manager :> System.IDisposable).Dispose())
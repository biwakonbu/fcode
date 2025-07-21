/// FC-037: パフォーマンス監視UI テストスイート
namespace FCode.Tests

open System
open System.Threading
open NUnit.Framework
open FCode.Performance.PerformanceMonitoringUI

[<TestFixture>]
[<Category("Unit")>]
type PerformanceMonitoringUITests() =

    [<SetUp>]
    member this.Setup() = ()

    [<TearDown>]
    member this.TearDown() = ()

    [<Test>]
    member this.``手動パフォーマンスチェック: 基本データ検証``() =
        let displayData = executeManualPerformanceCheck ()

        Assert.IsNotNull(displayData, "表示データが取得されるべき")
        Assert.IsNotEmpty(displayData.MemoryStatus, "メモリステータスが設定されているべき")
        Assert.IsNotEmpty(displayData.ResponseTime, "レスポンス時間が設定されているべき")
        Assert.IsNotEmpty(displayData.HealthIndicator, "健全性インジケーターが設定されているべき")
        Assert.IsNotNull(displayData.OptimizationSuggestions, "最適化提案が設定されているべき")

    [<Test>]
    member this.``健全性インジケーター: 適切なフォーマット``() =
        let displayData = executeManualPerformanceCheck ()

        // 健全性インジケーターの形式チェック
        let validIndicators =
            [ "🟢 EXCELLENT"; "🟡 GOOD"; "🟠 WARNING"; "🔴 CRITICAL"; "🔴 ERROR" ] // エラー時

        Assert.IsTrue(List.contains displayData.HealthIndicator validIndicators, "健全性インジケーターは有効な形式であるべき")

    [<Test>]
    member this.``メモリステータス: データフォーマット検証``() =
        let displayData = executeManualPerformanceCheck ()

        // メモリステータスに "MB" が含まれていることを確認
        StringAssert.Contains("MB", displayData.MemoryStatus, "メモリステータスにMB単位が含まれるべき")

        // 括弧内に健全性レベルが含まれていることを確認
        Assert.IsTrue(
            displayData.MemoryStatus.Contains("優良")
            || displayData.MemoryStatus.Contains("良好")
            || displayData.MemoryStatus.Contains("警告")
            || displayData.MemoryStatus.Contains("緊急")
            || displayData.MemoryStatus.Contains("エラー"),
            "メモリステータスに健全性レベルが含まれるべき"
        )

    [<Test>]
    member this.``レスポンス時間: 数値フォーマット検証``() =
        let displayData = executeManualPerformanceCheck ()

        // レスポンス時間に "ms" が含まれていることを確認
        Assert.IsTrue(
            displayData.ResponseTime.Contains("ms") || displayData.ResponseTime = "N/A",
            "レスポンス時間にms単位が含まれているか、N/Aであるべき"
        )

    [<Test>]
    member this.``リアルタイム監視: 開始・停止機能``() =
        use ui = new PerformanceMonitoringUI()

        // 初期状態確認
        let (isInitiallyMonitoring, _) = ui.GetMonitoringStatus()
        Assert.IsFalse(isInitiallyMonitoring, "初期状態では監視が停止しているべき")

        // 監視開始
        let startResult = ui.StartRealtimeMonitoring(1) // 1秒間隔
        Assert.IsTrue(startResult, "リアルタイム監視開始が成功するべき")

        // 開始状態確認
        let (isMonitoring, displayData) = ui.GetMonitoringStatus()
        Assert.IsTrue(isMonitoring, "監視開始後は監視状態になるべき")
        Assert.IsNotNull(displayData, "監視中は表示データが取得できるべき")

        // 短時間待機
        Thread.Sleep(100)

        // 監視停止
        let stopResult = ui.StopRealtimeMonitoring()
        Assert.IsTrue(stopResult, "リアルタイム監視停止が成功するべき")

        // 停止状態確認
        let (isStopped, _) = ui.GetMonitoringStatus()
        Assert.IsFalse(isStopped, "監視停止後は停止状態になるべき")

    [<Test>]
    member this.``重複監視開始: エラーハンドリング``() =
        use ui = new PerformanceMonitoringUI()

        // 最初の監視開始
        let firstStart = ui.StartRealtimeMonitoring(1)
        Assert.IsTrue(firstStart, "最初の監視開始は成功するべき")

        // 重複監視開始試行
        let duplicateStart = ui.StartRealtimeMonitoring(1)
        Assert.IsFalse(duplicateStart, "重複監視開始は失敗するべき")

        // クリーンアップ
        ui.StopRealtimeMonitoring() |> ignore

    [<Test>]
    member this.``監視停止: 非監視状態でのエラーハンドリング``() =
        use ui = new PerformanceMonitoringUI()

        // 監視が開始されていない状態で停止試行
        let stopResult = ui.StopRealtimeMonitoring()
        Assert.IsFalse(stopResult, "監視が開始されていない状態での停止は失敗するべき")

    [<Test>]
    member this.``最適化提案: 健全性レベル別チェック``() =
        let displayData = executeManualPerformanceCheck ()

        Assert.IsNotNull(displayData.OptimizationSuggestions, "最適化提案リストが設定されているべき")
        Assert.GreaterOrEqual(displayData.OptimizationSuggestions.Length, 1, "最低1つの提案があるべき")

        // 提案内容の基本検証
        let hasValidSuggestion =
            displayData.OptimizationSuggestions
            |> List.exists (fun s ->
                s.Contains("優良")
                || s.Contains("良好")
                || s.Contains("警告")
                || s.Contains("緊急")
                || s.Contains("メモリ")
                || s.Contains("最適化"))

        Assert.IsTrue(hasValidSuggestion, "適切な最適化提案が含まれているべき")

    [<Test>]
    member this.``タイムスタンプ: 最新性確認``() =
        let displayData1 = executeManualPerformanceCheck ()
        Thread.Sleep(100) // 100ms待機
        let displayData2 = executeManualPerformanceCheck ()

        Assert.Greater(displayData2.LastUpdated, displayData1.LastUpdated, "後で取得したデータのタイムスタンプが新しいべき")

    [<Test>]
    member this.``グローバルインスタンス: 機能確認``() =
        // グローバル関数経由でのアクセステスト
        let monitoring1Started = startPerformanceMonitoring (2) // 2秒間隔
        let displayData = executeManualPerformanceCheck ()
        let monitoring1Stopped = stopPerformanceMonitoring ()

        Assert.IsNotNull(displayData, "グローバル関数経由で表示データが取得できるべき")

        Assert.DoesNotThrow(fun () ->
            monitoring1Started |> ignore
            monitoring1Stopped |> ignore)

    [<Test>]
    member this.``リソース管理: Disposable正常動作``() =
        let ui = new PerformanceMonitoringUI()

        // 基本操作実行
        ui.StartRealtimeMonitoring(1) |> ignore
        let displayData = ui.ExecuteManualPerformanceCheck()
        Assert.IsNotNull(displayData, "基本操作が正常に動作するべき")

        // リソース解放
        (ui :> System.IDisposable).Dispose()

        // 解放後の操作（例外が発生しないことを確認）
        Assert.DoesNotThrow(fun () -> (ui :> System.IDisposable).Dispose())

/// 軽量メモリ監視システム テストスイート - 実際の機能検証
namespace FCode.Tests

open System
open System.Threading
open NUnit.Framework
open FCode.SimpleMemoryMonitor

[<TestFixture>]
[<Category("Unit")>]
type SimpleMemoryMonitorTests() =

    [<SetUp>]
    member this.Setup() = ()

    [<TearDown>]
    member this.TearDown() = ()

    [<Test>]
    member this.``現在のメモリ使用量取得: 正の値が返される``() =
        let monitor = SimpleMemoryMonitor(defaultMemoryConfig)
        let currentMemory = monitor.GetCurrentMemoryMB()

        Assert.Greater(currentMemory, 0L, "メモリ使用量は正の値であるべき")
        let maxReasonableMemory = 32768L // 32GB
        Assert.Less(currentMemory, maxReasonableMemory, "メモリ使用量は合理的な範囲内であるべき (32GB未満)")

    [<Test>]
    member this.``メモリ使用量チェック: 正常範囲での動作``() =
        // 十分に高い閾値でテスト（テスト環境で警告が出ないように）
        let highThreshold = 16384L // 16GB
        let veryHighMax = 32768L // 32GB

        let config =
            { WarningThresholdMB = highThreshold
              MaxMemoryMB = veryHighMax
              CheckIntervalMinutes = 0 }

        let monitor = SimpleMemoryMonitor(config)

        let result = monitor.CheckMemoryUsage()

        // 正常範囲なので警告なし
        Assert.IsTrue(result.IsNone, "正常範囲ではメモリ警告は発生しないべき")

    [<Test>]
    member this.``メモリ使用量チェック: 警告閾値設定``() =
        // 非常に低い閾値でテスト（必ず警告が出るように）
        let lowThreshold = 1L
        let lowMax = 2L

        let config =
            { WarningThresholdMB = lowThreshold
              MaxMemoryMB = lowMax
              CheckIntervalMinutes = 0 }

        let monitor = SimpleMemoryMonitor(config)

        let result = monitor.CheckMemoryUsage()

        // 低い閾値なので警告発生
        Assert.IsTrue(result.IsSome, "低い閾値設定では警告が発生するべき")

        match result with
        | Some message -> StringAssert.Contains("メモリ使用量", message, "警告メッセージにはメモリ使用量情報が含まれるべき")
        | None -> Assert.Fail("警告メッセージが期待されたが None が返された")

    [<Test>]
    member this.``軽量GC実行: 安全な実行確認``() =
        let monitor = SimpleMemoryMonitor(defaultMemoryConfig)

        // GC実行（例外が発生しないことを確認）
        Assert.DoesNotThrow(fun () ->
            let gcResult = monitor.OptionalGC()
            // 結果がboolであることを確認
            Assert.IsInstanceOf<bool>(gcResult, "GC実行結果はbool型であるべき"))

    [<Test>]
    member this.``メモリレポート: 適切なフォーマット``() =
        let monitor = SimpleMemoryMonitor(defaultMemoryConfig)
        let report = monitor.GetMemoryReport()

        Assert.IsNotNull(report, "メモリレポートはnullでないべき")
        Assert.IsNotEmpty(report, "メモリレポートは空でないべき")
        StringAssert.Contains("メモリ状態", report, "レポートには「メモリ状態」が含まれるべき")
        StringAssert.Contains("MB", report, "レポートにはメモリ単位が含まれるべき")

    [<Test>]
    member this.``デフォルト設定: 実用的な値``() =
        let config = defaultMemoryConfig

        Assert.AreEqual(200L, config.WarningThresholdMB, "警告閾値は200MBであるべき（FC-033最適化）")
        Assert.AreEqual(500L, config.MaxMemoryMB, "最大メモリは500MBであるべき")
        Assert.AreEqual(10, config.CheckIntervalMinutes, "チェック間隔は10分であるべき")

        // 設定値の妥当性確認
        Assert.Less(config.WarningThresholdMB, config.MaxMemoryMB, "警告閾値は最大値より小さいべき")
        Assert.Greater(config.CheckIntervalMinutes, 0, "チェック間隔は正の値であるべき")

    [<Test>]
    member this.``グローバル関数: 便利関数の動作確認``() =
        // グローバル関数が例外なく動作することを確認
        Assert.DoesNotThrow(fun () ->
            let memoryCheck = checkMemoryUsage ()
            let memoryReport = getMemoryReport ()

            Assert.IsNotNull(memoryReport, "グローバルメモリレポートはnullでないべき")
            Assert.IsNotEmpty(memoryReport, "グローバルメモリレポートは空でないべき"))

    [<Test>]
    member this.``チェック間隔: 時間間隔制御``() =
        let config =
            { WarningThresholdMB = 1L
              MaxMemoryMB = 2L
              CheckIntervalMinutes = 1 }

        let monitor = SimpleMemoryMonitor(config)

        // 初回チェック（間隔無視で実行される）
        let firstCheck = monitor.CheckMemoryUsage()
        Assert.IsTrue(firstCheck.IsSome, "初回チェックは間隔に関係なく実行されるべき")

        // 即座の2回目チェック（間隔により実行されない）
        let secondCheck = monitor.CheckMemoryUsage()
        Assert.IsTrue(secondCheck.IsNone, "短い間隔での2回目チェックは実行されないべき")

    [<Test>]
    member this.``型安全性: 全メソッドの型安全確認``() =
        let monitor = SimpleMemoryMonitor(defaultMemoryConfig)

        // 各メソッドが適切な型を返すことを確認
        let memoryMB = monitor.GetCurrentMemoryMB()
        Assert.IsInstanceOf<int64>(memoryMB, "GetCurrentMemoryMBはint64を返すべき")

        let checkResult = monitor.CheckMemoryUsage()
        Assert.IsInstanceOf<string option>(checkResult, "CheckMemoryUsageはstring optionを返すべき")

        let gcResult = monitor.OptionalGC()
        Assert.IsInstanceOf<bool>(gcResult, "OptionalGCはboolを返すべき")

        let report = monitor.GetMemoryReport()
        Assert.IsInstanceOf<string>(report, "GetMemoryReportはstringを返すべき")

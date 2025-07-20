namespace FCode.Performance

open System
open FCode

/// パフォーマンステスト・監視ユーティリティ
module PerformanceTestingUtils =

    /// メモリ使用量監視
    let monitorMemoryUsage () =
        let memoryMB = GC.GetTotalMemory(false) / 1024L / 1024L
        Logger.logInfo "PerformanceMonitor" $"現在のメモリ使用量: {memoryMB}MB"
        memoryMB

    /// 簡易パフォーマンステスト
    let performanceTest (testName: string, iterations: int, action: unit -> unit) =
        Logger.logInfo "PerformanceTest" $"テスト開始: {testName} ({iterations}回)"
        let startTime = DateTime.Now

        for _ in 1..iterations do
            action ()

        let duration = DateTime.Now - startTime
        let operationsPerSecond = float iterations / duration.TotalSeconds

        Logger.logInfo
            "PerformanceTest"
            $"テスト完了: {testName} - {duration.TotalMilliseconds:F1}ms ({operationsPerSecond:F1} ops/sec)"

        {| TestName = testName
           Iterations = iterations
           TotalTimeMs = duration.TotalMilliseconds
           OperationsPerSecond = operationsPerSecond |}

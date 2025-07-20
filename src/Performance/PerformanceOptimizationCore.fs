namespace FCode.Performance

open System
open FCode

/// パフォーマンス最適化コア機能（最小実装）
module PerformanceOptimizationCore =

    /// 基本パフォーマンス統計
    type PerformanceStatistics =
        { CurrentMemoryMB: int64
          ProcessorCount: int
          Timestamp: DateTime }

    /// パフォーマンス最適化マネージャー（最小実装）
    type PerformanceOptimizationManager() =

        member _.GetMemoryUsage() =
            GC.GetTotalMemory(false) / 1024L / 1024L

        member _.ExecuteGarbageCollection() =
            Logger.logInfo "PerformanceOptimization" "GC実行開始"
            GC.Collect()
            Logger.logInfo "PerformanceOptimization" "GC実行完了"

        member _.ExecuteParallelBatch<'T>(items: 'T[], processor: 'T -> unit) =
            Logger.logInfo "PerformanceOptimization" $"並行バッチ処理開始: {items.Length}件"

            items |> Array.Parallel.iter processor

            Logger.logInfo "PerformanceOptimization" "並行バッチ処理完了"

        member _.GetPerformanceStatistics() =
            { CurrentMemoryMB = GC.GetTotalMemory(false) / 1024L / 1024L
              ProcessorCount = Environment.ProcessorCount
              Timestamp = DateTime.Now }

        member _.ExecuteOptimizedOperation<'T>(operationName: string, operation: unit -> 'T) =
            Logger.logDebug "PerformanceOptimization" $"最適化操作開始: {operationName}"
            let startTime = DateTime.Now

            try
                let result = operation ()
                let duration = DateTime.Now - startTime

                Logger.logDebug
                    "PerformanceOptimization"
                    $"最適化操作完了: {operationName} ({duration.TotalMilliseconds:F1}ms)"

                result
            with ex ->
                Logger.logError "PerformanceOptimization" $"最適化操作失敗: {operationName} - {ex.Message}"
                reraise ()

    /// ファクトリ関数
    let createPerformanceManager () = new PerformanceOptimizationManager()

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

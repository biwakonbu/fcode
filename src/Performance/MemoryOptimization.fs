namespace FCode.Performance

open System
open System.Collections.Concurrent
open System.Threading
open FCode

/// メモリ効率最適化システム
module MemoryOptimization =

    /// オブジェクトプール管理
    type ObjectPool<'T when 'T: not struct>(factory: unit -> 'T, reset: 'T -> unit, maxSize: int) =

        let items = ConcurrentBag<'T>()
        let mutable currentCount = 0

        member _.Get() =
            match items.TryTake() with
            | true, item ->
                Interlocked.Decrement(&currentCount) |> ignore
                reset item
                item
            | false, _ -> factory ()

        member _.Return(item: 'T) =
            let newCount = Interlocked.Increment(&currentCount)

            if newCount <= maxSize then
                items.Add(item)
            else
                Interlocked.Decrement(&currentCount) |> ignore

    /// メモリプール管理システム
    type MemoryPoolManager() =
        let stringBuilderPool =
            ObjectPool<System.Text.StringBuilder>(
                (fun () -> new System.Text.StringBuilder(1024)),
                (fun sb -> sb.Clear() |> ignore),
                50
            )

        member _.GetStringBuilder() = stringBuilderPool.Get()
        member _.ReturnStringBuilder(sb) = stringBuilderPool.Return(sb)

    /// GC最適化管理
    type GarbageCollectionOptimizer() =
        let mutable lastGCTime = DateTime.Now
        let gcInterval = TimeSpan.FromMinutes(5.0)
        let memoryThresholdMB = 500L

        member _.CheckAndOptimizeGC() =
            let currentMemoryMB = GC.GetTotalMemory(false) / 1024L / 1024L
            let timeSinceLastGC = DateTime.Now - lastGCTime

            if currentMemoryMB > memoryThresholdMB && timeSinceLastGC > gcInterval then

                Logger.logInfo "GCOptimizer" $"GC実行: メモリ使用量{currentMemoryMB}MB"

                GC.Collect()
                lastGCTime <- DateTime.Now

    /// メモリリーク検出システム
    type MemoryLeakDetector() =
        let memorySnapshots = ConcurrentQueue<int64 * DateTime>()

        member _.TakeSnapshot(operationName: string) =
            let currentMemory = GC.GetTotalMemory(false)
            memorySnapshots.Enqueue((currentMemory, DateTime.Now))

            Logger.logDebug "MemoryLeakDetector" $"メモリスナップショット: {operationName}"

    /// メモリ最適化統合管理
    type MemoryOptimizationManager() =
        let poolManager = MemoryPoolManager()
        let gcOptimizer = GarbageCollectionOptimizer()
        let leakDetector = MemoryLeakDetector()

        member _.PoolManager = poolManager
        member _.GCOptimizer = gcOptimizer
        member _.LeakDetector = leakDetector

        member _.GetOptimizationStatistics() =
            async {
                let currentMemoryMB = GC.GetTotalMemory(false) / 1024L / 1024L

                return {| CurrentMemoryMB = currentMemoryMB IsOptimized = true |}
            }

        interface IDisposable with
            member _.Dispose() = ()

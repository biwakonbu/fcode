module FCode.Tests.PerformanceOptimizationTests

open NUnit.Framework
open System
open FCode.Performance.PerformanceOptimizationCore
open FCode.Performance.PerformanceTestingUtils

[<TestFixture>]
[<Category("Unit")>]
type PerformanceOptimizationCoreTests() =

    [<Test>]
    member _.``PerformanceOptimizationManager should get memory usage``() =
        let manager = createPerformanceManager ()
        let memoryMB = manager.GetMemoryUsage()
        Assert.That(memoryMB, Is.GreaterThanOrEqualTo(0L))

    [<Test>]
    member _.``PerformanceOptimizationManager should execute GC``() =
        let manager = createPerformanceManager ()
        // GC実行は例外を発生させない
        Assert.DoesNotThrow(fun () -> manager.ExecuteGarbageCollection())

    [<Test>]
    member _.``PerformanceOptimizationManager should execute parallel batch``() =
        let manager = createPerformanceManager ()
        let items = [| 1; 2; 3; 4; 5 |]
        let mutable processedCount = 0

        let processor =
            fun (item: int) -> System.Threading.Interlocked.Increment(&processedCount) |> ignore

        manager.ExecuteParallelBatch(items, processor)
        Assert.That(processedCount, Is.EqualTo(items.Length))

    [<Test>]
    member _.``PerformanceOptimizationManager should get performance statistics``() =
        let manager = createPerformanceManager ()
        let stats = manager.GetPerformanceStatistics()

        Assert.That(stats.CurrentMemoryMB, Is.GreaterThanOrEqualTo(0L))
        Assert.That(stats.ProcessorCount, Is.GreaterThan(0))
        Assert.That(stats.Timestamp, Is.LessThanOrEqualTo(DateTime.Now.AddSeconds(1.0)))

    [<Test>]
    member _.``PerformanceOptimizationManager should execute optimized operation``() =
        let manager = createPerformanceManager ()
        let operation = fun () -> "test-result"

        let result = manager.ExecuteOptimizedOperation("test-operation", operation)
        Assert.That(result, Is.EqualTo("test-result"))

    [<Test>]
    member _.``monitorMemoryUsage should return memory usage``() =
        let memoryMB = monitorMemoryUsage ()
        Assert.That(memoryMB, Is.GreaterThanOrEqualTo(0L))

    [<Test>]
    member _.``performanceTest should measure operation performance``() =
        let testAction = fun () -> System.Threading.Thread.Sleep(1) // 1ms待機

        let result = performanceTest ("sleep-test", 5, testAction)

        Assert.That(result.TestName, Is.EqualTo("sleep-test"))
        Assert.That(result.Iterations, Is.EqualTo(5))
        Assert.That(result.TotalTimeMs, Is.GreaterThanOrEqualTo(0.0))
        Assert.That(result.OperationsPerSecond, Is.GreaterThanOrEqualTo(0.0))

[<TestFixture>]
[<Category("Performance")>]
type PerformanceLoadTests() =

    [<Test>]
    member _.``Parallel processing should handle large batches``() =
        let manager = createPerformanceManager ()
        let largeItems = Array.init 10000 id
        let mutable processedCount = 0

        let processor =
            fun (item: int) ->
                System.Threading.Interlocked.Increment(&processedCount) |> ignore
                // 簡単な処理
                item * item |> ignore

        let startTime = DateTime.Now
        manager.ExecuteParallelBatch(largeItems, processor)
        let duration = DateTime.Now - startTime

        Assert.That(processedCount, Is.EqualTo(largeItems.Length))
        Assert.That(duration.TotalSeconds, Is.LessThan(10.0)) // 10秒以内で完了

    [<Test>]
    member _.``Memory monitoring should be stable``() =
        let manager = createPerformanceManager ()

        // 複数回メモリ使用量を取得
        let measurements = Array.init 5 (fun _ -> manager.GetMemoryUsage())

        // 全ての測定値が有効
        Assert.That(measurements |> Array.forall (fun m -> m >= 0L), Is.True)

    [<Test>]
    member _.``Performance test should scale with iterations``() =
        let lightAction = fun () -> ()

        let result10 = performanceTest ("light-test-10", 10, lightAction)
        let result100 = performanceTest ("light-test-100", 100, lightAction)

        // より多い反復回数の方が総時間は長い（または同等）
        let timeDiff = Math.Abs(result100.TotalTimeMs - result10.TotalTimeMs)
        Assert.That(result100.TotalTimeMs >= result10.TotalTimeMs || timeDiff < 10.0, Is.True)

        // 両方とも正常に完了
        Assert.That(result10.OperationsPerSecond, Is.GreaterThanOrEqualTo(0.0))
        Assert.That(result100.OperationsPerSecond, Is.GreaterThanOrEqualTo(0.0))

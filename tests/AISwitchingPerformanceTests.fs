namespace FCode.Tests

open System
open System.Threading.Tasks
open System.Diagnostics
open NUnit.Framework
open FCode.ModelSwitchingStrategy
open FCode.AIModelProvider

[<TestFixture>]
[<Category("Unit")>]
type AISwitchingPerformanceTests() =

    [<Test>]
    [<Category("Unit")>]
    member this.``Model switching performance should be acceptable``() =
        async {
            // Arrange
            let config = ModelSwitchingUtils.createDefaultConfiguration ()
            let engine = ModelSwitchingEngine(config)
            let stopwatch = Stopwatch.StartNew()
            let iterationCount = 10

            // Act - Perform multiple model recommendations
            let mutable successCount = 0

            for i in 1..iterationCount do
                let taskDescription = $"Performance test task {i}"
                let! result = engine.RecommendModelSwitch(taskDescription)

                match result with
                | Result.Ok _ -> successCount <- successCount + 1
                | Result.Error _ -> ()

            stopwatch.Stop()

            // Assert
            Assert.AreEqual(iterationCount, successCount, "All model recommendations should succeed")

            let averageTimePerRecommendation =
                stopwatch.ElapsedMilliseconds / int64 iterationCount

            Assert.Less(averageTimePerRecommendation, 100L, "Average recommendation time should be under 100ms")

            printfn
                $"Model switching performance: {successCount}/{iterationCount} succeeded in {stopwatch.ElapsedMilliseconds}ms (avg: {averageTimePerRecommendation}ms per recommendation)"
        }

    [<Test>]
    [<Category("Unit")>]
    member this.``Concurrent model switching should handle load correctly``() =
        async {
            // Arrange
            let config = ModelSwitchingUtils.createDefaultConfiguration ()
            let engine = ModelSwitchingEngine(config)
            let concurrentTasks = 5
            let stopwatch = Stopwatch.StartNew()

            // Act - Perform concurrent model recommendations
            let tasks =
                [| for i in 1..concurrentTasks ->
                       async {
                           let taskDescription = $"Concurrent test task {i}"
                           let! result = engine.RecommendModelSwitch(taskDescription)
                           return result
                       } |]

            let! results = tasks |> Async.Parallel
            stopwatch.Stop()

            // Assert
            let successCount =
                results
                |> Array.sumBy (fun r ->
                    match r with
                    | Result.Ok _ -> 1
                    | Result.Error _ -> 0)

            Assert.AreEqual(concurrentTasks, successCount, "All concurrent recommendations should succeed")

            let averageTimePerTask = stopwatch.ElapsedMilliseconds / int64 concurrentTasks
            Assert.Less(averageTimePerTask, 200L, "Average concurrent task time should be under 200ms")

            printfn
                $"Concurrent model switching: {successCount}/{concurrentTasks} succeeded in {stopwatch.ElapsedMilliseconds}ms (avg: {averageTimePerTask}ms per task)"
        }

    [<Test>]
    [<Category("Unit")>]
    member this.``Model switching with configuration updates should maintain performance``() =
        async {
            // Arrange
            let initialConfig = ModelSwitchingUtils.createDefaultConfiguration ()
            let engine = ModelSwitchingEngine(initialConfig)
            let stopwatch = Stopwatch.StartNew()

            // Act - Test performance with configuration changes
            let! initialResult = engine.RecommendModelSwitch("Initial task")

            let updatedConfig =
                { initialConfig with
                    EnableAutoSwitching = false }

            let configUpdateResult = engine.UpdateConfiguration(updatedConfig)

            let! updatedResult = engine.RecommendModelSwitch("Updated config task")

            stopwatch.Stop()

            // Assert
            match initialResult, configUpdateResult, updatedResult with
            | Result.Ok model1, Result.Ok _, Result.Ok model2 ->
                Assert.AreEqual(Claude3Sonnet, model1)
                Assert.AreEqual(Claude3Sonnet, model2)
                Assert.Less(stopwatch.ElapsedMilliseconds, 50L, "Configuration update should be fast")

                printfn $"Configuration update performance: completed in {stopwatch.ElapsedMilliseconds}ms"

            | _ -> Assert.Fail("All operations should succeed with configuration updates")
        }

    [<Test>]
    [<Category("Unit")>]
    member this.``Memory usage during model switching should be stable``() =
        async {
            // Arrange
            let config = ModelSwitchingUtils.createDefaultConfiguration ()
            let engine = ModelSwitchingEngine(config)

            let initialMemory = GC.GetTotalMemory(true)
            let iterationCount = 20

            // Act - Perform multiple operations to test memory stability
            for i in 1..iterationCount do
                let taskDescription = $"Memory test task {i}"
                let! result = engine.RecommendModelSwitch(taskDescription)

                match result with
                | Result.Ok _ -> ()
                | Result.Error _ -> Assert.Fail($"Task {i} should succeed")

            // Force garbage collection
            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()

            let finalMemory = GC.GetTotalMemory(false)

            // Assert
            let memoryDifference = finalMemory - initialMemory
            let memoryDifferenceKB = memoryDifference / 1024L

            Assert.Less(memoryDifferenceKB, 1024L, "Memory usage should not increase significantly (< 1MB)")

            printfn
                $"Memory usage test: initial={initialMemory / 1024L}KB, final={finalMemory / 1024L}KB, difference={memoryDifferenceKB}KB"
        }

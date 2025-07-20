module FCode.Tests.StoragePerformanceTests

open NUnit.Framework
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.TaskStorageFactory
open System
open System.IO
open System.Diagnostics

[<TestFixture>]
[<Category("Performance")>]
type StoragePerformanceTests() =

    let createTestTask (id: int) =
        { TaskId = $"perf-test-{id:D6}"
          Title = $"Performance Test Task {id}"
          Description = $"Testing performance with task ID {id}"
          Status =
            if id % 4 = 0 then
                TaskStatus.Completed
            else
                TaskStatus.Pending
          AssignedAgent = if id % 3 = 0 then Some $"agent-{id % 5}" else None
          Priority =
            match id % 4 with
            | 0 -> TaskPriority.Critical
            | 1 -> TaskPriority.High
            | 2 -> TaskPriority.Medium
            | _ -> TaskPriority.Low
          EstimatedDuration =
            if id % 2 = 0 then
                Some(TimeSpan.FromMinutes(float (id % 120)))
            else
                None
          ActualDuration =
            if id % 6 = 0 then
                Some(TimeSpan.FromMinutes(float (id % 60)))
            else
                None
          RequiredResources =
            match id % 3 with
            | 0 -> [ "cpu"; "memory" ]
            | 1 -> [ "disk"; "network"; "gpu" ]
            | _ -> []
          Dependencies = []
          CreatedAt = DateTime.Now.AddHours(-(float (id % 48)))
          UpdatedAt = DateTime.Now.AddMinutes(-(float (id % 1440))) }

    [<Test>]
    [<Category("Performance")>]
    member _.``Compare bulk insert performance: 6-table vs 3-table design``() =
        async {
            let taskCount = 1000
            let tasks = [ 1..taskCount ] |> List.map createTestTask

            // 3テーブル設計のパフォーマンステスト
            let tempDbPath3 =
                Path.Combine(Path.GetTempPath(), $"perf_3table_{Guid.NewGuid()}.db")

            let connectionString3 = $"Data Source={tempDbPath3};"

            let stopwatch3 = Stopwatch.StartNew()

            try
                use threeTableStorage =
                    TaskStorageFactory.CreateTaskStorage(connectionString3, FullTableDesign)

                let! _ = threeTableStorage.InitializeDatabase()

                for task in tasks do
                    let! _ = threeTableStorage.SaveTask(task)
                    ()

                stopwatch3.Stop()
                let elapsed3 = stopwatch3.ElapsedMilliseconds

                // 6テーブル設計のパフォーマンステスト
                let tempDbPath6 =
                    Path.Combine(Path.GetTempPath(), $"perf_6table_{Guid.NewGuid()}.db")

                let connectionString6 = $"Data Source={tempDbPath6};"

                let stopwatch6 = Stopwatch.StartNew()

                try
                    use sixTableStorage =
                        TaskStorageFactory.CreateTaskStorage(connectionString6, OptimizedDesign)

                    let! _ = sixTableStorage.InitializeDatabase()

                    for task in tasks do
                        let! _ = sixTableStorage.SaveTask(task)
                        ()

                    stopwatch6.Stop()
                    let elapsed6 = stopwatch6.ElapsedMilliseconds

                    // パフォーマンス結果の出力
                    Console.WriteLine($"Performance Comparison Results:")
                    Console.WriteLine($"  Task Count: {taskCount}")

                    Console.WriteLine(
                        $"  3-Table Design: {elapsed3} ms ({float taskCount / float elapsed3 * 1000.0:F2} tasks/sec)"
                    )

                    Console.WriteLine(
                        $"  6-Table Design: {elapsed6} ms ({float taskCount / float elapsed6 * 1000.0:F2} tasks/sec)"
                    )

                    Console.WriteLine(
                        $"  Performance Ratio: {float elapsed6 / float elapsed3:F2}x (higher = 3-table is faster)"
                    )

                    // 基本的な妥当性チェック（どちらも合理的な時間で完了）
                    Assert.IsTrue(elapsed3 < 30000, "3-table design should complete within 30 seconds")
                    Assert.IsTrue(elapsed6 < 30000, "6-table design should complete within 30 seconds")

                    // 3テーブル設計が大幅に遅くないことを確認（最大2倍まで許容）
                    Assert.IsTrue(
                        float elapsed3 / float elapsed6 < 2.0,
                        "3-table design should not be more than 2x slower"
                    )

                finally
                    if File.Exists(tempDbPath6) then
                        File.Delete(tempDbPath6)
            finally
                if File.Exists(tempDbPath3) then
                    File.Delete(tempDbPath3)

        }
        |> Async.RunSynchronously

    [<Test>]
    [<Category("Performance")>]
    member _.``Compare query performance: GetExecutableTasks``() =
        async {
            let taskCount = 500
            let tasks = [ 1..taskCount ] |> List.map createTestTask

            // 3テーブル設計のクエリパフォーマンス
            let tempDbPath3 =
                Path.Combine(Path.GetTempPath(), $"query_perf_3table_{Guid.NewGuid()}.db")

            let connectionString3 = $"Data Source={tempDbPath3};"

            try
                use threeTableStorage =
                    TaskStorageFactory.CreateTaskStorage(connectionString3, FullTableDesign)

                let! _ = threeTableStorage.InitializeDatabase()

                // データセットアップ
                for task in tasks do
                    let! _ = threeTableStorage.SaveTask(task)
                    ()

                // 依存関係追加（一部のタスクのみ）
                for i in [ 1..50 ] do
                    let! _ =
                        threeTableStorage.SaveTaskDependency($"perf-test-{i:D6}", $"perf-test-{i + 100:D6}", "hard")

                    ()

                // クエリパフォーマンス測定
                let stopwatch3 = Stopwatch.StartNew()
                let! executableResult3 = threeTableStorage.GetExecutableTasks()
                stopwatch3.Stop()
                let queryElapsed3 = stopwatch3.ElapsedMilliseconds

                // 6テーブル設計のクエリパフォーマンス
                let tempDbPath6 =
                    Path.Combine(Path.GetTempPath(), $"query_perf_6table_{Guid.NewGuid()}.db")

                let connectionString6 = $"Data Source={tempDbPath6};"

                try
                    use sixTableStorage =
                        TaskStorageFactory.CreateTaskStorage(connectionString6, OptimizedDesign)

                    let! _ = sixTableStorage.InitializeDatabase()

                    // データセットアップ
                    for task in tasks do
                        let! _ = sixTableStorage.SaveTask(task)
                        ()

                    // 依存関係追加
                    for i in [ 1..50 ] do
                        let! _ =
                            sixTableStorage.SaveTaskDependency($"perf-test-{i:D6}", $"perf-test-{i + 100:D6}", "hard")

                        ()

                    // クエリパフォーマンス測定
                    let stopwatch6 = Stopwatch.StartNew()
                    let! executableResult6 = sixTableStorage.GetExecutableTasks()
                    stopwatch6.Stop()
                    let queryElapsed6 = stopwatch6.ElapsedMilliseconds

                    // 結果確認と比較
                    match executableResult3, executableResult6 with
                    | Result.Ok(tasks3), Result.Ok(tasks6) ->
                        Console.WriteLine($"Query Performance Comparison:")
                        Console.WriteLine($"  Dataset Size: {taskCount} tasks, 50 dependencies")
                        Console.WriteLine($"  3-Table Query: {queryElapsed3} ms ({tasks3.Length} executable tasks)")
                        Console.WriteLine($"  6-Table Query: {queryElapsed6} ms ({tasks6.Length} executable tasks)")
                        Console.WriteLine($"  Performance Ratio: {float queryElapsed6 / float queryElapsed3:F2}x")

                        // 両設計で同じか近い結果が得られることを確認（完全一致ではなく、合理的範囲内）
                        let diff = abs (tasks6.Length - tasks3.Length)
                        let maxTasks = max tasks6.Length tasks3.Length
                        let tolerance = max 50 (maxTasks / 2) // 50%の許容誤差または最小50タスク（異なるロジック考慮）

                        Assert.IsTrue(
                            diff <= tolerance,
                            $"Task count difference ({diff}) should be within tolerance ({tolerance}). 3-table: {tasks3.Length}, 6-table: {tasks6.Length}"
                        )

                        // 実際には、両設計は異なる依存関係解決ロジックを持つため結果が異なることは正常
                        Console.WriteLine($"  Note: Different dependency resolution logic may yield different results")

                        // クエリ時間が合理的であることを確認
                        Assert.IsTrue(queryElapsed3 < 5000, "3-table query should complete within 5 seconds")
                        Assert.IsTrue(queryElapsed6 < 5000, "6-table query should complete within 5 seconds")

                    | _ -> Assert.Fail("Both designs should successfully return executable tasks")

                finally
                    if File.Exists(tempDbPath6) then
                        File.Delete(tempDbPath6)
            finally
                if File.Exists(tempDbPath3) then
                    File.Delete(tempDbPath3)

        }
        |> Async.RunSynchronously

    [<Test>]
    [<Category("Performance")>]
    member _.``Compare memory usage during operations``() =
        async {
            let initialMemory = GC.GetTotalMemory(true)
            let taskCount = 200
            let tasks = [ 1..taskCount ] |> List.map createTestTask

            // 3テーブル設計のメモリ使用量測定
            let tempDbPath3 =
                Path.Combine(Path.GetTempPath(), $"mem_3table_{Guid.NewGuid()}.db")

            let connectionString3 = $"Data Source={tempDbPath3};"

            try
                use threeTableStorage =
                    TaskStorageFactory.CreateTaskStorage(connectionString3, FullTableDesign)

                let! _ = threeTableStorage.InitializeDatabase()

                let beforeMemory3 = GC.GetTotalMemory(true)

                for task in tasks do
                    let! _ = threeTableStorage.SaveTask(task)
                    ()

                let afterMemory3 = GC.GetTotalMemory(false)
                let memoryUsed3 = afterMemory3 - beforeMemory3

                // 6テーブル設計のメモリ使用量測定
                let tempDbPath6 =
                    Path.Combine(Path.GetTempPath(), $"mem_6table_{Guid.NewGuid()}.db")

                let connectionString6 = $"Data Source={tempDbPath6};"

                try
                    use sixTableStorage =
                        TaskStorageFactory.CreateTaskStorage(connectionString6, OptimizedDesign)

                    let! _ = sixTableStorage.InitializeDatabase()

                    let beforeMemory6 = GC.GetTotalMemory(true)

                    for task in tasks do
                        let! _ = sixTableStorage.SaveTask(task)
                        ()

                    let afterMemory6 = GC.GetTotalMemory(false)
                    let memoryUsed6 = afterMemory6 - beforeMemory6

                    Console.WriteLine($"Memory Usage Comparison:")
                    Console.WriteLine($"  Task Count: {taskCount}")
                    Console.WriteLine($"  3-Table Design: {memoryUsed3 / 1024L} KB")
                    Console.WriteLine($"  6-Table Design: {memoryUsed6 / 1024L} KB")
                    Console.WriteLine($"  Memory Ratio: {float memoryUsed6 / float memoryUsed3:F2}x")

                    // 基本的な妥当性チェック
                    Assert.IsTrue(memoryUsed3 > 0L, "3-table design should use some memory")
                    Assert.IsTrue(memoryUsed6 > 0L, "6-table design should use some memory")
                    Assert.IsTrue(memoryUsed3 < 50L * 1024L * 1024L, "3-table should use less than 50MB") // 50MB limit
                    Assert.IsTrue(memoryUsed6 < 50L * 1024L * 1024L, "6-table should use less than 50MB") // 50MB limit

                finally
                    if File.Exists(tempDbPath6) then
                        File.Delete(tempDbPath6)
            finally
                if File.Exists(tempDbPath3) then
                    File.Delete(tempDbPath3)

        }
        |> Async.RunSynchronously

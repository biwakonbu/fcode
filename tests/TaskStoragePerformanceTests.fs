module FCode.Tests.TaskStoragePerformanceTests

open NUnit.Framework
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.TaskStorageManager
open System
open System.IO
open System.Diagnostics

/// TaskStorageManagerのパフォーマンステスト
[<TestFixture>]
type TaskStoragePerformanceTests() =

    let mutable tempDbPath = ""
    let mutable storageManager: TaskStorageManager option = None

    [<SetUp>]
    member _.SetUp() =
        tempDbPath <- Path.Combine(Path.GetTempPath(), $"perf_test_tasks_{Guid.NewGuid()}.db")
        let connectionString = $"Data Source={tempDbPath};"
        
        let manager = new TaskStorageManager(connectionString)
        storageManager <- Some manager
        
        // データベース初期化
        match storageManager with
        | Some(manager) ->
            manager.InitializeDatabase()
            |> Async.RunSynchronously
            |> ignore
        | None -> ()

    [<TearDown>]
    member _.TearDown() =
        match storageManager with
        | Some manager ->
            manager.Dispose()
            storageManager <- None
        | None -> ()

        if File.Exists(tempDbPath) then
            File.Delete(tempDbPath)

    /// 大量タスク保存パフォーマンステスト
    [<Test>]
    [<Category("Performance")>]
    member _.``Bulk Task Save Performance Test``() =
        async {
            match storageManager with
            | Some(manager: TaskStorageManager) ->
                let taskCount = 1000
                let stopwatch = Stopwatch.StartNew()
                
                // 大量タスク生成・保存
                for i in 1..taskCount do
                    let testTask =
                        { TaskId = $"perf-task-{i:D6}"
                          Title = $"Performance Test Task {i}"
                          Description = $"パフォーマンステスト用タスク {i}"
                          Status = if i % 3 = 0 then TaskStatus.InProgress else TaskStatus.Pending
                          AssignedAgent = Some $"agent-{i % 5}"
                          Priority = if i % 2 = 0 then TaskPriority.High else TaskPriority.Medium
                          EstimatedDuration = Some(TimeSpan.FromMinutes(float(i % 60 + 15)))
                          ActualDuration = None
                          RequiredResources = [ $"resource-{i % 10}" ]
                          CreatedAt = DateTime.Now.AddMinutes(float(-i))
                          UpdatedAt = DateTime.Now }
                    
                    let! saveResult = manager.SaveTask(testTask)
                    match saveResult with
                    | Result.Ok _ -> ()
                    | Result.Error(error) -> Assert.Fail($"Task save failed: {error}")
                
                stopwatch.Stop()
                
                // パフォーマンス評価
                let avgTimePerTask = stopwatch.ElapsedMilliseconds / int64(taskCount)
                Assert.That(avgTimePerTask, Is.LessThan(50L), $"平均保存時間が50ms以下: 実際 {avgTimePerTask}ms")
                
                printfn $"大量タスク保存パフォーマンス: {taskCount}タスク、総時間: {stopwatch.ElapsedMilliseconds}ms、平均: {avgTimePerTask}ms/タスク"
                
            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously

    /// 大量タスク検索パフォーマンステスト
    [<Test>]
    [<Category("Performance")>]
    member _.``Bulk Task Retrieval Performance Test``() =
        async {
            match storageManager with
            | Some(manager: TaskStorageManager) ->
                // テストデータ準備（500タスク）
                let taskCount = 500
                for i in 1..taskCount do
                    let testTask =
                        { TaskId = $"search-task-{i:D6}"
                          Title = $"Search Test Task {i}"
                          Description = "検索パフォーマンステスト用"
                          Status = if i % 4 = 0 then TaskStatus.Completed else TaskStatus.InProgress
                          AssignedAgent = Some $"search-agent-{i % 3}"
                          Priority = TaskPriority.Medium
                          EstimatedDuration = Some(TimeSpan.FromMinutes(30.0))
                          ActualDuration = None
                          RequiredResources = []
                          CreatedAt = DateTime.Now
                          UpdatedAt = DateTime.Now }
                    
                    let! _ = manager.SaveTask(testTask)
                    ()
                
                // 検索パフォーマンステスト
                let stopwatch = Stopwatch.StartNew()
                
                // 実行可能タスク取得
                let! allTasks = manager.GetExecutableTasks()
                let midTime = stopwatch.ElapsedMilliseconds
                
                // プログレスサマリー取得（代替）
                let! progressSummary = manager.GetProgressSummary()
                
                stopwatch.Stop()
                
                // 結果検証
                match allTasks, progressSummary with
                | Result.Ok(tasks), Result.Ok(summary) ->
                    Assert.That(tasks.Length, Is.GreaterThan(0), "タスクが取得できること")
                    Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(1000L), $"検索時間が1秒以下: 実際 {stopwatch.ElapsedMilliseconds}ms")
                    
                    printfn $"検索パフォーマンス: 全取得 {midTime}ms、サマリー取得 {stopwatch.ElapsedMilliseconds - midTime}ms"
                
                | _ -> Assert.Fail("タスク検索に失敗")
                
            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously

    /// 並行アクセステスト
    [<Test>]
    [<Category("Performance")>]
    member _.``Concurrent Access Test``() =
        async {
            match storageManager with
            | Some(manager: TaskStorageManager) ->
                let taskCount = 100
                let concurrentTasks = 5
                
                // 並行タスク生成
                let concurrentOperations =
                    [1..concurrentTasks]
                    |> List.map (fun workerIndex ->
                        async {
                            for i in 1..taskCount do
                                let taskId = $"concurrent-{workerIndex}-{i:D3}"
                                let testTask =
                                    { TaskId = taskId
                                      Title = $"Concurrent Test Task {workerIndex}-{i}"
                                      Description = "並行アクセステスト"
                                      Status = TaskStatus.Pending
                                      AssignedAgent = Some $"worker-{workerIndex}"
                                      Priority = TaskPriority.Low
                                      EstimatedDuration = Some(TimeSpan.FromMinutes(15.0))
                                      ActualDuration = None
                                      RequiredResources = []
                                      CreatedAt = DateTime.Now
                                      UpdatedAt = DateTime.Now }
                                
                                let! saveResult = manager.SaveTask(testTask)
                                match saveResult with
                                | Result.Ok _ -> ()
                                | Result.Error(error) -> failwith $"並行保存失敗: {error}"
                        })
                
                // 並行実行
                let stopwatch = Stopwatch.StartNew()
                do! Async.Parallel concurrentOperations |> Async.Ignore
                stopwatch.Stop()
                
                // 結果確認
                let! allTasks = manager.GetExecutableTasks()
                match allTasks with
                | Result.Ok(tasks) ->
                    let expectedCount = taskCount * concurrentTasks
                    Assert.That(tasks.Length, Is.EqualTo(expectedCount), $"並行処理結果: 期待 {expectedCount}、実際 {tasks.Length}")
                    
                    printfn $"並行アクセステスト完了: {concurrentTasks}並行、{taskCount}タスク/並行、総時間: {stopwatch.ElapsedMilliseconds}ms"
                
                | Result.Error(error) -> Assert.Fail($"並行アクセステスト検証失敗: {error}")
                
            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously
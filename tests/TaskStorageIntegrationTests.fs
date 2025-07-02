module FCode.Tests.TaskStorageIntegrationTests

open NUnit.Framework
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.TaskStorageManager
open System
open System.IO

[<TestFixture>]
type TaskStorageIntegrationTests() =

    let mutable tempDbPath = ""
    let mutable storageManager: TaskStorageManager option = None

    [<SetUp>]
    member _.SetUp() =
        // テスト用一時データベースファイル作成
        tempDbPath <- Path.Combine(Path.GetTempPath(), $"test_tasks_{Guid.NewGuid()}.db")
        let connectionString = $"Data Source={tempDbPath};"

        let manager = new TaskStorageManager(connectionString)
        storageManager <- Some manager

    [<TearDown>]
    member _.TearDown() =
        match storageManager with
        | Some manager ->
            manager.Dispose()
            storageManager <- None
        | None -> ()

        // テスト用データベースファイル削除
        if File.Exists(tempDbPath) then
            File.Delete(tempDbPath)

    [<Test>]
    [<Category("Integration")>]
    member _.``Database Initialization Test``() =
        async {
            match storageManager with
            | Some(manager: TaskStorageManager) ->
                let! result = manager.InitializeDatabase()

                match result with
                | Result.Ok() -> Assert.Pass()
                | Result.Error(error) -> Assert.Fail($"Database initialization failed: {error}")
            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously

    [<Test>]
    [<Category("Integration")>]
    member _.``Task Save and Retrieve Test``() =
        async {
            match storageManager with
            | Some(manager: TaskStorageManager) ->
                // データベース初期化
                let! initResult = manager.InitializeDatabase()

                match initResult with
                | Result.Ok() -> ()
                | Result.Error(error) -> Assert.Fail($"Database initialization failed: {error}")

                // テストタスク作成
                let testTask =
                    { TaskId = "test-task-001"
                      Title = "Test Task"
                      Description = "This is a test task"
                      Status = TaskStatus.Pending
                      AssignedAgent = Some "test-agent"
                      Priority = TaskPriority.Medium
                      EstimatedDuration = Some(TimeSpan.FromMinutes(30.0))
                      ActualDuration = None
                      RequiredResources = [ "test-resource" ]
                      CreatedAt = DateTime.Now
                      UpdatedAt = DateTime.Now }

                // タスク保存
                let! saveResult = manager.SaveTask(testTask)

                match saveResult with
                | Result.Ok(_) -> () // 戻り値（int）を無視
                | Result.Error(error) -> Assert.Fail($"Task save failed: {error}")

                // タスク取得
                let! retrieveResult = manager.GetTask("test-task-001")

                match retrieveResult with
                | Result.Ok(Some retrievedTask) ->
                    Assert.AreEqual(testTask.TaskId, retrievedTask.TaskId)
                    Assert.AreEqual(testTask.Title, retrievedTask.Title)
                    Assert.AreEqual(testTask.Status, retrievedTask.Status)
                | Result.Ok(None) -> Assert.Fail("Task not found")
                | Result.Error(error) -> Assert.Fail($"Task retrieval failed: {error}")

            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously

    [<Test>]
    [<Category("Integration")>]
    member _.``Task Dependencies Integration Test``() =
        async {
            match storageManager with
            | Some(manager: TaskStorageManager) ->
                // データベース初期化
                let! initResult = manager.InitializeDatabase()

                match initResult with
                | Result.Ok() -> ()
                | Result.Error(error) -> Assert.Fail($"Database initialization failed: {error}")

                // 依存関係保存テスト
                let! depResult = manager.SaveTaskDependency("task-a", "task-b", "hard")

                match depResult with
                | Result.Ok(_) -> () // 戻り値（int）を無視
                | Result.Error(error) -> Assert.Fail($"Dependency save failed: {error}")

                // 実行可能タスク取得テスト（基本機能確認）
                let! executableResult = manager.GetExecutableTasks()

                match executableResult with
                | Result.Ok(_) -> ()
                | Result.Error(error) -> Assert.Fail($"Get executable tasks failed: {error}")

            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously

    [<Test>]
    [<Category("Integration")>]
    member _.``Progress Summary Integration Test``() =
        async {
            match storageManager with
            | Some(manager: TaskStorageManager) ->
                // データベース初期化
                let! initResult = manager.InitializeDatabase()

                match initResult with
                | Result.Ok() -> ()
                | Result.Error(error) -> Assert.Fail($"Database initialization failed: {error}")

                // 進捗サマリー取得テスト
                let! summaryResult = manager.GetProgressSummary()

                match summaryResult with
                | Result.Ok(summary) ->
                    // サマリーの基本構造確認
                    Assert.IsTrue(summary.TotalTasks >= 0)
                    Assert.IsTrue(summary.CompletedTasks >= 0)
                    Assert.IsTrue(summary.ActiveAgents >= 0)
                    Assert.IsTrue(summary.OverallProgress >= 0.0)
                | Result.Error(error) -> Assert.Fail($"Progress summary failed: {error}")

            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously
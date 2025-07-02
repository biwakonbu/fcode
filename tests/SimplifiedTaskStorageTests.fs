module FCode.Tests.SimplifiedTaskStorageTests

open NUnit.Framework
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.SimplifiedTaskStorageManager
open FCode.Collaboration.SimplifiedDatabaseSchema.TypeSafeMapping
open System
open System.IO

[<TestFixture>]
type SimplifiedTaskStorageTests() =

    let mutable tempDbPath = ""
    let mutable storageManager: SimplifiedTaskStorageManager option = None

    [<SetUp>]
    member _.SetUp() =
        // テスト用一時データベースファイル作成
        tempDbPath <- Path.Combine(Path.GetTempPath(), $"simplified_test_tasks_{Guid.NewGuid()}.db")
        let connectionString = $"Data Source={tempDbPath};"

        let manager = new SimplifiedTaskStorageManager(connectionString)
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
    [<Category("Unit")>]
    member _.``Simplified Database Initialization Test``() =
        async {
            match storageManager with
            | Some(manager: SimplifiedTaskStorageManager) ->
                let! result = manager.InitializeDatabase()

                match result with
                | Result.Ok() -> Assert.Pass()
                | Result.Error(error) -> Assert.Fail($"Database initialization failed: {error}")
            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously

    [<Test>]
    [<Category("Unit")>]
    member _.``Simplified Task Save and Retrieve Test``() =
        async {
            match storageManager with
            | Some(manager: SimplifiedTaskStorageManager) ->
                // データベース初期化
                let! initResult = manager.InitializeDatabase()

                match initResult with
                | Result.Ok() -> ()
                | Result.Error(error) -> Assert.Fail($"Database initialization failed: {error}")

                // テストタスク作成
                let testTask =
                    { TaskId = "simplified-test-001"
                      Title = "Simplified Test Task"
                      Description = "This is a simplified test task"
                      Status = TaskStatus.Pending
                      AssignedAgent = Some "test-agent"
                      Priority = TaskPriority.Medium
                      EstimatedDuration = Some(TimeSpan.FromMinutes(30.0))
                      ActualDuration = None
                      RequiredResources = [ "cpu"; "memory"; "storage" ]
                      CreatedAt = DateTime.Now
                      UpdatedAt = DateTime.Now }

                // タスク保存
                let! saveResult = manager.SaveTask(testTask)

                match saveResult with
                | Result.Ok(_) -> ()
                | Result.Error(error) -> Assert.Fail($"Task save failed: {error}")

                // タスク取得
                let! retrieveResult = manager.GetTask("simplified-test-001")

                match retrieveResult with
                | Result.Ok(Some retrievedTask) ->
                    Assert.AreEqual(testTask.TaskId, retrievedTask.TaskId)
                    Assert.AreEqual(testTask.Title, retrievedTask.Title)
                    Assert.AreEqual(testTask.Status, retrievedTask.Status)
                    Assert.AreEqual(testTask.Priority, retrievedTask.Priority)
                    Assert.AreEqual(testTask.RequiredResources.Length, retrievedTask.RequiredResources.Length)
                    
                    // JSON配列として保存されたリソースの確認
                    Assert.IsTrue(List.contains "cpu" retrievedTask.RequiredResources)
                    Assert.IsTrue(List.contains "memory" retrievedTask.RequiredResources)
                    Assert.IsTrue(List.contains "storage" retrievedTask.RequiredResources)
                    
                | Result.Ok(None) -> Assert.Fail("Task not found")
                | Result.Error(error) -> Assert.Fail($"Task retrieval failed: {error}")

            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously

    [<Test>]
    [<Category("Unit")>]
    member _.``Simplified Task Dependencies Test``() =
        async {
            match storageManager with
            | Some(manager: SimplifiedTaskStorageManager) ->
                // データベース初期化
                let! initResult = manager.InitializeDatabase()

                match initResult with
                | Result.Ok() -> ()
                | Result.Error(error) -> Assert.Fail($"Database initialization failed: {error}")

                // テスト用タスク作成
                let taskA =
                    { TaskId = "task-a"
                      Title = "Task A"
                      Description = "First task"
                      Status = TaskStatus.Pending
                      AssignedAgent = None
                      Priority = TaskPriority.Medium
                      EstimatedDuration = None
                      ActualDuration = None
                      RequiredResources = []
                      CreatedAt = DateTime.Now
                      UpdatedAt = DateTime.Now }

                let taskB =
                    { TaskId = "task-b"
                      Title = "Task B"
                      Description = "Second task"
                      Status = TaskStatus.Completed  // 完了状態
                      AssignedAgent = None
                      Priority = TaskPriority.Medium
                      EstimatedDuration = None
                      ActualDuration = None
                      RequiredResources = []
                      CreatedAt = DateTime.Now
                      UpdatedAt = DateTime.Now }

                // タスク保存
                let! saveResultA = manager.SaveTask(taskA)
                let! saveResultB = manager.SaveTask(taskB)

                match saveResultA, saveResultB with
                | Result.Ok(_), Result.Ok(_) -> ()
                | _ -> Assert.Fail("Task save failed")

                // 依存関係保存
                let! depResult = manager.SaveTaskDependency("task-a", "task-b", "hard")

                match depResult with
                | Result.Ok(_) -> ()
                | Result.Error(error) -> Assert.Fail($"Dependency save failed: {error}")

                // 実行可能タスク取得（task-bが完了しているのでtask-aは実行可能）
                let! executableResult = manager.GetExecutableTasks()

                match executableResult with
                | Result.Ok(tasks) ->
                    Assert.IsTrue(tasks.Length >= 0)  // 基本的に実行可能
                    // task-aが実行可能タスクに含まれるかチェック
                    let taskAExists = tasks |> List.exists (fun t -> t.TaskId = "task-a")
                    Assert.IsTrue(taskAExists, "Task A should be executable since Task B is completed")
                | Result.Error(error) -> Assert.Fail($"Get executable tasks failed: {error}")

            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously

    [<Test>]
    [<Category("Unit")>]
    member _.``Simplified Progress Summary Test``() =
        async {
            match storageManager with
            | Some(manager: SimplifiedTaskStorageManager) ->
                // データベース初期化
                let! initResult = manager.InitializeDatabase()

                match initResult with
                | Result.Ok() -> ()
                | Result.Error(error) -> Assert.Fail($"Database initialization failed: {error}")

                // テストタスクを追加
                let completedTask =
                    { TaskId = "completed-task"
                      Title = "Completed Task"
                      Description = "A completed task"
                      Status = TaskStatus.Completed
                      AssignedAgent = Some "agent1"
                      Priority = TaskPriority.Medium
                      EstimatedDuration = None
                      ActualDuration = None
                      RequiredResources = []
                      CreatedAt = DateTime.Now
                      UpdatedAt = DateTime.Now }

                let activeTask =
                    { TaskId = "active-task"
                      Title = "Active Task"
                      Description = "An active task"
                      Status = TaskStatus.InProgress
                      AssignedAgent = Some "agent2"
                      Priority = TaskPriority.High
                      EstimatedDuration = None
                      ActualDuration = None
                      RequiredResources = []
                      CreatedAt = DateTime.Now
                      UpdatedAt = DateTime.Now }

                let! saveResult1 = manager.SaveTask(completedTask)
                let! saveResult2 = manager.SaveTask(activeTask)

                match saveResult1, saveResult2 with
                | Result.Ok(_), Result.Ok(_) -> ()
                | _ -> Assert.Fail("Task save failed")

                // 進捗サマリー取得
                let! summaryResult = manager.GetProgressSummary()

                match summaryResult with
                | Result.Ok(summary) ->
                    Assert.IsTrue(summary.TotalTasks >= 2)
                    Assert.IsTrue(summary.CompletedTasks >= 1)
                    Assert.IsTrue(summary.InProgressTasks >= 1)
                    Assert.IsTrue(summary.ActiveAgents >= 2)
                    Assert.IsTrue(summary.OverallProgress >= 0.0)
                | Result.Error(error) -> Assert.Fail($"Progress summary failed: {error}")

            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously

    [<Test>]
    [<Category("Unit")>]
    member _.``Type Safe Enum Mapping Test``() =
        // 型安全な列挙型マッピングのテスト
        
        // TaskStatus マッピング
        Assert.AreEqual(1, taskStatusToInt TaskStatus.Pending)
        Assert.AreEqual(2, taskStatusToInt TaskStatus.InProgress)
        Assert.AreEqual(3, taskStatusToInt TaskStatus.Completed)
        Assert.AreEqual(4, taskStatusToInt TaskStatus.Failed)
        Assert.AreEqual(5, taskStatusToInt TaskStatus.Cancelled)
        
        Assert.AreEqual(TaskStatus.Pending, intToTaskStatus 1)
        Assert.AreEqual(TaskStatus.InProgress, intToTaskStatus 2)
        Assert.AreEqual(TaskStatus.Completed, intToTaskStatus 3)
        Assert.AreEqual(TaskStatus.Failed, intToTaskStatus 4)
        Assert.AreEqual(TaskStatus.Cancelled, intToTaskStatus 5)
        
        // AgentStatus マッピング
        Assert.AreEqual(1, agentStatusToInt AgentStatus.Idle)
        Assert.AreEqual(2, agentStatusToInt AgentStatus.Working)
        Assert.AreEqual(3, agentStatusToInt AgentStatus.Blocked)
        Assert.AreEqual(4, agentStatusToInt AgentStatus.Error)
        Assert.AreEqual(5, agentStatusToInt AgentStatus.Completed)
        
        // JSON安全変換
        let testList = ["resource1"; "resource2"; "resource3"]
        let json = listToJson testList
        let restoredList = jsonToList json
        
        Assert.AreEqual(testList.Length, restoredList.Length)
        Assert.IsTrue(List.contains "resource1" restoredList)
        Assert.IsTrue(List.contains "resource2" restoredList)
        Assert.IsTrue(List.contains "resource3" restoredList)

    [<Test>]
    [<Category("Unit")>]
    member _.``Agent State History Test``() =
        async {
            match storageManager with
            | Some(manager: SimplifiedTaskStorageManager) ->
                // データベース初期化
                let! initResult = manager.InitializeDatabase()

                match initResult with
                | Result.Ok() -> ()
                | Result.Error(error) -> Assert.Fail($"Database initialization failed: {error}")

                // テスト用タスクを先に作成
                let testTask =
                    { TaskId = "test-task"
                      Title = "Test Task for Agent"
                      Description = "Test task for agent state"
                      Status = TaskStatus.InProgress
                      AssignedAgent = Some "test-agent"
                      Priority = TaskPriority.Medium
                      EstimatedDuration = None
                      ActualDuration = None
                      RequiredResources = []
                      CreatedAt = DateTime.Now
                      UpdatedAt = DateTime.Now }

                let! saveTaskResult = manager.SaveTask(testTask)
                
                match saveTaskResult with
                | Result.Ok(_) -> ()
                | Result.Error(error) -> Assert.Fail($"Task save failed: {error}")

                // エージェント状態作成
                let agentState =
                    { AgentId = "test-agent"
                      Status = AgentStatus.Working
                      Progress = 50.0
                      LastUpdate = DateTime.Now
                      CurrentTask = Some "test-task"  // 上で作成したタスクを参照
                      WorkingDirectory = "/tmp/test"
                      ProcessId = Some 12345 }

                // エージェント状態履歴保存
                let! saveResult = manager.SaveAgentStateHistory(agentState)

                match saveResult with
                | Result.Ok(rowsAffected) ->
                    Assert.AreEqual(1, rowsAffected)
                | Result.Error(error) -> Assert.Fail($"Agent state history save failed: {error}")

            | None -> Assert.Fail("Storage manager not initialized")
        }
        |> Async.RunSynchronously
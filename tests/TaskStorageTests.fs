module FCode.Tests.TaskStorageTests

open System
open System.IO
open NUnit.Framework
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.TaskStorageManager
open FCode.Collaboration.DatabaseSchemaManager
open FCode.Collaboration.SimpleTaskRepository
open FCode.Collaboration.SimpleAgentRepository
open FCode.Collaboration.SimpleProgressRepository

/// テスト用SQLiteデータベース作成
let createTestDatabase () =
    let testDbPath = Path.GetTempFileName()
    let connectionString = $"Data Source={testDbPath}"
    let storage = new TaskStorageManager(connectionString)
    storage, testDbPath

/// テスト用設定
let createTestConfig (dbPath: string) =
    { CollaborationConfig.Default with
        DatabasePath = dbPath
        MaxConcurrentAgents = 5
        TaskTimeoutMinutes = 10 }

[<Test>]
[<Category("Unit")>]
let ``TaskStorageManager database initialization test`` () =
    let storage, dbPath = createTestDatabase ()

    try
        // データベース初期化
        let initResult = storage.InitializeDatabase() |> Async.RunSynchronously

        match initResult with
        | Result.Ok _ -> Assert.Pass()
        | Result.Error e -> Assert.Fail($"Database initialization failed: {e}")

        Assert.IsTrue(File.Exists(dbPath))

    finally
        storage.Dispose()

        if File.Exists(dbPath) then
            File.Delete(dbPath)

[<Test>]
[<Category("Unit")>]
let ``TaskStorageManager task save and retrieve test`` () =
    let storage, dbPath = createTestDatabase ()

    try
        // データベース初期化
        storage.InitializeDatabase() |> Async.RunSynchronously |> ignore

        let testTask =
            { TaskId = "test-task-1"
              Title = "Test Task"
              Description = "Test Description"
              Status = Pending
              AssignedAgent = None
              Priority = TaskPriority.High
              EstimatedDuration = Some(TimeSpan.FromHours(2.0))
              ActualDuration = None
              RequiredResources = [ "cpu"; "memory" ]
              CreatedAt = DateTime.UtcNow
              UpdatedAt = DateTime.UtcNow }

        // タスク保存テスト
        let saveResult = storage.SaveTask(testTask) |> Async.RunSynchronously

        match saveResult with
        | Result.Ok _ -> Assert.Pass()
        | Result.Error e -> Assert.Fail($"Task save failed: {e}")

        // 実行可能タスク取得テスト
        let executableTasks = storage.GetExecutableTasks() |> Async.RunSynchronously

        match executableTasks with
        | Result.Ok tasks ->
            Assert.IsTrue(tasks |> List.exists (fun t -> t.TaskId = "test-task-1"))
            let retrievedTask = tasks |> List.find (fun t -> t.TaskId = "test-task-1")
            Assert.AreEqual(testTask.Title, retrievedTask.Title)
            Assert.AreEqual(testTask.Priority, retrievedTask.Priority)
        | Result.Error e -> Assert.Fail($"タスク取得に失敗: {e}")

    finally
        storage.Dispose()

        if File.Exists(dbPath) then
            File.Delete(dbPath)

[<Test>]
[<Category("Unit")>]
let ``TaskStorageManager dependency save test`` () =
    let storage, dbPath = createTestDatabase ()

    try
        // データベース初期化
        storage.InitializeDatabase() |> Async.RunSynchronously |> ignore

        // 依存関係保存テスト
        let depResult =
            storage.SaveTaskDependency("task-2", "task-1", "hard") |> Async.RunSynchronously

        match depResult with
        | Result.Ok _ -> Assert.Pass()
        | Result.Error e -> Assert.Fail($"Dependency save failed: {e}")

    finally
        storage.Dispose()

        if File.Exists(dbPath) then
            File.Delete(dbPath)

[<Test>]
[<Category("Unit")>]
let ``TaskStorageManager agent state history save test`` () =
    let storage, dbPath = createTestDatabase ()

    try
        // データベース初期化
        storage.InitializeDatabase() |> Async.RunSynchronously |> ignore

        let testAgentState =
            { AgentId = "test-agent-1"
              Status = Working
              Progress = 50.0
              LastUpdate = DateTime.UtcNow
              CurrentTask = Some "test-task-1"
              WorkingDirectory = "/tmp/test"
              ProcessId = Some 1234 }

        // エージェント状態履歴保存テスト
        let historyResult =
            storage.SaveAgentStateHistory(testAgentState) |> Async.RunSynchronously

        match historyResult with
        | Result.Ok _ -> Assert.Pass()
        | Result.Error e -> Assert.Fail($"Agent state history save failed: {e}")

    finally
        storage.Dispose()

        if File.Exists(dbPath) then
            File.Delete(dbPath)

[<Test>]
[<Category("Unit")>]
let ``TaskStorageManager progress summary test`` () =
    let storage, dbPath = createTestDatabase ()

    try
        // データベース初期化
        storage.InitializeDatabase() |> Async.RunSynchronously |> ignore

        // テストタスクを複数保存
        let testTasks =
            [ { TaskId = "task-1"
                Title = "Task 1"
                Description = ""
                Status = Completed
                AssignedAgent = Some "agent-1"
                Priority = TaskPriority.High
                EstimatedDuration = None
                ActualDuration = None
                RequiredResources = []
                CreatedAt = DateTime.UtcNow
                UpdatedAt = DateTime.UtcNow }
              { TaskId = "task-2"
                Title = "Task 2"
                Description = ""
                Status = InProgress
                AssignedAgent = Some "agent-2"
                Priority = TaskPriority.Medium
                EstimatedDuration = None
                ActualDuration = None
                RequiredResources = []
                CreatedAt = DateTime.UtcNow
                UpdatedAt = DateTime.UtcNow }
              { TaskId = "task-3"
                Title = "Task 3"
                Description = ""
                Status = Pending
                AssignedAgent = None
                Priority = TaskPriority.Low
                EstimatedDuration = None
                ActualDuration = None
                RequiredResources = []
                CreatedAt = DateTime.UtcNow
                UpdatedAt = DateTime.UtcNow } ]

        for task in testTasks do
            storage.SaveTask(task) |> Async.RunSynchronously |> ignore

        // 進捗サマリー取得テスト
        let summaryResult = storage.GetProgressSummary() |> Async.RunSynchronously

        match summaryResult with
        | Result.Ok summary ->
            Assert.AreEqual(3, summary.TotalTasks)
            Assert.AreEqual(1, summary.CompletedTasks)
            Assert.AreEqual(1, summary.InProgressTasks)
        | Result.Error e -> Assert.Fail($"進捗サマリー取得に失敗: {e}")

    finally
        storage.Dispose()

        if File.Exists(dbPath) then
            File.Delete(dbPath)

[<Test>]
[<Category("Unit")>]
[<Category("Integration")>]
let ``TaskRepository save and retrieve test`` () =
    let testDbPath = Path.GetTempFileName()
    let connectionString = $"Data Source={testDbPath}"

    try
        // スキーマ初期化
        let schemaManager = new DatabaseSchemaManager(connectionString)
        schemaManager.InitializeDatabase() |> Async.RunSynchronously |> ignore

        let taskRepo = new SimpleTaskRepository(connectionString)

        let testTask =
            { TaskId = "task-repo-test-1"
              Title = "Task Repository Test"
              Description = "Testing task repository directly"
              Status = Pending
              AssignedAgent = None
              Priority = TaskPriority.High
              EstimatedDuration = Some(TimeSpan.FromMinutes(30.0))
              ActualDuration = None
              RequiredResources = [ "cpu" ]
              CreatedAt = DateTime.UtcNow
              UpdatedAt = DateTime.UtcNow }

        // タスク保存テスト
        let saveResult = taskRepo.SaveTask(testTask) |> Async.RunSynchronously

        match saveResult with
        | Result.Ok _ -> Assert.Pass()
        | Result.Error e -> Assert.Fail($"Task save failed: {e}")

        // タスク取得テスト
        let getResult = taskRepo.GetTask("task-repo-test-1") |> Async.RunSynchronously

        match getResult with
        | Result.Ok(Some retrievedTask) ->
            Assert.AreEqual(testTask.Title, retrievedTask.Title)
            Assert.AreEqual(testTask.Priority, retrievedTask.Priority)
        | _ -> Assert.Fail("タスクリポジトリからのタスク取得に失敗")

    finally
        if File.Exists(testDbPath) then
            File.Delete(testDbPath)

[<Test>]
[<Category("Unit")>]
[<Category("Integration")>]
let ``AgentRepository state management test`` () =
    let testDbPath = Path.GetTempFileName()
    let connectionString = $"Data Source={testDbPath}"

    try
        // スキーマ初期化
        let schemaManager = new DatabaseSchemaManager(connectionString)
        schemaManager.InitializeDatabase() |> Async.RunSynchronously |> ignore

        let agentRepo = new SimpleAgentRepository(connectionString)

        let testAgentState =
            { AgentId = "agent-repo-test-1"
              Status = Working
              Progress = 75.0
              LastUpdate = DateTime.UtcNow
              CurrentTask = Some "test-task-1"
              WorkingDirectory = "/tmp/test"
              ProcessId = Some 1234 }

        // エージェント状態保存テスト
        let saveResult =
            agentRepo.SaveAgentStateHistory(testAgentState) |> Async.RunSynchronously

        match saveResult with
        | Result.Ok _ -> Assert.Pass()
        | Result.Error e -> Assert.Fail($"Agent state save failed: {e}")

        // エージェント最新状態取得テスト
        let getResult =
            agentRepo.GetLatestAgentState("agent-repo-test-1") |> Async.RunSynchronously

        match getResult with
        | Result.Ok(Some retrievedState) ->
            Assert.AreEqual(testAgentState.AgentId, retrievedState.AgentId)
            Assert.AreEqual(testAgentState.Status, retrievedState.Status)
            Assert.AreEqual(testAgentState.Progress, retrievedState.Progress)
        | _ -> Assert.Fail("エージェントリポジトリからの状態取得に失敗")

    finally
        if File.Exists(testDbPath) then
            File.Delete(testDbPath)

[<Test>]
[<Category("Unit")>]
[<Category("Integration")>]
let ``ProgressRepository progress management test`` () =
    let testDbPath = Path.GetTempFileName()
    let connectionString = $"Data Source={testDbPath}"

    try
        // スキーマ初期化
        let schemaManager = new DatabaseSchemaManager(connectionString)
        schemaManager.InitializeDatabase() |> Async.RunSynchronously |> ignore

        let progressRepo = new SimpleProgressRepository(connectionString)

        // 進捗イベント保存テスト
        let eventResult =
            progressRepo.SaveProgressEvent(
                "TaskStarted",
                "test-agent-1",
                Some "test-task-1",
                Some 0.0,
                Some "{\"duration_ms\": 0}"
            )
            |> Async.RunSynchronously

        match eventResult with
        | Result.Ok _ -> Assert.Pass()
        | Result.Error e -> Assert.Fail($"Progress event save failed: {e}")

        // 最新進捗イベント取得テスト
        let eventsResult =
            progressRepo.GetRecentProgressEvents(10) |> Async.RunSynchronously

        match eventsResult with
        | Result.Ok events ->
            Assert.Greater(events.Length, 0)
            let (eventType, agentId, _, _, _, _, _) = events.Head
            Assert.AreEqual("TaskStarted", eventType)
            Assert.AreEqual("test-agent-1", agentId)
        | _ -> Assert.Fail("進捗リポジトリからのイベント取得に失敗")

    finally
        if File.Exists(testDbPath) then
            File.Delete(testDbPath)

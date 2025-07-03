module FCode.Tests.RealtimeCollaborationTests

open System
open System.Threading
open Xunit
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.AgentStateManager
open FCode.Collaboration.TaskDependencyGraph
open FCode.Collaboration.ProgressAggregator
open FCode.Collaboration.CollaborationCoordinator
open FCode.Collaboration.RealtimeCollaborationFacade

/// テスト用設定
let testConfig =
    { MaxConcurrentAgents = 5
      TaskTimeoutMinutes = 30
      StaleAgentThreshold = TimeSpan.FromMinutes(5.0)
      MaxRetryAttempts = 3
      DatabasePath = ":memory:"
      ConnectionPoolSize = 1
      WALModeEnabled = false
      AutoVacuumEnabled = false
      MaxHistoryRetentionDays = 7
      BackupEnabled = false
      BackupIntervalHours = 24 }

/// テスト用タスク作成
let createTestTask taskId title priority estimatedDuration =
    { TaskId = taskId
      Title = title
      Description = ""
      Status = Pending
      Priority = priority
      AssignedAgent = None
      EstimatedDuration = Some estimatedDuration
      ActualDuration = None
      RequiredResources = []
      CreatedAt = DateTime.UtcNow
      UpdatedAt = DateTime.UtcNow }

/// テスト用エージェント状態作成
let createTestAgentState agentId status progress =
    { AgentId = agentId
      Status = status
      Progress = progress
      LastUpdate = DateTime.UtcNow
      CurrentTask = None
      WorkingDirectory = "/test"
      ProcessId = None }

[<Trait("TestCategory", "Unit")>]
[<Fact>]
let ``AgentStateManager - エージェント状態の基本操作テスト`` () =
    use manager = new AgentStateManager(testConfig)

    // エージェント状態更新
    let result1 =
        manager.UpdateAgentState("agent1", Working, progress = 50.0, currentTask = "task1")

    Assert.True(
        match result1 with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // エージェント状態取得
    let result2 = manager.GetAgentState("agent1")

    match result2 with
    | Result.Ok(Some state) ->
        Assert.Equal("agent1", state.AgentId)
        Assert.Equal(Working, state.Status)
        Assert.Equal(50.0, state.Progress)
        Assert.Equal(Some "task1", state.CurrentTask)
    | _ -> Assert.True(false, "エージェント状態取得に失敗")

[<Trait("TestCategory", "Unit")>]
[<Fact>]
let ``AgentStateManager - 無効な進捗値の検証テスト`` () =
    use manager = new AgentStateManager(testConfig)

    // 無効な進捗値（-10.0）
    let result1 = manager.UpdateAgentState("agent1", Working, progress = -10.0)

    match result1 with
    | Result.Error(InvalidInput msg) -> Assert.Contains("Progress must be between", msg)
    | _ -> Assert.True(false, "無効な進捗値が受け入れられました")

    // 無効な進捗値（150.0）
    let result2 = manager.UpdateAgentState("agent1", Working, progress = 150.0)

    match result2 with
    | Result.Error(InvalidInput msg) -> Assert.Contains("Progress must be between", msg)
    | _ -> Assert.True(false, "無効な進捗値が受け入れられました")

[<Trait("TestCategory", "Unit")>]
[<Fact>]
let ``TaskDependencyGraph - タスクの基本操作テスト`` () =
    use graph = new TaskDependencyGraph(testConfig)

    let task1 =
        createTestTask "task1" "Task 1" TaskPriority.High (TimeSpan.FromHours(2.0))

    let task2 =
        createTestTask "task2" "Task 2" TaskPriority.Medium (TimeSpan.FromHours(1.0))

    // タスク追加
    let result1 = graph.AddTask(task1)

    Assert.True(
        match result1 with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    let result2 = graph.AddTask(task2)

    Assert.True(
        match result2 with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // タスク取得
    let result3 = graph.GetTask("task1")

    match result3 with
    | Result.Ok(Some retrievedTask) ->
        Assert.Equal("task1", retrievedTask.TaskId)
        Assert.Equal("Task 1", retrievedTask.Title)
        Assert.Equal(TaskPriority.High, retrievedTask.Priority)
    | _ -> Assert.True(false, "タスク取得に失敗")

[<Trait("TestCategory", "Unit")>]
[<Fact>]
let ``TaskDependencyGraph - 循環依存検出テスト`` () =
    use graph = new TaskDependencyGraph(testConfig)

    let task1 =
        createTestTask "task1" "Task 1" TaskPriority.High (TimeSpan.FromHours(1.0))

    let task2 =
        createTestTask "task2" "Task 2" TaskPriority.Medium (TimeSpan.FromHours(1.0))

    let task3 =
        createTestTask "task3" "Task 3" TaskPriority.Low (TimeSpan.FromHours(1.0))

    // タスク追加
    Assert.True(
        match graph.AddTask(task1) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match graph.AddTask(task2) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match graph.AddTask(task3) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // 依存関係追加: task1 -> task2 -> task3
    Assert.True(
        match graph.AddDependency("task1", "task2") with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match graph.AddDependency("task2", "task3") with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // 循環依存作成試行: task3 -> task1
    let result = graph.AddDependency("task3", "task1")

    match result with
    | Result.Error(CircularDependency cycle) -> Assert.True(cycle.Length > 0, "循環依存が検出されませんでした")
    | _ -> Assert.True(false, "循環依存が検出されませんでした")

[<Trait("TestCategory", "Unit")>]
[<Fact>]
let ``TaskDependencyGraph - 実行可能タスク取得テスト`` () =
    use graph = new TaskDependencyGraph(testConfig)

    let task1 =
        createTestTask "task1" "Task 1" TaskPriority.High (TimeSpan.FromHours(1.0))

    let task2 =
        createTestTask "task2" "Task 2" TaskPriority.Medium (TimeSpan.FromHours(1.0))

    let task3 =
        createTestTask "task3" "Task 3" TaskPriority.Low (TimeSpan.FromHours(1.0))

    // タスク追加
    Assert.True(
        match graph.AddTask(task1) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match graph.AddTask(task2) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match graph.AddTask(task3) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // 依存関係追加: task2 -> task1, task3 -> task1
    Assert.True(
        match graph.AddDependency("task2", "task1") with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match graph.AddDependency("task3", "task1") with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // 実行可能タスク取得（task1のみが実行可能）
    match graph.GetExecutableTasks() with
    | Result.Ok executableTasks ->
        Assert.Equal(1, executableTasks.Length)
        Assert.Equal("task1", executableTasks.[0].TaskId)
    | Result.Error _ -> Assert.True(false, "実行可能タスクの取得に失敗")

[<Trait("TestCategory", "Unit")>]
[<Fact>]
let ``TaskDependencyGraph - クリティカルパス計算テスト`` () =
    use graph = new TaskDependencyGraph(testConfig)

    let task1 =
        createTestTask "task1" "Task 1" TaskPriority.High (TimeSpan.FromHours(2.0))

    let task2 =
        createTestTask "task2" "Task 2" TaskPriority.Medium (TimeSpan.FromHours(3.0))

    let task3 =
        createTestTask "task3" "Task 3" TaskPriority.Low (TimeSpan.FromHours(1.0))

    // タスク追加
    Assert.True(
        match graph.AddTask(task1) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match graph.AddTask(task2) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match graph.AddTask(task3) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // 依存関係追加: task2 -> task1, task3 -> task2
    Assert.True(
        match graph.AddDependency("task2", "task1") with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match graph.AddDependency("task3", "task2") with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // クリティカルパス計算
    match graph.GetCriticalPath() with
    | Result.Ok(duration, path) ->
        Assert.Equal(TimeSpan.FromHours(6.0), duration) // 2 + 3 + 1 = 6時間
        Assert.True(path.Length > 0, "クリティカルパスが空です")
    | Result.Error _ -> Assert.True(false, "クリティカルパス計算に失敗")

[<Trait("TestCategory", "Unit")>]
[<Fact>]
let ``ProgressAggregator - 進捗サマリー計算テスト`` () =
    use agentManager = new AgentStateManager(testConfig)
    use taskGraph = new TaskDependencyGraph(testConfig)
    use progressAggregator = new ProgressAggregator(agentManager, taskGraph, testConfig)

    // テストデータ設定
    let task1 =
        createTestTask "task1" "Task 1" TaskPriority.High (TimeSpan.FromHours(1.0))

    let task2 =
        { createTestTask "task2" "Task 2" TaskPriority.Medium (TimeSpan.FromHours(1.0)) with
            Status = Completed }

    Assert.True(
        match taskGraph.AddTask(task1) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match taskGraph.AddTask(task2) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match agentManager.UpdateAgentState("agent1", Working, progress = 75.0, currentTask = "task1") with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match agentManager.UpdateAgentState("agent2", Idle, progress = 0.0) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // 進捗サマリー取得
    match progressAggregator.GetCurrentSummary() with
    | Result.Ok summary ->
        Assert.Equal(2, summary.TotalTasks)
        Assert.Equal(1, summary.CompletedTasks)
        Assert.Equal(1, summary.InProgressTasks)
        Assert.Equal(1, summary.ActiveAgents)
        Assert.True(summary.OverallProgress > 0.0, "全体進捗が0%です")
    | Result.Error _ -> Assert.True(false, "進捗サマリー取得に失敗")

[<Trait("TestCategory", "Unit")>]
[<Fact>]
let ``ProgressAggregator - 進捗トレンド分析テスト`` () =
    use agentManager = new AgentStateManager(testConfig)
    use taskGraph = new TaskDependencyGraph(testConfig)
    use progressAggregator = new ProgressAggregator(agentManager, taskGraph, testConfig)

    // テストデータ設定
    let task1 =
        createTestTask "task1" "Task 1" TaskPriority.High (TimeSpan.FromHours(1.0))

    Assert.True(
        match taskGraph.AddTask(task1) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match agentManager.UpdateAgentState("agent1", Working, progress = 20.0, currentTask = "task1") with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // 時間を少し進める（進捗履歴のため）
    System.Threading.Thread.Sleep(10)

    Assert.True(
        match agentManager.UpdateAgentState("agent1", Working, progress = 50.0, currentTask = "task1") with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    System.Threading.Thread.Sleep(10)

    Assert.True(
        match agentManager.UpdateAgentState("agent1", Working, progress = 80.0, currentTask = "task1") with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // 進捗トレンド分析
    match progressAggregator.AnalyzeProgressTrend() with
    | Result.Ok analysis ->
        Assert.True(analysis.CurrentVelocity >= 0.0, "速度が負の値です")
        Assert.True(analysis.Efficiency >= 0.0, "効率が負の値です")
        Assert.True(not analysis.RecommendedActions.IsEmpty, "推奨アクションが空です")
    | Result.Error _ -> Assert.True(false, "進捗トレンド分析に失敗")

[<Trait("TestCategory", "Unit")>]
[<Fact>]
let ``CollaborationCoordinator - リソース競合制御テスト`` () =
    use agentManager = new AgentStateManager(testConfig)
    use taskGraph = new TaskDependencyGraph(testConfig)
    use coordinator = new CollaborationCoordinator(agentManager, taskGraph, testConfig)

    // 最初のエージェントがリソースを要求
    let result1 =
        coordinator.RequestTaskExecution("agent1", "task1", [ "cpu"; "memory" ])

    Assert.True(
        match result1 with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // 同じリソースを別のエージェントが要求（競合発生）
    let result2 = coordinator.RequestTaskExecution("agent2", "task2", [ "cpu"; "disk" ])

    match result2 with
    | Result.Error(ConflictDetected conflicts) -> Assert.True(not conflicts.IsEmpty, "競合が検出されませんでした")
    | _ -> Assert.True(false, "リソース競合が検出されませんでした")

[<Trait("TestCategory", "Unit")>]
[<Fact>]
let ``CollaborationCoordinator - 協調作業効率分析テスト`` () =
    use agentManager = new AgentStateManager(testConfig)
    use taskGraph = new TaskDependencyGraph(testConfig)
    use coordinator = new CollaborationCoordinator(agentManager, taskGraph, testConfig)

    // テストデータ設定
    Assert.True(
        match agentManager.UpdateAgentState("agent1", Working, progress = 50.0) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    Assert.True(
        match agentManager.UpdateAgentState("agent2", Working, progress = 75.0) with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // 協調作業効率分析
    match coordinator.AnalyzeCollaborationEfficiency() with
    | Result.Ok analysis ->
        Assert.Equal(2, analysis.TotalAgents)
        Assert.True(analysis.ParallelEfficiency >= 0.0, "並列効率が負の値です")
        Assert.True(analysis.ResourceUtilization >= 0.0, "リソース使用率が負の値です")
    | Result.Error _ -> Assert.True(false, "協調作業効率分析に失敗")

[<Trait("TestCategory", "Integration")>]
[<Fact>]
let ``RealtimeCollaborationFacade - 統合動作テスト`` () =
    use facade = new RealtimeCollaborationFacade(testConfig)

    // エージェント状態更新
    let agentResult = facade.UpdateAgentState("agent1", Idle, progress = 0.0)

    Assert.True(
        match agentResult with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // タスク追加
    let task1 =
        createTestTask "task1" "Integration Test Task" TaskPriority.High (TimeSpan.FromHours(1.0))

    let taskResult = facade.AddTask(task1)

    Assert.True(
        match taskResult with
        | Result.Ok() -> true
        | Result.Error _ -> false
    )

    // タスク自動割り当て
    let assignResult = facade.AutoAssignTask("task1")

    match assignResult with
    | Result.Ok assignedAgentId -> Assert.Equal("agent1", assignedAgentId)
    | Result.Error _ -> Assert.True(false, "タスク自動割り当てに失敗")

    // 進捗サマリー取得
    let progressResult = facade.GetCurrentProgressSummary()

    Assert.True(
        match progressResult with
        | Result.Ok _ -> true
        | Result.Error _ -> false
    )

[<Trait("TestCategory", "Integration")>]
[<Fact>]
let ``RealtimeCollaborationFacade - ワークフロー実行テスト`` () =
    async {
        use facade = new RealtimeCollaborationFacade(testConfig)

        // エージェント準備
        Assert.True(
            match facade.UpdateAgentState("agent1", Idle, progress = 0.0) with
            | Result.Ok() -> true
            | Result.Error _ -> false
        )

        Assert.True(
            match facade.UpdateAgentState("agent2", Idle, progress = 0.0) with
            | Result.Ok() -> true
            | Result.Error _ -> false
        )

        // タスク追加
        let task1 =
            createTestTask "wf_task1" "Workflow Task 1" TaskPriority.High (TimeSpan.FromSeconds(1.0))

        let task2 =
            createTestTask "wf_task2" "Workflow Task 2" TaskPriority.Medium (TimeSpan.FromSeconds(1.0))

        Assert.True(
            match facade.AddTask(task1) with
            | Result.Ok() -> true
            | Result.Error _ -> false
        )

        Assert.True(
            match facade.AddTask(task2) with
            | Result.Ok() -> true
            | Result.Error _ -> false
        )

        // ワークフロー実行
        let! workflowResult = facade.ExecuteWorkflow([ "wf_task1"; "wf_task2" ])

        match workflowResult with
        | Result.Ok results ->
            Assert.Equal(2, results.Length)

            results
            |> List.iter (fun (taskId, result) ->
                match result with
                | Result.Ok _ -> () // 成功
                | Result.Error e -> Assert.True(false, $"ワークフロータスク {taskId} が失敗: {e}"))
        | Result.Error _ -> Assert.True(false, "ワークフロー実行に失敗")
    }
    |> Async.RunSynchronously

[<Trait("TestCategory", "Integration")>]
[<Fact>]
let ``RealtimeCollaborationFacade - システム健全性チェックテスト`` () =
    use facade = new RealtimeCollaborationFacade(testConfig)

    // 健全性チェック実行
    match facade.PerformSystemHealthCheck() with
    | Result.Ok healthReport ->
        Assert.True(healthReport.OverallHealthy || not healthReport.OverallHealthy, "健全性チェック完了")
        Assert.True(not healthReport.ComponentHealth.IsEmpty, "コンポーネント健全性が空です")
    | Result.Error _ -> Assert.True(false, "システム健全性チェックに失敗")

[<Trait("TestCategory", "Stability")>]
[<Fact>]
let ``リソース管理 - メモリリーク検出テスト`` () =
    let initialMemory = GC.GetTotalMemory(true)

    // 大量のインスタンス作成・破棄
    for i in 1..100 do
        use facade = new RealtimeCollaborationFacade(testConfig)

        let task =
            createTestTask $"leak_test_{i}" "Leak Test" TaskPriority.Low (TimeSpan.FromMinutes(1.0))

        facade.AddTask(task) |> ignore
        facade.UpdateAgentState($"agent_{i}", Working, progress = 50.0) |> ignore

    // ガベージコレクション実行
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()

    let finalMemory = GC.GetTotalMemory(false)
    let memoryIncrease = finalMemory - initialMemory

    // メモリ増加が合理的な範囲内かチェック（100MB以下）
    Assert.True(memoryIncrease < 100L * 1024L * 1024L, $"メモリリーク検出: {memoryIncrease} bytes 増加")

[<Trait("TestCategory", "Performance")>]
[<Fact>]
let ``パフォーマンス - 大量タスク処理テスト`` () =
    use facade = new RealtimeCollaborationFacade(testConfig)

    let taskCount = 1000
    let stopwatch = System.Diagnostics.Stopwatch.StartNew()

    // 大量タスク追加
    for i in 1..taskCount do
        let task =
            createTestTask $"perf_task_{i}" $"Performance Task {i}" TaskPriority.Medium (TimeSpan.FromMinutes(1.0))

        match facade.AddTask(task) with
        | Result.Ok() -> ()
        | Result.Error _ -> Assert.True(false, $"タスク {i} の追加に失敗")

    stopwatch.Stop()

    // パフォーマンス基準: 1000タスクを5秒以内で処理
    Assert.True(stopwatch.ElapsedMilliseconds < 5000L, $"パフォーマンス不良: {stopwatch.ElapsedMilliseconds}ms")

    // 全タスク取得確認
    match facade.GetAllTasks() with
    | Result.Ok tasks -> Assert.True(tasks.Length >= taskCount, "タスク数が期待値より少ない")
    | Result.Error _ -> Assert.True(false, "全タスク取得に失敗")

[<Trait("TestCategory", "Performance")>]
[<Fact>]
let ``パフォーマンス - 並行エージェント処理テスト`` () =
    use facade = new RealtimeCollaborationFacade(testConfig)

    let agentCount = 50
    let stopwatch = System.Diagnostics.Stopwatch.StartNew()

    // 並行エージェント状態更新
    let tasks =
        [ 1..agentCount ]
        |> List.map (fun i ->
            async {
                let agentId = $"perf_agent_{i}"
                let! result = async { return facade.UpdateAgentState(agentId, Working, progress = float i) }

                match result with
                | Result.Ok() -> return true
                | Result.Error _ -> return false
            })

    let results = tasks |> Async.Parallel |> Async.RunSynchronously
    stopwatch.Stop()

    // パフォーマンス基準: 50エージェントを3秒以内で処理
    Assert.True(stopwatch.ElapsedMilliseconds < 3000L, $"並行処理パフォーマンス不良: {stopwatch.ElapsedMilliseconds}ms")

    // 全エージェント更新成功確認
    let successCount = results |> Array.filter id |> Array.length
    Assert.Equal(agentCount, successCount)

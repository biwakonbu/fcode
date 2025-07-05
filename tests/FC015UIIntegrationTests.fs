module FCode.Tests.FC015UIIntegrationTests

open System
open System.Threading
open Xunit
open Terminal.Gui
open FCode.RealtimeUIIntegration
open FCode.FullWorkflowCoordinator
open FCode.UnifiedActivityView
open FCode.Logger

/// CI環境判定
let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

/// テスト用TextView作成
let createTestTextView () =
    if isCI then
        // CI環境: モックTextView
        null
    else
        new TextView()

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``RealtimeUIIntegrationManager - 基本作成テスト`` () =
    let manager = new RealtimeUIIntegrationManager()
    Assert.NotNull(manager)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``RealtimeUIIntegrationManager - UIコンポーネント登録テスト`` () =
    if not isCI then
        Application.Init()

        try
            use manager = new RealtimeUIIntegrationManager()

            // テスト用TextView作成
            let conversationView = new TextView()
            let pmTimelineView = new TextView()
            let qa1View = new TextView()
            let uxView = new TextView()

            let agentViews =
                [ ("dev1", new TextView()); ("dev2", new TextView()) ] |> Map.ofList

            // UIコンポーネント登録
            manager.RegisterUIComponents(conversationView, pmTimelineView, qa1View, uxView, agentViews)

            // テスト成功
            Assert.True(true)

        finally
            if not isCI then
                Application.Shutdown()
    else
        // CI環境では基本チェックのみ
        let manager = new RealtimeUIIntegrationManager()
        Assert.NotNull(manager)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``FullWorkflowCoordinator - 基本作成テスト`` () =
    let coordinator = new FullWorkflowCoordinator()
    Assert.NotNull(coordinator)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``FullWorkflowCoordinator - ワークフロー開始テスト`` () =
    use coordinator = new FullWorkflowCoordinator()

    let instructions = [ "ECサイトのカート機能を改善する"; "ユーザビリティテストを実装する" ]

    let result = coordinator.StartWorkflow(instructions) |> Async.RunSynchronously

    match result with
    | Result.Ok message ->
        Assert.Contains("ワークフロー開始完了", message)

        // ワークフロー状態確認
        let state = coordinator.GetCurrentWorkflowState()
        Assert.True(state.IsSome)

        match state with
        | Some workflow ->
            Assert.Equal<string list>(instructions, workflow.Instructions)
            Assert.Equal(WorkflowStage.Instruction, workflow.Stage)
            Assert.False(workflow.IsCompleted)
        | None -> Assert.True(false, "ワークフロー状態が取得できません")

    | Result.Error error -> Assert.True(false, $"ワークフロー開始に失敗: {error}")

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``FullWorkflowCoordinator - 緊急停止テスト`` () =
    use coordinator = new FullWorkflowCoordinator()

    // ワークフロー開始
    let instructions = [ "テスト用ワークフロー" ]
    let startResult = coordinator.StartWorkflow(instructions) |> Async.RunSynchronously
    Assert.True(Result.isOk startResult)

    // 緊急停止実行
    let stopResult = coordinator.EmergencyStop("テスト用緊急停止") |> Async.RunSynchronously

    match stopResult with
    | Result.Ok message ->
        Assert.Contains("緊急停止完了", message)

        // ワークフロー状態確認（停止後はNone）
        let state = coordinator.GetCurrentWorkflowState()
        Assert.True(state.IsNone)

    | Result.Error error -> Assert.True(false, $"緊急停止に失敗: {error}")

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``FullWorkflowCoordinator - ワークフロー状態管理テスト`` () =
    use coordinator = new FullWorkflowCoordinator()

    // 初期状態（ワークフローなし）
    let initialState = coordinator.GetCurrentWorkflowState()
    Assert.True(initialState.IsNone)

    // ワークフロー開始
    let instructions = [ "状態管理テスト" ]
    let _ = coordinator.StartWorkflow(instructions) |> Async.RunSynchronously

    // ワークフロー状態確認
    let activeState = coordinator.GetCurrentWorkflowState()
    Assert.True(activeState.IsSome)

    match activeState with
    | Some workflow ->
        Assert.Contains("sprint-", workflow.SprintId)
        Assert.True(workflow.StartTime <= DateTime.UtcNow)
        Assert.Equal<Map<string, string list>>(Map.empty, workflow.AssignedTasks)
    | None -> Assert.True(false, "アクティブなワークフロー状態が取得できません")

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``WorkflowStage - 全段階定義確認テスト`` () =
    let stages =
        [ WorkflowStage.Instruction
          WorkflowStage.TaskDecomposition
          WorkflowStage.SprintExecution
          WorkflowStage.QualityAssessment
          WorkflowStage.ContinuationDecision
          WorkflowStage.Completion ]

    // 全段階が定義されていることを確認
    Assert.Equal(6, stages.Length)

    // 各段階の文字列表現確認
    for stage in stages do
        let stageString = stage.ToString()
        Assert.False(String.IsNullOrEmpty(stageString))

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``WorkflowState - レコード構造確認テスト`` () =
    let testState =
        { Stage = WorkflowStage.Instruction
          SprintId = "test-sprint-001"
          StartTime = DateTime.UtcNow
          Instructions = [ "テスト指示1"; "テスト指示2" ]
          AssignedTasks = Map.ofList [ ("dev1", [ "task1"; "task2" ]); ("qa1", [ "task3" ]) ]
          IsCompleted = false }

    // レコードフィールド確認
    Assert.Equal(WorkflowStage.Instruction, testState.Stage)
    Assert.Equal("test-sprint-001", testState.SprintId)
    Assert.Equal(2, testState.Instructions.Length)
    Assert.Equal(2, testState.AssignedTasks.Count)
    Assert.False(testState.IsCompleted)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``RealtimeUIIntegrationManager - Dispose安全性テスト`` () =
    let manager = new RealtimeUIIntegrationManager()

    // 正常なDispose
    manager.Dispose()

    // 二重Dispose（例外が発生しないことを確認）
    manager.Dispose()

    Assert.True(true) // Disposeで例外が発生しなければ成功

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``FullWorkflowCoordinator - Dispose安全性テスト`` () =
    let coordinator = new FullWorkflowCoordinator()

    // ワークフロー開始してからDispose
    let _ = coordinator.StartWorkflow([ "Disposeテスト" ]) |> Async.RunSynchronously

    // 正常なDispose
    coordinator.Dispose()

    // 二重Dispose（例外が発生しないことを確認）
    coordinator.Dispose()

    Assert.True(true) // Disposeで例外が発生しなければ成功

[<Fact>]
[<Trait("TestCategory", "Integration")>]
let ``FC015 統合動作テスト - UI統合・ワークフロー連携`` () =
    if not isCI then
        Application.Init()

        try
            use uiManager = new RealtimeUIIntegrationManager()
            use workflowCoordinator = new FullWorkflowCoordinator()

            // UI統合設定
            let conversationView = new TextView()
            let pmTimelineView = new TextView()
            let qa1View = new TextView()
            let uxView = new TextView()
            let agentViews = [ ("dev1", new TextView()); ("qa1", new TextView()) ] |> Map.ofList

            uiManager.RegisterUIComponents(conversationView, pmTimelineView, qa1View, uxView, agentViews)

            // ワークフロー開始
            let instructions = [ "統合テスト用指示" ]

            let result =
                workflowCoordinator.StartWorkflow(instructions) |> Async.RunSynchronously

            Assert.True(Result.isOk result)

            // 統合動作確認
            let state = workflowCoordinator.GetCurrentWorkflowState()
            Assert.True(state.IsSome)

        finally
            if not isCI then
                Application.Shutdown()
    else
        // CI環境では基本チェックのみ
        use uiManager = new RealtimeUIIntegrationManager()
        use workflowCoordinator = new FullWorkflowCoordinator()
        Assert.NotNull(uiManager)
        Assert.NotNull(workflowCoordinator)

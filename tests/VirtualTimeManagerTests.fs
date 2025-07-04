module FCode.Tests.VirtualTimeManagerTests

open System
open Xunit
open FCode.VirtualTimeCoordinator
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.AgentStateManager
open FCode.Collaboration.TaskDependencyGraph
open FCode.Collaboration.ProgressAggregator
open FCode.Collaboration.TimeCalculationManager
open FCode.Collaboration.MeetingScheduler
open FCode.Collaboration.EventProcessor

// テスト用の依存関係モック
let createTestDependencies () =
    let config = CollaborationConfig.Default
    let agentStateManager = new AgentStateManager(config)
    let taskDependencyGraph = new TaskDependencyGraph(config)

    let progressAggregator =
        new ProgressAggregator(agentStateManager, taskDependencyGraph, config)

    (agentStateManager, taskDependencyGraph, progressAggregator)

let createVirtualTimeManager () =
    let (agentStateManager, taskDependencyGraph, progressAggregator) =
        createTestDependencies ()

    let config = VirtualTimeConfig.Default
    let timeCalculationManager = new TimeCalculationManager(config)
    let meetingScheduler = new MeetingScheduler(timeCalculationManager, config)

    let eventProcessor =
        new EventProcessor(timeCalculationManager, meetingScheduler, config)

    new VirtualTimeCoordinator(timeCalculationManager, meetingScheduler, eventProcessor, config)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeCoordinator - 基本作成テスト`` () =
    use manager = createVirtualTimeManager ()
    Assert.NotNull(manager)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeCoordinator - スプリント開始テスト`` () =
    use manager = createVirtualTimeManager ()

    let vtManager =
        manager :> FCode.Collaboration.IVirtualTimeManager.IVirtualTimeManager

    let sprintId = "test-sprint-1"

    // スプリント開始
    let startResult = vtManager.StartSprint(sprintId) |> Async.RunSynchronously

    Assert.True(
        match startResult with
        | Result.Ok _ -> true
        | _ -> false
    )

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeCoordinator - アクティブスプリント取得テスト`` () =
    use manager = createVirtualTimeManager ()

    let vtManager =
        manager :> FCode.Collaboration.IVirtualTimeManager.IVirtualTimeManager

    // アクティブスプリント確認（空でもOK）
    let activeSprintsResult = vtManager.GetActiveSprints() |> Async.RunSynchronously

    match activeSprintsResult with
    | Result.Ok sprints -> Assert.True(sprints.Length >= 0)
    | Result.Error _ -> Assert.True(false, "アクティブスプリント取得失敗")

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeCoordinator - 健全性チェックテスト`` () =
    use manager = createVirtualTimeManager ()

    let vtManager =
        manager :> FCode.Collaboration.IVirtualTimeManager.IVirtualTimeManager

    // 健全性チェック
    let healthResult = vtManager.PerformHealthCheck() |> Async.RunSynchronously

    match healthResult with
    | Result.Ok(isHealthy, message) ->
        Assert.True(isHealthy)
        Assert.False(String.IsNullOrEmpty(message))
    | Result.Error _ -> Assert.True(false, "健全性チェック失敗")

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``TimeCalculationManager - 時間計算テスト`` () =
    let config = VirtualTimeConfig.Default
    let timeManager = new TimeCalculationManager(config)

    // 実時間から仮想時間計算
    let realElapsed = TimeSpan.FromMinutes(30.0)
    let virtualTime = timeManager.CalculateVirtualTime(realElapsed)

    match virtualTime with
    | VirtualHour 30 -> Assert.True(true)
    | _ -> Assert.True(false, sprintf "予期しない仮想時間: %A" virtualTime)

    // 仮想時間から実時間計算
    let realDuration = timeManager.CalculateRealDuration(VirtualHour 60)
    Assert.Equal(TimeSpan.FromMinutes(60.0), realDuration)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``MeetingScheduler - 基本作成テスト`` () =
    let config = VirtualTimeConfig.Default
    let timeManager = new TimeCalculationManager(config)
    let meetingScheduler = new MeetingScheduler(timeManager, config)

    Assert.NotNull(meetingScheduler)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``EventProcessor - 基本作成テスト`` () =
    let config = VirtualTimeConfig.Default
    let timeManager = new TimeCalculationManager(config)
    let meetingScheduler = new MeetingScheduler(timeManager, config)
    let eventProcessor = new EventProcessor(timeManager, meetingScheduler, config)

    Assert.NotNull(eventProcessor)

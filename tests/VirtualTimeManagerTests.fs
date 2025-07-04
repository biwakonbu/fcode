module FCode.Tests.VirtualTimeManagerTests

open System
open Xunit
open FCode.VirtualTimeManager
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.AgentStateManager
open FCode.Collaboration.TaskDependencyGraph
open FCode.Collaboration.ProgressAggregator

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
    new VirtualTimeManager(agentStateManager, taskDependencyGraph, progressAggregator, config)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeManager - スプリント開始・停止テスト`` () =
    use manager = createVirtualTimeManager ()
    let sprintId = "test-sprint-1"

    // スプリント開始
    let startResult = manager.StartSprint(sprintId) |> Async.RunSynchronously

    Assert.True(
        match startResult with
        | Result.Ok _ -> true
        | _ -> false
    )

    // アクティブスプリント確認
    let activeSprintsResult = manager.GetActiveSprints() |> Async.RunSynchronously

    match activeSprintsResult with
    | Result.Ok sprints -> Assert.True(sprints |> List.exists (fun s -> s.IsActive))
    | Result.Error _ -> Assert.True(false, "アクティブスプリント取得失敗")

    // スプリント停止
    let stopResult = manager.StopSprint(sprintId) |> Async.RunSynchronously

    Assert.True(
        match stopResult with
        | Result.Ok _ -> true
        | _ -> false
    )

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeManager - 仮想時間計算テスト`` () =
    use manager = createVirtualTimeManager ()

    // 1時間 = 60分リアルタイム → 60vh
    let realTime60Min = TimeSpan.FromMinutes(60.0)
    let virtualTime = manager.CalculateVirtualTime(realTime60Min)

    match virtualTime with
    | VirtualHour 60 -> Assert.True(true)
    | _ -> Assert.True(false, sprintf "期待値: VirtualHour 60, 実際: %A" virtualTime)

    // 25分 = 25vh → 1vd + 1vh
    let realTime25Min = TimeSpan.FromMinutes(25.0)
    let virtualTime25 = manager.CalculateVirtualTime(realTime25Min)

    match virtualTime25 with
    | VirtualDay 1 -> Assert.True(true)
    | VirtualHour 25 -> Assert.True(true)
    | _ -> Assert.True(false, sprintf "期待値: VirtualDay 1 or VirtualHour 25, 実際: %A" virtualTime25)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeManager - スタンドアップ実行テスト`` () =
    use manager = createVirtualTimeManager ()
    let meetingId = "test-meeting-1"

    let progressReports =
        [ ("dev1", "タスク1完了、タスク2進行中"); ("dev2", "設計レビュー完了、実装開始"); ("qa1", "テスト環境準備完了") ]

    let result =
        manager.ExecuteStandup(meetingId, progressReports) |> Async.RunSynchronously

    match result with
    | Result.Ok meeting ->
        Assert.Equal(meetingId, meeting.MeetingId)
        Assert.Equal(progressReports.Length, meeting.ProgressReports.Length)
        Assert.Equal(progressReports.Length, meeting.Participants.Length)
        Assert.True(meeting.Decisions.Length > 0)
    | Result.Error error -> Assert.True(false, sprintf "スタンドアップ実行失敗: %A" error)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeManager - 完成度評価テスト`` () =
    use manager = createVirtualTimeManager ()
    let sprintId = "test-sprint-assessment"
    let taskIds = [ "task1"; "task2"; "task3"; "task4"; "task5" ]

    let result = manager.AssessCompletion(sprintId, taskIds) |> Async.RunSynchronously

    match result with
    | Result.Ok assessment ->
        Assert.True(assessment.TasksCompleted >= 0)
        Assert.True(assessment.TasksInProgress >= 0)
        Assert.True(assessment.TasksBlocked >= 0)

        Assert.True(
            assessment.OverallCompletionRate >= 0.0
            && assessment.OverallCompletionRate <= 1.0
        )

        Assert.True(assessment.QualityScore >= 0.0 && assessment.QualityScore <= 1.0)
    | Result.Error error -> Assert.True(false, sprintf "完成度評価失敗: %A" error)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeManager - 継続判定テスト`` () =
    use manager = createVirtualTimeManager ()
    let sprintId = "test-sprint-continuation"

    // 高品質完成の場合
    let highQualityAssessment =
        { TasksCompleted = 9
          TasksInProgress = 1
          TasksBlocked = 0
          OverallCompletionRate = 0.9
          QualityScore = 0.95
          AcceptanceCriteriaMet = true
          RequiresPOApproval = false }

    let result1 =
        manager.DecideContinuation(sprintId, highQualityAssessment)
        |> Async.RunSynchronously

    match result1 with
    | Result.Ok decision ->
        match decision with
        | AutoContinue _ -> Assert.True(true)
        | _ -> Assert.True(false, sprintf "期待値: AutoContinue, 実際: %A" decision)
    | Result.Error error -> Assert.True(false, sprintf "継続判定失敗: %A" error)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeManager - 継続判定: 低品質ケース`` () =
    use manager = createVirtualTimeManager ()
    let sprintId = "test-sprint-low-quality"

    // 低品質の場合
    let lowQualityAssessment =
        { TasksCompleted = 3
          TasksInProgress = 2
          TasksBlocked = 5
          OverallCompletionRate = 0.3
          QualityScore = 0.4 // 低品質
          AcceptanceCriteriaMet = false
          RequiresPOApproval = true }

    let result =
        manager.DecideContinuation(sprintId, lowQualityAssessment)
        |> Async.RunSynchronously

    match result with
    | Result.Ok decision ->
        match decision with
        | RequirePOApproval _ -> Assert.True(true)
        | StopExecution _ -> Assert.True(true)
        | _ -> Assert.True(false, sprintf "期待値: RequirePOApproval or StopExecution, 実際: %A" decision)
    | Result.Error error -> Assert.True(false, sprintf "継続判定失敗: %A" error)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeManager - 継続判定: ブロックタスク多数ケース`` () =
    use manager = createVirtualTimeManager ()
    let sprintId = "test-sprint-blocked"

    // ブロックタスクが多い場合
    let blockedAssessment =
        { TasksCompleted = 2
          TasksInProgress = 1
          TasksBlocked = 7 // ブロックタスク多数
          OverallCompletionRate = 0.2
          QualityScore = 0.8 // 品質は良い
          AcceptanceCriteriaMet = false
          RequiresPOApproval = true }

    let result =
        manager.DecideContinuation(sprintId, blockedAssessment)
        |> Async.RunSynchronously

    match result with
    | Result.Ok decision ->
        match decision with
        | StopExecution _ -> Assert.True(true) // ブロックタスク多数の場合は停止が正しい
        | RequirePOApproval _ -> Assert.True(true)
        | _ -> Assert.True(false, sprintf "期待値: StopExecution or RequirePOApproval, 実際: %A" decision)
    | Result.Error error -> Assert.True(false, sprintf "継続判定失敗: %A" error)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeManager - イベント管理テスト`` () =
    use manager = createVirtualTimeManager ()
    let sprintId = "test-sprint-events"

    // イベント登録
    let event1 = StandupScheduled(VirtualHour 6, [ "dev1"; "dev2" ])
    let event2 = ReviewMeetingTriggered(VirtualSprint 1)

    let registerResult1 =
        manager.RegisterTimeEvent(sprintId, event1) |> Async.RunSynchronously

    let registerResult2 =
        manager.RegisterTimeEvent(sprintId, event2) |> Async.RunSynchronously

    Assert.True(
        match registerResult1 with
        | Result.Ok _ -> true
        | _ -> false
    )

    Assert.True(
        match registerResult2 with
        | Result.Ok _ -> true
        | _ -> false
    )

    // 保留中イベント取得
    let eventsResult = manager.GetPendingEvents(sprintId) |> Async.RunSynchronously

    match eventsResult with
    | Result.Ok events ->
        Assert.Equal(2, events.Length)

        Assert.True(
            events
            |> List.exists (function
                | StandupScheduled _ -> true
                | _ -> false)
        )

        Assert.True(
            events
            |> List.exists (function
                | ReviewMeetingTriggered _ -> true
                | _ -> false)
        )
    | Result.Error error -> Assert.True(false, sprintf "保留中イベント取得失敗: %A" error)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeManager - システム健全性チェックテスト`` () =
    use manager = createVirtualTimeManager ()

    let healthResult = manager.PerformHealthCheck() |> Async.RunSynchronously

    match healthResult with
    | Result.Ok(healthy, message) ->
        Assert.True(healthy) // 初期状態では健全
        Assert.True(message.Contains("正常"))
    | Result.Error error -> Assert.True(false, sprintf "健全性チェック失敗: %A" error)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``VirtualTimeManager - エラーハンドリングテスト`` () =
    use manager = createVirtualTimeManager ()
    let nonExistentSprintId = "non-existent-sprint"

    // 存在しないスプリントに対する操作
    let currentTimeResult =
        manager.GetCurrentVirtualTime(nonExistentSprintId) |> Async.RunSynchronously

    match currentTimeResult with
    | Result.Error(NotFound _) -> Assert.True(true)
    | _ -> Assert.True(false, "存在しないスプリントでNotFoundエラーが期待される")

    let stopResult = manager.StopSprint(nonExistentSprintId) |> Async.RunSynchronously

    match stopResult with
    | Result.Error(NotFound _) -> Assert.True(true)
    | _ -> Assert.True(false, "存在しないスプリント停止でNotFoundエラーが期待される")

[<Fact>]
[<Trait("TestCategory", "Integration")>]
let ``VirtualTimeManager - 複数スプリント並行処理テスト`` () =
    use manager = createVirtualTimeManager ()
    let sprintIds = [ "sprint-1"; "sprint-2"; "sprint-3" ]

    // 複数スプリント同時開始
    let startResults =
        sprintIds
        |> List.map (fun id -> manager.StartSprint(id) |> Async.RunSynchronously)

    startResults
    |> List.iter (fun result ->
        Assert.True(
            match result with
            | Result.Ok _ -> true
            | _ -> false
        ))

    // アクティブスプリント確認
    let activeSprintsResult = manager.GetActiveSprints() |> Async.RunSynchronously

    match activeSprintsResult with
    | Result.Ok sprints -> Assert.Equal(sprintIds.Length, sprints.Length)
    | Result.Error error -> Assert.True(false, sprintf "アクティブスプリント取得失敗: %A" error)

    // 全スプリント停止
    let stopResults =
        sprintIds
        |> List.map (fun id -> manager.StopSprint(id) |> Async.RunSynchronously)

    stopResults
    |> List.iter (fun result ->
        Assert.True(
            match result with
            | Result.Ok _ -> true
            | _ -> false
        ))

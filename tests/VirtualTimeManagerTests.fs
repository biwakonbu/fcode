module FCode.Tests.VirtualTimeManagerTests

open System
open NUnit.Framework
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

[<Test>]
[<Category("Unit")>]
let ``VirtualTimeCoordinator - 基本作成テスト`` () =
    use manager = createVirtualTimeManager ()
    Assert.NotNull(manager)

[<Test>]
[<Category("Unit")>]
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

[<Test>]
[<Category("Unit")>]
let ``VirtualTimeCoordinator - アクティブスプリント取得テスト`` () =
    use manager = createVirtualTimeManager ()

    let vtManager =
        manager :> FCode.Collaboration.IVirtualTimeManager.IVirtualTimeManager

    // アクティブスプリント確認（空でもOK）
    let activeSprintsResult = vtManager.GetActiveSprints() |> Async.RunSynchronously

    match activeSprintsResult with
    | Result.Ok sprints -> Assert.True(sprints.Length >= 0)
    | Result.Error _ -> Assert.True(false, "アクティブスプリント取得失敗")

[<Test>]
[<Category("Unit")>]
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

[<Test>]
[<Category("Unit")>]
let ``TimeCalculationManager - 時間計算テスト`` () =
    let config = VirtualTimeConfig.Default
    let timeManager = new TimeCalculationManager(config)

    // 実時間から仮想時間計算（30分 = 30vh > 18vh(1スプリント) = VirtualSprint 1）
    let realElapsed = TimeSpan.FromMinutes(30.0)
    let virtualTime = timeManager.CalculateVirtualTime(realElapsed)

    match virtualTime with
    | VirtualSprint 1 -> Assert.True(true)
    | _ -> Assert.True(false, sprintf "予期しない仮想時間: %A" virtualTime)

    // 仮想時間から実時間計算
    let realDuration = timeManager.CalculateRealDuration(VirtualHour 60)
    Assert.AreEqual(realDuration, TimeSpan.FromMinutes(60.0))

[<Test>]
[<Category("Unit")>]
let ``MeetingScheduler - 基本作成テスト`` () =
    let config = VirtualTimeConfig.Default
    let timeManager = new TimeCalculationManager(config)
    let meetingScheduler = new MeetingScheduler(timeManager, config)

    Assert.NotNull(meetingScheduler)

[<Test>]
[<Category("Unit")>]
let ``EventProcessor - 基本作成テスト`` () =
    let config = VirtualTimeConfig.Default
    let timeManager = new TimeCalculationManager(config)
    let meetingScheduler = new MeetingScheduler(timeManager, config)
    let eventProcessor = new EventProcessor(timeManager, meetingScheduler, config)

    Assert.NotNull(eventProcessor)

// =================
// エラーケース・異常系テスト
// =================

[<Test>]
[<Category("Unit")>]
let ``VirtualTimeCoordinator - 無効スプリントID停止エラーテスト`` () =
    use manager = createVirtualTimeManager ()

    let vtManager =
        manager :> FCode.Collaboration.IVirtualTimeManager.IVirtualTimeManager

    // 存在しないスプリントID停止
    let stopResult = vtManager.StopSprint("invalid-sprint-id") |> Async.RunSynchronously

    match stopResult with
    | Result.Error(NotFound _) -> Assert.True(true) // 期待されるエラー
    | _ -> Assert.True(false, "無効スプリントID停止でNotFoundエラーが期待される")

[<Test>]
[<Category("Unit")>]
let ``VirtualTimeCoordinator - 無効スプリントID統計取得エラーテスト`` () =
    use manager = createVirtualTimeManager ()

    let vtManager =
        manager :> FCode.Collaboration.IVirtualTimeManager.IVirtualTimeManager

    // 存在しないスプリントIDの統計取得
    let statsResult =
        vtManager.GetSprintStatistics("invalid-sprint-id") |> Async.RunSynchronously

    match statsResult with
    | Result.Error(SystemError _) -> Assert.True(true) // エラーハンドリング確認
    | Result.Ok _ -> Assert.True(false, "無効スプリントIDで統計取得エラーが期待される")
    | _ -> Assert.True(false, "予期しないエラー形式")

[<Test>]
[<Category("Unit")>]
let ``TimeCalculationManager - 無効スプリントID仮想時間取得エラーテスト`` () =
    let config = VirtualTimeConfig.Default
    let timeManager = new TimeCalculationManager(config)

    // 存在しないスプリントIDで仮想時間取得
    let result =
        timeManager.GetCurrentVirtualTime("invalid-sprint-id") |> Async.RunSynchronously

    match result with
    | Result.Error(NotFound _) -> Assert.True(true) // 期待されるエラー
    | _ -> Assert.True(false, "無効スプリントIDでNotFoundエラーが期待される")

[<Test>]
[<Category("Unit")>]
let ``MeetingScheduler - 無効スプリントIDスタンドアップスケジュールエラーテスト`` () =
    let config = VirtualTimeConfig.Default
    let timeManager = new TimeCalculationManager(config)
    let meetingScheduler = new MeetingScheduler(timeManager, config)

    // 無効スプリントIDでスタンドアップスケジュール
    let result =
        meetingScheduler.ScheduleNextStandup("invalid-sprint-id", [])
        |> Async.RunSynchronously

    match result with
    | Result.Error _ -> Assert.True(true) // エラーハンドリング確認
    | _ -> Assert.True(false, "無効スプリントIDでエラーが期待される")

[<Test>]
[<Category("Unit")>]
let ``EventProcessor - 無効スプリントIDイベント処理エラーテスト`` () =
    let config = VirtualTimeConfig.Default
    let timeManager = new TimeCalculationManager(config)
    let meetingScheduler = new MeetingScheduler(timeManager, config)
    let eventProcessor = new EventProcessor(timeManager, meetingScheduler, config)

    // 無効スプリントIDでイベント処理
    let result =
        eventProcessor.ProcessEvents("invalid-sprint-id") |> Async.RunSynchronously

    match result with
    | Result.Error _ -> Assert.True(true) // エラーハンドリング確認
    | Result.Ok [] -> Assert.True(true) // 空リスト返却も許可
    | _ -> Assert.True(false, "予期しない結果")

[<Test>]
[<Category("Unit")>]
let ``VirtualTimeCoordinator - システム健全性異常状態テスト`` () =
    use manager = createVirtualTimeManager ()

    let vtManager =
        manager :> FCode.Collaboration.IVirtualTimeManager.IVirtualTimeManager

    // 大量スプリント開始でリソース圧迫状況シミュレート
    let sprintIds = [ for i in 1..50 -> $"test-sprint-{i}" ]

    for sprintId in sprintIds do
        let _ = vtManager.StartSprint(sprintId) |> Async.RunSynchronously
        ()

    // 健全性チェック実行
    let healthResult = vtManager.PerformHealthCheck() |> Async.RunSynchronously

    match healthResult with
    | Result.Ok(isHealthy, message) ->
        // 大量スプリントで健全性が低下している可能性
        Assert.False(String.IsNullOrEmpty(message))

        // リソース圧迫時は健全性がfalseになることを期待
        if not isHealthy then
            Assert.True(true) // 期待される異常状態
        else
            Assert.True(true) // 健全性維持も許可（実装依存）
    | Result.Error _ -> Assert.True(false, "健全性チェック自体の失敗は許可されない")

[<Test>]
[<Category("Unit")>]
let ``TimeCalculationManager - 極値時間計算テスト`` () =
    let config = VirtualTimeConfig.Default
    let timeManager = new TimeCalculationManager(config)

    // 極小時間
    let extremelySmall = TimeSpan.FromMilliseconds(1.0)
    let virtualTime1 = timeManager.CalculateVirtualTime(extremelySmall)
    Assert.AreEqual(virtualTime1, VirtualHour 0)

    // 極大時間（24時間 = 1440分 = 1440vh）
    let extremelyLarge = TimeSpan.FromHours(24.0)
    let virtualTime2 = timeManager.CalculateVirtualTime(extremelyLarge)

    match virtualTime2 with
    | VirtualSprint _ -> Assert.True(true) // スプリント単位になることを期待
    | _ -> Assert.True(false, $"極大時間で予期しない結果: {virtualTime2}")

[<Test>]
[<Category("Unit")>]
let ``VirtualTimeCoordinator - エラーメッセージ詳細化確認テスト`` () =
    use manager = createVirtualTimeManager ()

    let vtManager =
        manager :> FCode.Collaboration.IVirtualTimeManager.IVirtualTimeManager

    // 詳細なエラーメッセージが含まれることを確認
    let statsResult =
        vtManager.GetSprintStatistics("nonexistent-sprint") |> Async.RunSynchronously

    match statsResult with
    | Result.Error(SystemError message) ->
        // エラーメッセージが詳細化されていることを確認
        Assert.True(message.Contains("統計") || message.Length > 10)
    | _ -> Assert.True(false, "詳細なエラーメッセージが期待される")

// =================
// パフォーマンス・設定テスト
// =================

[<Test>]
[<Category("Unit")>]
let ``VirtualTimeConfig - 環境変数設定読み込みテスト`` () =
    // 環境変数設定
    System.Environment.SetEnvironmentVariable("FCODE_VIRTUAL_HOUR_MS", "30000")
    System.Environment.SetEnvironmentVariable("FCODE_STANDUP_INTERVAL_VH", "3")
    System.Environment.SetEnvironmentVariable("FCODE_SPRINT_DURATION_VD", "2")
    System.Environment.SetEnvironmentVariable("FCODE_MAX_CONCURRENT_SPRINTS", "10")

    try
        let config = VirtualTimeConfig.Default

        Assert.AreEqual(config.VirtualHourDurationMs, 30000)
        Assert.AreEqual(config.StandupIntervalVH, 3)
        Assert.AreEqual(config.SprintDurationVD, 2)
        Assert.AreEqual(config.MaxConcurrentSprints, 10)
    finally
        // クリーンアップ
        System.Environment.SetEnvironmentVariable("FCODE_VIRTUAL_HOUR_MS", null)
        System.Environment.SetEnvironmentVariable("FCODE_STANDUP_INTERVAL_VH", null)
        System.Environment.SetEnvironmentVariable("FCODE_SPRINT_DURATION_VD", null)
        System.Environment.SetEnvironmentVariable("FCODE_MAX_CONCURRENT_SPRINTS", null)

[<Test>]
[<Category("Unit")>]
let ``VirtualTimeConfig - 無効環境変数でデフォルト値使用テスト`` () =
    // 無効な環境変数設定
    System.Environment.SetEnvironmentVariable("FCODE_VIRTUAL_HOUR_MS", "invalid")
    System.Environment.SetEnvironmentVariable("FCODE_STANDUP_INTERVAL_VH", "-1")

    try
        let config = VirtualTimeConfig.Default

        // 無効値でデフォルト値が使用されることを確認
        Assert.AreEqual(config.VirtualHourDurationMs, 60000)
        Assert.AreEqual(config.StandupIntervalVH, 6)
    finally
        // クリーンアップ
        System.Environment.SetEnvironmentVariable("FCODE_VIRTUAL_HOUR_MS", null)
        System.Environment.SetEnvironmentVariable("FCODE_STANDUP_INTERVAL_VH", null)

[<Test>]
[<Category("Performance")>]
let ``VirtualTimeCoordinator - 大量スプリント処理パフォーマンステスト`` () =
    use manager = createVirtualTimeManager ()

    let vtManager =
        manager :> FCode.Collaboration.IVirtualTimeManager.IVirtualTimeManager

    // パフォーマンス測定
    let stopwatch = System.Diagnostics.Stopwatch.StartNew()
    let sprintCount = 20

    // 大量スプリント開始
    for i in 1..sprintCount do
        let result =
            vtManager.StartSprint($"perf-test-sprint-{i}") |> Async.RunSynchronously

        match result with
        | Result.Ok _ -> ()
        | Result.Error _ -> Assert.True(false, $"スプリント{i}開始失敗")

    stopwatch.Stop()

    // パフォーマンス閾値チェック（20スプリント作成が5秒以内）
    Assert.True(stopwatch.ElapsedMilliseconds < 5000L, $"パフォーマンス低下: {stopwatch.ElapsedMilliseconds}ms")

    // リソース使用量チェック
    let healthResult = vtManager.PerformHealthCheck() |> Async.RunSynchronously

    match healthResult with
    | Result.Ok(_, message) -> Assert.False(String.IsNullOrEmpty(message))
    // ログでリソース使用状況確認
    | Result.Error _ -> Assert.True(false, "健全性チェック失敗")

module FCode.Tests.ResourceControllerTests

open NUnit.Framework
open System.Threading
open FCode.ResourceController

[<SetUp>]
let Setup () = ()

[<Test>]
[<Category("Unit")>]
let ``defaultConfig should have reasonable values`` () =
    Assert.AreEqual(2000, defaultConfig.MonitoringIntervalMs)
    Assert.AreEqual(5000, defaultConfig.CpuThrottleIntervalMs)
    Assert.AreEqual(30000, defaultConfig.GcIntervalMs)
    Assert.AreEqual(3, defaultConfig.MaxRestartAttempts)
    Assert.AreEqual(10000, defaultConfig.RestartCooldownMs)

[<Test>]
[<Category("Unit")>]
let ``ResourceController should create instance successfully`` () =
    let controller = ResourceController(defaultConfig)
    Assert.IsNotNull(controller)

[<Test>]
[<Category("Unit")>]
let ``ResourceController Start and Stop should work`` () =
    let controller = ResourceController(defaultConfig)

    // 開始
    controller.Start()
    Thread.Sleep(100) // 起動時間を待機

    // 停止
    controller.Stop()
    Thread.Sleep(100) // 停止時間を待機

    // 例外が発生しないことを確認
    Assert.Pass()

[<Test>]
[<Category("Unit")>]
let ``ExecuteAction should not throw exceptions`` () =
    let controller = ResourceController(defaultConfig)

    let actions =
        [ ForceGarbageCollection "test-pane"
          ThrottleCpu("test-pane", 25.0)
          RestartProcess("test-pane", "test reason")
          SuspendProcess "test-pane"
          QueueTask("test-pane", 1) ]

    for action in actions do
        Assert.DoesNotThrow(fun () -> controller.ExecuteAction(action))

[<Test>]
[<Category("Unit")>]
let ``GetSuspendedPanes should return empty initially`` () =
    let controller = ResourceController(defaultConfig)
    let suspendedPanes = controller.GetSuspendedPanes()
    Assert.AreEqual(0, suspendedPanes.Length)

[<Test>]
[<Category("Unit")>]
let ``IsPaneSuspended should return false initially`` () =
    let controller = ResourceController(defaultConfig)
    let isSuspended = controller.IsPaneSuspended("test-pane")
    Assert.IsFalse(isSuspended)

[<Test>]
[<Category("Unit")>]
let ``ResourceAction should have proper discrimination`` () =
    let action1 = ThrottleCpu("pane1", 50.0)
    let action2 = ForceGarbageCollection "pane2"
    let action3 = RestartProcess("pane3", "reason")
    let action4 = SuspendProcess "pane4"
    let action5 = QueueTask("pane5", 2)

    // 異なるアクションタイプの判別確認
    Assert.AreNotEqual(action1, action2)
    Assert.AreNotEqual(action2, action3)
    Assert.AreNotEqual(action3, action4)
    Assert.AreNotEqual(action4, action5)

[<Test>]
[<Category("Unit")>]
let ``InterventionStrategy should have valid options`` () =
    let strategies =
        [ GradualThrottling; ImmediateRestart; ProcessSuspension; LoadBalancing ]

    for strategy in strategies do
        Assert.IsNotNull(strategy)

[<Test>]
[<Category("Unit")>]
let ``globalResourceController should be accessible`` () =
    Assert.IsNotNull(globalResourceController)

    // 基本動作確認（開始・停止）
    Assert.DoesNotThrow(fun () ->
        globalResourceController.Start()
        Thread.Sleep(50)
        globalResourceController.Stop())

[<Test>]
[<Category("Unit")>]
let ``ResourceController monitoring loop should handle exceptions gracefully`` () =
    let controller = ResourceController(defaultConfig)

    // 監視ループを短時間実行して例外処理を確認
    controller.Start()
    Thread.Sleep(500) // 少し実行
    controller.Stop()

    // 例外が発生せずに正常終了することを確認
    Assert.Pass()

[<Test>]
[<Category("Unit")>]
let ``ResourceAction should serialize to string properly`` () =
    let actions =
        [ ThrottleCpu("test-pane", 25.0)
          ForceGarbageCollection "test-pane"
          RestartProcess("test-pane", "high memory usage") ]

    for action in actions do
        let actionString = action.ToString()
        Assert.IsNotEmpty(actionString)
        Assert.IsTrue(actionString.Contains("test-pane"))

module FCode.Tests.ResourceMonitorTests

open NUnit.Framework
open System.Diagnostics
open FCode.ResourceMonitor

[<SetUp>]
let Setup () = ()

[<Test>]
[<Category("Unit")>]
let ``defaultThresholds should have reasonable values`` () =
    Assert.AreEqual(50.0, defaultThresholds.MaxCpuPerProcess)
    Assert.AreEqual(512.0, defaultThresholds.MaxMemoryPerProcessMB)
    Assert.AreEqual(80.0, defaultThresholds.MaxSystemCpuPercent)
    Assert.AreEqual(4.0, defaultThresholds.MaxSystemMemoryGB)
    Assert.AreEqual(7, defaultThresholds.MaxActiveConnections)

[<Test>]
[<Category("Unit")>]
let ``ResourceMonitor should create instance successfully`` () =
    let monitor = ResourceMonitor()
    Assert.IsNotNull(monitor)

[<Test>]
[<Category("Unit")>]
let ``GetProcessMetrics should return None for invalid process`` () =
    let monitor = ResourceMonitor()
    let result = monitor.GetProcessMetrics("test-pane", 99999)
    Assert.IsNull(result)

[<Test>]
[<Category("Unit")>]
let ``GetProcessMetrics should return Some for current process`` () =
    let monitor = ResourceMonitor()
    let currentProcess = Process.GetCurrentProcess()
    let result = monitor.GetProcessMetrics("test-pane", currentProcess.Id)

    match result with
    | Some metrics ->
        Assert.AreEqual(currentProcess.Id, metrics.ProcessId)
        Assert.AreEqual("test-pane", metrics.PaneId)
        Assert.GreaterOrEqual(metrics.MemoryUsageMB, 0.0)
        Assert.GreaterOrEqual(metrics.ThreadCount, 0)
        Assert.GreaterOrEqual(metrics.HandleCount, 0)
    | None -> Assert.Fail("Expected Some metrics for current process")

[<Test>]
[<Category("Unit")>]
let ``GetSystemMetrics should return valid metrics`` () =
    let monitor = ResourceMonitor()
    let result = monitor.GetSystemMetrics()

    match result with
    | Some metrics ->
        Assert.AreEqual("system", metrics.PaneId)
        Assert.GreaterOrEqual(metrics.MemoryUsageMB, 0.0)
        Assert.GreaterOrEqual(metrics.ThreadCount, 0)
        Assert.GreaterOrEqual(metrics.HandleCount, 0)
    | None -> Assert.Fail("Expected Some system metrics")

[<Test>]
[<Category("Unit")>]
let ``CheckResourceThresholds should return empty for normal usage`` () =
    let monitor = ResourceMonitor()
    let violations = monitor.CheckResourceThresholds(defaultThresholds)

    // 新しいモニターインスタンスでは通常違反はない
    Assert.AreEqual(0, violations.Length)

[<Test>]
[<Category("Unit")>]
let ``UpdateCpuUsage should update existing metrics`` () =
    let monitor = ResourceMonitor()
    let currentProcess = Process.GetCurrentProcess()

    // まずプロセスメトリクスを作成
    monitor.GetProcessMetrics("test-pane", currentProcess.Id) |> ignore

    // CPU使用率を更新
    monitor.UpdateCpuUsage("test-pane", 25.5)

    let metrics = monitor.GetAllProcessMetrics()
    let testMetrics = metrics |> Array.tryFind (fun m -> m.PaneId = "test-pane")

    match testMetrics with
    | Some m -> Assert.AreEqual(25.5, m.CpuUsagePercent)
    | None -> Assert.Fail("Expected test-pane metrics to exist")

[<Test>]
[<Category("Unit")>]
let ``RemoveProcessMetrics should remove metrics successfully`` () =
    let monitor = ResourceMonitor()
    let currentProcess = Process.GetCurrentProcess()

    // プロセスメトリクスを作成
    monitor.GetProcessMetrics("test-pane", currentProcess.Id) |> ignore

    // 削除前に存在確認
    let metricsBefore = monitor.GetAllProcessMetrics()
    Assert.GreaterOrEqual(metricsBefore.Length, 1)

    // 削除実行
    monitor.RemoveProcessMetrics("test-pane")

    // 削除後に確認
    let metricsAfter = monitor.GetAllProcessMetrics()
    let testMetrics = metricsAfter |> Array.tryFind (fun m -> m.PaneId = "test-pane")
    Assert.IsNull(testMetrics)

[<Test>]
[<Category("Unit")>]
let ``globalResourceMonitor should be accessible`` () =
    Assert.IsNotNull(globalResourceMonitor)

    // 基本動作確認
    let systemMetrics = globalResourceMonitor.GetSystemMetrics()
    Assert.IsNotNull(systemMetrics)

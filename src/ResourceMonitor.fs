module FCode.ResourceMonitor

open System
open System.Diagnostics
open System.Collections.Concurrent
open FCode.Logger

// ===============================================
// リソースメトリクス定義
// ===============================================

type ResourceMetrics =
    { ProcessId: int
      PaneId: string
      CpuUsagePercent: float
      MemoryUsageMB: float
      ThreadCount: int
      HandleCount: int
      Timestamp: DateTime }

type ResourceThresholds =
    { MaxCpuPerProcess: float // 50%
      MaxMemoryPerProcessMB: float // 512MB
      MaxSystemCpuPercent: float // 80%
      MaxSystemMemoryGB: float // 4GB
      MaxActiveConnections: int } // 7

// デフォルト閾値
let defaultThresholds =
    { MaxCpuPerProcess = 50.0
      MaxMemoryPerProcessMB = 512.0
      MaxSystemCpuPercent = 80.0
      MaxSystemMemoryGB = 4.0
      MaxActiveConnections = 7 }

// ===============================================
// リソース監視機能
// ===============================================

type ResourceMonitor() =
    let mutable processMetrics = ConcurrentDictionary<string, ResourceMetrics>()
    let mutable systemMetrics = None
    let lockObj = obj ()

    member _.GetProcessMetrics(paneId: string, processId: int) =
        try
            let proc = Process.GetProcessById(processId)

            if not proc.HasExited then
                let metrics =
                    { ProcessId = processId
                      PaneId = paneId
                      CpuUsagePercent = 0.0 // CPU使用率は別途計算
                      MemoryUsageMB = float proc.WorkingSet64 / (1024.0 * 1024.0)
                      ThreadCount = proc.Threads.Count
                      HandleCount = proc.HandleCount
                      Timestamp = DateTime.Now }

                processMetrics.AddOrUpdate(paneId, metrics, fun _ _ -> metrics) |> ignore

                logDebug
                    "ResourceMonitor"
                    $"Process metrics updated for {paneId}: Memory={metrics.MemoryUsageMB:F1}MB, Threads={metrics.ThreadCount}"

                Some metrics
            else
                logWarning "ResourceMonitor" $"Process {processId} for pane {paneId} has exited"
                None
        with
        | :? ArgumentException ->
            logWarning "ResourceMonitor" $"Process {processId} for pane {paneId} not found"
            None
        | ex ->
            logException "ResourceMonitor" $"Error getting process metrics for {paneId}" ex
            None

    member _.GetSystemMetrics() =
        try
            let totalMemoryGB = float (GC.GetTotalMemory(false)) / (1024.0 * 1024.0 * 1024.0)
            let processCount = Process.GetProcesses().Length
            let currentProcess = Process.GetCurrentProcess()

            let systemMetrics =
                { ProcessId = currentProcess.Id
                  PaneId = "system"
                  CpuUsagePercent = 0.0 // システム全体CPU使用率は別途計算
                  MemoryUsageMB = totalMemoryGB * 1024.0
                  ThreadCount = currentProcess.Threads.Count
                  HandleCount = processCount
                  Timestamp = DateTime.Now }

            logDebug "ResourceMonitor" $"System metrics: Memory={totalMemoryGB:F2}GB, Processes={processCount}"
            Some systemMetrics
        with ex ->
            logException "ResourceMonitor" "Error getting system metrics" ex
            None

    member _.CheckResourceThresholds(thresholds: ResourceThresholds) =
        let violations = ResizeArray<string * ResourceMetrics>()

        // プロセス別閾値チェック
        for kvp in processMetrics do
            let paneId = kvp.Key
            let metrics = kvp.Value

            if metrics.CpuUsagePercent > thresholds.MaxCpuPerProcess then
                violations.Add(
                    ($"CPU usage exceeded for {paneId}: {metrics.CpuUsagePercent:F1}% > {thresholds.MaxCpuPerProcess}%",
                     metrics)
                )

            if metrics.MemoryUsageMB > thresholds.MaxMemoryPerProcessMB then
                violations.Add(
                    ($"Memory usage exceeded for {paneId}: {metrics.MemoryUsageMB:F1}MB > {thresholds.MaxMemoryPerProcessMB}MB",
                     metrics)
                )

        // システム全体閾値チェック
        match systemMetrics with
        | Some sysMetrics ->
            if sysMetrics.CpuUsagePercent > thresholds.MaxSystemCpuPercent then
                violations.Add(
                    ($"System CPU usage exceeded: {sysMetrics.CpuUsagePercent:F1}% > {thresholds.MaxSystemCpuPercent}%",
                     sysMetrics)
                )

            if sysMetrics.MemoryUsageMB / 1024.0 > thresholds.MaxSystemMemoryGB then
                violations.Add(
                    ($"System memory usage exceeded: {sysMetrics.MemoryUsageMB / 1024.0:F2}GB > {thresholds.MaxSystemMemoryGB}GB",
                     sysMetrics)
                )
        | None -> ()

        violations.ToArray()

    member _.GetAllProcessMetrics() = processMetrics.Values |> Seq.toArray

    member _.RemoveProcessMetrics(paneId: string) =
        processMetrics.TryRemove(paneId) |> ignore
        logDebug "ResourceMonitor" $"Removed process metrics for {paneId}"

    member _.UpdateCpuUsage(paneId: string, cpuUsage: float) =
        match processMetrics.TryGetValue(paneId) with
        | true, metrics ->
            let updatedMetrics =
                { metrics with
                    CpuUsagePercent = cpuUsage
                    Timestamp = DateTime.Now }

            processMetrics.AddOrUpdate(paneId, updatedMetrics, fun _ _ -> updatedMetrics)
            |> ignore
        | _ -> ()

    member _.UpdateSystemCpuUsage(cpuUsage: float) =
        lock lockObj (fun () ->
            match systemMetrics with
            | Some metrics ->
                systemMetrics <-
                    Some
                        { metrics with
                            CpuUsagePercent = cpuUsage
                            Timestamp = DateTime.Now }
            | None ->
                systemMetrics <-
                    Some
                        { ProcessId = Process.GetCurrentProcess().Id
                          PaneId = "system"
                          CpuUsagePercent = cpuUsage
                          MemoryUsageMB = 0.0
                          ThreadCount = 0
                          HandleCount = 0
                          Timestamp = DateTime.Now })

// ===============================================
// グローバルリソース監視インスタンス
// ===============================================

let globalResourceMonitor = ResourceMonitor()

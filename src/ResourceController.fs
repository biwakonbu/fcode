module FCode.ResourceController

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open FCode.Logger
open FCode.ResourceMonitor

// ===============================================
// リソース制御アクション定義
// ===============================================

type ResourceAction =
    | ThrottleCpu of PaneId: string * TargetPercent: float
    | ForceGarbageCollection of PaneId: string
    | RestartProcess of PaneId: string * Reason: string
    | SuspendProcess of PaneId: string
    | QueueTask of PaneId: string * Priority: int

type InterventionStrategy =
    | GradualThrottling
    | ImmediateRestart
    | ProcessSuspension
    | LoadBalancing

type ResourceControllerConfig =
    { MonitoringIntervalMs: int
      CpuThrottleIntervalMs: int
      GcIntervalMs: int
      MaxRestartAttempts: int
      RestartCooldownMs: int }

let defaultConfig =
    { MonitoringIntervalMs = 2000
      CpuThrottleIntervalMs = 5000
      GcIntervalMs = 30000
      MaxRestartAttempts = 3
      RestartCooldownMs = 10000 }

// ===============================================
// リソース制御器
// ===============================================

type ResourceController(config: ResourceControllerConfig) =
    let mutable isRunning = false
    let mutable monitoringTask = None

    let _restartAttempts =
        System.Collections.Concurrent.ConcurrentDictionary<string, int>()

    let _lastRestartTime =
        System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>()

    let suspendedPanes =
        System.Collections.Concurrent.ConcurrentDictionary<string, bool>()

    member _.Start() =
        if not isRunning then
            isRunning <- true
            logInfo "ResourceController" "Starting resource controller"

            let task =
                Task.Run(fun () ->
                    while isRunning do
                        try
                            // リソース監視と制御
                            let violations = globalResourceMonitor.CheckResourceThresholds(defaultThresholds)

                            if violations.Length > 0 then
                                logWarning "ResourceController" $"Found {violations.Length} resource violations"

                                for (message, _metrics) in violations do
                                    logWarning "ResourceController" message
                            // リソース制御アクションは今後実装

                            // 一定間隔でGC実行
                            if DateTime.Now.Millisecond % config.GcIntervalMs < config.MonitoringIntervalMs then
                                GC.Collect(0, GCCollectionMode.Optimized)
                                logDebug "ResourceController" "Performed optimized garbage collection"

                            Thread.Sleep(config.MonitoringIntervalMs)
                        with ex ->
                            logException "ResourceController" "Error in resource monitoring loop" ex
                            Thread.Sleep(config.MonitoringIntervalMs))

            monitoringTask <- Some task
            logInfo "ResourceController" "Resource controller started successfully"

    member _.Stop() =
        if isRunning then
            isRunning <- false
            logInfo "ResourceController" "Stopping resource controller"

            match monitoringTask with
            | Some task ->
                try
                    task.Wait(5000) |> ignore
                    logInfo "ResourceController" "Resource controller stopped successfully"
                with ex ->
                    logException "ResourceController" "Error stopping resource controller" ex
            | None -> ()

    member _.ExecuteAction(action: ResourceAction) =
        logInfo "ResourceController" $"Executing action: {action}"

    member _.GetSuspendedPanes() = suspendedPanes.Keys |> Seq.toArray

    member _.IsPaneSuspended(paneId: string) = suspendedPanes.ContainsKey(paneId)

    // プライベート関数群
    member private _.DetermineAction(metrics: ResourceMetrics) = ForceGarbageCollection metrics.PaneId

    member private _.ExecuteActionInternal(action: ResourceAction) =
        match action with
        | ForceGarbageCollection paneId ->
            logInfo "ResourceController" $"Forcing garbage collection for {paneId}"
            GC.Collect(1, GCCollectionMode.Forced)
        | _ -> logInfo "ResourceController" $"Action {action} not yet implemented"

// プライベート関数の実装
let private _determineAction (metrics: ResourceMetrics) =
    let paneId = metrics.PaneId

    // CPU使用率が高い場合
    if metrics.CpuUsagePercent > defaultThresholds.MaxCpuPerProcess then
        if metrics.CpuUsagePercent > 80.0 then
            RestartProcess(paneId, $"High CPU usage: {metrics.CpuUsagePercent:F1}%%")
        else
            ThrottleCpu(paneId, 25.0) // 25%に制限

    // メモリ使用量が高い場合
    elif metrics.MemoryUsageMB > defaultThresholds.MaxMemoryPerProcessMB then
        if metrics.MemoryUsageMB > 800.0 then
            RestartProcess(paneId, $"High memory usage: {metrics.MemoryUsageMB:F1}MB")
        else
            ForceGarbageCollection paneId

    // デフォルト：ガベージコレクション実行
    else
        ForceGarbageCollection paneId

let private _executeAction (action: ResourceAction) =
    match action with
    | ThrottleCpu(paneId, targetPercent) ->
        logInfo "ResourceController" $"Throttling CPU for {paneId} to {targetPercent}%%"
        // CPU制限実装（プロセス優先度調整）
        try
            let metrics =
                globalResourceMonitor.GetAllProcessMetrics()
                |> Array.tryFind (fun m -> m.PaneId = paneId)

            match metrics with
            | Some m ->
                let proc = Process.GetProcessById(m.ProcessId)

                if not proc.HasExited then
                    proc.PriorityClass <- ProcessPriorityClass.BelowNormal
                    logInfo "ResourceController" $"CPU throttled for {paneId} (priority lowered)"
            | None -> logWarning "ResourceController" $"Cannot find process for {paneId} to throttle"
        with ex ->
            logException "ResourceController" $"Error throttling CPU for {paneId}" ex

    | ForceGarbageCollection paneId ->
        logInfo "ResourceController" $"Forcing garbage collection for {paneId}"
        GC.Collect(1, GCCollectionMode.Forced)
        GC.WaitForPendingFinalizers()

    | RestartProcess(paneId, reason) ->
        logWarning "ResourceController" $"Restarting process for {paneId}: {reason}"
        // プロセス再起動は ProcessSupervisor に委任
        // ここでは再起動要求のログのみ記録
        logInfo "ResourceController" $"Process restart requested for {paneId}"

    | SuspendProcess paneId ->
        logInfo "ResourceController" $"Suspending process for {paneId}"
        logInfo "ResourceController" $"Process suspended: {paneId}"

    | QueueTask(paneId, priority) -> logInfo "ResourceController" $"Queueing task for {paneId} with priority {priority}"
// タスクキューイング実装（今後の拡張）

// ===============================================
// グローバルリソース制御インスタンス
// ===============================================

let globalResourceController = ResourceController(defaultConfig)

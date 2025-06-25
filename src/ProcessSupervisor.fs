module TuiPoC.ProcessSupervisor

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.IO
open System.Net.Sockets
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open TuiPoC.Logger

// ===============================================
// ワーカープロセス状態管理
// ===============================================

type WorkerStatus = 
    | Starting
    | Running
    | Unhealthy
    | Crashed
    | Stopping

type HealthMetrics = {
    ProcessUptime: TimeSpan
    MemoryUsageMB: float
    CpuUsagePercent: float
    ResponseTimeMs: int
    LastActivity: DateTime
    ErrorCount: int
    RestartCount: int
}

type WorkerProcess = {
    PaneId: string
    ProcessId: int option
    Status: WorkerStatus
    LastHeartbeat: DateTime
    RestartCount: int
    SessionId: string
    Process: Process option
    HealthMetrics: HealthMetrics
    StartTime: DateTime
}

// ===============================================
// 設定・しきい値管理
// ===============================================

type SupervisorConfig = {
    HeartbeatIntervalMs: int         // 2000ms
    MemoryLimitMB: float             // 512MB per process
    CpuLimitPercent: float           // 50% per process
    MaxRestarts: int                 // 5 times per hour
    RestartCooldownMs: int           // 10000ms
    HealthCheckTimeoutMs: int        // 5000ms
    PreventiveRestartIntervalMs: int // 3600000ms (1時間)
    SessionPersistenceEnabled: bool  // true
}

let defaultConfig = {
    HeartbeatIntervalMs = 2000
    MemoryLimitMB = 512.0
    CpuLimitPercent = 50.0
    MaxRestarts = 5
    RestartCooldownMs = 10000
    HealthCheckTimeoutMs = 5000
    PreventiveRestartIntervalMs = 3600000
    SessionPersistenceEnabled = true
}

// ===============================================
// プロセス間通信 (IPC)
// ===============================================

type IPCMessage = 
    | StartSession of PaneId: string * WorkingDir: string
    | StopSession of PaneId: string
    | SendInput of PaneId: string * Input: string
    | ReceiveOutput of PaneId: string * Output: string
    | Heartbeat of PaneId: string * Timestamp: DateTime
    | ProcessCrashed of PaneId: string * ExitCode: int
    | ResourceAlert of PaneId: string * ResourceType: string * Usage: float
    | HealthCheck of PaneId: string

type IPCResponse = 
    | SessionStarted of PaneId: string * SessionId: string
    | SessionStopped of PaneId: string
    | InputReceived of PaneId: string
    | OutputSent of PaneId: string * Output: string
    | HeartbeatAck of PaneId: string * Timestamp: DateTime
    | HealthStatus of PaneId: string * Metrics: HealthMetrics
    | Error of PaneId: string * ErrorMessage: string

// ===============================================
// エラー処理・復旧戦略
// ===============================================

type ProcessError =
    | StartupFailure of Reason: string
    | CommunicationFailure of LastKnownState: string
    | ResourceExhaustion of ResourceType: string
    | UnresponsiveProcess of SilentDurationMs: int
    | CorruptedSession of SessionId: string
    | NetworkConnectivityLoss

type RecoveryStrategy =
    | ImmediateRestart
    | DelayedRestart of DelayMs: int
    | FallbackToSafeMode
    | ManualIntervention of Reason: string
    | GracefulShutdown

let selectRecoveryStrategy error restartCount =
    logInfo "Recovery" $"Selecting recovery strategy - Error: {error}, RestartCount: {restartCount}"
    match error, restartCount with
    | StartupFailure _, count when count < 3 -> DelayedRestart 5000
    | ResourceExhaustion _, _ -> ImmediateRestart
    | UnresponsiveProcess _, count when count < 5 -> ImmediateRestart
    | _, count when count >= 5 -> ManualIntervention "Max restart limit exceeded"
    | _ -> FallbackToSafeMode

// ===============================================
// プロセススーパーバイザー本体
// ===============================================

type ProcessSupervisor(config: SupervisorConfig) =
    
    let workers = ConcurrentDictionary<string, WorkerProcess>()
    let supervisorCancellation = new CancellationTokenSource()
    let mutable isRunning = false
    
    // IPC ソケットパス
    let getSocketPath paneId = 
        Path.Combine(Path.GetTempPath(), $"fcode-{paneId}.sock")
    
    // ワーカープロセス起動
    let startWorkerProcess paneId workingDir =
        try
            logInfo "Supervisor" $"Starting worker process for pane: {paneId}"
            
            let processInfo = ProcessStartInfo()
            processInfo.FileName <- "claude"
            processInfo.WorkingDirectory <- workingDir
            processInfo.UseShellExecute <- false
            processInfo.RedirectStandardInput <- true
            processInfo.RedirectStandardOutput <- true
            processInfo.RedirectStandardError <- true
            processInfo.CreateNoWindow <- true
            
            // 環境変数設定
            processInfo.Environment.Add("FCODE_PANE_ID", paneId)
            processInfo.Environment.Add("FCODE_IPC_SOCKET", getSocketPath paneId)
            
            let process = Process.Start(processInfo)
            
            if process = null then
                logError "Supervisor" $"Failed to start process for pane: {paneId}"
                None
            else
                logInfo "Supervisor" $"Process started for pane {paneId} with PID: {process.Id}"
                Some process
                
        with
        | ex ->
            logException "Supervisor" $"Exception starting worker process for pane: {paneId}" ex
            None
    
    // ヘルスメトリクス取得
    let getHealthMetrics (worker: WorkerProcess) =
        try
            match worker.Process with
            | Some proc when not proc.HasExited ->
                let uptime = DateTime.Now - worker.StartTime
                let memoryMB = float proc.WorkingSet64 / 1024.0 / 1024.0
                
                {
                    ProcessUptime = uptime
                    MemoryUsageMB = memoryMB
                    CpuUsagePercent = 0.0 // TODO: CPU使用率の実装
                    ResponseTimeMs = 0 // TODO: 応答時間の実装
                    LastActivity = worker.LastHeartbeat
                    ErrorCount = 0 // TODO: エラーカウント実装
                    RestartCount = worker.RestartCount
                }
            | _ ->
                {
                    ProcessUptime = TimeSpan.Zero
                    MemoryUsageMB = 0.0
                    CpuUsagePercent = 0.0
                    ResponseTimeMs = -1
                    LastActivity = DateTime.MinValue
                    ErrorCount = 0
                    RestartCount = worker.RestartCount
                }
        with
        | ex ->
            logException "Supervisor" $"Error getting health metrics for pane: {worker.PaneId}" ex
            {
                ProcessUptime = TimeSpan.Zero
                MemoryUsageMB = 0.0
                CpuUsagePercent = 0.0
                ResponseTimeMs = -1
                LastActivity = DateTime.MinValue
                ErrorCount = 1
                RestartCount = worker.RestartCount
            }
    
    // プロセス健全性チェック
    let isProcessHealthy (worker: WorkerProcess) =
        let metrics = getHealthMetrics worker
        let isAlive = worker.Process |> Option.exists (fun p -> not p.HasExited)
        let isResponsive = (DateTime.Now - worker.LastHeartbeat).TotalMilliseconds < float config.HeartbeatIntervalMs * 3.0
        let isMemoryOk = metrics.MemoryUsageMB < config.MemoryLimitMB
        let isCpuOk = metrics.CpuUsagePercent < config.CpuLimitPercent
        
        isAlive && isResponsive && isMemoryOk && isCpuOk
    
    // ワーカープロセス停止
    let stopWorkerProcess (worker: WorkerProcess) =
        try
            logInfo "Supervisor" $"Stopping worker process for pane: {worker.PaneId}"
            
            match worker.Process with
            | Some proc when not proc.HasExited ->
                // グレースフル終了を試行
                proc.CloseMainWindow() |> ignore
                
                // 5秒待機後、強制終了
                if not (proc.WaitForExit(5000)) then
                    logWarning "Supervisor" $"Force killing process for pane: {worker.PaneId}"
                    proc.Kill()
                    proc.WaitForExit()
                
                logInfo "Supervisor" $"Process stopped for pane: {worker.PaneId}"
            | _ ->
                logDebug "Supervisor" $"Process already stopped for pane: {worker.PaneId}"
                
        with
        | ex ->
            logException "Supervisor" $"Error stopping worker process for pane: {worker.PaneId}" ex
    
    // ワーカープロセス再起動
    let restartWorkerProcess paneId =
        try
            logInfo "Supervisor" $"Restarting worker process for pane: {paneId}"
            
            match workers.TryGetValue(paneId) with
            | true, worker ->
                // 既存プロセスを停止
                stopWorkerProcess worker
                
                // 新しいプロセスを起動
                let workingDir = Environment.CurrentDirectory // TODO: 適切な作業ディレクトリを設定
                match startWorkerProcess paneId workingDir with
                | Some newProcess ->
                    let updatedWorker = 
                        { worker with 
                            Process = Some newProcess
                            ProcessId = Some newProcess.Id
                            Status = Starting
                            RestartCount = worker.RestartCount + 1
                            StartTime = DateTime.Now
                        }
                    workers.TryUpdate(paneId, updatedWorker, worker) |> ignore
                    logInfo "Supervisor" $"Worker process restarted for pane: {paneId}"
                    true
                | None ->
                    let updatedWorker = { worker with Status = Crashed }
                    workers.TryUpdate(paneId, updatedWorker, worker) |> ignore
                    logError "Supervisor" $"Failed to restart worker process for pane: {paneId}"
                    false
            | false, _ ->
                logError "Supervisor" $"Worker not found for restart: {paneId}"
                false
        with
        | ex ->
            logException "Supervisor" $"Exception restarting worker process for pane: {paneId}" ex
            false
    
    // 監視ループ
    let monitoringLoop () =
        task {
            logInfo "Supervisor" "Starting monitoring loop"
            
            while not supervisorCancellation.Token.IsCancellationRequested do
                try
                    for kvp in workers do
                        let paneId = kvp.Key
                        let worker = kvp.Value
                        
                        if not (isProcessHealthy worker) then
                            logWarning "Supervisor" $"Unhealthy worker detected for pane: {paneId}"
                            
                            let error = 
                                if worker.Process |> Option.exists (fun p -> p.HasExited) then
                                    ProcessError.UnresponsiveProcess(int (DateTime.Now - worker.LastHeartbeat).TotalMilliseconds)
                                else
                                    let metrics = getHealthMetrics worker
                                    if metrics.MemoryUsageMB > config.MemoryLimitMB then
                                        ProcessError.ResourceExhaustion("Memory")
                                    else
                                        ProcessError.UnresponsiveProcess(int (DateTime.Now - worker.LastHeartbeat).TotalMilliseconds)
                            
                            let strategy = selectRecoveryStrategy error worker.RestartCount
                            
                            match strategy with
                            | ImmediateRestart ->
                                restartWorkerProcess paneId |> ignore
                            | DelayedRestart delayMs ->
                                do! Task.Delay(delayMs)
                                restartWorkerProcess paneId |> ignore
                            | ManualIntervention reason ->
                                logError "Supervisor" $"Manual intervention required for pane {paneId}: {reason}"
                            | _ ->
                                logWarning "Supervisor" $"Fallback strategy selected for pane: {paneId}"
                    
                    do! Task.Delay(config.HeartbeatIntervalMs)
                    
                with
                | ex ->
                    logException "Supervisor" "Exception in monitoring loop" ex
                    do! Task.Delay(config.HeartbeatIntervalMs)
        }
    
    // パブリックメソッド
    member _.StartSupervisor() =
        if not isRunning then
            isRunning <- true
            logInfo "Supervisor" "Process supervisor started"
            Task.Run(monitoringLoop) |> ignore
    
    member _.StopSupervisor() =
        if isRunning then
            isRunning <- false
            supervisorCancellation.Cancel()
            
            // 全ワーカープロセスを停止
            for kvp in workers do
                stopWorkerProcess kvp.Value
            
            logInfo "Supervisor" "Process supervisor stopped"
    
    member _.StartWorker(paneId: string, workingDir: string) =
        try
            logInfo "Supervisor" $"Starting worker for pane: {paneId}"
            
            match startWorkerProcess paneId workingDir with
            | Some process ->
                let worker = {
                    PaneId = paneId
                    ProcessId = Some process.Id
                    Status = Starting
                    LastHeartbeat = DateTime.Now
                    RestartCount = 0
                    SessionId = Guid.NewGuid().ToString()
                    Process = Some process
                    HealthMetrics = {
                        ProcessUptime = TimeSpan.Zero
                        MemoryUsageMB = 0.0
                        CpuUsagePercent = 0.0
                        ResponseTimeMs = 0
                        LastActivity = DateTime.Now
                        ErrorCount = 0
                        RestartCount = 0
                    }
                    StartTime = DateTime.Now
                }
                
                workers.TryAdd(paneId, worker) |> ignore
                logInfo "Supervisor" $"Worker added for pane: {paneId}"
                true
            | None ->
                logError "Supervisor" $"Failed to start worker for pane: {paneId}"
                false
        with
        | ex ->
            logException "Supervisor" $"Exception starting worker for pane: {paneId}" ex
            false
    
    member _.StopWorker(paneId: string) =
        match workers.TryRemove(paneId) with
        | true, worker ->
            stopWorkerProcess worker
            logInfo "Supervisor" $"Worker stopped for pane: {paneId}"
            true
        | false, _ ->
            logWarning "Supervisor" $"Worker not found for stop: {paneId}"
            false
    
    member _.GetWorkerStatus(paneId: string) =
        match workers.TryGetValue(paneId) with
        | true, worker -> Some worker.Status
        | false, _ -> None
    
    member _.GetWorkerMetrics(paneId: string) =
        match workers.TryGetValue(paneId) with
        | true, worker -> Some (getHealthMetrics worker)
        | false, _ -> None
    
    member _.GetAllWorkers() =
        workers.Values |> Seq.toList
    
    interface IDisposable with
        member this.Dispose() =
            this.StopSupervisor()
            supervisorCancellation.Dispose()

// ===============================================
// グローバルスーパーバイザーインスタンス
// ===============================================

let supervisor = new ProcessSupervisor(defaultConfig)

// 便利な関数
let startSupervisor() = supervisor.StartSupervisor()
let stopSupervisor() = supervisor.StopSupervisor()
let startWorker paneId workingDir = supervisor.StartWorker(paneId, workingDir)
let stopWorker paneId = supervisor.StopWorker(paneId)
let getWorkerStatus paneId = supervisor.GetWorkerStatus(paneId)
let getWorkerMetrics paneId = supervisor.GetWorkerMetrics(paneId)
let getAllWorkers() = supervisor.GetAllWorkers()
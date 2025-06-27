module FCode.ProcessSupervisor

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.IO
open System.Net.Sockets
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FCode.Logger
open FCode.UnixDomainSocketManager
open FCode.IPCChannel
open FCode.ResourceMonitor
open FCode.ResourceController

// ===============================================
// CircularBuffer - 固定サイズ履歴管理
// ===============================================

type CircularBuffer<'T>(capacity: int) =
    let buffer = Array.zeroCreate<'T option> capacity
    let mutable head = 0
    let mutable size = 0
    let lockObj = obj ()

    member _.Add(item: 'T) =
        lock lockObj (fun () ->
            buffer.[head] <- Some item
            head <- (head + 1) % capacity
            size <- min (size + 1) capacity)

    member _.GetAverage(selector: 'T -> float) =
        lock lockObj (fun () ->
            if size = 0 then
                0.0
            else
                let sum = buffer |> Array.take size |> Array.choose id |> Array.sumBy selector
                sum / float size)

    member _.GetLast(count: int) =
        lock lockObj (fun () ->
            let actualCount = min count size
            let result = Array.zeroCreate<'T> actualCount

            for i = 0 to actualCount - 1 do
                let index = (head - 1 - i + capacity) % capacity

                match buffer.[index] with
                | Some value -> result.[i] <- value
                | None -> ()

            result)

    member _.Count = size
    member _.IsEmpty = size = 0

// 文字列専用CircularBuffer
type CircularStringBuffer(capacity: int) =
    let buffer = Array.zeroCreate<string option> capacity
    let mutable head = 0
    let mutable size = 0
    let lockObj = obj ()

    member _.AddLine(line: string) =
        lock lockObj (fun () ->
            buffer.[head] <- Some line
            head <- (head + 1) % capacity
            size <- min (size + 1) capacity)

    member _.GetAllLines() =
        lock lockObj (fun () ->
            if size = 0 then
                ""
            else
                let lines = Array.zeroCreate<string> size

                for i = 0 to size - 1 do
                    let index = (head - size + i + capacity) % capacity

                    match buffer.[index] with
                    | Some line -> lines.[i] <- line
                    | None -> lines.[i] <- ""

                String.Join("\n", lines))

    member _.Count = size
    member _.IsEmpty = size = 0

// ===============================================
// プロセスメトリクス追跡
// ===============================================

type ProcessMetrics =
    { LastCpuTime: TimeSpan
      LastMeasureTime: DateTime
      CpuUsageHistory: CircularBuffer<float>
      ProcessId: int }

type ResponseTimeTracker =
    { PendingRequests: ConcurrentDictionary<string, Stopwatch>
      ResponseHistory: CircularBuffer<int>
      mutable AverageResponseTime: float }

type ErrorCounter =
    { mutable IPCErrors: int
      mutable ProcessCrashes: int
      mutable TimeoutErrors: int
      mutable TotalErrors: int
      LastErrorTime: DateTime ref
      ErrorHistory: CircularBuffer<DateTime> }

// 新しいメトリクス用の型定義
type CpuUsageStats =
    { Current: float
      Average: float
      History: float array }

type ResponseTimeStats =
    { AverageMs: float
      RecentHistory: int array
      PendingRequests: int }

type ErrorStats =
    { TotalErrors: int
      IPCErrors: int
      ProcessCrashes: int
      TimeoutErrors: int
      LastErrorTime: DateTime
      ErrorRate: float }

// ===============================================
// ワーカープロセス状態管理
// ===============================================

type WorkerStatus =
    | Starting
    | Running
    | Unhealthy
    | Crashed
    | Stopping

type HealthMetrics =
    { ProcessUptime: TimeSpan
      MemoryUsageMB: float
      CpuUsagePercent: float
      ResponseTimeMs: int
      LastActivity: DateTime
      ErrorCount: int
      RestartCount: int
      // 新しいメトリクス
      AverageResponseTimeMs: float
      CpuUsageHistory: float array
      ErrorRate: float
      MemoryTrend: string // "stable", "increasing", "decreasing"
      LastCpuMeasurement: DateTime }

// CPU使用率計算用のヘルパー関数
let calculateCpuUsage (proc: Process) (lastMetrics: ProcessMetrics option) =
    try
        let currentTime = DateTime.Now
        let currentCpuTime = proc.TotalProcessorTime

        match lastMetrics with
        | Some last when last.ProcessId = proc.Id ->
            let timeDiff = (currentTime - last.LastMeasureTime).TotalMilliseconds

            if timeDiff > 0.0 then
                let cpuDiff = (currentCpuTime - last.LastCpuTime).TotalMilliseconds
                let cpuUsage = (cpuDiff / timeDiff) * 100.0 / float Environment.ProcessorCount
                let normalizedUsage = min 100.0 (max 0.0 cpuUsage)

                // CPU使用率履歴を更新
                last.CpuUsageHistory.Add(normalizedUsage)
                normalizedUsage
            else
                0.0
        | _ -> 0.0
    with ex ->
        logException "ProcessSupervisor" "Error calculating CPU usage" ex
        0.0

// 応答時間測定機能
let createResponseTimeTracker () =
    { PendingRequests = ConcurrentDictionary<string, Stopwatch>()
      ResponseHistory = CircularBuffer<int>(100) // 最新100件の履歴
      AverageResponseTime = 0.0 }

let startResponseTimeMeasurement (tracker: ResponseTimeTracker) (requestId: string) =
    let stopwatch = Stopwatch.StartNew()
    tracker.PendingRequests.TryAdd(requestId, stopwatch) |> ignore

let completeResponseTimeMeasurement (tracker: ResponseTimeTracker) (requestId: string) =
    match tracker.PendingRequests.TryRemove(requestId) with
    | true, stopwatch ->
        stopwatch.Stop()
        let responseTimeMs = int stopwatch.ElapsedMilliseconds
        tracker.ResponseHistory.Add(responseTimeMs)
        tracker.AverageResponseTime <- tracker.ResponseHistory.GetAverage(float)
        responseTimeMs
    | false, _ -> -1

// エラーカウンター機能
let createErrorCounter () =
    { IPCErrors = 0
      ProcessCrashes = 0
      TimeoutErrors = 0
      TotalErrors = 0
      LastErrorTime = ref DateTime.MinValue
      ErrorHistory = CircularBuffer<DateTime>(50) } // 最新50件のエラー履歴

let incrementErrorCount (counter: ErrorCounter) (errorType: string) =
    let now = DateTime.Now
    counter.LastErrorTime := now
    counter.ErrorHistory.Add(now)
    counter.TotalErrors <- counter.TotalErrors + 1

    match errorType.ToLower() with
    | "ipc" -> counter.IPCErrors <- counter.IPCErrors + 1
    | "crash" -> counter.ProcessCrashes <- counter.ProcessCrashes + 1
    | "timeout" -> counter.TimeoutErrors <- counter.TimeoutErrors + 1
    | _ -> ()

    logWarning "ProcessSupervisor" $"Error count incremented: {errorType} (Total: {counter.TotalErrors})"

type WorkerProcess =
    { PaneId: string
      ProcessId: int option
      Status: WorkerStatus
      LastHeartbeat: DateTime
      RestartCount: int
      SessionId: string
      Process: Process option
      HealthMetrics: HealthMetrics
      StartTime: DateTime
      WorkingDirectory: string
      // 新しいメトリクス追跡機能
      ProcessMetrics: ProcessMetrics option
      ResponseTimeTracker: ResponseTimeTracker
      ErrorCounter: ErrorCounter }

// ===============================================
// 設定・しきい値管理
// ===============================================

type SupervisorConfig =
    { HeartbeatIntervalMs: int // 2000ms
      MemoryLimitMB: float // 512MB per process
      CpuLimitPercent: float // 50% per process
      MaxRestarts: int // 5 times per hour
      RestartCooldownMs: int // 10000ms
      HealthCheckTimeoutMs: int // 5000ms
      PreventiveRestartIntervalMs: int // 3600000ms (1時間)
      SessionPersistenceEnabled: bool } // true

let defaultConfig =
    { HeartbeatIntervalMs = 2000
      MemoryLimitMB = 512.0
      CpuLimitPercent = 50.0
      MaxRestarts = 5
      RestartCooldownMs = 10000
      HealthCheckTimeoutMs = 5000
      PreventiveRestartIntervalMs = 3600000
      SessionPersistenceEnabled = true }

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

    // IPC チャネル統合
    let mutable ipcChannel: IPCChannel option = None
    let mutable udsServer: UdsServer option = None

    // IPC ソケットパス
    let getSocketPath paneId =
        Path.Combine(Path.GetTempPath(), $"fcode-{paneId}.sock")

    // IPC サーバーソケットパス
    let getServerSocketPath () =
        Path.Combine(Path.GetTempPath(), "fcode-supervisor.sock")

    // ソケットファイルのセキュア権限設定
    let setSocketFilePermissions (socketPath: string) =
        try
            if File.Exists(socketPath) then
                // Unix系OS専用: ファイル権限を600 (所有者のみ読み書き可能)に設定
                if Environment.OSVersion.Platform = PlatformID.Unix then
                    let chmodCmd = $"chmod 600 \"{socketPath}\""
                    let processInfo = ProcessStartInfo("sh", $"-c \"{chmodCmd}\"")
                    processInfo.UseShellExecute <- false
                    processInfo.RedirectStandardOutput <- true
                    processInfo.RedirectStandardError <- true
                    let proc = Process.Start(processInfo)
                    proc.WaitForExit()

                    if proc.ExitCode = 0 then
                        logDebug "ProcessSupervisor" $"Socket file permissions set to 600: {socketPath}"
                    else
                        logWarning "ProcessSupervisor" $"Failed to set socket file permissions: {socketPath}"
        with ex ->
            logException "ProcessSupervisor" $"Error setting socket file permissions: {socketPath}" ex

    // IPC クライアント接続処理
    let handleClientConnection (connection: UdsConnection) =
        try
            logInfo "Supervisor" "New IPC client connected"

            // 接続ごとの処理タスクを開始
            let handleConnection () =
                try
                    try
                        while connection.IsConnected do
                            try
                                // SessionCommand メッセージを受信
                                let envelopeTask =
                                    connection.ReceiveAsync<SessionCommand>(supervisorCancellation.Token)

                                let envelope = envelopeTask.Result

                                logDebug "Supervisor" $"Received IPC command: {envelope.MessageId}"

                                // IPCチャネル経由で処理
                                match ipcChannel with
                                | Some channel ->
                                    let responseTask =
                                        channel.SendCommandAsync(envelope.Data, supervisorCancellation.Token)

                                    let response = responseTask.Result

                                    // レスポンスを送信
                                    let responseEnvelope = createEnvelope response
                                    let sendTask = connection.SendAsync(responseEnvelope, supervisorCancellation.Token)
                                    sendTask.Wait()

                                    logDebug "Supervisor" $"Sent IPC response: {responseEnvelope.MessageId}"

                                | None ->
                                    logError "Supervisor" "IPC channel not initialized"
                                    let errorResponse = Error("", "IPC channel not available")
                                    let errorEnvelope = createEnvelope errorResponse
                                    let sendTask = connection.SendAsync(errorEnvelope, supervisorCancellation.Token)
                                    sendTask.Wait()

                            with
                            | :? OperationCanceledException -> ()
                            | ex -> logException "Supervisor" "Error handling IPC client message" ex

                    with ex ->
                        logException "Supervisor" "Error in IPC client connection handler" ex
                finally
                    connection.Close()
                    logInfo "Supervisor" "IPC client disconnected"

            Task.Run(handleConnection) |> ignore

        with ex ->
            logException "Supervisor" "Error setting up IPC client connection handler" ex

    // IPC サーバー初期化
    let initializeIPCServer () =
        try
            let serverSocketPath = getServerSocketPath ()
            let config = defaultUdsConfig serverSocketPath
            let server = new UdsServer(config)
            server.OnClientConnected <- Some handleClientConnection

            udsServer <- Some server
            logInfo "Supervisor" $"IPC server initialized at: {serverSocketPath}"
            server.StartAsync()

        with ex ->
            logException "Supervisor" "Failed to initialize IPC server" ex
            Task.CompletedTask

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

            let proc = Process.Start(processInfo)

            if proc = null then
                logError "Supervisor" $"Failed to start process for pane: {paneId}"
                None
            else
                logInfo "Supervisor" $"Process started for pane {paneId} with PID: {proc.Id}"
                Some proc

        with ex ->
            logException "Supervisor" $"Exception starting worker process for pane: {paneId}" ex
            None

    // ヘルスメトリクス取得（新しいメトリクス機能付き）
    let getHealthMetrics (worker: WorkerProcess) =
        try
            match worker.Process with
            | Some workerProc when not workerProc.HasExited ->
                let uptime = DateTime.Now - worker.StartTime
                let memoryMB = float workerProc.WorkingSet64 / 1024.0 / 1024.0

                // CPU使用率を計算
                let cpuUsage = calculateCpuUsage workerProc worker.ProcessMetrics

                // 最新の応答時間を取得
                let responseTimeMs =
                    if worker.ResponseTimeTracker.ResponseHistory.IsEmpty then
                        0
                    else
                        let recentTimes = worker.ResponseTimeTracker.ResponseHistory.GetLast(1)
                        if recentTimes.Length > 0 then recentTimes.[0] else 0

                // CPU使用率履歴を取得
                let cpuHistory =
                    match worker.ProcessMetrics with
                    | Some metrics -> metrics.CpuUsageHistory.GetLast(10)
                    | None -> Array.empty

                // メモリトレンドを計算（簡易版）
                let memoryTrend =
                    if memoryMB > config.MemoryLimitMB * 0.8 then "increasing"
                    elif memoryMB < config.MemoryLimitMB * 0.3 then "stable"
                    else "stable"

                // エラーレート計算（過去1時間のエラー数）
                let errorRate =
                    let oneHourAgo = DateTime.Now.AddHours(-1.0)

                    let recentErrors =
                        worker.ErrorCounter.ErrorHistory.GetLast(50)
                        |> Array.filter (fun dt -> dt > oneHourAgo)

                    float recentErrors.Length

                { ProcessUptime = uptime
                  MemoryUsageMB = memoryMB
                  CpuUsagePercent = cpuUsage
                  ResponseTimeMs = responseTimeMs
                  LastActivity = worker.LastHeartbeat
                  ErrorCount = worker.ErrorCounter.TotalErrors
                  RestartCount = worker.RestartCount
                  // 新しいメトリクス
                  AverageResponseTimeMs = worker.ResponseTimeTracker.AverageResponseTime
                  CpuUsageHistory = cpuHistory
                  ErrorRate = errorRate
                  MemoryTrend = memoryTrend
                  LastCpuMeasurement = DateTime.Now }

            | _ ->
                { ProcessUptime = TimeSpan.Zero
                  MemoryUsageMB = 0.0
                  CpuUsagePercent = 0.0
                  ResponseTimeMs = -1
                  LastActivity = DateTime.MinValue
                  ErrorCount = worker.ErrorCounter.TotalErrors
                  RestartCount = worker.RestartCount
                  // 新しいメトリクス（デフォルト値）
                  AverageResponseTimeMs = 0.0
                  CpuUsageHistory = Array.empty
                  ErrorRate = 0.0
                  MemoryTrend = "unknown"
                  LastCpuMeasurement = DateTime.MinValue }
        with ex ->
            logException "Supervisor" $"Error getting health metrics for pane: {worker.PaneId}" ex

            { ProcessUptime = TimeSpan.Zero
              MemoryUsageMB = 0.0
              CpuUsagePercent = 0.0
              ResponseTimeMs = -1
              LastActivity = DateTime.MinValue
              ErrorCount = worker.ErrorCounter.TotalErrors + 1
              RestartCount = worker.RestartCount
              // 新しいメトリクス（エラー時のデフォルト値）
              AverageResponseTimeMs = 0.0
              CpuUsageHistory = Array.empty
              ErrorRate = 1.0
              MemoryTrend = "error"
              LastCpuMeasurement = DateTime.Now }

    // プロセス健全性チェック
    let isProcessHealthy (worker: WorkerProcess) =
        let metrics = getHealthMetrics worker
        let isAlive = worker.Process |> Option.exists (fun p -> not p.HasExited)

        let isResponsive =
            (DateTime.Now - worker.LastHeartbeat).TotalMilliseconds < float config.HeartbeatIntervalMs * 3.0

        let isMemoryOk = metrics.MemoryUsageMB < config.MemoryLimitMB
        let isCpuOk = metrics.CpuUsagePercent < config.CpuLimitPercent

        isAlive && isResponsive && isMemoryOk && isCpuOk

    // ワーカープロセス停止
    let stopWorkerProcess (worker: WorkerProcess) =
        try
            logInfo "Supervisor" $"Stopping worker process for pane: {worker.PaneId}"

            match worker.Process with
            | Some workerProc when not workerProc.HasExited ->
                // グレースフル終了を試行
                workerProc.CloseMainWindow() |> ignore

                // 5秒待機後、強制終了
                if not (workerProc.WaitForExit(5000)) then
                    logWarning "Supervisor" $"Force killing process for pane: {worker.PaneId}"
                    workerProc.Kill()
                    workerProc.WaitForExit()

                logInfo "Supervisor" $"Process stopped for pane: {worker.PaneId}"
            | _ -> logDebug "Supervisor" $"Process already stopped for pane: {worker.PaneId}"

        with ex ->
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
                let workingDir = worker.WorkingDirectory

                match startWorkerProcess paneId workingDir with
                | Some newProc ->
                    let updatedWorker =
                        { worker with
                            Process = Some newProc
                            ProcessId = Some newProc.Id
                            Status = Starting
                            RestartCount = worker.RestartCount + 1
                            StartTime = DateTime.Now
                            // プロセスメトリクスを新しいプロセス用に再初期化
                            ProcessMetrics =
                                Some
                                    { LastCpuTime = newProc.TotalProcessorTime
                                      LastMeasureTime = DateTime.Now
                                      CpuUsageHistory = CircularBuffer<float>(20)
                                      ProcessId = newProc.Id }
                        // エラーカウンターはリセットしない（履歴として保持）
                        // ResponseTimeTrackerは継続使用
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
        with ex ->
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
                                    ProcessError.UnresponsiveProcess(
                                        int (DateTime.Now - worker.LastHeartbeat).TotalMilliseconds
                                    )
                                else
                                    let metrics = getHealthMetrics worker

                                    if metrics.MemoryUsageMB > config.MemoryLimitMB then
                                        ProcessError.ResourceExhaustion("Memory")
                                    else
                                        ProcessError.UnresponsiveProcess(
                                            int (DateTime.Now - worker.LastHeartbeat).TotalMilliseconds
                                        )

                            let strategy = selectRecoveryStrategy error worker.RestartCount

                            match strategy with
                            | ImmediateRestart -> restartWorkerProcess paneId |> ignore
                            | DelayedRestart delayMs ->
                                do! Task.Delay(delayMs)
                                restartWorkerProcess paneId |> ignore
                            | ManualIntervention reason ->
                                logError "Supervisor" $"Manual intervention required for pane {paneId}: {reason}"
                            | _ -> logWarning "Supervisor" $"Fallback strategy selected for pane: {paneId}"

                    do! Task.Delay(config.HeartbeatIntervalMs)

                with ex ->
                    logException "Supervisor" "Exception in monitoring loop" ex
                    do! Task.Delay(config.HeartbeatIntervalMs)
        }

    // パブリックメソッド
    member this.StartSupervisor() =
        if not isRunning then
            isRunning <- true

            // IPCチャネル初期化
            let channel = createIPCChannel ()
            ipcChannel <- Some channel
            channel.StartAsync() |> ignore

            // IPC サーバー初期化
            initializeIPCServer () |> ignore

            // 接続健全性監視を開始
            this.StartConnectionHealthMonitoring() |> ignore

            logInfo "Supervisor" "Process supervisor started with IPC support"
            Task.Run(Func<Task>(fun () -> monitoringLoop ())) |> ignore

    member _.StopSupervisor() =
        if isRunning then
            isRunning <- false
            supervisorCancellation.Cancel()

            // IPC リソースクリーンアップ
            match ipcChannel with
            | Some channel ->
                channel.Stop()
                ipcChannel <- None
            | None -> ()

            match udsServer with
            | Some server ->
                server.Stop()
                udsServer <- None
            | None -> ()

            // 全ワーカープロセスを停止
            for kvp in workers do
                stopWorkerProcess kvp.Value

            logInfo "Supervisor" "Process supervisor stopped"

    member _.StartWorker(paneId: string, workingDir: string) =
        try
            logInfo "Supervisor" $"Starting worker for pane: {paneId}"

            match startWorkerProcess paneId workingDir with
            | Some workerProc ->
                let worker =
                    { PaneId = paneId
                      ProcessId = Some workerProc.Id
                      Status = Starting
                      LastHeartbeat = DateTime.Now
                      RestartCount = 0
                      SessionId = Guid.NewGuid().ToString()
                      Process = Some workerProc
                      HealthMetrics =
                        { ProcessUptime = TimeSpan.Zero
                          MemoryUsageMB = 0.0
                          CpuUsagePercent = 0.0
                          ResponseTimeMs = 0
                          LastActivity = DateTime.Now
                          ErrorCount = 0
                          RestartCount = 0
                          // 新しいメトリクス初期化
                          AverageResponseTimeMs = 0.0
                          CpuUsageHistory = Array.empty
                          ErrorRate = 0.0
                          MemoryTrend = "stable"
                          LastCpuMeasurement = DateTime.Now }
                      StartTime = DateTime.Now
                      WorkingDirectory = workingDir
                      // 新しいメトリクス追跡機能を初期化
                      ProcessMetrics =
                        Some
                            { LastCpuTime = workerProc.TotalProcessorTime
                              LastMeasureTime = DateTime.Now
                              CpuUsageHistory = CircularBuffer<float>(20)
                              ProcessId = workerProc.Id }
                      ResponseTimeTracker = createResponseTimeTracker ()
                      ErrorCounter = createErrorCounter () }

                workers.TryAdd(paneId, worker) |> ignore
                logInfo "Supervisor" $"Worker added for pane: {paneId}"
                true
            | None ->
                logError "Supervisor" $"Failed to start worker for pane: {paneId}"
                false
        with ex ->
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
        | true, worker -> Some(getHealthMetrics worker)
        | false, _ -> None

    member _.GetAllWorkers() = workers.Values |> Seq.toList

    member _.GetIPCMetrics() =
        match ipcChannel with
        | Some channel -> Some(channel.GetMetrics())
        | None -> None

    member _.SendIPCCommand(command: SessionCommand) =
        task {
            match ipcChannel with
            | Some channel ->
                try
                    // 再接続機能付きでコマンドを送信
                    let! response = channel.SendCommandWithRetryAsync(command)
                    return Some response
                with ex ->
                    logException "Supervisor" "Failed to send IPC command with retry" ex
                    return None
            | None ->
                logError "Supervisor" "IPC channel not available"
                return None
        }

    // 応答時間測定付きIPCコマンド送信
    member this.SendIPCCommandWithMetrics(paneId: string, command: SessionCommand) =
        task {
            match workers.TryGetValue(paneId) with
            | true, worker ->
                let requestId = Guid.NewGuid().ToString()
                startResponseTimeMeasurement worker.ResponseTimeTracker requestId

                try
                    let! response = this.SendIPCCommand(command)

                    let responseTime =
                        completeResponseTimeMeasurement worker.ResponseTimeTracker requestId

                    logDebug "Supervisor" $"IPC command completed for pane {paneId} in {responseTime}ms"
                    return response
                with ex ->
                    completeResponseTimeMeasurement worker.ResponseTimeTracker requestId |> ignore
                    incrementErrorCount worker.ErrorCounter "ipc"
                    logException "Supervisor" $"IPC command failed for pane {paneId}" ex
                    return None
            | false, _ ->
                logError "Supervisor" $"Worker not found for IPC command: {paneId}"
                return None
        }

    // 新しいメトリクス取得メソッド
    member _.GetCpuUsageStats(paneId: string) =
        match workers.TryGetValue(paneId) with
        | true, worker ->
            match worker.ProcessMetrics with
            | Some metrics ->
                let history = metrics.CpuUsageHistory.GetLast(10)
                let average = if history.Length > 0 then Array.average history else 0.0

                Some
                    { Current = if history.Length > 0 then history.[0] else 0.0
                      Average = average
                      History = history }
            | None -> None
        | false, _ -> None

    member _.GetResponseTimeStats(paneId: string) =
        match workers.TryGetValue(paneId) with
        | true, worker ->
            let tracker = worker.ResponseTimeTracker

            Some
                { AverageMs = tracker.AverageResponseTime
                  RecentHistory = tracker.ResponseHistory.GetLast(10)
                  PendingRequests = tracker.PendingRequests.Count }
        | false, _ -> None

    member _.GetErrorStatistics(paneId: string) =
        match workers.TryGetValue(paneId) with
        | true, worker ->
            let counter = worker.ErrorCounter

            Some
                { TotalErrors = counter.TotalErrors
                  IPCErrors = counter.IPCErrors
                  ProcessCrashes = counter.ProcessCrashes
                  TimeoutErrors = counter.TimeoutErrors
                  LastErrorTime = !counter.LastErrorTime
                  ErrorRate = float counter.TotalErrors }
        | false, _ -> None

    // 接続健全性を定期的にチェック
    member this.StartConnectionHealthMonitoring() =
        Task.Run(
            Func<Task>(fun () ->
                task {
                    while not supervisorCancellation.Token.IsCancellationRequested do
                        try
                            match ipcChannel with
                            | Some channel ->
                                let! isHealthy = channel.CheckConnectionHealth()

                                if not isHealthy then
                                    logWarning
                                        "Supervisor"
                                        "IPC connection health check failed - attempting to restore"
                            // 必要に応じて接続の再初期化を実行
                            | None -> logDebug "Supervisor" "IPC channel not initialized for health check"

                            do! Task.Delay(config.HeartbeatIntervalMs * 5, supervisorCancellation.Token) // 5倍の間隔でヘルスチェック

                        with
                        | :? OperationCanceledException -> ()
                        | ex ->
                            logException "Supervisor" "Error in connection health monitoring" ex
                            do! Task.Delay(10000, supervisorCancellation.Token) // エラー時は10秒待機
                })
        )

    interface IDisposable with
        member this.Dispose() =
            this.StopSupervisor()
            supervisorCancellation.Dispose()

// ===============================================
// グローバルスーパーバイザーインスタンス
// ===============================================

let supervisor = new ProcessSupervisor(defaultConfig)

// 便利な関数
let startSupervisor () = supervisor.StartSupervisor()
let stopSupervisor () = supervisor.StopSupervisor()

let startWorker paneId workingDir =
    supervisor.StartWorker(paneId, workingDir)

let stopWorker paneId = supervisor.StopWorker(paneId)
let getWorkerStatus paneId = supervisor.GetWorkerStatus(paneId)
let getWorkerMetrics paneId = supervisor.GetWorkerMetrics(paneId)
let getAllWorkers () = supervisor.GetAllWorkers()

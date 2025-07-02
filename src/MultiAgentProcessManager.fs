module FCode.MultiAgentProcessManager

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open FCode.Logger
open FCode.AgentCLI
open FCode.AgentConfiguration

// ===============================================
// エージェントプロセス状態管理
// ===============================================

/// エージェントプロセス状態定義
type AgentProcessState =
    | Idle // 待機中
    | Running // 実行中
    | Completed // 完了
    | Failed // 失敗
    | Terminated // 強制終了

/// エージェントプロセス情報
type AgentProcessInfo =
    { Agent: IAgentCLI
      Process: Process option
      State: AgentProcessState
      StartTime: DateTime option
      EndTime: DateTime option
      LastOutput: AgentOutput option
      ResourceUsage: ResourceUsage }

/// リソース使用状況
and ResourceUsage =
    { CPUUsage: float // CPU使用率 (%)
      MemoryUsage: int64 // メモリ使用量 (bytes)
      Priority: ProcessPriorityClass
      MaxMemoryLimit: int64 // メモリ制限 (bytes)
      ExecutionTimeLimit: TimeSpan } // 実行時間制限

// ===============================================
// MultiAgentProcessManager実装
// ===============================================

/// 複数エージェントプロセス統合管理
type MultiAgentProcessManager() =
    let config = getConfiguration ()
    let processInfos = ConcurrentDictionary<string, AgentProcessInfo>()
    let cts = new CancellationTokenSource()
    let maxConcurrentProcesses = config.MaxConcurrentProcesses
    let semaphore = new SemaphoreSlim(maxConcurrentProcesses, maxConcurrentProcesses)

    /// エージェント登録
    member _.RegisterAgent(agentId: string, agent: IAgentCLI) =
        try
            let resourceUsage =
                { CPUUsage = 0.0
                  MemoryUsage = 0L
                  Priority = ProcessPriorityClass.Normal
                  MaxMemoryLimit = config.ResourceLimits.MaxMemoryMB * 1024L * 1024L // MB -> bytes
                  ExecutionTimeLimit = agent.Config.Timeout }

            let processInfo =
                { Agent = agent
                  Process = None
                  State = Idle
                  StartTime = None
                  EndTime = None
                  LastOutput = None
                  ResourceUsage = resourceUsage }

            processInfos.TryAdd(agentId, processInfo) |> ignore
            logInfo "MultiAgentProcessManager" $"Agent registered: {agentId} ({agent.Name})"
            true
        with ex ->
            logException "MultiAgentProcessManager" $"Failed to register agent: {agentId}" ex
            false

    /// エージェント実行
    member this.ExecuteAgent(agentId: string, input: string) =
        async {
            try
                do! semaphore.WaitAsync(cts.Token) |> Async.AwaitTask |> Async.Ignore

                match processInfos.TryGetValue(agentId) with
                | (true, processInfo) when processInfo.State = Idle ->
                    logInfo "MultiAgentProcessManager" $"Starting agent execution: {agentId}"

                    // プロセス開始
                    let startInfo = processInfo.Agent.StartCommand(input)
                    let proc = Process.Start(startInfo)

                    // リソース制限設定
                    this.SetResourceLimits(proc, processInfo.ResourceUsage)

                    // プロセス情報更新
                    let updatedInfo =
                        { processInfo with
                            Process = Some proc
                            State = Running
                            StartTime = Some DateTime.Now }

                    processInfos.TryUpdate(agentId, updatedInfo, processInfo) |> ignore

                    // 非同期実行開始
                    let! result = this.MonitorProcessExecution(agentId, proc, processInfo.Agent)

                    semaphore.Release() |> ignore
                    return Some result

                | (true, processInfo) ->
                    logWarning "MultiAgentProcessManager" $"Agent {agentId} is not idle (state: {processInfo.State})"
                    semaphore.Release() |> ignore
                    return None

                | (false, _) ->
                    logWarning "MultiAgentProcessManager" $"Agent not found: {agentId}"
                    semaphore.Release() |> ignore
                    return None

            with ex ->
                logException "MultiAgentProcessManager" $"Failed to execute agent: {agentId}" ex
                semaphore.Release() |> ignore
                return None
        }

    /// プロセス実行監視
    member private this.MonitorProcessExecution(agentId: string, proc: Process, agent: IAgentCLI) =
        async {
            try
                let mutable outputBuffer = ""
                let mutable errorBuffer = ""

                // 出力データ収集
                proc.OutputDataReceived.Add(fun args ->
                    if not (isNull args.Data) then
                        outputBuffer <- outputBuffer + args.Data + "\n"
                        logDebug $"Agent-{agentId}" $"STDOUT: {args.Data}")

                proc.ErrorDataReceived.Add(fun args ->
                    if not (isNull args.Data) then
                        errorBuffer <- errorBuffer + args.Data + "\n"
                        logDebug $"Agent-{agentId}" $"STDERR: {args.Data}")

                // 非同期読み取り開始
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()

                // リソース監視タスク開始（非同期・非ブロッキング）
                Async.Start(this.StartResourceMonitoring(agentId, proc), cts.Token)

                // プロセス完了待機
                proc.WaitForExit()
                let exitCode = proc.ExitCode

                // 結果解析
                let combinedOutput = outputBuffer + errorBuffer
                let agentOutput = agent.ParseOutput(combinedOutput)

                // SourceAgentは元のエージェント名を保持
                let correctedOutput = agentOutput

                // プロセス情報更新
                match processInfos.TryGetValue(agentId) with
                | (true, currentInfo) ->
                    let finalState = if exitCode = 0 then Completed else Failed

                    let updatedInfo =
                        { currentInfo with
                            State = finalState
                            EndTime = Some DateTime.Now
                            LastOutput = Some correctedOutput }

                    processInfos.TryUpdate(agentId, updatedInfo, currentInfo) |> ignore
                | (false, _) -> logWarning "MultiAgentProcessManager" $"Agent not found during completion: {agentId}"

                logInfo "MultiAgentProcessManager" $"Agent execution completed: {agentId} (exit code: {exitCode})"
                return correctedOutput

            with ex ->
                logException "MultiAgentProcessManager" $"Process monitoring failed for agent: {agentId}" ex

                // エラー状態に更新
                match processInfos.TryGetValue(agentId) with
                | (true, currentInfo) ->
                    let errorInfo =
                        { currentInfo with
                            State = Failed
                            EndTime = Some DateTime.Now }

                    processInfos.TryUpdate(agentId, errorInfo, currentInfo) |> ignore
                | _ -> ()

                return
                    { Status = Error
                      Content = $"Process monitoring error: {ex.Message}"
                      Metadata = Map.empty.Add("error_type", "monitoring_failure")
                      Timestamp = DateTime.Now
                      SourceAgent = agentId
                      Capabilities = [] }
        }

    /// リソース制限設定
    member private _.SetResourceLimits(proc: Process, resourceUsage: ResourceUsage) =
        try
            proc.PriorityClass <- resourceUsage.Priority
            logDebug "MultiAgentProcessManager" $"Set process priority: {resourceUsage.Priority} for PID: {proc.Id}"
        with ex ->
            logWarning "MultiAgentProcessManager" $"Failed to set process priority: {ex.Message}"

    /// リソース監視開始（イベントベース実装）
    member private this.StartResourceMonitoring(agentId: string, proc: Process) =
        async {
            try
                // プロセス終了イベント監視設定
                let exitEventReceived = ref false

                proc.EnableRaisingEvents <- true

                proc.Exited.Add(fun _ ->
                    exitEventReceived := true
                    logDebug "MultiAgentProcessManager" $"Process exit event received for agent: {agentId}")

                // 設定可能間隔でリソース監視
                while not !exitEventReceived && not proc.HasExited do
                    let! _ = Async.Sleep(config.ResourceLimits.MonitoringIntervalMs)

                    // プロセス状態の安全チェック
                    if not !exitEventReceived && not proc.HasExited then
                        match processInfos.TryGetValue(agentId) with
                        | (true, currentInfo) ->
                            try
                                // プロセスアクセス前の最終チェック
                                if not proc.HasExited then
                                    let cpuUsage = this.GetCPUUsage(proc)

                                    let memoryUsage =
                                        try
                                            proc.WorkingSet64
                                        with
                                        | :? InvalidOperationException -> 0L // プロセス終了済み
                                        | _ -> 0L

                                    // メモリ制限チェック
                                    if memoryUsage > currentInfo.ResourceUsage.MaxMemoryLimit && memoryUsage > 0L then
                                        logWarning
                                            "MultiAgentProcessManager"
                                            $"Memory limit exceeded for agent {agentId}: {memoryUsage} bytes"

                                        try
                                            proc.Kill()
                                        with _ ->
                                            () // 終了済みの場合はエラー無視

                                    // 実行時間制限チェック
                                    match currentInfo.StartTime with
                                    | Some startTime ->
                                        let elapsed = DateTime.Now - startTime

                                        if elapsed > currentInfo.ResourceUsage.ExecutionTimeLimit then
                                            logWarning
                                                "MultiAgentProcessManager"
                                                $"Execution time limit exceeded for agent {agentId}: {elapsed}"

                                            try
                                                proc.Kill()
                                            with _ ->
                                                () // 終了済みの場合はエラー無視
                                    | None -> ()

                                    // リソース使用状況更新
                                    let updatedResourceUsage =
                                        { currentInfo.ResourceUsage with
                                            CPUUsage = cpuUsage
                                            MemoryUsage = memoryUsage }

                                    let updatedInfo =
                                        { currentInfo with
                                            ResourceUsage = updatedResourceUsage }

                                    processInfos.TryUpdate(agentId, updatedInfo, currentInfo) |> ignore

                            with ex ->
                                logDebug
                                    "MultiAgentProcessManager"
                                    $"Resource monitoring warning (process state changed): {ex.Message}"

                        | _ -> ()

                logDebug "MultiAgentProcessManager" $"Resource monitoring completed for agent: {agentId}"
                return ()
            with ex ->
                logException "MultiAgentProcessManager" $"Resource monitoring failed for agent: {agentId}" ex
                return ()
        }

    /// CPU使用率取得（簡易実装）
    member private _.GetCPUUsage(proc: Process) =
        try
            proc.TotalProcessorTime.TotalMilliseconds / (float Environment.ProcessorCount)
        with _ ->
            0.0

    /// エージェント状態取得
    member _.GetAgentState(agentId: string) =
        match processInfos.TryGetValue(agentId) with
        | (true, info) -> Some info
        | _ -> None

    /// 全エージェント状態取得
    member _.GetAllAgentStates() = processInfos.Values |> Seq.toList

    /// エージェント強制終了
    member _.TerminateAgent(agentId: string) =
        match processInfos.TryGetValue(agentId) with
        | (true, processInfo) ->
            match processInfo.Process with
            | Some proc when not proc.HasExited ->
                try
                    proc.Kill()

                    let updatedInfo =
                        { processInfo with
                            State = Terminated
                            EndTime = Some DateTime.Now }

                    processInfos.TryUpdate(agentId, updatedInfo, processInfo) |> ignore
                    logInfo "MultiAgentProcessManager" $"Agent terminated: {agentId}"
                    true
                with ex ->
                    logException "MultiAgentProcessManager" $"Failed to terminate agent: {agentId}" ex
                    false
            | _ ->
                logWarning "MultiAgentProcessManager" $"No running process found for agent: {agentId}"
                false
        | _ ->
            logWarning "MultiAgentProcessManager" $"Agent not found: {agentId}"
            false

    /// 全エージェント終了
    member this.TerminateAllAgents() =
        processInfos.Keys
        |> Seq.iter (fun agentId -> this.TerminateAgent(agentId) |> ignore)

    /// リソース解放
    member this.Dispose() =
        this.TerminateAllAgents()
        cts.Cancel()
        cts.Dispose()
        semaphore.Dispose()

    interface IDisposable with
        member this.Dispose() = this.Dispose()

// ===============================================
// グローバルマネージャーインスタンス
// ===============================================

/// グローバルMultiAgentProcessManagerインスタンス
let multiAgentProcessManager = new MultiAgentProcessManager()

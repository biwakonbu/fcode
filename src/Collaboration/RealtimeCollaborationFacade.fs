module FCode.Collaboration.RealtimeCollaborationFacade

open System
open System.Threading.Tasks
open FCode.Logger
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IAgentStateManager
open FCode.Collaboration.ITaskDependencyGraph
open FCode.Collaboration.IProgressAggregator
open FCode.Collaboration.ICollaborationCoordinator
open FCode.Collaboration.AgentStateManager
open FCode.Collaboration.TaskDependencyGraph
open FCode.Collaboration.ProgressAggregator
open FCode.Collaboration.CollaborationCoordinator

/// リアルタイム協調作業統合ファサード
type RealtimeCollaborationFacade(config: CollaborationConfig) =
    let mutable disposed = false

    // コアコンポーネント初期化
    let agentStateManager = new AgentStateManager(config)
    let taskDependencyGraph = new TaskDependencyGraph(config)

    let progressAggregator =
        new ProgressAggregator(agentStateManager, taskDependencyGraph, config)

    let collaborationCoordinator =
        new CollaborationCoordinator(agentStateManager, taskDependencyGraph, config)

    // 統合イベント
    let systemEvent = Event<SystemEvent>()

    /// イベント統合セットアップ
    do
        // エージェント状態変更の監視
        agentStateManager.StateChanged.Add(fun state ->
            logInfo "RealtimeCollaborationFacade"
            <| sprintf "Agent state changed: %s -> %A" state.AgentId state.Status

            systemEvent.Trigger(AgentStateChanged state))

        // タスク変更の監視
        taskDependencyGraph.TaskChanged.Add(fun task ->
            logInfo "RealtimeCollaborationFacade"
            <| sprintf "Task changed: %s -> %A" task.TaskId task.Status

            systemEvent.Trigger(TaskChanged task))

        // 進捗変更の監視
        progressAggregator.ProgressChanged.Add(fun summary ->
            logInfo "RealtimeCollaborationFacade"
            <| sprintf "Progress updated: %.1f%%" summary.OverallProgress

            systemEvent.Trigger(ProgressUpdated summary))

        // 協調作業イベントの監視
        collaborationCoordinator.CollaborationEvent.Add(fun collaborationEvent ->
            logInfo "RealtimeCollaborationFacade"
            <| sprintf "Collaboration event: %A" collaborationEvent

            systemEvent.Trigger(CollaborationEventOccurred collaborationEvent))

    /// システムイベント
    [<CLIEvent>]
    member _.SystemEvent = systemEvent.Publish

    /// ==== エージェント管理 ====
    /// エージェント状態を更新
    member _.UpdateAgentState
        (
            agentId: string,
            status: AgentStatus,
            ?progress: float,
            ?currentTask: string,
            ?workingDir: string,
            ?processId: int
        ) =
        agentStateManager.UpdateAgentState(
            agentId,
            status,
            ?progress = progress,
            ?currentTask = currentTask,
            ?workingDir = workingDir,
            ?processId = processId
        )

    /// エージェント状態を取得
    member _.GetAgentState(agentId: string) =
        agentStateManager.GetAgentState(agentId)

    /// 全エージェント状態を取得
    member _.GetAllAgentStates() = agentStateManager.GetAllAgentStates()

    /// 特定状態のエージェント一覧を取得
    member _.GetAgentsByStatus(status: AgentStatus) =
        agentStateManager.GetAgentsByStatus(status)

    /// エージェントを削除
    member _.RemoveAgent(agentId: string) = agentStateManager.RemoveAgent(agentId)

    /// ==== タスク管理 ====
    /// タスクを追加
    member _.AddTask(task: TaskInfo) = taskDependencyGraph.AddTask(task)

    /// タスクを取得
    member _.GetTask(taskId: string) = taskDependencyGraph.GetTask(taskId)

    /// 全タスクを取得
    member _.GetAllTasks() = taskDependencyGraph.GetAllTasks()

    /// 依存関係を追加
    member _.AddTaskDependency(taskId: string, dependsOnTaskId: string) =
        taskDependencyGraph.AddDependency(taskId, dependsOnTaskId)

    /// 依存関係を削除
    member _.RemoveTaskDependency(taskId: string, dependsOnTaskId: string) =
        taskDependencyGraph.RemoveDependency(taskId, dependsOnTaskId)

    /// 実行可能タスク一覧を取得
    member _.GetExecutableTasks() =
        taskDependencyGraph.GetExecutableTasks()

    /// ブロックされたタスク一覧を取得
    member _.GetBlockedTasks() = taskDependencyGraph.GetBlockedTasks()

    /// タスク完了処理
    member _.CompleteTask(taskId: string) =
        taskDependencyGraph.CompleteTask(taskId)

    /// タスク状態更新
    member _.UpdateTaskStatus(taskId: string, status: TaskStatus) =
        taskDependencyGraph.UpdateTaskStatus(taskId, status)

    /// ==== 進捗管理 ====
    /// 現在の進捗サマリーを取得
    member _.GetCurrentProgressSummary() = progressAggregator.GetCurrentSummary()

    /// エージェント別進捗詳細を取得
    member _.GetAgentProgressDetails() =
        progressAggregator.GetAgentProgressDetails()

    /// タスク別進捗詳細を取得
    member _.GetTaskProgressDetails() =
        progressAggregator.GetTaskProgressDetails()

    /// 進捗トレンド分析
    member _.AnalyzeProgressTrend() =
        progressAggregator.AnalyzeProgressTrend()

    /// マイルストーン進捗チェック
    member _.CheckMilestones(milestones: (string * float) list) =
        progressAggregator.CheckMilestones(milestones)

    /// 進捗レポート生成
    member _.GenerateProgressReport() =
        progressAggregator.GenerateProgressReport()

    /// 進捗監視開始
    member _.StartProgressMonitoring(intervalSeconds: int) =
        progressAggregator.StartMonitoring(intervalSeconds)

    /// ==== 協調作業制御 ====
    /// タスク実行要求
    member _.RequestTaskExecution(agentId: string, taskId: string, requiredResources: string list) =
        collaborationCoordinator.RequestTaskExecution(agentId, taskId, requiredResources)

    /// タスク完了通知
    member _.NotifyTaskCompletion(agentId: string, taskId: string, releasedResources: string list) =
        collaborationCoordinator.NotifyTaskCompletion(agentId, taskId, releasedResources)

    /// 同期要求
    member _.RequestSynchronization(participatingAgents: string list, reason: string) =
        collaborationCoordinator.RequestSynchronization(participatingAgents, reason)

    /// 協調作業効率分析
    member _.AnalyzeCollaborationEfficiency() =
        collaborationCoordinator.AnalyzeCollaborationEfficiency()

    /// デッドロック検出
    member _.DetectDeadlock() =
        collaborationCoordinator.DetectDeadlock()

    /// 協調作業統計取得
    member _.GetCollaborationStatistics() =
        collaborationCoordinator.GetCollaborationStatistics()

    /// ==== 高レベル統合機能 ====
    /// エージェントにタスクを自動割り当て
    member this.AutoAssignTask(taskId: string) =
        try
            if disposed then
                Result.Error(SystemError "RealtimeCollaborationFacade has been disposed")
            else
                match this.GetTask(taskId), this.GetAgentsByStatus(Idle) with
                | Result.Ok(Some task), Result.Ok idleAgents when not idleAgents.IsEmpty ->
                    // 最適なエージェントを選択（簡易的にランダム選択）
                    let selectedAgent = idleAgents |> List.head

                    // エージェント状態を更新
                    match this.UpdateAgentState(selectedAgent.AgentId, Working, 0.0, ?currentTask = Some taskId) with
                    | Result.Ok() ->
                        // タスク状態を更新
                        match this.UpdateTaskStatus(taskId, InProgress) with
                        | Result.Ok() ->
                            logInfo "RealtimeCollaborationFacade"
                            <| sprintf "Task %s auto-assigned to agent %s" taskId selectedAgent.AgentId

                            Result.Ok selectedAgent.AgentId
                        | Result.Error e -> Result.Error e
                    | Result.Error e -> Result.Error e
                | Result.Ok(Some _), Result.Ok [] -> Result.Error(ResourceUnavailable "No idle agents available")
                | Result.Ok None, _ -> Result.Error(NotFound(sprintf "Task not found: %s" taskId))
                | Result.Error e, _ -> Result.Error e
        with ex ->
            logError "RealtimeCollaborationFacade"
            <| sprintf "Error auto-assigning task: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// ワークフロー実行（タスクチェーンの自動実行）
    member this.ExecuteWorkflow(taskIds: string list) =
        async {
            try
                if disposed then
                    return Result.Error(SystemError "RealtimeCollaborationFacade has been disposed")
                else
                    let results = ResizeArray<string * Result<string, CollaborationError>>()

                    for taskId in taskIds do
                        match! this.ExecuteTaskWithCoordination(taskId) with
                        | Result.Ok agentId ->
                            results.Add((taskId, Result.Ok agentId))

                            logInfo "RealtimeCollaborationFacade"
                            <| sprintf "Workflow task %s completed by %s" taskId agentId
                        | Result.Error e ->
                            results.Add((taskId, Result.Error e))

                            logError "RealtimeCollaborationFacade"
                            <| sprintf "Workflow task %s failed: %A" taskId e

                    return Result.Ok(results.ToArray() |> Array.toList)
            with ex ->
                logError "RealtimeCollaborationFacade"
                <| sprintf "Error executing workflow: %s" ex.Message

                return Result.Error(SystemError ex.Message)
        }

    /// 協調制御付きタスク実行
    member this.ExecuteTaskWithCoordination(taskId: string) =
        async {
            try
                if disposed then
                    return Result.Error(SystemError "RealtimeCollaborationFacade has been disposed")
                else
                    // 1. タスクの実行可能性チェック
                    match this.GetExecutableTasks() with
                    | Result.Ok executableTasks ->
                        match executableTasks |> List.tryFind (fun t -> t.TaskId = taskId) with
                        | Some task ->
                            // 2. エージェント自動割り当て
                            match this.AutoAssignTask(taskId) with
                            | Result.Ok agentId ->
                                // 3. リソース要求（簡易実装）
                                let requiredResources = [ "cpu"; "memory" ] // 実際の実装では詳細な解析が必要

                                match this.RequestTaskExecution(agentId, taskId, requiredResources) with
                                | Result.Ok() ->
                                    // 4. タスク実行シミュレーション（実際の実装では外部プロセス制御）
                                    do! Async.Sleep(1000) // 1秒のシミュレーション

                                    // 5. タスク完了通知
                                    match this.CompleteTask(taskId) with
                                    | Result.Ok newlyExecutable ->
                                        match this.NotifyTaskCompletion(agentId, taskId, requiredResources) with
                                        | Result.Ok() ->
                                            // 6. エージェント状態をIdle に戻す
                                            match this.UpdateAgentState(agentId, Idle, 100.0) with
                                            | Result.Ok() ->
                                                logInfo "RealtimeCollaborationFacade"
                                                <| sprintf "Task %s executed successfully by %s" taskId agentId

                                                return Result.Ok agentId
                                            | Result.Error e -> return Result.Error e
                                        | Result.Error e -> return Result.Error e
                                    | Result.Error e -> return Result.Error e
                                | Result.Error e -> return Result.Error e
                            | Result.Error e -> return Result.Error e
                        | None ->
                            return
                                Result.Error(
                                    InvalidInput(sprintf "Task %s is not executable (dependencies not met)" taskId)
                                )
                    | Result.Error e -> return Result.Error e
            with ex ->
                logError "RealtimeCollaborationFacade"
                <| sprintf "Error executing task with coordination: %s" ex.Message

                return Result.Error(SystemError ex.Message)
        }

    /// システム健全性チェック
    member this.PerformSystemHealthCheck() =
        try
            if disposed then
                Result.Error(SystemError "RealtimeCollaborationFacade has been disposed")
            else
                let results = ResizeArray<string * bool>()

                // エージェント健全性チェック
                match agentStateManager.PerformHealthCheck() with
                | Result.Ok staleAgents -> results.Add(("AgentHealth", staleAgents.IsEmpty))
                | Result.Error _ -> results.Add(("AgentHealth", false))

                // デッドロック検出
                match this.DetectDeadlock() with
                | Result.Ok deadlock -> results.Add(("DeadlockFree", deadlock.IsNone))
                | Result.Error _ -> results.Add(("DeadlockFree", false))

                // 循環依存検出
                match taskDependencyGraph.DetectCircularDependencies() with
                | Result.Ok cycles -> results.Add(("CircularDependencyFree", cycles.IsEmpty))
                | Result.Error _ -> results.Add(("CircularDependencyFree", false))

                let overallHealth = results |> Seq.forall snd

                let healthReport =
                    {| OverallHealthy = overallHealth
                       ComponentHealth = results.ToArray() |> Array.toList
                       Timestamp = DateTime.UtcNow |}

                logInfo "RealtimeCollaborationFacade"
                <| sprintf "System health check completed: %b" overallHealth

                Result.Ok healthReport
        with ex ->
            logError "RealtimeCollaborationFacade"
            <| sprintf "Error performing system health check: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// システム全体リセット
    member this.ResetSystem() =
        try
            if disposed then
                Result.Error(SystemError "RealtimeCollaborationFacade has been disposed")
            else
                let results = ResizeArray<string * Result<unit, CollaborationError>>()

                results.Add(("AgentStateManager", agentStateManager.Reset()))
                results.Add(("TaskDependencyGraph", taskDependencyGraph.Reset()))
                results.Add(("ProgressAggregator", progressAggregator.Reset()))
                results.Add(("CollaborationCoordinator", collaborationCoordinator.ResetCoordinationState()))

                let allSuccess =
                    results
                    |> Seq.forall (fun (_, result) ->
                        match result with
                        | Result.Ok() -> true
                        | Result.Error _ -> false)

                if allSuccess then
                    systemEvent.Trigger(SystemReset)
                    logInfo "RealtimeCollaborationFacade" "System reset completed successfully"
                    Result.Ok()
                else
                    let failures =
                        results
                        |> Seq.filter (fun (_, result) ->
                            match result with
                            | Result.Error _ -> true
                            | _ -> false)
                        |> Seq.map fst
                        |> String.concat ", "

                    Result.Error(SystemError(sprintf "System reset failed for components: %s" failures))
        with ex ->
            logError "RealtimeCollaborationFacade"
            <| sprintf "Error resetting system: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// リソース解放
    member _.Dispose() =
        if not disposed then
            disposed <- true
            progressAggregator.Dispose()
            collaborationCoordinator.Dispose()
            taskDependencyGraph.Dispose()
            agentStateManager.Dispose()
            logInfo "RealtimeCollaborationFacade" "RealtimeCollaborationFacade disposed"

    interface IDisposable with
        member this.Dispose() = this.Dispose()

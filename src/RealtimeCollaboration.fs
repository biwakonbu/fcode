module FCode.RealtimeCollaboration

open System
open System.Collections.Concurrent
open FCode.Logger
open FCode.Collaboration.CollaborationTypes

/// リアルタイム協調機能基盤（統合版）
type RealtimeCollaborationManager() =
    let agentStates = ConcurrentDictionary<string, AgentState>()
    let tasks = ConcurrentDictionary<string, TaskInfo>()
    let dependencies = ConcurrentDictionary<string, string list>()
    let lockObj = obj ()

    // イベント定義
    let stateChangedEvent = Event<string * AgentState>()
    let taskCompletedEvent = Event<string>()
    let progressUpdatedEvent = Event<ProgressSummary>()

    /// イベント公開
    [<CLIEvent>]
    member _.StateChanged = stateChangedEvent.Publish

    [<CLIEvent>]
    member _.TaskCompleted = taskCompletedEvent.Publish

    [<CLIEvent>]
    member _.ProgressUpdated = progressUpdatedEvent.Publish

    /// 進捗サマリー計算（内部メソッド）
    member private this.CalculateProgressSummary() =
        let allTasks = tasks.Values |> Seq.toList
        let completedTasks = allTasks |> List.filter (fun t -> t.Status = Completed)
        let inProgressTasks = allTasks |> List.filter (fun t -> t.Status = InProgress)

        let activeAgents =
            agentStates.Values |> Seq.filter (fun s -> s.Status = Working) |> Seq.length

        let overallProgress =
            if allTasks.IsEmpty then
                0.0
            else
                float completedTasks.Length / float allTasks.Length * 100.0

        { TotalTasks = allTasks.Length
          CompletedTasks = completedTasks.Length
          InProgressTasks = inProgressTasks.Length
          BlockedTasks = 0 // 簡略版では計算しない
          ActiveAgents = activeAgents
          OverallProgress = overallProgress
          EstimatedTimeRemaining = None
          LastUpdated = DateTime.UtcNow }

    /// エージェント状態更新
    member this.UpdateAgentState(agentId: string, status: AgentStatus, ?progress: float, ?currentTask: string) =
        lock lockObj (fun () ->
            let newState =
                { AgentId = agentId
                  Status = status
                  Progress = defaultArg progress 0.0
                  LastUpdate = DateTime.UtcNow
                  CurrentTask = currentTask
                  WorkingDirectory = ""
                  ProcessId = None
                  ActiveTasks = [] }

            agentStates.AddOrUpdate(agentId, newState, fun _ _ -> newState) |> ignore
            stateChangedEvent.Trigger(agentId, newState)
            logInfo "RealtimeCollaboration" $"Agent {agentId} state updated: {status}"

            // 進捗更新イベント発火
            let summary = this.CalculateProgressSummary()
            progressUpdatedEvent.Trigger(summary))

    /// タスク追加
    member _.AddTask(taskId: string, title: string, ?assignedAgent: string, ?priority: int) =
        lock lockObj (fun () ->
            let now = DateTime.UtcNow

            let taskPriority =
                match priority with
                | Some p when p >= 1 && p <= 4 -> enum<TaskPriority> p
                | _ -> TaskPriority.Medium

            let task =
                { TaskId = taskId
                  Title = title
                  Description = ""
                  Status = Pending
                  AssignedAgent = assignedAgent
                  Priority = taskPriority
                  EstimatedDuration = None
                  ActualDuration = None
                  RequiredResources = []
                  Dependencies = []
                  CreatedAt = now
                  UpdatedAt = now }

            tasks.AddOrUpdate(taskId, task, fun _ _ -> task) |> ignore
            logInfo "RealtimeCollaboration" $"Task added: {taskId} - {title}")

    /// タスク完了
    member this.CompleteTask(taskId: string) =
        lock lockObj (fun () ->
            match tasks.TryGetValue(taskId) with
            | true, task ->
                let completedTask =
                    { task with
                        Status = Completed
                        UpdatedAt = DateTime.UtcNow }

                tasks.[taskId] <- completedTask
                taskCompletedEvent.Trigger(taskId)
                logInfo "RealtimeCollaboration" $"Task completed: {taskId}"

                // 進捗更新
                let summary = this.CalculateProgressSummary()
                progressUpdatedEvent.Trigger(summary)
            | false, _ -> logWarning "RealtimeCollaboration" $"Task not found: {taskId}")

    /// 依存関係追加
    member _.AddTaskDependency(taskId: string, dependsOnTaskId: string) =
        lock lockObj (fun () ->
            let currentDeps =
                match dependencies.TryGetValue(taskId) with
                | true, deps -> deps
                | false, _ -> []

            let newDeps = dependsOnTaskId :: currentDeps |> List.distinct
            dependencies.AddOrUpdate(taskId, newDeps, fun _ _ -> newDeps) |> ignore
            logInfo "RealtimeCollaboration" $"Dependency added: {taskId} depends on {dependsOnTaskId}")

    /// 実行可能タスク取得
    member _.GetExecutableTasks() =
        lock lockObj (fun () ->
            tasks.Values
            |> Seq.filter (fun task ->
                task.Status = Pending
                && match dependencies.TryGetValue(task.TaskId) with
                   | true, deps ->
                       deps
                       |> List.forall (fun depId ->
                           match tasks.TryGetValue(depId) with
                           | true, depTask -> depTask.Status = Completed
                           | false, _ -> false)
                   | false, _ -> true)
            |> Seq.toList)

    /// 進捗サマリー取得（パブリック）
    member this.GetProgressSummary() = this.CalculateProgressSummary()

    /// エージェント状態一覧取得
    member _.GetAllAgentStates() = agentStates.Values |> Seq.toList

    /// タスク一覧取得
    member _.GetAllTasks() = tasks.Values |> Seq.toList

    /// ブロックされたタスク取得
    member _.GetBlockedTasks() =
        lock lockObj (fun () ->
            tasks.Values
            |> Seq.filter (fun task ->
                task.Status = Pending
                && match dependencies.TryGetValue(task.TaskId) with
                   | true, deps ->
                       deps
                       |> List.exists (fun depId ->
                           match tasks.TryGetValue(depId) with
                           | true, depTask -> depTask.Status <> Completed
                           | false, _ -> true)
                   | false, _ -> false)
            |> Seq.toList)

    /// 健全性チェック
    member _.PerformHealthCheck() =
        let staleThreshold = TimeSpan.FromMinutes(5.0)
        let now = DateTime.UtcNow

        agentStates.Values
        |> Seq.filter (fun state -> now - state.LastUpdate > staleThreshold && state.Status = Working)
        |> Seq.iter (fun state -> logWarning "RealtimeCollaboration" $"Agent {state.AgentId} appears stale")

    /// 統計情報取得
    member this.GetStatistics() =
        let summary = this.GetProgressSummary()
        let blockedTaskCount = this.GetBlockedTasks().Length
        let executableTaskCount = this.GetExecutableTasks().Length

        logInfo
            "RealtimeCollaboration"
            $"Statistics - Total: {summary.TotalTasks}, Completed: {summary.CompletedTasks}, Blocked: {blockedTaskCount}, Executable: {executableTaskCount}"

        {| TotalTasks = summary.TotalTasks
           CompletedTasks = summary.CompletedTasks
           BlockedTasks = blockedTaskCount
           ExecutableTasks = executableTaskCount
           ActiveAgents = summary.ActiveAgents
           OverallProgress = summary.OverallProgress |}

    /// システムリセット
    member _.Reset() =
        lock lockObj (fun () ->
            agentStates.Clear()
            tasks.Clear()
            dependencies.Clear()
            logInfo "RealtimeCollaboration" "System state reset")

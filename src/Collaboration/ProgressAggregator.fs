module FCode.Collaboration.ProgressAggregator

open System
open System.Threading
open FCode.Logger
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IAgentStateManager
open FCode.Collaboration.ITaskDependencyGraph
open FCode.Collaboration.IProgressAggregator

/// 進捗集計・可視化実装
type ProgressAggregator
    (agentStateManager: IAgentStateManager, taskDependencyGraph: ITaskDependencyGraph, config: CollaborationConfig) =
    let progressChangedEvent = Event<ProgressSummary>()
    let mutable lastSummary = None
    let mutable monitoringTimer: Timer option = None
    let lockObj = obj ()
    let mutable disposed = false

    /// 内部進捗計算
    let calculateProgressInternal () =
        try
            if disposed then
                Result.Error(SystemError "ProgressAggregator has been disposed")
            else
                match agentStateManager.GetAllAgentStates(), taskDependencyGraph.GetStatistics() with
                | Result.Ok agentStates, Result.Ok taskStats ->
                    let activeAgents =
                        agentStates
                        |> List.filter (fun state ->
                            match state.Status with
                            | Working
                            | Blocked -> true
                            | _ -> false)
                        |> List.length

                    let inProgressTasks =
                        agentStates |> List.filter (fun state -> state.Status = Working) |> List.length

                    let estimatedTimeRemaining =
                        if taskStats.CompletionRate >= 100.0 then
                            Some TimeSpan.Zero
                        else
                            match taskDependencyGraph.GetCriticalPath() with
                            | Result.Ok(criticalPathDuration, _) ->
                                let remainingRatio = (100.0 - taskStats.CompletionRate) / 100.0

                                let estimatedDuration =
                                    TimeSpan.FromTicks(int64 (float criticalPathDuration.Ticks * remainingRatio))

                                Some estimatedDuration
                            | Result.Error _ -> None

                    let summary =
                        { TotalTasks = taskStats.TotalTasks
                          CompletedTasks = taskStats.CompletedTasks
                          InProgressTasks = inProgressTasks
                          BlockedTasks = taskStats.BlockedTasks
                          ActiveAgents = activeAgents
                          OverallProgress = taskStats.CompletionRate
                          EstimatedTimeRemaining = estimatedTimeRemaining
                          LastUpdated = DateTime.UtcNow }

                    Result.Ok summary
                | Result.Error e, _
                | _, Result.Error e -> Result.Error e
        with ex ->
            logError "ProgressAggregator"
            <| sprintf "Error calculating progress: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 進捗変更イベント
    [<CLIEvent>]
    member _.ProgressChanged = progressChangedEvent.Publish

    /// 現在の進捗サマリーを取得
    member _.GetCurrentSummary() =
        try
            lock lockObj (fun () ->
                match calculateProgressInternal () with
                | Result.Ok summary ->
                    // 前回と差異がある場合のみイベント発火
                    match lastSummary with
                    | Some prevSummary when prevSummary <> summary ->
                        progressChangedEvent.Trigger(summary)

                        logInfo "ProgressAggregator"
                        <| sprintf
                            "Progress updated: %.1f%% (%d/%d tasks)"
                            summary.OverallProgress
                            summary.CompletedTasks
                            summary.TotalTasks
                    | None ->
                        progressChangedEvent.Trigger(summary)
                        logInfo "ProgressAggregator" "Initial progress summary generated"
                    | _ -> () // 変更なし

                    lastSummary <- Some summary
                    Result.Ok summary
                | Result.Error e -> Result.Error e)
        with ex ->
            logError "ProgressAggregator"
            <| sprintf "Error getting current summary: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// エージェント別進捗詳細を取得
    member _.GetAgentProgressDetails() =
        try
            if disposed then
                Result.Error(SystemError "ProgressAggregator has been disposed")
            else
                match agentStateManager.GetAllAgentStates() with
                | Result.Ok agentStates ->
                    let now = DateTime.UtcNow

                    let details =
                        agentStates
                        |> List.map (fun state ->
                            {| AgentId = state.AgentId
                               Status = state.Status.ToString()
                               Progress = state.Progress
                               CurrentTask = state.CurrentTask |> Option.defaultValue "None"
                               LastUpdate = state.LastUpdate
                               WorkingTime = now - state.LastUpdate |})
                        |> List.sortByDescending (fun details -> details.Progress)

                    Result.Ok details
                | Result.Error e -> Result.Error e
        with ex ->
            logError "ProgressAggregator"
            <| sprintf "Error getting agent progress details: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// タスク別進捗詳細を取得
    member _.GetTaskProgressDetails() =
        try
            if disposed then
                Result.Error(SystemError "ProgressAggregator has been disposed")
            else
                match
                    taskDependencyGraph.GetAllTasks(),
                    taskDependencyGraph.GetBlockedTasks(),
                    taskDependencyGraph.GetExecutableTasks()
                with
                | Result.Ok allTasks, Result.Ok blockedTasks, Result.Ok executableTasks ->
                    let details =
                        allTasks
                        |> List.map (fun task ->
                            let isBlocked =
                                blockedTasks
                                |> List.exists (fun (blockedTask, _) -> blockedTask.TaskId = task.TaskId)

                            let isExecutable =
                                executableTasks
                                |> List.exists (fun executableTask -> executableTask.TaskId = task.TaskId)

                            {| TaskId = task.TaskId
                               Status = task.Status.ToString()
                               AssignedAgent = task.AssignedAgent |> Option.defaultValue "Unassigned"
                               IsBlocked = isBlocked
                               IsExecutable = isExecutable
                               Priority = task.Priority |})

                    Result.Ok details
                | Result.Error e, _, _
                | _, Result.Error e, _
                | _, _, Result.Error e -> Result.Error e
        with ex ->
            logError "ProgressAggregator"
            <| sprintf "Error getting task progress details: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 進捗トレンド分析
    member this.AnalyzeProgressTrend() =
        try
            if disposed then
                Result.Error(SystemError "ProgressAggregator has been disposed")
            else
                match this.GetCurrentSummary() with
                | Result.Ok summary ->
                    let velocity =
                        if summary.ActiveAgents > 0 then
                            summary.OverallProgress / float summary.ActiveAgents
                        else
                            0.0

                    let efficiency =
                        if summary.TotalTasks > 0 then
                            float summary.CompletedTasks / float summary.TotalTasks * 100.0
                        else
                            0.0

                    let recommendedActions =
                        if summary.BlockedTasks > 0 then
                            [ "ブロッカー解決を優先"; "リソース再配分を検討" ]
                        elif summary.ActiveAgents = 0 then
                            [ "エージェント起動を確認"; "タスク配分を実行" ]
                        else
                            [ "順調に進行中" ]

                    let analysis =
                        {| CurrentVelocity = velocity
                           Efficiency = efficiency
                           BottleneckRisk = summary.BlockedTasks > summary.InProgressTasks
                           RecommendedActions = recommendedActions |}

                    Result.Ok analysis
                | Result.Error e -> Result.Error e
        with ex ->
            logError "ProgressAggregator"
            <| sprintf "Error analyzing progress trend: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// マイルストーン進捗チェック
    member this.CheckMilestones(milestones: (string * float) list) =
        try
            if disposed then
                Result.Error(SystemError "ProgressAggregator has been disposed")
            else
                match this.GetCurrentSummary() with
                | Result.Ok summary ->
                    let currentProgress = summary.OverallProgress

                    let milestoneStatus =
                        milestones
                        |> List.map (fun (milestone, targetProgress) ->
                            let achieved = currentProgress >= targetProgress
                            let gap = if achieved then 0.0 else targetProgress - currentProgress

                            {| Milestone = milestone
                               TargetProgress = targetProgress
                               Achieved = achieved
                               Gap = gap |})

                    Result.Ok milestoneStatus
                | Result.Error e -> Result.Error e
        with ex ->
            logError "ProgressAggregator"
            <| sprintf "Error checking milestones: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 進捗レポート生成
    member this.GenerateProgressReport() =
        try
            if disposed then
                Result.Error(SystemError "ProgressAggregator has been disposed")
            else
                match
                    this.GetCurrentSummary(),
                    this.GetAgentProgressDetails(),
                    this.GetTaskProgressDetails(),
                    this.AnalyzeProgressTrend()
                with
                | Result.Ok summary, Result.Ok agentDetails, Result.Ok taskDetails, Result.Ok trend ->
                    let report =
                        sprintf
                            """=== Progress Report ===
Generated: %s

Overall Progress: %.1f%% (%d/%d tasks completed)
Estimated Time Remaining: %s
Active Agents: %d

Agent Status:
%s

Task Status:
- Completed: %d
- In Progress: %d  
- Blocked: %d
- Executable: %d

Performance Analysis:
- Velocity: %.2f
- Efficiency: %.1f%%
- Bottleneck Risk: %b

Recommendations:
%s
========================"""
                            (DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                            summary.OverallProgress
                            summary.CompletedTasks
                            summary.TotalTasks
                            (summary.EstimatedTimeRemaining
                             |> Option.map (fun ts -> ts.ToString(@"hh\:mm\:ss"))
                             |> Option.defaultValue "Unknown")
                            summary.ActiveAgents
                            (agentDetails
                             |> List.take (min 5 agentDetails.Length) // 最大5エージェント表示
                             |> List.map (fun agent ->
                                 sprintf "  - %s: %s (%.1f%%)" agent.AgentId agent.Status agent.Progress)
                             |> String.concat "\n")
                            summary.CompletedTasks
                            summary.InProgressTasks
                            summary.BlockedTasks
                            (taskDetails |> List.filter (fun t -> t.IsExecutable) |> List.length)
                            trend.CurrentVelocity
                            trend.Efficiency
                            trend.BottleneckRisk
                            (trend.RecommendedActions |> List.map (sprintf "  - %s") |> String.concat "\n")

                    logInfo "ProgressAggregator" "Progress report generated"
                    Result.Ok report
                | Result.Error e, _, _, _
                | _, Result.Error e, _, _
                | _, _, Result.Error e, _
                | _, _, _, Result.Error e -> Result.Error e
        with ex ->
            logError "ProgressAggregator"
            <| sprintf "Error generating progress report: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// リアルタイム進捗監視開始
    member this.StartMonitoring(intervalSeconds: int) =
        try
            if disposed then
                Result.Error(SystemError "ProgressAggregator has been disposed")
            elif intervalSeconds <= 0 then
                Result.Error(InvalidInput "Interval must be positive")
            else
                lock lockObj (fun () ->
                    // 既存のタイマーを停止
                    monitoringTimer |> Option.iter (fun timer -> timer.Dispose())

                    // 安全なタイマー実装：例外時の自動停止機能付き
                    let createSafeTimer () =
                        let mutable isDisposed = false

                        let safeCallback =
                            fun _ ->
                                if not isDisposed then
                                    try
                                        this.GetCurrentSummary() |> ignore
                                    with ex ->
                                        logError "ProgressAggregator"
                                        <| sprintf "Monitoring error: %s - Stopping timer" ex.Message

                                        isDisposed <- true
                                        // 例外時にタイマーを停止
                                        lock lockObj (fun () ->
                                            monitoringTimer |> Option.iter (fun t -> t.Dispose())
                                            monitoringTimer <- None)

                        new Timer(safeCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(float intervalSeconds))

                    let timer = createSafeTimer ()

                    monitoringTimer <- Some timer

                    logInfo "ProgressAggregator"
                    <| sprintf "Progress monitoring started (interval: %ds)" intervalSeconds

                    Result.Ok(timer :> IDisposable))
        with ex ->
            logError "ProgressAggregator"
            <| sprintf "Error starting monitoring: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 手動進捗更新トリガー
    member this.TriggerProgressUpdate() = this.GetCurrentSummary()

    /// システムリセット
    member _.Reset() =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "ProgressAggregator has been disposed")
                else
                    lastSummary <- None
                    monitoringTimer |> Option.iter (fun timer -> timer.Dispose())
                    monitoringTimer <- None
                    logInfo "ProgressAggregator" "System state reset"
                    Result.Ok())
        with ex ->
            logError "ProgressAggregator" <| sprintf "Error resetting system: %s" ex.Message
            Result.Error(SystemError ex.Message)

    /// リソース解放
    member _.Dispose() =
        if not disposed then
            disposed <- true
            monitoringTimer |> Option.iter (fun timer -> timer.Dispose())
            monitoringTimer <- None
            logInfo "ProgressAggregator" "ProgressAggregator disposed"

    interface IProgressAggregator with
        member this.GetCurrentSummary() = this.GetCurrentSummary()
        member this.GetAgentProgressDetails() = this.GetAgentProgressDetails()
        member this.GetTaskProgressDetails() = this.GetTaskProgressDetails()
        member this.AnalyzeProgressTrend() = this.AnalyzeProgressTrend()
        member this.CheckMilestones(milestones) = this.CheckMilestones(milestones)
        member this.GenerateProgressReport() = this.GenerateProgressReport()
        member this.StartMonitoring(intervalSeconds) = this.StartMonitoring(intervalSeconds)
        member this.ProgressChanged = this.ProgressChanged
        member this.TriggerProgressUpdate() = this.TriggerProgressUpdate()
        member this.Reset() = this.Reset()

    interface IDisposable with
        member this.Dispose() = this.Dispose()

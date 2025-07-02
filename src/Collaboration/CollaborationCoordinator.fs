module FCode.Collaboration.CollaborationCoordinator

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open FCode.Logger
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.ICollaborationCoordinator
open FCode.Collaboration.IAgentStateManager
open FCode.Collaboration.ITaskDependencyGraph

/// 協調作業制御・競合回避実装
type CollaborationCoordinator
    (agentStateManager: IAgentStateManager, taskDependencyGraph: ITaskDependencyGraph, config: CollaborationConfig) =
    let resourceLocks = ConcurrentDictionary<string, (string * DateTime)>() // resource -> (agentId, lockTime)
    let operationQueue = ConcurrentQueue<string * string * DateTime>() // agentId, taskId, requestTime

    let activeOperations =
        ConcurrentDictionary<string, (string * DateTime * string list)>() // operationId -> (agentId, startTime, resources)

    let collaborationEvent = Event<CollaborationEvent>()

    let syncBarriers =
        ConcurrentDictionary<string, TaskCompletionSource<bool> * string list>() // reason -> (tcs, agents)

    let lockObj = obj ()
    let mutable disposed = false

    /// リソースID検証
    let validateResourceId resourceId =
        if String.IsNullOrWhiteSpace(resourceId) then
            Result.Error(InvalidInput "ResourceId cannot be null or empty")
        else
            Result.Ok resourceId

    /// エージェントID検証
    let validateAgentIds agentIds =
        let invalidAgents = agentIds |> List.filter String.IsNullOrWhiteSpace

        if not invalidAgents.IsEmpty then
            let invalidAgentsStr = String.Join(", ", invalidAgents)
            Result.Error(InvalidInput(sprintf "Invalid agent IDs: %s" invalidAgentsStr))
        else
            Result.Ok agentIds

    /// 操作ID生成
    let generateOperationId agentId taskId =
        sprintf "%s_%s_%d" agentId taskId DateTime.UtcNow.Ticks

    /// リソース競合チェック
    let checkResourceConflicts (requiredResources: string list) =
        try
            let conflicts =
                requiredResources
                |> List.filter (fun resource -> resourceLocks.ContainsKey(resource))
                |> List.map (fun resource ->
                    let (lockingAgent, lockTime) = resourceLocks.[resource]

                    let msg =
                        sprintf
                            "Resource '%s' locked by agent '%s' since %s"
                            resource
                            lockingAgent
                            (lockTime.ToString())

                    CollaborationTypes.ResourceConflict msg)

            if conflicts.IsEmpty then
                Result.Ok()
            else
                Result.Error(ConflictDetected conflicts)
        with ex ->
            logError "CollaborationCoordinator"
            <| sprintf "Error checking resource conflicts: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// デッドロック検出アルゴリズム
    let detectDeadlockInternal () =
        try
            let waitGraph = Dictionary<string, string list>()

            // 待機グラフ構築: エージェント -> 待機中のリソースを持つエージェント
            for kvp in resourceLocks do
                let resource = kvp.Key
                let (lockingAgent, _) = kvp.Value

                // このリソースを待機しているエージェントを探す
                let waitingAgents =
                    operationQueue.ToArray()
                    |> Array.filter (fun (_, taskId, _) ->
                        match taskDependencyGraph.GetTask(taskId) with
                        | Result.Ok(Some task) ->
                            // 実際のリソース依存関係をチェック
                            task.RequiredResources |> List.contains resource
                        | _ -> false)
                    |> Array.map (fun (agentId, _, _) -> agentId)
                    |> Array.distinct

                waitingAgents
                |> Array.iter (fun waitingAgent ->
                    if not (waitGraph.ContainsKey(waitingAgent)) then
                        waitGraph.[waitingAgent] <- []

                    waitGraph.[waitingAgent] <- lockingAgent :: waitGraph.[waitingAgent])

            // 循環検出（簡易DFS）
            let visited = HashSet<string>()
            let recursionStack = HashSet<string>()
            let cycles = ResizeArray<string list>()

            let rec detectCycle agent path =
                if recursionStack.Contains(agent) then
                    let cycleStart = path |> List.findIndex ((=) agent)
                    let cycle = path |> List.skip cycleStart
                    cycles.Add(agent :: cycle)
                    true
                elif visited.Contains(agent) then
                    false
                else
                    visited.Add(agent) |> ignore
                    recursionStack.Add(agent) |> ignore

                    let hasCycle =
                        match waitGraph.TryGetValue(agent) with
                        | true, dependents -> dependents |> List.exists (fun dep -> detectCycle dep (agent :: path))
                        | false, _ -> false

                    recursionStack.Remove(agent) |> ignore
                    hasCycle

            waitGraph.Keys |> Seq.iter (fun agent -> detectCycle agent [] |> ignore)

            if cycles.Count > 0 then
                let deadlockedAgents = cycles |> Seq.head |> List.distinct
                Some deadlockedAgents
            else
                None
        with ex ->
            logError "CollaborationCoordinator"
            <| sprintf "Error detecting deadlock: %s" ex.Message

            None

    /// 協調作業イベント
    [<CLIEvent>]
    member _.CollaborationEvent = collaborationEvent.Publish

    /// タスク開始前の競合チェック・許可
    member _.RequestTaskExecution(agentId: string, taskId: string, requiredResources: string list) =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "CollaborationCoordinator has been disposed")
                else
                    match
                        validateAgentIds [ agentId ],
                        requiredResources
                        |> List.map validateResourceId
                        |> List.fold
                            (fun acc r ->
                                match acc, r with
                                | Result.Ok accList, Result.Ok resource -> Result.Ok(resource :: accList)
                                | Result.Error e, _
                                | _, Result.Error e -> Result.Error e)
                            (Result.Ok [])
                    with
                    | Result.Error e, _
                    | _, Result.Error e -> Result.Error e
                    | Result.Ok _, Result.Ok validResources ->
                        // デッドロック検出
                        match detectDeadlockInternal () with
                        | Some deadlockedAgents when List.contains agentId deadlockedAgents ->
                            logError "CollaborationCoordinator"
                            <| sprintf "Deadlock detected involving agent %s" agentId

                            Result.Error(CollaborationError.DeadlockDetected deadlockedAgents)
                        | _ ->
                            // リソース競合チェック
                            match checkResourceConflicts validResources with
                            | Result.Ok() ->
                                // リソースをロック
                                let lockTime = DateTime.UtcNow

                                validResources
                                |> List.iter (fun resource -> resourceLocks.[resource] <- (agentId, lockTime))

                                // アクティブオペレーション記録
                                let operationId = generateOperationId agentId taskId
                                activeOperations.[operationId] <- (agentId, lockTime, validResources)

                                collaborationEvent.Trigger(TaskStarted(agentId, taskId, validResources))
                                let resourcesStr = String.Join(", ", validResources)

                                logInfo "CollaborationCoordinator"
                                <| sprintf
                                    "Task execution approved: %s -> %s (Resources: %s)"
                                    agentId
                                    taskId
                                    resourcesStr

                                Result.Ok()
                            | Result.Error conflictError ->
                                // キューに追加して後で再試行
                                operationQueue.Enqueue((agentId, taskId, DateTime.UtcNow))

                                logInfo "CollaborationCoordinator"
                                <| sprintf "Task queued due to resource conflicts: %s -> %s" agentId taskId

                                Result.Error conflictError)
        with ex ->
            logError "CollaborationCoordinator"
            <| sprintf "Error requesting task execution: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// タスク完了通知・リソース解放
    member this.NotifyTaskCompletion(agentId: string, taskId: string, releasedResources: string list) =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "CollaborationCoordinator has been disposed")
                else
                    match
                        validateAgentIds [ agentId ],
                        releasedResources
                        |> List.map validateResourceId
                        |> List.fold
                            (fun acc r ->
                                match acc, r with
                                | Result.Ok accList, Result.Ok resource -> Result.Ok(resource :: accList)
                                | Result.Error e, _
                                | _, Result.Error e -> Result.Error e)
                            (Result.Ok [])
                    with
                    | Result.Error e, _
                    | _, Result.Error e -> Result.Error e
                    | Result.Ok _, Result.Ok validResources ->
                        // リソース解放
                        validResources
                        |> List.iter (fun resource -> resourceLocks.TryRemove(resource) |> ignore)

                        // アクティブオペレーション削除
                        let operationsToRemove =
                            activeOperations.ToArray()
                            |> Array.filter (fun kvp ->
                                let (opAgentId, _, _) = kvp.Value
                                opAgentId = agentId)
                            |> Array.map (fun kvp -> kvp.Key)

                        operationsToRemove
                        |> Array.iter (fun opId -> activeOperations.TryRemove(opId) |> ignore)

                        collaborationEvent.Trigger(TaskCompleted(agentId, taskId, validResources))
                        let releasedStr = String.Join(", ", validResources)

                        logInfo "CollaborationCoordinator"
                        <| sprintf "Task completion processed: %s -> %s (Released: %s)" agentId taskId releasedStr

                        // キューにあるタスクの再試行チェック
                        let queuedOperations = operationQueue.ToArray()
                        operationQueue.Clear()

                        queuedOperations
                        |> Array.iter (fun (queuedAgent, queuedTask, _) ->
                            // タスクの実際のリソース要求を取得して再試行
                            match taskDependencyGraph.GetTask(queuedTask) with
                            | Result.Ok(Some task) ->
                                match this.RequestTaskExecution(queuedAgent, queuedTask, task.RequiredResources) with
                                | Result.Ok() -> ()
                                | Result.Error _ -> operationQueue.Enqueue((queuedAgent, queuedTask, DateTime.UtcNow))
                            | _ ->
                                // タスクが見つからない場合はキューに戻さない
                                ())

                        Result.Ok())
        with ex ->
            logError "CollaborationCoordinator"
            <| sprintf "Error notifying task completion: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 同期ポイントでの協調制御
    member _.RequestSynchronization(participatingAgents: string list, reason: string) =
        async {
            try
                match validateAgentIds participatingAgents with
                | Result.Error e -> return Result.Error e
                | Result.Ok validAgents ->
                    if String.IsNullOrWhiteSpace(reason) then
                        return Result.Error(InvalidInput "Synchronization reason cannot be empty")
                    else
                        let tcs = TaskCompletionSource<bool>()
                        syncBarriers.[reason] <- (tcs, validAgents)

                        let agentsStr = String.Join(", ", validAgents)

                        logInfo "CollaborationCoordinator"
                        <| sprintf "Synchronization requested: %s (Agents: %s)" reason agentsStr

                        collaborationEvent.Trigger(SynchronizationRequested(validAgents, reason))

                        // 全エージェントの準備完了を待機（簡易実装）
                        let! result = Async.AwaitTask(tcs.Task)
                        return Result.Ok result
            with ex ->
                logError "CollaborationCoordinator"
                <| sprintf "Error requesting synchronization: %s" ex.Message

                return Result.Error(SystemError ex.Message)
        }

    /// 競合自動解決戦略の実行
    member _.ResolveConflict(conflict: ConflictType) =
        try
            match conflict with
            | ResourceConflict resource ->
                logInfo "CollaborationCoordinator"
                <| sprintf "Resolving resource conflict: %s" resource

                Result.Ok Queue // デフォルトはキューイング戦略
            | TaskConflict(task1, task2) ->
                logInfo "CollaborationCoordinator"
                <| sprintf "Resolving task conflict: %s vs %s" task1 task2

                Result.Ok Parallel // 可能な限り並列実行
            | AgentConflict(agent1, agent2) ->
                logInfo "CollaborationCoordinator"
                <| sprintf "Resolving agent conflict: %s vs %s" agent1 agent2

                Result.Ok(Delegate agent1) // 最初のエージェントに委任
        with ex ->
            logError "CollaborationCoordinator"
            <| sprintf "Error resolving conflict: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 並列作業効率分析
    member _.AnalyzeCollaborationEfficiency() =
        try
            if disposed then
                Result.Error(SystemError "CollaborationCoordinator has been disposed")
            else
                let activeOpsCount = activeOperations.Count
                let totalResourcesLocked = resourceLocks.Count

                match agentStateManager.GetActiveAgentCount() with
                | Result.Ok activeAgentCount ->
                    let parallelEfficiency =
                        if activeAgentCount > 0 then
                            float activeOpsCount / float activeAgentCount
                        else
                            0.0

                    let resourceUtilization =
                        let totalConfiguredResources = 10 // 設定から取得すべき

                        if totalConfiguredResources > 0 then
                            float totalResourcesLocked / float totalConfiguredResources
                        else
                            0.0

                    let bottleneckDetected = resourceUtilization > 0.8 || parallelEfficiency < 0.5

                    let analysis =
                        {| ActiveOperations = activeOpsCount
                           TotalAgents = activeAgentCount
                           ParallelEfficiency = parallelEfficiency
                           ResourceUtilization = resourceUtilization
                           BottleneckDetected = bottleneckDetected |}

                    logInfo "CollaborationCoordinator"
                    <| sprintf
                        "Collaboration efficiency analyzed: %.2f efficiency, %.2f resource utilization"
                        parallelEfficiency
                        resourceUtilization

                    Result.Ok analysis
                | Result.Error e -> Result.Error e
        with ex ->
            logError "CollaborationCoordinator"
            <| sprintf "Error analyzing collaboration efficiency: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// デッドロック検出
    member _.DetectDeadlock() =
        try
            if disposed then
                Result.Error(SystemError "CollaborationCoordinator has been disposed")
            else
                let deadlockedAgents = detectDeadlockInternal ()

                match deadlockedAgents with
                | Some agents ->
                    let agentsStr = String.Join(", ", agents)

                    logWarning "CollaborationCoordinator"
                    <| sprintf "Deadlock detected involving agents: %s" agentsStr

                    collaborationEvent.Trigger(DeadlockDetected agents)
                | None -> logInfo "CollaborationCoordinator" "No deadlock detected"

                Result.Ok deadlockedAgents
        with ex ->
            logError "CollaborationCoordinator"
            <| sprintf "Error detecting deadlock: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 協調作業統計取得
    member _.GetCollaborationStatistics() =
        try
            if disposed then
                Result.Error(SystemError "CollaborationCoordinator has been disposed")
            else
                let activeOpsCount = activeOperations.Count
                let lockedResourcesCount = resourceLocks.Count

                let averageDuration =
                    if activeOpsCount > 0 then
                        let now = DateTime.UtcNow

                        let totalDuration =
                            activeOperations.Values
                            |> Seq.map (fun (_, startTime, _) -> now - startTime)
                            |> Seq.fold (+) TimeSpan.Zero

                        TimeSpan.FromTicks(totalDuration.Ticks / int64 activeOpsCount)
                    else
                        TimeSpan.Zero

                let stats =
                    {| ActiveOperations = activeOpsCount
                       LockedResources = lockedResourcesCount
                       AverageOperationDuration = averageDuration |}

                Result.Ok stats
        with ex ->
            logError "CollaborationCoordinator"
            <| sprintf "Error getting collaboration statistics: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// システム状態のリセット（緊急時用）
    member _.ResetCoordinationState() =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "CollaborationCoordinator has been disposed")
                else
                    resourceLocks.Clear()
                    operationQueue.Clear()
                    activeOperations.Clear()
                    syncBarriers.Clear()

                    collaborationEvent.Trigger(CollaborationEvent.SystemReset)
                    logInfo "CollaborationCoordinator" "Coordination state reset"
                    Result.Ok())
        with ex ->
            logError "CollaborationCoordinator"
            <| sprintf "Error resetting coordination state: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// リソース解放
    member _.Dispose() =
        if not disposed then
            disposed <- true
            resourceLocks.Clear()
            operationQueue.Clear()
            activeOperations.Clear()
            syncBarriers.Clear()
            logInfo "CollaborationCoordinator" "CollaborationCoordinator disposed"

    interface ICollaborationCoordinator with
        member this.RequestTaskExecution(agentId, taskId, requiredResources) =
            this.RequestTaskExecution(agentId, taskId, requiredResources)

        member this.NotifyTaskCompletion(agentId, taskId, releasedResources) =
            this.NotifyTaskCompletion(agentId, taskId, releasedResources)

        member this.RequestSynchronization(participatingAgents, reason) =
            this.RequestSynchronization(participatingAgents, reason)

        member this.ResolveConflict(conflict) = this.ResolveConflict(conflict)
        member this.AnalyzeCollaborationEfficiency() = this.AnalyzeCollaborationEfficiency()
        member this.DetectDeadlock() = this.DetectDeadlock()
        member this.GetCollaborationStatistics() = this.GetCollaborationStatistics()
        member this.CollaborationEvent = this.CollaborationEvent
        member this.ResetCoordinationState() = this.ResetCoordinationState()

    interface IDisposable with
        member this.Dispose() = this.Dispose()

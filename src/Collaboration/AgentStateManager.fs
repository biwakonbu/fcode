module FCode.Collaboration.AgentStateManager

open System
open System.Collections.Concurrent
open System.Threading
open FCode.Logger
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IAgentStateManager

/// エージェント状態管理実装
type AgentStateManager(config: CollaborationConfig) =
    let states = ConcurrentDictionary<string, AgentState>()
    let stateChangedEvent = Event<AgentState>()
    let lockObj = obj ()
    let mutable disposed = false

    /// 入力検証
    let validateAgentId agentId =
        if String.IsNullOrWhiteSpace(agentId) then
            Result.Error(InvalidInput "AgentId cannot be null or empty")
        else
            Result.Ok agentId

    /// 進捗値検証
    let validateProgress progress =
        if progress < 0.0 || progress > 100.0 then
            Result.Error(InvalidInput(sprintf "Progress must be between 0.0 and 100.0, got %f" progress))
        else
            Result.Ok progress

    /// 状態変更イベント
    [<CLIEvent>]
    member _.StateChanged = stateChangedEvent.Publish

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
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "AgentStateManager has been disposed")
                else
                    match validateAgentId agentId with
                    | Result.Error e -> Result.Error e
                    | Result.Ok validId ->
                        let progressValue = defaultArg progress 0.0

                        match validateProgress progressValue with
                        | Result.Error e -> Result.Error e
                        | Result.Ok validProgress ->
                            let newState =
                                { AgentId = validId
                                  Status = status
                                  Progress = validProgress
                                  LastUpdate = DateTime.UtcNow
                                  CurrentTask = currentTask
                                  WorkingDirectory = defaultArg workingDir ""
                                  ProcessId = processId }

                            states.AddOrUpdate(validId, newState, fun _ _ -> newState) |> ignore
                            stateChangedEvent.Trigger(newState)

                            logInfo "AgentStateManager"
                            <| sprintf "Agent %s state updated: %A (Progress: %.1f%%)" validId status validProgress

                            Result.Ok())
        with ex ->
            logError "AgentStateManager"
            <| sprintf "Error updating agent state: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// エージェント状態を取得
    member _.GetAgentState(agentId: string) =
        try
            match validateAgentId agentId with
            | Result.Error e -> Result.Error e
            | Result.Ok validId ->
                match states.TryGetValue(validId) with
                | true, state -> Result.Ok(Some state)
                | false, _ -> Result.Ok None
        with ex ->
            logError "AgentStateManager"
            <| sprintf "Error getting agent state: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 全エージェント状態を取得
    member _.GetAllAgentStates() =
        try
            if disposed then
                Result.Error(SystemError "AgentStateManager has been disposed")
            else
                let statesList = states.Values |> Seq.toList
                Result.Ok statesList
        with ex ->
            logError "AgentStateManager"
            <| sprintf "Error getting all agent states: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 特定状態のエージェント一覧を取得
    member _.GetAgentsByStatus(status: AgentStatus) =
        try
            if disposed then
                Result.Error(SystemError "AgentStateManager has been disposed")
            else
                // 安全なスナップショット取得
                let statesSnapshot = states.Values |> Seq.toList

                let filteredStates =
                    statesSnapshot |> List.filter (fun state -> state.Status = status)

                Result.Ok filteredStates
        with ex ->
            logError "AgentStateManager"
            <| sprintf "Error getting agents by status: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// エージェントを削除
    member _.RemoveAgent(agentId: string) =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "AgentStateManager has been disposed")
                else
                    match validateAgentId agentId with
                    | Result.Error e -> Result.Error e
                    | Result.Ok validId ->
                        states.TryRemove(validId) |> ignore

                        logInfo "AgentStateManager"
                        <| sprintf "Agent %s removed from state manager" validId

                        Result.Ok())
        with ex ->
            logError "AgentStateManager" <| sprintf "Error removing agent: %s" ex.Message
            Result.Error(SystemError ex.Message)

    /// アクティブなエージェント数を取得
    member _.GetActiveAgentCount() =
        try
            if disposed then
                Result.Error(SystemError "AgentStateManager has been disposed")
            else
                // 安全なスナップショット取得
                let statesSnapshot = states.Values |> Seq.toList

                let activeCount =
                    statesSnapshot
                    |> List.filter (fun state ->
                        match state.Status with
                        | Working
                        | Blocked -> true
                        | _ -> false)
                    |> List.length

                Result.Ok activeCount
        with ex ->
            logError "AgentStateManager"
            <| sprintf "Error getting active agent count: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// エージェント進捗の平均値を計算
    member _.GetAverageProgress() =
        try
            if disposed then
                Result.Error(SystemError "AgentStateManager has been disposed")
            else
                // 安全なスナップショット取得
                let statesSnapshot = states.Values |> Seq.toList

                let workingStates =
                    statesSnapshot |> List.filter (fun state -> state.Status = Working)

                if workingStates.IsEmpty then
                    Result.Ok 0.0
                else
                    let avgProgress =
                        workingStates |> List.map (fun state -> state.Progress) |> List.average

                    Result.Ok avgProgress
        with ex ->
            logError "AgentStateManager"
            <| sprintf "Error calculating average progress: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// エージェント健全性チェック
    member _.PerformHealthCheck() =
        try
            if disposed then
                Result.Error(SystemError "AgentStateManager has been disposed")
            else
                let now = DateTime.UtcNow
                // 安全なスナップショット取得
                let statesSnapshot = states.Values |> Seq.toList

                let staleAgents =
                    statesSnapshot
                    |> List.filter (fun state ->
                        now - state.LastUpdate > config.StaleAgentThreshold && state.Status = Working)

                staleAgents
                |> List.iter (fun state ->
                    logWarning "AgentStateManager"
                    <| sprintf "Agent %s appears stale (last update: %s)" state.AgentId (state.LastUpdate.ToString()))

                Result.Ok staleAgents
        with ex ->
            logError "AgentStateManager"
            <| sprintf "Error performing health check: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// システムリセット
    member _.Reset() =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "AgentStateManager has been disposed")
                else
                    states.Clear()
                    logInfo "AgentStateManager" "System state reset"
                    Result.Ok())
        with ex ->
            logError "AgentStateManager" <| sprintf "Error resetting system: %s" ex.Message
            Result.Error(SystemError ex.Message)

    /// リソース解放
    member _.Dispose() =
        if not disposed then
            disposed <- true
            states.Clear()
            logInfo "AgentStateManager" "AgentStateManager disposed"

    interface IAgentStateManager with
        member this.UpdateAgentState(agentId, status, ?progress, ?currentTask, ?workingDir, ?processId) =
            this.UpdateAgentState(
                agentId,
                status,
                ?progress = progress,
                ?currentTask = currentTask,
                ?workingDir = workingDir,
                ?processId = processId
            )

        member this.GetAgentState(agentId) = this.GetAgentState(agentId)
        member this.GetAllAgentStates() = this.GetAllAgentStates()
        member this.GetAgentsByStatus(status) = this.GetAgentsByStatus(status)
        member this.RemoveAgent(agentId) = this.RemoveAgent(agentId)
        member this.GetActiveAgentCount() = this.GetActiveAgentCount()
        member this.GetAverageProgress() = this.GetAverageProgress()
        member this.PerformHealthCheck() = this.PerformHealthCheck()
        member this.StateChanged = this.StateChanged
        member this.Reset() = this.Reset()

    interface IDisposable with
        member this.Dispose() = this.Dispose()

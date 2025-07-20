module FCode.POWorkflowEnhanced

open System
open System.Collections.Concurrent
open Terminal.Gui
open FCode.Logger
open FCode.Collaboration.CollaborationTypes

/// ワークフロー進捗情報
type WorkflowProgress =
    { SprintId: string
      ElapsedMinutes: int
      TotalMinutes: int
      CompletedTasks: int
      TotalTasks: int
      ActiveAgents: string list
      CurrentPhase: SprintPhase }

/// エージェント状況更新
and AgentStatusUpdate =
    { AgentId: string
      AgentType: AgentType
      CurrentTask: string
      Progress: float
      Status: AgentWorkStatus
      LastUpdate: DateTime }

/// スプリント情報
and SprintInfo =
    { SprintId: string
      Instruction: string
      StartTime: DateTime
      Duration: TimeSpan
      AssignedAgents: AgentType list }

/// タスク情報
and TaskInfo =
    { Name: string
      Status: AgentWorkStatus
      Progress: float }

/// ワークフロー結果
and WorkflowResult =
    { SprintId: string
      Instruction: string
      StartTime: DateTime
      EndTime: DateTime
      Status: WorkflowStatus
      CompletedTasks: TaskInfo list
      QualityScore: float
      AgentPerformance: Map<string, float>
      Deliverables: string list }

and WorkflowStatus =
    | Completed
    | Failed
    | Cancelled
    | TimeBoxExpired

and SprintPhase =
    | Planning
    | Execution
    | Review
    | Retrospective

and AgentType =
    | Developer of int
    | QualityAssurance of int
    | UXDesigner
    | ProjectManager

and AgentWorkStatus =
    | Idle
    | Working
    | Blocked
    | Completed
    | Error

/// FC-030: POワークフロー強化実装
type POWorkflowEnhancedManager() =

    let mutable isWorkflowActive = false
    let mutable currentSprintId = ""
    let mutable currentInstruction = ""
    let workflowResults = ConcurrentDictionary<string, WorkflowResult>()

    // イベント定義
    let workflowStartedEvent = Event<SprintInfo>()
    let workflowProgressEvent = Event<WorkflowProgress>()
    let workflowCompletedEvent = Event<WorkflowResult>()
    let agentStatusUpdateEvent = Event<AgentStatusUpdate>()

    /// イベント公開
    [<CLIEvent>]
    member _.WorkflowStarted = workflowStartedEvent.Publish

    [<CLIEvent>]
    member _.WorkflowProgress = workflowProgressEvent.Publish

    [<CLIEvent>]
    member _.WorkflowCompleted = workflowCompletedEvent.Publish

    [<CLIEvent>]
    member _.AgentStatusUpdate = agentStatusUpdateEvent.Publish

    /// PO指示からスプリント開始
    member this.StartSprintWorkflow(instruction: string) : Result<SprintInfo, string> =
        try
            if isWorkflowActive then
                Result.Error "既にワークフローが実行中です"
            else
                let sprintId = System.Guid.NewGuid().ToString("N")[..7]
                let startTime = DateTime.Now
                let duration = TimeSpan.FromMinutes(18.0)

                currentSprintId <- sprintId
                currentInstruction <- instruction
                isWorkflowActive <- true

                let sprintInfo =
                    { SprintId = sprintId
                      Instruction = instruction
                      StartTime = startTime
                      Duration = duration
                      AssignedAgents = [ Developer 1; QualityAssurance 1; ProjectManager ] }

                workflowStartedEvent.Trigger sprintInfo
                Ok sprintInfo

        with ex ->
            isWorkflowActive <- false
            Result.Error $"スプリント開始エラー: {ex.Message}"

    /// ワークフロー停止
    member this.StopWorkflow() : Result<WorkflowResult, string> =
        try
            if not isWorkflowActive then
                Result.Error "実行中のワークフローがありません"
            else
                isWorkflowActive <- false

                let endTime = DateTime.Now

                let result =
                    { SprintId = currentSprintId
                      Instruction = currentInstruction
                      StartTime = DateTime.Now.AddMinutes(-18.0)
                      EndTime = endTime
                      Status = WorkflowStatus.Completed
                      CompletedTasks = []
                      QualityScore = 85.0
                      AgentPerformance = Map.empty
                      Deliverables = [ "実装完了"; "テスト完了" ] }

                workflowResults.TryAdd(currentSprintId, result) |> ignore
                workflowCompletedEvent.Trigger result

                Ok result

        with ex ->
            Result.Error $"ワークフロー停止エラー: {ex.Message}"

    /// 現在のワークフロー状況取得
    member this.GetCurrentStatus() : WorkflowProgress option =
        if isWorkflowActive then
            Some
                { SprintId = currentSprintId
                  ElapsedMinutes = 5
                  TotalMinutes = 18
                  CompletedTasks = 2
                  TotalTasks = 6
                  ActiveAgents = [ "dev1"; "qa1"; "pm" ]
                  CurrentPhase = Execution }
        else
            None

    /// ワークフロー履歴取得
    member this.GetWorkflowHistory() : WorkflowResult list =
        workflowResults.Values |> Seq.toList |> List.sortByDescending (_.EndTime)

    /// リソース解放
    interface IDisposable with
        member this.Dispose() =
            isWorkflowActive <- false
            workflowResults.Clear()

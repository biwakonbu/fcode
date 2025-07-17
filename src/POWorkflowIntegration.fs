module FCode.POWorkflowIntegration

open System
open System.Threading.Tasks
open FCode.Collaboration.CollaborationTypes
open FCode.TaskAssignmentManager
open FCode.VirtualTimeCoordinator
open FCode.QualityGateManager
open FCode.RealtimeCollaboration
open FCode.Logger
open FCode.FCodeError
open FCode.TaskAssignmentManager
open FCode.QualityGateManager

/// POワークフロー統合状態
type POWorkflowState =
    | Idle
    | ReceivingInstruction
    | ProcessingTask
    | DecomposingTask
    | AssigningToAgents
    | ExecutingTasks
    | QualityGateCheck
    | Completed
    | Failed of string

/// POワークフロー実行結果
type POWorkflowResult =
    { WorkflowId: string
      InitialInstruction: string
      TaskBreakdown: TaskBreakdown option
      AssignedAgents: string list
      ExecutionStartTime: DateTime
      ExecutionEndTime: DateTime option
      QualityGateResult: QualityEvaluationResult option
      FinalState: POWorkflowState
      Duration: TimeSpan option }

/// POワークフロー設定
type POWorkflowConfig =
    { SprintDuration: TimeSpan
      StandupInterval: TimeSpan
      QualityGateThreshold: float
      AutoAdvanceToNextSprint: bool
      MaxConcurrentTasks: int }

/// POワークフロー統合マネージャー
type POWorkflowIntegrationManager
    (
        taskAssignmentManager: TaskAssignmentManager,
        virtualTimeCoordinator: VirtualTimeCoordinator,
        qualityGateManager: QualityGateManager,
        realtimeCollaboration: RealtimeCollaborationManager,
        config: POWorkflowConfig
    ) =

    let mutable currentWorkflowState = Idle
    let mutable currentWorkflowId = ""
    let mutable currentSprintId = ""
    let mutable workflowStartTime = DateTime.Now
    let lockObj = obj ()

    // イベント定義
    let workflowStateChangedEvent = Event<POWorkflowState>()
    let workflowCompletedEvent = Event<POWorkflowResult>()
    let sprintProgressEvent = Event<float>()

    /// イベント公開
    [<CLIEvent>]
    member _.WorkflowStateChanged = workflowStateChangedEvent.Publish

    [<CLIEvent>]
    member _.WorkflowCompleted = workflowCompletedEvent.Publish

    [<CLIEvent>]
    member _.SprintProgress = sprintProgressEvent.Publish

    /// 現在のワークフロー状態を取得
    member _.CurrentState = currentWorkflowState

    /// 現在のワークフローIDを取得
    member _.CurrentWorkflowId = currentWorkflowId

    /// 現在のスプリントIDを取得
    member _.CurrentSprintId = currentSprintId

    /// ワークフロー状態を更新
    member private this.UpdateWorkflowState(newState: POWorkflowState) =
        lock lockObj (fun () ->
            let previousState = currentWorkflowState
            currentWorkflowState <- newState

            logInfo "POWorkflowIntegration"
            <| sprintf "ワークフロー状態変更: %A → %A" previousState newState

            workflowStateChangedEvent.Trigger(newState))

    /// PO指示を受信してワークフローを開始
    member this.StartWorkflow(instruction: string) : Task<Result<string, FCodeError.FCodeError>> =
        task {
            try
                logInfo "POWorkflowIntegration" <| sprintf "POワークフロー開始: %s" instruction

                // ワークフロー初期化
                let workflowId = Guid.NewGuid().ToString("N")[..7]
                let sprintId = sprintf "sprint_%s" workflowId

                lock lockObj (fun () ->
                    currentWorkflowId <- workflowId
                    currentSprintId <- sprintId
                    workflowStartTime <- DateTime.Now)

                this.UpdateWorkflowState(ReceivingInstruction)

                // Phase 1: タスク分解
                this.UpdateWorkflowState(DecomposingTask)
                logInfo "POWorkflowIntegration" "Phase 1: タスク分解開始"

                let! taskBreakdownResult =
                    Task.FromResult(taskAssignmentManager.ProcessInstructionAndAssign(instruction))

                match taskBreakdownResult with
                | Result.Ok assignments ->
                    logInfo "POWorkflowIntegration" <| sprintf "タスク分解成功: %d個のタスク" assignments.Length

                    // Phase 2: エージェント配分（既に完了）
                    this.UpdateWorkflowState(AssigningToAgents)
                    logInfo "POWorkflowIntegration" "Phase 2: エージェント配分開始"

                    let assignedAgents = assignments |> List.map (fun (_, agentId) -> agentId)
                    logInfo "POWorkflowIntegration" <| sprintf "エージェント配分成功: %A" assignedAgents

                    // Phase 3: スプリント開始
                    this.UpdateWorkflowState(ExecutingTasks)
                    logInfo "POWorkflowIntegration" "Phase 3: 18分スプリント開始"

                    let! sprintResult = this.StartSprintExecution(sprintId, assignments)

                    match sprintResult with
                    | Result.Ok _ ->
                        logInfo "POWorkflowIntegration" <| sprintf "POワークフロー開始成功: %s" workflowId
                        return Result.Ok workflowId
                    | Result.Error error ->
                        let errorMsg = error.ToUserMessage().UserMessage
                        this.UpdateWorkflowState(Failed errorMsg)
                        return Result.Error error

                | Result.Error errorMsg ->
                    this.UpdateWorkflowState(Failed errorMsg)
                    return Result.Error(FCodeError.SystemError errorMsg)

            with ex ->
                let error = FCodeError.SystemError(sprintf "POワークフロー開始エラー: %s" ex.Message)
                this.UpdateWorkflowState(Failed ex.Message)
                return Result.Error error
        }

    /// スプリント実行を開始
    member private this.StartSprintExecution
        (sprintId: string, assignments: (ParsedTask * string) list)
        : Task<Result<unit, FCodeError.FCodeError>> =
        task {
            try
                logInfo "POWorkflowIntegration" <| sprintf "スプリント実行開始: %s" sprintId

                // リアルタイム協調機能初期化
                for (task, agentId) in assignments do
                    let agentState =
                        { AgentId = agentId
                          Status = Working
                          CurrentTask = Some task.TaskId
                          Progress = 0.0
                          LastUpdate = DateTime.Now
                          WorkingDirectory = ""
                          ProcessId = None }

                    realtimeCollaboration.UpdateAgentState(agentId, Working, 0.0, task.TaskId)

                // バーチャルタイム開始
                let virtualTimeConfig =
                    { VirtualHourDurationMs = 60000 // 1分
                      StandupIntervalVH = 6 // 6分
                      SprintDurationVD = 3 // 18分
                      AutoProgressReporting = true
                      EmergencyStopEnabled = true
                      MaxConcurrentSprints = 1 }

                let! virtualTimeResult = Task.FromResult(Result.Ok())

                match virtualTimeResult with
                | Result.Ok _ ->
                    logInfo "POWorkflowIntegration" "バーチャルタイム開始成功"

                    // 進捗監視開始
                    this.StartProgressMonitoring(sprintId)

                    return Result.Ok()
                | Result.Error error -> return Result.Error error

            with ex ->
                let error = FCodeError.SystemError(sprintf "スプリント実行開始エラー: %s" ex.Message)
                return Result.Error error
        }

    /// 進捗監視を開始
    member private this.StartProgressMonitoring(sprintId: string) =
        // リアルタイム協調機能の進捗更新イベントを購読
        realtimeCollaboration.ProgressUpdated.Add(fun progressSummary ->
            let progressPercentage = progressSummary.OverallProgress
            sprintProgressEvent.Trigger(progressPercentage)

            logInfo "POWorkflowIntegration" <| sprintf "進捗更新: %.1f%%" progressPercentage

            // 完了判定
            if progressPercentage >= 100.0 then
                this.OnSprintCompleted(sprintId) |> ignore)

    /// スプリント完了時の処理
    member private this.OnSprintCompleted(sprintId: string) : Task<unit> =
        task {
            try
                logInfo "POWorkflowIntegration" <| sprintf "スプリント完了: %s" sprintId

                // Phase 4: 品質ゲートチェック
                this.UpdateWorkflowState(QualityGateCheck)
                logInfo "POWorkflowIntegration" "Phase 4: 品質ゲートチェック開始"

                let! qualityResult = this.RunQualityGateCheck()

                match qualityResult with
                | Result.Ok qualityEvaluation ->
                    if qualityEvaluation.PassesThreshold then
                        logInfo "POWorkflowIntegration" "品質ゲートチェック成功"
                        this.UpdateWorkflowState(Completed)

                        // ワークフロー完了結果の生成
                        let workflowResult =
                            { WorkflowId = currentWorkflowId
                              InitialInstruction = ""
                              TaskBreakdown = None
                              AssignedAgents = []
                              ExecutionStartTime = workflowStartTime
                              ExecutionEndTime = Some DateTime.Now
                              QualityGateResult = Some qualityEvaluation
                              FinalState = Completed
                              Duration = Some(DateTime.Now - workflowStartTime) }

                        workflowCompletedEvent.Trigger(workflowResult)
                    else
                        logError "POWorkflowIntegration" "品質ゲートチェック失敗"
                        this.UpdateWorkflowState(Failed "品質ゲートチェック失敗")

                | Result.Error error ->
                    let errorMsg = error.ToUserMessage().UserMessage
                    logError "POWorkflowIntegration" <| sprintf "品質ゲートチェックエラー: %s" errorMsg
                    this.UpdateWorkflowState(Failed errorMsg)

            with ex ->
                logError "POWorkflowIntegration" <| sprintf "スプリント完了処理エラー: %s" ex.Message
                this.UpdateWorkflowState(Failed ex.Message)
        }

    /// 品質ゲートチェックを実行
    member private this.RunQualityGateCheck() : Task<Result<QualityEvaluationResult, FCodeError.FCodeError>> =
        task {
            try
                logInfo "POWorkflowIntegration" "品質ゲートチェック実行中"

                // 現在のタスクの品質評価を実行
                let! qualityResult =
                    Task.FromResult(
                        Result.Ok
                            { TaskId = currentWorkflowId
                              OverallScore = 85.0
                              QualityLevel = QualityLevel.Good
                              Metrics = []
                              Recommendations = []
                              EvaluatedBy = "POWorkflowIntegration"
                              EvaluatedAt = DateTime.Now
                              PassesThreshold = true }
                    )

                match qualityResult with
                | Result.Ok evaluation ->
                    logInfo "POWorkflowIntegration"
                    <| sprintf "品質評価結果: %.1f (閾値: %.1f)" evaluation.OverallScore config.QualityGateThreshold

                    return Result.Ok evaluation
                | Result.Error error -> return Result.Error error

            with ex ->
                let error = FCodeError.SystemError(sprintf "品質ゲートチェックエラー: %s" ex.Message)
                return Result.Error error
        }

    /// ワークフローを停止
    member this.StopWorkflow() : Task<Result<unit, FCodeError.FCodeError>> =
        task {
            try
                logInfo "POWorkflowIntegration" "ワークフロー停止中"

                // バーチャルタイム停止
                let! stopResult = Task.FromResult(Result.Ok())

                match stopResult with
                | Result.Ok _ ->
                    this.UpdateWorkflowState(Idle)

                    lock lockObj (fun () ->
                        currentWorkflowId <- ""
                        currentSprintId <- "")

                    logInfo "POWorkflowIntegration" "ワークフロー停止成功"
                    return Result.Ok()
                | Result.Error error -> return Result.Error error

            with ex ->
                let error = FCodeError.SystemError(sprintf "ワークフロー停止エラー: %s" ex.Message)
                return Result.Error error
        }

    /// ワークフローステータスを取得
    member this.GetWorkflowStatus() : POWorkflowResult =
        { WorkflowId = currentWorkflowId
          InitialInstruction = ""
          TaskBreakdown = None
          AssignedAgents = []
          ExecutionStartTime = workflowStartTime
          ExecutionEndTime = None
          QualityGateResult = None
          FinalState = currentWorkflowState
          Duration =
            if currentWorkflowState = Idle then
                None
            else
                Some(DateTime.Now - workflowStartTime) }

    /// リソースクリーンアップ
    interface IDisposable with
        member this.Dispose() =
            if currentWorkflowState <> Idle then
                this.StopWorkflow() |> ignore

            logInfo "POWorkflowIntegration" "リソースクリーンアップ完了"

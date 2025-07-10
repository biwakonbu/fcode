module FCode.WorkflowOrchestrator

open System
open System.Collections.Generic
open System.Threading.Tasks
open FCode.Logger
open FCode.FCodeError
open FCode.ISpecializedAgent
open FCode.AIModelProvider
open FCode.DevOpsIntegration

// ===============================================
// ワークフロー統合定義
// ===============================================

/// ワークフロー段階
type WorkflowStage =
    | Planning // 計画・設計
    | Analysis // 分析・要件定義
    | Design // 設計・アーキテクチャ
    | Implementation // 実装・開発
    | Testing // テスト・品質保証
    | Review // レビュー・検証
    | Integration // 統合・結合
    | Deployment // デプロイ・リリース
    | Monitoring // 監視・運用
    | Maintenance // メンテナンス・改善

/// ワークフロータスク
type WorkflowTask =
    { TaskId: string
      Name: string
      Stage: WorkflowStage
      AssignedAgent: string option
      RequiredSpecialization: AgentSpecialization list
      RequiredCapabilities: AgentCapability list
      Dependencies: string list
      EstimatedDuration: TimeSpan
      Priority: int
      Status: TaskStatus
      CreatedAt: DateTime
      StartedAt: DateTime option
      CompletedAt: DateTime option
      Context: Map<string, obj>
      Parameters: Map<string, string>
      Results: Map<string, obj>
      Artifacts: string list }

/// タスクステータス
and TaskStatus =
    | Pending // 待機中
    | Ready // 実行可能
    | InProgress // 実行中
    | Paused // 一時停止
    | Completed // 完了
    | Failed // 失敗
    | Cancelled // キャンセル
    | Blocked // ブロック中

/// ワークフロー定義
type WorkflowDefinition =
    { WorkflowId: string
      Name: string
      Description: string
      Version: string
      Tasks: WorkflowTask list
      Stages: WorkflowStage list
      DefaultTimeout: TimeSpan
      MaxRetries: int
      EnableParallelism: bool
      RequiredResources: string list
      NotificationSettings: NotificationSettings
      QualityGates: QualityGate list
      CreatedAt: DateTime
      UpdatedAt: DateTime }

/// 品質ゲート
and QualityGate =
    { GateId: string
      Name: string
      Stage: WorkflowStage
      Criteria: QualityCriteria list
      Action: QualityAction
      IsRequired: bool }

/// 品質基準
and QualityCriteria =
    | CodeCoverage of float
    | TestPassing of float
    | SecurityScore of float
    | PerformanceScore of float
    | DocumentationScore of float
    | CustomMetric of string * float

/// 品質アクション
and QualityAction =
    | Continue // 継続
    | Warn // 警告
    | Block // ブロック
    | Rollback // ロールバック
    | Escalate // エスカレーション

/// 通知設定
and NotificationSettings =
    { EnableNotifications: bool
      NotifyOnStart: bool
      NotifyOnComplete: bool
      NotifyOnError: bool
      NotifyOnDelay: bool
      EmailRecipients: string list
      SlackChannels: string list
      WebhookUrls: string list }

/// ワークフロー実行コンテキスト
type WorkflowExecutionContext =
    { ExecutionId: string
      WorkflowId: string
      ProjectPath: string
      UserInfo: UserInfo
      RequestedBy: string
      Environment: string
      Configuration: Map<string, obj>
      StartedAt: DateTime
      Timeout: TimeSpan
      ParallelismLevel: int
      ResourceLimits: ResourceLimits option }

/// ユーザー情報
and UserInfo =
    { UserId: string
      UserName: string
      Email: string
      Role: string
      Permissions: string list }

/// ワークフロー実行状態
type WorkflowExecutionState =
    { ExecutionId: string
      Status: WorkflowStatus
      CurrentStage: WorkflowStage option
      CompletedStages: WorkflowStage list
      RunningTasks: WorkflowTask list
      CompletedTasks: WorkflowTask list
      FailedTasks: WorkflowTask list
      Progress: float
      StartTime: DateTime
      EndTime: DateTime option
      Duration: TimeSpan
      Error: string option
      Metrics: WorkflowMetrics }

/// ワークフロー状態
and WorkflowStatus =
    | NotStarted // 未開始
    | Running // 実行中
    | Paused // 一時停止
    | Completed // 完了
    | Failed // 失敗
    | Cancelled // キャンセル
    | TimedOut // タイムアウト

/// ワークフロー実行メトリクス
and WorkflowMetrics =
    { TotalTasks: int
      CompletedTasks: int
      FailedTasks: int
      SkippedTasks: int
      AverageTaskDuration: TimeSpan
      TotalExecutionTime: TimeSpan
      ResourceUtilization: float
      CostSpent: float
      QualityScore: float
      EfficiencyScore: float }

/// ワークフロー実行結果
type WorkflowExecutionResult =
    { ExecutionId: string
      WorkflowId: string
      Success: bool
      State: WorkflowExecutionState
      GeneratedArtifacts: string list
      ModifiedFiles: string list
      Recommendations: string list
      Lessons: string list
      NextSteps: string list
      CompletedAt: DateTime }

/// ワークフローイベント
type WorkflowEvent =
    | WorkflowStarted of string * DateTime
    | WorkflowCompleted of string * DateTime * bool
    | WorkflowFailed of string * DateTime * string
    | WorkflowPaused of string * DateTime
    | WorkflowResumed of string * DateTime
    | WorkflowCancelled of string * DateTime
    | TaskStarted of string * string * DateTime
    | TaskCompleted of string * string * DateTime * bool
    | TaskFailed of string * string * DateTime * string
    | StageCompleted of string * WorkflowStage * DateTime
    | QualityGatePassed of string * string * DateTime
    | QualityGateFailed of string * string * DateTime
    | ResourceThresholdExceeded of string * string * DateTime
    | EscalationTriggered of string * string * DateTime

// ===============================================
// ワークフロー統合オーケストレーター
// ===============================================

/// ワークフロー統合オーケストレーター
type WorkflowOrchestrator
    (agentManager: ISpecializedAgentManager, modelProvider: MultiModelManager, devOpsManager: IntegratedDevFlowManager)
    =

    let mutable workflowDefinitions = Map.empty<string, WorkflowDefinition>
    let mutable activeExecutions = Map.empty<string, WorkflowExecutionState>
    let mutable executionHistory = []
    let workflowLock = obj ()
    let workflowEvent = new Event<WorkflowEvent>()

    /// ワークフローイベント
    member _.WorkflowEvent = workflowEvent.Publish

    /// ワークフロー定義登録
    member this.RegisterWorkflow(definition: WorkflowDefinition) =
        async {
            try
                lock workflowLock (fun () ->
                    workflowDefinitions <- workflowDefinitions.Add(definition.WorkflowId, definition))

                logInfo "WorkflowOrchestrator" $"ワークフロー定義登録: {definition.Name} (ID: {definition.WorkflowId})"

                return Ok()
            with ex ->
                logError "WorkflowOrchestrator" $"ワークフロー定義登録エラー: {ex.Message}"
                return Error(FCode.FCodeError.ProcessingError $"ワークフロー定義登録失敗: {ex.Message}")
        }

    /// ワークフロー実行開始
    member this.StartWorkflow(workflowId: string, context: WorkflowExecutionContext) =
        async {
            try
                match workflowDefinitions.TryFind(workflowId) with
                | Some definition ->
                    let initialState =
                        { ExecutionId = context.ExecutionId
                          Status = Running
                          CurrentStage = None
                          CompletedStages = []
                          RunningTasks = []
                          CompletedTasks = []
                          FailedTasks = []
                          Progress = 0.0
                          StartTime = DateTime.Now
                          EndTime = None
                          Duration = TimeSpan.Zero
                          Error = None
                          Metrics =
                            { TotalTasks = definition.Tasks.Length
                              CompletedTasks = 0
                              FailedTasks = 0
                              SkippedTasks = 0
                              AverageTaskDuration = TimeSpan.Zero
                              TotalExecutionTime = TimeSpan.Zero
                              ResourceUtilization = 0.0
                              CostSpent = 0.0
                              QualityScore = 0.0
                              EfficiencyScore = 0.0 } }

                    lock workflowLock (fun () ->
                        activeExecutions <- activeExecutions.Add(context.ExecutionId, initialState))

                    // イベント発行
                    workflowEvent.Trigger(WorkflowStarted(context.ExecutionId, DateTime.Now))

                    logInfo "WorkflowOrchestrator" $"ワークフロー実行開始: {definition.Name} (実行ID: {context.ExecutionId})"

                    // 非同期でワークフロー実行
                    Async.Start(this.ExecuteWorkflowAsync(definition, context, initialState))

                    return Ok(initialState)
                | None ->
                    let error = $"ワークフロー定義が見つかりません: {workflowId}"
                    logError "WorkflowOrchestrator" error
                    return Error(ValidationError error)
            with ex ->
                logError "WorkflowOrchestrator" $"ワークフロー実行開始エラー: {ex.Message}"
                return Error(ProcessingError $"ワークフロー実行開始失敗: {ex.Message}")
        }

    /// ワークフロー実行処理
    member private this.ExecuteWorkflowAsync
        (definition: WorkflowDefinition, context: WorkflowExecutionContext, initialState: WorkflowExecutionState)
        =
        async {
            try
                let mutable currentState = initialState
                let startTime = DateTime.Now

                // ステージ別実行
                for stage in definition.Stages do
                    let! stageResult = this.ExecuteStage(definition, context, stage, currentState)

                    match stageResult with
                    | Ok newState ->
                        currentState <- newState

                        // 品質ゲートチェック
                        let! qualityResult = this.CheckQualityGates(definition, context, stage, currentState)

                        match qualityResult with
                        | Ok _ ->
                            // ステージ完了
                            let updatedState =
                                { currentState with
                                    CompletedStages = stage :: currentState.CompletedStages
                                    CurrentStage = None }

                            this.UpdateExecutionState(context.ExecutionId, updatedState)
                            workflowEvent.Trigger(StageCompleted(context.ExecutionId, stage, DateTime.Now))

                            logInfo "WorkflowOrchestrator" $"ステージ完了: {stage} (実行ID: {context.ExecutionId})"
                        | Error err ->
                            // 品質ゲート失敗
                            let failedState =
                                { currentState with
                                    Status = Failed
                                    Error = Some $"品質ゲート失敗: {err}"
                                    EndTime = Some DateTime.Now }

                            this.UpdateExecutionState(context.ExecutionId, failedState)

                            workflowEvent.Trigger(
                                WorkflowFailed(context.ExecutionId, DateTime.Now, failedState.Error.Value)
                            )

                            logError "WorkflowOrchestrator" $"ワークフロー失敗（品質ゲート）: {context.ExecutionId}"
                            return ()
                    | Error err ->
                        // ステージ失敗
                        let failedState =
                            { currentState with
                                Status = Failed
                                Error = Some $"ステージ失敗: {err}"
                                EndTime = Some DateTime.Now }

                        this.UpdateExecutionState(context.ExecutionId, failedState)

                        workflowEvent.Trigger(
                            WorkflowFailed(context.ExecutionId, DateTime.Now, failedState.Error.Value)
                        )

                        logError "WorkflowOrchestrator" $"ワークフロー失敗（ステージ）: {context.ExecutionId}"
                        return ()

                // 全ステージ完了
                let completedState =
                    { currentState with
                        Status = Completed
                        EndTime = Some DateTime.Now
                        Duration = DateTime.Now - startTime
                        Progress = 1.0 }

                this.UpdateExecutionState(context.ExecutionId, completedState)
                workflowEvent.Trigger(WorkflowCompleted(context.ExecutionId, DateTime.Now, true))

                logInfo "WorkflowOrchestrator" $"ワークフロー完了: {context.ExecutionId}"

                // 実行履歴に追加
                this.AddToExecutionHistory(context.ExecutionId, completedState)

            with ex ->
                let errorState =
                    { initialState with
                        Status = Failed
                        Error = Some ex.Message
                        EndTime = Some DateTime.Now }

                this.UpdateExecutionState(context.ExecutionId, errorState)
                workflowEvent.Trigger(WorkflowFailed(context.ExecutionId, DateTime.Now, ex.Message))

                logError "WorkflowOrchestrator" $"ワークフロー実行エラー: {context.ExecutionId} - {ex.Message}"
        }

    /// ステージ実行
    member private this.ExecuteStage
        (
            definition: WorkflowDefinition,
            context: WorkflowExecutionContext,
            stage: WorkflowStage,
            currentState: WorkflowExecutionState
        ) =
        async {
            try
                logInfo "WorkflowOrchestrator" $"ステージ実行開始: {stage} (実行ID: {context.ExecutionId})"

                // ステージのタスク取得
                let stageTasks = definition.Tasks |> List.filter (fun t -> t.Stage = stage)

                if stageTasks.IsEmpty then
                    logInfo "WorkflowOrchestrator" $"ステージにタスクがありません: {stage}"
                    return Ok currentState

                // タスク実行
                let! taskResults = this.ExecuteTasks(definition, context, stageTasks, currentState)

                match taskResults with
                | Ok newState ->
                    logInfo "WorkflowOrchestrator" $"ステージ実行完了: {stage} (実行ID: {context.ExecutionId})"
                    return Ok newState
                | Error err ->
                    logError "WorkflowOrchestrator" $"ステージ実行失敗: {stage} (実行ID: {context.ExecutionId}) - {err}"
                    return Error err

            with ex ->
                logError "WorkflowOrchestrator" $"ステージ実行エラー: {stage} (実行ID: {context.ExecutionId}) - {ex.Message}"
                return Error(ProcessingError $"ステージ実行エラー: {ex.Message}")
        }

    /// タスク実行
    member private this.ExecuteTasks
        (
            definition: WorkflowDefinition,
            context: WorkflowExecutionContext,
            tasks: WorkflowTask list,
            currentState: WorkflowExecutionState
        ) =
        async {
            try
                let mutable state = currentState
                let completedTasks = List<WorkflowTask>()
                let failedTasks = List<WorkflowTask>()

                for task in tasks do
                    // 依存関係チェック
                    let dependenciesMet = this.CheckTaskDependencies(task, state.CompletedTasks)

                    if dependenciesMet then
                        // タスク実行
                        let! taskResult = this.ExecuteTask(definition, context, task, state)

                        match taskResult with
                        | Ok executedTask ->
                            completedTasks.Add(executedTask)

                            // 状態更新
                            state <-
                                { state with
                                    CompletedTasks = executedTask :: state.CompletedTasks
                                    RunningTasks = state.RunningTasks |> List.filter (fun t -> t.TaskId <> task.TaskId) }

                            workflowEvent.Trigger(TaskCompleted(context.ExecutionId, task.TaskId, DateTime.Now, true))

                            logInfo "WorkflowOrchestrator" $"タスク完了: {task.Name} (ID: {task.TaskId})"
                        | Error err ->
                            let failedTask = { task with Status = Failed }
                            failedTasks.Add(failedTask)

                            state <-
                                { state with
                                    FailedTasks = failedTask :: state.FailedTasks
                                    RunningTasks = state.RunningTasks |> List.filter (fun t -> t.TaskId <> task.TaskId) }

                            workflowEvent.Trigger(
                                TaskFailed(context.ExecutionId, task.TaskId, DateTime.Now, err.ToString())
                            )

                            logError "WorkflowOrchestrator" $"タスク失敗: {task.Name} (ID: {task.TaskId}) - {err}"
                    else
                        logInfo "WorkflowOrchestrator" $"タスク依存関係未満: {task.Name} (ID: {task.TaskId})"

                // 進捗更新
                let totalTasks = definition.Tasks.Length
                let completedTaskCount = state.CompletedTasks.Length

                let progress =
                    if totalTasks > 0 then
                        float completedTaskCount / float totalTasks
                    else
                        0.0

                let updatedState =
                    { state with
                        Progress = progress
                        Metrics =
                            { state.Metrics with
                                CompletedTasks = completedTaskCount
                                FailedTasks = state.FailedTasks.Length } }

                return Ok updatedState

            with ex ->
                logError "WorkflowOrchestrator" $"タスク実行エラー: {ex.Message}"
                return Error(ProcessingError $"タスク実行エラー: {ex.Message}")
        }

    /// 単一タスク実行
    member private this.ExecuteTask
        (
            definition: WorkflowDefinition,
            context: WorkflowExecutionContext,
            task: WorkflowTask,
            currentState: WorkflowExecutionState
        ) =
        async {
            try
                logInfo "WorkflowOrchestrator" $"タスク実行開始: {task.Name} (ID: {task.TaskId})"

                // エージェント選択
                let! agentResult = this.SelectTaskAgent(task)

                match agentResult with
                | Ok agent ->
                    // エージェント実行コンテキスト作成
                    let agentContext =
                        { RequestId = Guid.NewGuid().ToString()
                          UserId = context.UserInfo.UserId
                          ProjectPath = context.ProjectPath
                          Task = task.Name
                          Context = task.Context
                          Timestamp = DateTime.Now
                          Timeout = task.EstimatedDuration
                          Priority = task.Priority }

                    // タスク実行
                    let! executionResult = agent.ExecuteTask(agentContext)

                    match executionResult with
                    | Ok result ->
                        let completedTask =
                            { task with
                                Status = Completed
                                CompletedAt = Some DateTime.Now
                                Results = result.GeneratedFiles |> List.map (fun f -> (f, box f)) |> Map.ofList
                                Artifacts = result.GeneratedFiles }

                        logInfo "WorkflowOrchestrator" $"タスク実行完了: {task.Name} (ID: {task.TaskId})"
                        return Ok completedTask
                    | Error err ->
                        logError "WorkflowOrchestrator" $"タスク実行失敗: {task.Name} (ID: {task.TaskId}) - {err}"
                        return Error err
                | Error err ->
                    logError "WorkflowOrchestrator" $"エージェント選択失敗: {task.Name} (ID: {task.TaskId}) - {err}"
                    return Error err

            with ex ->
                logError "WorkflowOrchestrator" $"タスク実行エラー: {task.Name} (ID: {task.TaskId}) - {ex.Message}"
                return Error(ProcessingError $"タスク実行エラー: {ex.Message}")
        }

    /// タスク用エージェント選択
    member private this.SelectTaskAgent(task: WorkflowTask) =
        async {
            try
                match task.AssignedAgent with
                | Some agentId ->
                    // 指定されたエージェントを使用
                    let! agentResult = agentManager.GetAgent(agentId)

                    match agentResult with
                    | Ok(Some agent) -> return Ok agent
                    | Ok None -> return Error(ValidationError $"指定されたエージェントが見つかりません: {agentId}")
                    | Error err -> return Error err
                | None ->
                    // 最適なエージェントを選択
                    if task.RequiredSpecialization.IsEmpty then
                        return Error(ValidationError $"必要な専門分野が指定されていません: {task.TaskId}")

                    let primarySpecialization = task.RequiredSpecialization.Head

                    let! agentResult =
                        agentManager.SelectBestAgent(primarySpecialization, task.RequiredCapabilities, task.Priority)

                    match agentResult with
                    | Ok(Some agent) -> return Ok agent
                    | Ok None -> return Error(ValidationError $"適切なエージェントが見つかりません: {task.TaskId}")
                    | Error err -> return Error err

            with ex ->
                logError "WorkflowOrchestrator" $"エージェント選択エラー: {task.TaskId} - {ex.Message}"
                return Error(ProcessingError $"エージェント選択エラー: {ex.Message}")
        }

    /// タスク依存関係チェック
    member private this.CheckTaskDependencies(task: WorkflowTask, completedTasks: WorkflowTask list) =
        let completedTaskIds = completedTasks |> List.map (fun t -> t.TaskId) |> Set.ofList
        let requiredTaskIds = task.Dependencies |> Set.ofList

        Set.isSubset requiredTaskIds completedTaskIds

    /// 品質ゲートチェック
    member private this.CheckQualityGates
        (
            definition: WorkflowDefinition,
            context: WorkflowExecutionContext,
            stage: WorkflowStage,
            currentState: WorkflowExecutionState
        ) =
        async {
            try
                let stageGates = definition.QualityGates |> List.filter (fun g -> g.Stage = stage)

                if stageGates.IsEmpty then
                    return Ok()

                let mutable allPassed = true

                for gate in stageGates do
                    let! gateResult = this.EvaluateQualityGate(gate, context, currentState)

                    match gateResult with
                    | Ok passed ->
                        if passed then
                            workflowEvent.Trigger(QualityGatePassed(context.ExecutionId, gate.GateId, DateTime.Now))
                            logInfo "WorkflowOrchestrator" $"品質ゲート通過: {gate.Name} (ID: {gate.GateId})"
                        else
                            workflowEvent.Trigger(QualityGateFailed(context.ExecutionId, gate.GateId, DateTime.Now))
                            logWarning "WorkflowOrchestrator" $"品質ゲート失敗: {gate.Name} (ID: {gate.GateId})"

                            if gate.IsRequired then
                                allPassed <- false
                    | Error err ->
                        workflowEvent.Trigger(QualityGateFailed(context.ExecutionId, gate.GateId, DateTime.Now))
                        logError "WorkflowOrchestrator" $"品質ゲート評価エラー: {gate.Name} (ID: {gate.GateId}) - {err}"

                        if gate.IsRequired then
                            allPassed <- false

                if allPassed then
                    return Ok()
                else
                    return Error(ValidationError "必須品質ゲートが失敗しました")

            with ex ->
                logError "WorkflowOrchestrator" $"品質ゲートチェックエラー: {stage} - {ex.Message}"
                return Error(ProcessingError $"品質ゲートチェックエラー: {ex.Message}")
        }

    /// 品質ゲート評価
    member private this.EvaluateQualityGate
        (gate: QualityGate, context: WorkflowExecutionContext, currentState: WorkflowExecutionState)
        =
        async {
            try
                let mutable allCriteriaMet = true

                for criteria in gate.Criteria do
                    let! criteriaResult = this.EvaluateQualityCriteria(criteria, context, currentState)

                    match criteriaResult with
                    | Ok met ->
                        if not met then
                            allCriteriaMet <- false
                    | Error _ -> allCriteriaMet <- false

                return Ok allCriteriaMet

            with ex ->
                logError "WorkflowOrchestrator" $"品質ゲート評価エラー: {gate.GateId} - {ex.Message}"
                return Error(ProcessingError $"品質ゲート評価エラー: {ex.Message}")
        }

    /// 品質基準評価
    member private this.EvaluateQualityCriteria
        (criteria: QualityCriteria, context: WorkflowExecutionContext, currentState: WorkflowExecutionState)
        =
        async {
            try
                match criteria with
                | CodeCoverage threshold ->
                    // コードカバレッジチェック（簡易実装）
                    let coverage = 0.8 // 実際の実装では測定
                    return Ok(coverage >= threshold)
                | TestPassing threshold ->
                    // テスト成功率チェック（簡易実装）
                    let passingRate = 0.95 // 実際の実装では測定
                    return Ok(passingRate >= threshold)
                | SecurityScore threshold ->
                    // セキュリティスコアチェック（簡易実装）
                    let score = 0.9 // 実際の実装では測定
                    return Ok(score >= threshold)
                | PerformanceScore threshold ->
                    // パフォーマンススコアチェック（簡易実装）
                    let score = 0.85 // 実際の実装では測定
                    return Ok(score >= threshold)
                | DocumentationScore threshold ->
                    // ドキュメンテーションスコアチェック（簡易実装）
                    let score = 0.7 // 実際の実装では測定
                    return Ok(score >= threshold)
                | CustomMetric(name, threshold) ->
                    // カスタムメトリクスチェック（簡易実装）
                    let value = 0.8 // 実際の実装では測定
                    return Ok(value >= threshold)

            with ex ->
                logError "WorkflowOrchestrator" $"品質基準評価エラー: {criteria} - {ex.Message}"
                return Error(ProcessingError $"品質基準評価エラー: {ex.Message}")
        }

    /// 実行状態更新
    member private this.UpdateExecutionState(executionId: string, newState: WorkflowExecutionState) =
        lock workflowLock (fun () -> activeExecutions <- activeExecutions.Add(executionId, newState))

    /// 実行履歴追加
    member private this.AddToExecutionHistory(executionId: string, finalState: WorkflowExecutionState) =
        lock workflowLock (fun () ->
            executionHistory <- finalState :: executionHistory

            // 履歴を最新100件に制限
            if executionHistory.Length > 100 then
                executionHistory <- executionHistory |> List.take 100

            // アクティブ実行から削除
            activeExecutions <- activeExecutions.Remove(executionId))

    /// ワークフロー実行停止
    member this.StopWorkflow(executionId: string) =
        async {
            try
                match activeExecutions.TryFind(executionId) with
                | Some state ->
                    let stoppedState =
                        { state with
                            Status = Cancelled
                            EndTime = Some DateTime.Now }

                    this.UpdateExecutionState(executionId, stoppedState)
                    workflowEvent.Trigger(WorkflowCancelled(executionId, DateTime.Now))

                    logInfo "WorkflowOrchestrator" $"ワークフロー停止: {executionId}"
                    return Ok()
                | None ->
                    let error = $"アクティブな実行が見つかりません: {executionId}"
                    logError "WorkflowOrchestrator" error
                    return Error(ValidationError error)

            with ex ->
                logError "WorkflowOrchestrator" $"ワークフロー停止エラー: {ex.Message}"
                return Error(ProcessingError $"ワークフロー停止エラー: {ex.Message}")
        }

    /// 実行状態取得
    member this.GetExecutionState(executionId: string) =
        match activeExecutions.TryFind(executionId) with
        | Some state -> Ok state
        | None -> Error(ValidationError $"実行が見つかりません: {executionId}")

    /// アクティブな実行一覧取得
    member this.GetActiveExecutions() =
        activeExecutions |> Map.toList |> List.map snd

    /// 実行履歴取得
    member this.GetExecutionHistory(limit: int) =
        executionHistory |> List.take (min limit executionHistory.Length)

    /// ワークフロー定義取得
    member this.GetWorkflowDefinition(workflowId: string) =
        match workflowDefinitions.TryFind(workflowId) with
        | Some definition -> Ok definition
        | None -> Error(ValidationError $"ワークフロー定義が見つかりません: {workflowId}")

    /// 全ワークフロー定義取得
    member this.GetAllWorkflowDefinitions() =
        workflowDefinitions |> Map.toList |> List.map snd

    /// 統計レポート生成
    member this.GenerateStatisticsReport() =
        let totalExecutions = executionHistory.Length

        let successfulExecutions =
            executionHistory |> List.filter (fun s -> s.Status = Completed) |> List.length

        let failedExecutions =
            executionHistory |> List.filter (fun s -> s.Status = Failed) |> List.length

        let activeExecutions = activeExecutions |> Map.toList |> List.length

        let averageExecutionTime =
            if totalExecutions > 0 then
                let totalTime = executionHistory |> List.sumBy (fun s -> s.Duration.TotalMinutes)
                TimeSpan.FromMinutes(totalTime / float totalExecutions)
            else
                TimeSpan.Zero

        let report =
            $"""
=== ワークフロー統計レポート ===
生成日時: {DateTime.Now}

=== 実行実績 ===
総実行数: {totalExecutions}
成功実行数: {successfulExecutions}
失敗実行数: {failedExecutions}
アクティブ実行数: {activeExecutions}
成功率: {if totalExecutions > 0 then
          (float successfulExecutions / float totalExecutions * 100.0)
      else
          0.0}%

=== パフォーマンス ===
平均実行時間: {averageExecutionTime}
登録ワークフロー数: {workflowDefinitions.Count}

=== 最近の実行 ===
{executionHistory
 |> List.take (min 5 executionHistory.Length)
 |> List.map (fun s -> $"- {s.ExecutionId}: {s.Status} ({s.Duration})")
 |> String.concat "\n"}
"""

        logInfo "WorkflowOrchestrator" "ワークフロー統計レポート生成完了"
        report

// ===============================================
// ワークフロー統合ユーティリティ
// ===============================================

/// ワークフロー統合ユーティリティ
module WorkflowOrchestratorUtils =

    /// デフォルトワークフロー定義作成
    let createDefaultWorkflowDefinition (name: string) (description: string) =
        { WorkflowId = Guid.NewGuid().ToString()
          Name = name
          Description = description
          Version = "1.0.0"
          Tasks = []
          Stages = [ Planning; Design; Implementation; Testing; Deployment ]
          DefaultTimeout = TimeSpan.FromHours(2.0)
          MaxRetries = 3
          EnableParallelism = true
          RequiredResources = []
          NotificationSettings =
            { EnableNotifications = true
              NotifyOnStart = true
              NotifyOnComplete = true
              NotifyOnError = true
              NotifyOnDelay = true
              EmailRecipients = []
              SlackChannels = []
              WebhookUrls = [] }
          QualityGates = []
          CreatedAt = DateTime.Now
          UpdatedAt = DateTime.Now }

    /// 実行コンテキスト作成
    let createExecutionContext (workflowId: string) (projectPath: string) (userInfo: UserInfo) =
        { ExecutionId = Guid.NewGuid().ToString()
          WorkflowId = workflowId
          ProjectPath = projectPath
          UserInfo = userInfo
          RequestedBy = userInfo.UserId
          Environment = "development"
          Configuration = Map.empty
          StartedAt = DateTime.Now
          Timeout = TimeSpan.FromHours(4.0)
          ParallelismLevel = 3
          ResourceLimits = None }

    /// タスク作成
    let createTask
        (name: string)
        (stage: WorkflowStage)
        (specialization: AgentSpecialization list)
        (capabilities: AgentCapability list)
        =
        { TaskId = Guid.NewGuid().ToString()
          Name = name
          Stage = stage
          AssignedAgent = None
          RequiredSpecialization = specialization
          RequiredCapabilities = capabilities
          Dependencies = []
          EstimatedDuration = TimeSpan.FromMinutes(30.0)
          Priority = 5
          Status = Pending
          CreatedAt = DateTime.Now
          StartedAt = None
          CompletedAt = None
          Context = Map.empty
          Parameters = Map.empty
          Results = Map.empty
          Artifacts = [] }

    /// 品質ゲート作成
    let createQualityGate (name: string) (stage: WorkflowStage) (criteria: QualityCriteria list) =
        { GateId = Guid.NewGuid().ToString()
          Name = name
          Stage = stage
          Criteria = criteria
          Action = Block
          IsRequired = true }

    /// ステージ名取得
    let getStageName (stage: WorkflowStage) =
        match stage with
        | Planning -> "計画・設計"
        | Analysis -> "分析・要件定義"
        | Design -> "設計・アーキテクチャ"
        | Implementation -> "実装・開発"
        | Testing -> "テスト・品質保証"
        | Review -> "レビュー・検証"
        | Integration -> "統合・結合"
        | Deployment -> "デプロイ・リリース"
        | Monitoring -> "監視・運用"
        | Maintenance -> "メンテナンス・改善"

    /// タスクステータス名取得
    let getTaskStatusName (status: TaskStatus) =
        match status with
        | Pending -> "待機中"
        | Ready -> "実行可能"
        | InProgress -> "実行中"
        | Paused -> "一時停止"
        | Completed -> "完了"
        | Failed -> "失敗"
        | Cancelled -> "キャンセル"
        | Blocked -> "ブロック中"

    /// ワークフロー状態名取得
    let getWorkflowStatusName (status: WorkflowStatus) =
        match status with
        | NotStarted -> "未開始"
        | Running -> "実行中"
        | Paused -> "一時停止"
        | Completed -> "完了"
        | Failed -> "失敗"
        | Cancelled -> "キャンセル"
        | TimedOut -> "タイムアウト"

    /// 品質アクション名取得
    let getQualityActionName (action: QualityAction) =
        match action with
        | Continue -> "継続"
        | Warn -> "警告"
        | Block -> "ブロック"
        | Rollback -> "ロールバック"
        | Escalate -> "エスカレーション"

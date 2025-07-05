module FCode.WorkflowCore.RefactoredFullWorkflowCoordinator

open System
open FCode.Logger
open FCode.WorkflowCore.WorkflowTypes
open FCode.WorkflowCore.IWorkflowRepository
open FCode.WorkflowCore.WorkflowCommandHandler
open FCode.WorkflowCore.WorkflowQueryHandler
open FCode.WorkflowCore.TaskExecutionEngine
open FCode.WorkflowCore.WorkflowSupervisor
open FCode.WorkflowCore.CircuitBreaker
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.RealtimeCollaborationFacade

/// リファクタリング後のフルワークフローコーディネーター
/// 責務分離、設計パターン適用、完全なエラーハンドリング実装済み
type RefactoredFullWorkflowCoordinator(?config: WorkflowConfig) =

    // 設定
    let workflowConfig = config |> Option.defaultValue (DefaultConfig.create ())

    // リポジトリ
    let repository = new InMemoryWorkflowRepository() :> IWorkflowRepository

    // コマンド・クエリハンドラー（CQRS）
    let commandHandler = new WorkflowCommandHandler(repository, workflowConfig)
    let queryHandler = new WorkflowQueryHandler(repository)

    // サーキットブレーカー
    let collaborationBreaker = CircuitBreakerFactory.createForCollaboration ()
    let taskExecutionBreaker = CircuitBreakerFactory.createForTaskExecution ()

    // Collaboration層初期化
    let collaborationConfig =
        { CollaborationConfig.MaxConcurrentAgents = workflowConfig.DefaultAgentCount + 2
          TaskTimeoutMinutes = workflowConfig.TaskTimeoutMinutes * workflowConfig.DefaultAgentCount
          StaleAgentThreshold = TimeSpan.FromMinutes(30.0)
          MaxRetryAttempts = workflowConfig.MaxRetryAttempts
          DatabasePath = ":memory:"
          ConnectionPoolSize = 10
          WALModeEnabled = true
          AutoVacuumEnabled = true
          MaxHistoryRetentionDays = 30
          BackupEnabled = false
          BackupIntervalHours = 24
          EscalationEnabled = true
          AutoRecoveryMaxAttempts = 3
          PONotificationThreshold = EscalationSeverity.Important
          CriticalEscalationTimeoutMinutes = 5
          DataProtectionModeEnabled = true
          EmergencyShutdownEnabled = true }

    let collaborationFacade = new RealtimeCollaborationFacade(collaborationConfig)

    // タスク実行エンジン
    let taskExecutionEngine =
        new TaskExecutionEngine(collaborationFacade, workflowConfig)

    // スーパーバイザー（異常時自動復旧）
    let supervisor = new WorkflowSupervisor(repository, workflowConfig)

    // 初期化フラグ
    let mutable isInitialized = false
    let initLock = obj ()

    /// 初期化
    let initialize () =
        lock initLock (fun () ->
            if not isInitialized then
                supervisor.Start()
                isInitialized <- true
                logInfo "RefactoredFullWorkflowCoordinator" "システム初期化完了")

    /// 保護された実行（サーキットブレーカー付き）
    let executeWithProtection<'T> (breaker: CircuitBreaker) (operation: unit -> Async<'T>) = breaker.Execute(operation)

    /// ワークフロー開始（外部API）
    member this.StartWorkflow(instructions: string list) =
        async {
            try
                initialize ()

                logInfo "RefactoredFullWorkflowCoordinator" $"ワークフロー開始要求: {instructions.Length}件の指示"

                // サーキットブレーカー保護下でコマンド実行
                let! commandResult =
                    executeWithProtection collaborationBreaker (fun () -> commandHandler.StartWorkflow(instructions))

                match commandResult with
                | Ok workflowId ->
                    supervisor.AddMonitoredWorkflow(workflowId)

                    // タスク実行パイプライン開始
                    let! pipelineResult = this.ExecuteWorkflowPipeline(workflowId, instructions)

                    match pipelineResult with
                    | Ok _ -> return Ok "ワークフロー開始完了"
                    | Error error -> return Error $"ワークフロー実行失敗: {error}"

                | Error error -> return Error $"ワークフロー開始失敗: {error}"

            with ex ->
                logError "RefactoredFullWorkflowCoordinator" $"ワークフロー開始例外: {ex.Message}"
                return Error $"ワークフロー開始失敗: {ex.Message}"
        }

    /// ワークフロー実行パイプライン（内部）
    member private this.ExecuteWorkflowPipeline(workflowId: string, instructions: string list) =
        async {
            try
                // 1. ワークフロー状態取得
                let! workflowResult = queryHandler.GetWorkflowState(workflowId)

                match workflowResult with
                | Ok(Some workflow) ->
                    // 2. タスク分解段階に移行
                    let! stageUpdateResult = commandHandler.UpdateStage(workflowId, TaskDecomposition)

                    match stageUpdateResult with
                    | Ok() ->
                        // 3. スプリント実行段階に移行
                        let! sprintStageResult = commandHandler.UpdateStage(workflowId, SprintExecution)

                        match sprintStageResult with
                        | Ok() ->
                            // 4. タスク実行パイプライン実行
                            let! pipelineResult =
                                executeWithProtection taskExecutionBreaker (fun () ->
                                    taskExecutionEngine.ExecuteWorkflowPipeline(
                                        workflowId,
                                        workflow.SprintId,
                                        workflow.AssignedTasks,
                                        instructions
                                    ))

                            match pipelineResult with
                            | Ok(assessment, decision) ->
                                // 5. 決定に基づく最終処理
                                match decision with
                                | "completed" ->
                                    let! completionResult = commandHandler.CompleteWorkflow(workflowId)

                                    match completionResult with
                                    | Ok() ->
                                        supervisor.RemoveMonitoredWorkflow(workflowId)
                                        return Ok "ワークフロー完了"
                                    | Error error -> return Error $"ワークフロー完了処理失敗: {error}"
                                | "continue" -> return Ok "継続実行決定"
                                | "restart" -> return Ok "再設計決定"
                                | _ -> return Error "不明な判断結果"
                            | Error error -> return Error $"タスク実行パイプライン失敗: {error}"
                        | Error error -> return Error $"スプリント実行段階移行失敗: {error}"
                    | Error error -> return Error $"タスク分解段階移行失敗: {error}"

                | Ok None -> return Error $"ワークフローが見つかりません: {workflowId}"
                | Error error -> return Error $"ワークフロー状態取得失敗: {error}"

            with ex ->
                logError "RefactoredFullWorkflowCoordinator" $"ワークフロー実行パイプラインエラー: {ex.Message}"
                return Error $"ワークフロー実行パイプライン失敗: {ex.Message}"
        }

    /// 現在のワークフロー状態取得（外部API）
    member this.GetCurrentWorkflowState(workflowId: string) =
        async {
            try
                let! result = queryHandler.GetWorkflowState(workflowId)
                return result
            with ex ->
                logError "RefactoredFullWorkflowCoordinator" $"ワークフロー状態取得エラー: {ex.Message}"
                return Error $"ワークフロー状態取得失敗: {ex.Message}"
        }

    /// アクティブワークフロー一覧取得（外部API）
    member this.GetActiveWorkflows() =
        async {
            try
                let! result = queryHandler.GetActiveWorkflows()
                return result
            with ex ->
                logError "RefactoredFullWorkflowCoordinator" $"アクティブワークフロー取得エラー: {ex.Message}"
                return Error $"アクティブワークフロー取得失敗: {ex.Message}"
        }

    /// 緊急停止（外部API）
    member this.EmergencyStop(workflowId: string, reason: string) =
        async {
            try
                let! result = commandHandler.EmergencyStop(workflowId, reason)

                match result with
                | Ok() ->
                    supervisor.RemoveMonitoredWorkflow(workflowId)
                    return Ok "緊急停止完了"
                | Error error -> return Error $"緊急停止失敗: {error}"

            with ex ->
                logError "RefactoredFullWorkflowCoordinator" $"緊急停止エラー: {ex.Message}"
                return Error $"緊急停止失敗: {ex.Message}"
        }

    /// システム統計情報取得
    member this.GetSystemStatistics() =
        async {
            try
                let! workflowStats = queryHandler.GetWorkflowStatistics()
                let supervisorStatus = supervisor.GetStatus()
                let collaborationStats = collaborationBreaker.GetStatistics()
                let taskExecutionStats = taskExecutionBreaker.GetStatistics()

                let systemStats =
                    {| WorkflowStatistics = workflowStats
                       SupervisorStatus = supervisorStatus
                       CircuitBreakers =
                        {| Collaboration = collaborationStats
                           TaskExecution = taskExecutionStats |}
                       SystemUptime =
                        if isInitialized then
                            TimeSpan.FromSeconds(Environment.TickCount / 1000)
                        else
                            TimeSpan.Zero
                       MemoryUsage = GC.GetTotalMemory(false) |}

                return Ok systemStats

            with ex ->
                logError "RefactoredFullWorkflowCoordinator" $"システム統計取得エラー: {ex.Message}"
                return Error $"システム統計取得失敗: {ex.Message}"
        }

    /// ヘルスチェック
    member this.HealthCheck() =
        async {
            try
                let! activeWorkflowsResult = queryHandler.GetActiveWorkflows()
                let supervisorStatus = supervisor.GetStatus()
                let collaborationState = collaborationBreaker.GetState()
                let taskExecutionState = taskExecutionBreaker.GetState()

                let isHealthy =
                    Result.isOk activeWorkflowsResult
                    && supervisorStatus.IsRunning
                    && collaborationState <> Open
                    && taskExecutionState <> Open

                let healthReport =
                    {| IsHealthy = isHealthy
                       Timestamp = DateTime.UtcNow
                       Components =
                        {| Repository = Result.isOk activeWorkflowsResult
                           Supervisor = supervisorStatus.IsRunning
                           CollaborationCircuitBreaker = collaborationState <> Open
                           TaskExecutionCircuitBreaker = taskExecutionState <> Open |}
                       Details =
                        {| ActiveWorkflows = activeWorkflowsResult
                           SupervisorStatus = supervisorStatus
                           CircuitBreakerStates =
                            {| Collaboration = collaborationState
                               TaskExecution = taskExecutionState |} |} |}

                return Ok healthReport

            with ex ->
                logError "RefactoredFullWorkflowCoordinator" $"ヘルスチェックエラー: {ex.Message}"
                return Error $"ヘルスチェック失敗: {ex.Message}"
        }

    /// リソース解放
    member this.Dispose() =
        try
            logInfo "RefactoredFullWorkflowCoordinator" "リソース解放開始"

            supervisor.Dispose()
            taskExecutionEngine |> ignore // TaskExecutionEngineにはIDisposableは不要
            collaborationFacade.Dispose()
            repository |> ignore // InMemoryRepositoryにはIDisposableは不要

            isInitialized <- false

            logInfo "RefactoredFullWorkflowCoordinator" "RefactoredFullWorkflowCoordinator disposed"
        with ex ->
            logError "RefactoredFullWorkflowCoordinator" $"Dispose例外: {ex.Message}"

    interface IDisposable with
        member this.Dispose() = this.Dispose()

/// 後方互換性のための旧インターフェース実装
type FullWorkflowCoordinator(?config: WorkflowConfig) =

    let refactoredCoordinator = new RefactoredFullWorkflowCoordinator(?config = config)
    let mutable currentWorkflowId: string option = None

    /// ワークフロー開始（旧インターフェース）
    member this.StartWorkflow(instructions: string list) =
        async {
            let! result = refactoredCoordinator.StartWorkflow(instructions)

            match result with
            | Ok _ ->
                // アクティブワークフローを取得して現在のワークフローIDを設定
                let! activeResult = refactoredCoordinator.GetActiveWorkflows()

                match activeResult with
                | Ok workflows when not workflows.IsEmpty -> currentWorkflowId <- Some workflows.Head.WorkflowId
                | _ -> ()

                return result
            | Error _ -> return result
        }

    /// 現在のワークフロー状態取得（旧インターフェース）
    member this.GetCurrentWorkflowState() =
        match currentWorkflowId with
        | Some workflowId ->
            async {
                let! result = refactoredCoordinator.GetCurrentWorkflowState(workflowId)

                match result with
                | Ok workflowOption -> return workflowOption
                | Error _ -> return None
            }
            |> Async.RunSynchronously
        | None -> None

    /// 緊急停止（旧インターフェース）
    member this.EmergencyStop(reason: string) =
        async {
            match currentWorkflowId with
            | Some workflowId ->
                let! result = refactoredCoordinator.EmergencyStop(workflowId, reason)

                if Result.isOk result then
                    currentWorkflowId <- None

                return result
            | None -> return Error "アクティブなワークフローがありません"
        }

    /// リソース解放
    member this.Dispose() =
        try
            // 現在のワークフローをクリア
            currentWorkflowId <- None
            refactoredCoordinator.Dispose()
        with ex ->
            FCode.Logger.logError "FullWorkflowCoordinator" $"Dispose例外: {ex.Message}"

    interface IDisposable with
        member this.Dispose() = this.Dispose()

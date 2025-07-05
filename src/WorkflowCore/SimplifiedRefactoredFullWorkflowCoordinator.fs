module FCode.WorkflowCore.SimplifiedRefactoredFullWorkflowCoordinator

open System
open FCode.Logger
open FCode.WorkflowCore.WorkflowTypes
open FCode.WorkflowCore.IWorkflowRepository
open FCode.WorkflowCore.SimplifiedTaskExecutionEngine
open FCode.WorkflowCore.SimplifiedWorkflowSupervisor

/// デフォルト設定モジュール
module DefaultConfig =
    let create () =
        { SprintDurationMinutes = 18
          QualityThresholdHigh = 80
          QualityThresholdLow = 60
          TaskTimeoutMinutes = 6
          DefaultAgentCount = 3
          MaxRetryAttempts = 5
          RetryDelayMs = 1000 }

/// 簡易サーキットブレーカー（ダミー実装）
type DummyCircuitBreaker() =
    interface System.IDisposable with
        member _.Dispose() = ()

module CircuitBreakerFactory =
    let createForTesting () =
        new DummyCircuitBreaker() :> System.IDisposable

/// 簡略化されたリファクタリング済みフルワークフローコーディネーター
type SimplifiedRefactoredFullWorkflowCoordinator(?config: WorkflowConfig) =

    // 設定
    let workflowConfig = config |> Option.defaultValue (DefaultConfig.create ())

    // リポジトリ
    let repository = new InMemoryWorkflowRepository() :> IWorkflowRepository

    // サーキットブレーカー
    let taskExecutionBreaker = CircuitBreakerFactory.createForTesting ()

    // 簡略化されたタスク実行エンジン
    let taskExecutionEngine = new SimplifiedTaskExecutionEngine(workflowConfig)

    // 簡略化されたスーパーバイザー
    let supervisor = new SimplifiedWorkflowSupervisor(repository, workflowConfig)

    // 初期化フラグ
    let mutable isInitialized = false
    let initLock = obj ()

    /// 初期化
    let initialize () =
        lock initLock (fun () ->
            if not isInitialized then
                supervisor.Start()
                isInitialized <- true
                logInfo "SimplifiedRefactoredFullWorkflowCoordinator" "システム初期化完了")

    /// 保護された実行（簡易版）
    let executeWithProtection (breaker: System.IDisposable) (operation: unit -> Async<'T>) = operation ()

    /// 簡易ワークフロー開始
    let startWorkflowSimple (instructions: string list) =
        async {
            try
                let workflowId = System.Guid.NewGuid().ToString("N").[..11]
                logInfo "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "簡易ワークフロー開始: %s" workflowId)
                return Ok workflowId
            with ex ->
                return Error(sprintf "ワークフロー開始失敗: %s" ex.Message)
        }

    /// 簡易ワークフロー状態更新
    let updateStageSimple (workflowId: string) (newStage: WorkflowStage) =
        async {
            logInfo "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "段階更新: %s -> %A" workflowId newStage)
            return Ok()
        }

    /// 簡易ワークフロー完了
    let completeWorkflowSimple (workflowId: string) =
        async {
            logInfo "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "ワークフロー完了: %s" workflowId)
            return Ok()
        }

    /// ワークフロー開始（外部API）
    member this.StartWorkflow(instructions: string list) =
        async {
            try
                initialize ()

                logInfo "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "ワークフロー開始要求: %d件の指示" instructions.Length)

                // 簡易コマンド実行
                let! commandResult = startWorkflowSimple (instructions)

                match commandResult with
                | Ok workflowId ->
                    // タスク実行パイプライン開始
                    let! pipelineResult = this.ExecuteWorkflowPipeline(workflowId, instructions)

                    match pipelineResult with
                    | Ok _ -> return Ok "ワークフロー開始完了"
                    | Error error -> return Error(sprintf "ワークフロー実行失敗: %s" error)

                | Error error -> return Error(sprintf "ワークフロー開始失敗: %s" error)

            with ex ->
                logError "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "ワークフロー開始例外: %s" ex.Message)
                return Error(sprintf "ワークフロー開始失敗: %s" ex.Message)
        }

    /// ワークフロー実行パイプライン（内部）
    member private this.ExecuteWorkflowPipeline(workflowId: string, instructions: string list) =
        async {
            try
                // 1. 簡易段階更新（タスク分解段階）
                let! stageUpdateResult = updateStageSimple workflowId TaskDecomposition

                match stageUpdateResult with
                | Ok() ->
                    // 2. 簡易段階更新（スプリント実行段階）
                    let! sprintStageResult = updateStageSimple workflowId SprintExecution

                    match sprintStageResult with
                    | Ok() ->
                        // 3. タスク実行パイプライン実行
                        let! pipelineResult =
                            executeWithProtection taskExecutionBreaker (fun () ->
                                taskExecutionEngine.ExecuteWorkflowPipeline(
                                    workflowId,
                                    $"sprint-{workflowId}",
                                    Map.ofList [ ("dev1", [ "task1"; "task2" ]); ("qa1", [ "task3" ]) ],
                                    instructions
                                ))

                        match pipelineResult with
                        | Ok(assessment, decision) ->
                            // 4. 決定に基づく最終処理
                            match decision with
                            | "completed" ->
                                let! completionResult = completeWorkflowSimple workflowId

                                match completionResult with
                                | Ok() -> return Ok "ワークフロー完了"
                                | Error error -> return Error(sprintf "ワークフロー完了処理失敗: %s" error)
                            | _ -> return Ok decision
                        | Error error -> return Error(sprintf "タスク実行パイプライン失敗: %s" error)
                    | Error error -> return Error(sprintf "スプリント実行段階移行失敗: %s" error)
                | Error error -> return Error(sprintf "タスク分解段階移行失敗: %s" error)

            with ex ->
                logError "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "ワークフロー実行パイプラインエラー: %s" ex.Message)
                return Error(sprintf "ワークフロー実行パイプライン失敗: %s" ex.Message)
        }

    /// 現在のワークフロー状態取得（外部API）
    member this.GetCurrentWorkflowState(workflowId: string) =
        async {
            try
                logInfo "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "ワークフロー状態取得: %s" workflowId)
                return Ok None // 簡易版
            with ex ->
                logError "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "ワークフロー状態取得エラー: %s" ex.Message)
                return Error(sprintf "ワークフロー状態取得失敗: %s" ex.Message)
        }

    /// アクティブワークフロー一覧取得（外部API）
    member this.GetActiveWorkflows() =
        async {
            try
                logInfo "SimplifiedRefactoredFullWorkflowCoordinator" "アクティブワークフロー取得"
                return Ok [] // 簡易版
            with ex ->
                logError "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "アクティブワークフロー取得エラー: %s" ex.Message)
                return Error(sprintf "アクティブワークフロー取得失敗: %s" ex.Message)
        }

    /// 緊急停止（外部API）
    member this.EmergencyStop(workflowId: string, reason: string) =
        async {
            try
                logInfo "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "緊急停止: %s, 理由: %s" workflowId reason)
                return Ok()
            with ex ->
                logError "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "緊急停止エラー: %s" ex.Message)
                return Error(sprintf "緊急停止失敗: %s" ex.Message)
        }

    /// リソース解放
    member this.Dispose() =
        try
            logInfo "SimplifiedRefactoredFullWorkflowCoordinator" "リソース解放開始"

            supervisor.Dispose()
            isInitialized <- false

            logInfo "SimplifiedRefactoredFullWorkflowCoordinator" "SimplifiedRefactoredFullWorkflowCoordinator disposed"
        with ex ->
            logError "SimplifiedRefactoredFullWorkflowCoordinator" (sprintf "Dispose例外: %s" ex.Message)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

/// 後方互換性のための旧インターフェース実装
type FullWorkflowCoordinator(?config: WorkflowConfig) =

    let simplifiedCoordinator =
        new SimplifiedRefactoredFullWorkflowCoordinator(?config = config)

    let mutable currentWorkflowId: string option = None

    /// ワークフロー開始（旧インターフェース）
    member this.StartWorkflow(instructions: string list) =
        async {
            let! result = simplifiedCoordinator.StartWorkflow(instructions)

            match result with
            | Ok _ ->
                // アクティブワークフローを取得して現在のワークフローIDを設定
                let! activeResult = simplifiedCoordinator.GetActiveWorkflows()

                match activeResult with
                | Ok workflows when not workflows.IsEmpty -> currentWorkflowId <- Some "simplified-workflow"
                | _ -> ()

                return result
            | Error _ -> return result
        }

    /// 現在のワークフロー状態取得（旧インターフェース）
    member this.GetCurrentWorkflowState() =
        match currentWorkflowId with
        | Some workflowId ->
            async {
                let! result = simplifiedCoordinator.GetCurrentWorkflowState(workflowId)

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
                let! result = simplifiedCoordinator.EmergencyStop(workflowId, reason)

                match result with
                | Ok _ -> currentWorkflowId <- None

                return result
            | None -> return Error "アクティブなワークフローがありません"
        }

    /// リソース解放
    member this.Dispose() = simplifiedCoordinator.Dispose()

    interface IDisposable with
        member this.Dispose() = this.Dispose()

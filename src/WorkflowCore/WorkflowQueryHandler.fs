module FCode.WorkflowCore.WorkflowQueryHandler

open FCode.Logger
open FCode.WorkflowCore.WorkflowTypes
open FCode.WorkflowCore.IWorkflowRepository

/// ワークフロークエリ処理ハンドラー
type WorkflowQueryHandler(repository: IWorkflowRepository) =

    /// ワークフロー状態取得クエリ処理
    member this.GetWorkflowState(workflowId: string) =
        async {
            try
                let! result = repository.GetWorkflow(workflowId)

                match result with
                | Ok workflowOption ->
                    logInfo "WorkflowQueryHandler" $"ワークフロー状態取得: {workflowId}"
                    return Ok workflowOption
                | Error error ->
                    logWarning "WorkflowQueryHandler" $"ワークフロー状態取得失敗: {workflowId}, {error}"
                    return Error error

            with ex ->
                logError "WorkflowQueryHandler" $"ワークフロー状態取得エラー: {ex.Message}"
                return Error $"ワークフロー状態取得失敗: {ex.Message}"
        }

    /// アクティブワークフロー一覧取得クエリ処理
    member this.GetActiveWorkflows() =
        async {
            try
                let! result = repository.GetActiveWorkflows()

                match result with
                | Ok workflows ->
                    logInfo "WorkflowQueryHandler" $"アクティブワークフロー取得: {workflows.Length}件"
                    return Ok workflows
                | Error error ->
                    logWarning "WorkflowQueryHandler" $"アクティブワークフロー取得失敗: {error}"
                    return Error error

            with ex ->
                logError "WorkflowQueryHandler" $"アクティブワークフロー取得エラー: {ex.Message}"
                return Error $"アクティブワークフロー取得失敗: {ex.Message}"
        }

    /// ワークフロー履歴取得クエリ処理
    member this.GetWorkflowHistory(workflowId: string) =
        async {
            try
                let! result = repository.GetWorkflowHistory(workflowId)

                match result with
                | Ok history ->
                    logInfo "WorkflowQueryHandler" $"ワークフロー履歴取得: {workflowId}, {history.Length}件"
                    return Ok history
                | Error error ->
                    logWarning "WorkflowQueryHandler" $"ワークフロー履歴取得失敗: {workflowId}, {error}"
                    return Error error

            with ex ->
                logError "WorkflowQueryHandler" $"ワークフロー履歴取得エラー: {ex.Message}"
                return Error $"ワークフロー履歴取得失敗: {ex.Message}"
        }

    /// ワークフロー統計情報取得
    member this.GetWorkflowStatistics() =
        async {
            try
                let! activeResult = repository.GetActiveWorkflows()

                match activeResult with
                | Ok activeWorkflows ->
                    let statistics =
                        {| ActiveWorkflowCount = activeWorkflows.Length
                           StageDistribution =
                            activeWorkflows
                            |> List.groupBy (fun w -> w.Stage)
                            |> List.map (fun (stage, workflows) -> (stage, workflows.Length))
                            |> Map.ofList
                           AverageExecutionTime =
                            activeWorkflows
                            |> List.filter (fun w -> w.IsCompleted)
                            |> List.map (fun w -> (w.LastUpdated - w.StartTime).TotalMinutes)
                            |> fun times -> if times.IsEmpty then 0.0 else List.average times
                           LastUpdated = System.DateTime.UtcNow |}

                    logInfo "WorkflowQueryHandler" $"ワークフロー統計取得完了"
                    return Ok statistics
                | Error error -> return Error error

            with ex ->
                logError "WorkflowQueryHandler" $"ワークフロー統計取得エラー: {ex.Message}"
                return Error $"ワークフロー統計取得失敗: {ex.Message}"
        }

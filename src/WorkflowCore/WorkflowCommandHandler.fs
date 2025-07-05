module FCode.WorkflowCore.WorkflowCommandHandler

open System
open FCode.Logger
open FCode.UnifiedActivityView
open FCode.WorkflowCore.WorkflowTypes
open FCode.WorkflowCore.IWorkflowRepository

/// ワークフローコマンド処理ハンドラー
type WorkflowCommandHandler(repository: IWorkflowRepository, config: WorkflowConfig) =

    /// 新しいワークフローID生成
    let generateWorkflowId () =
        let timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
        let guid = Guid.NewGuid().ToString("N").[..7]
        $"workflow-{timestamp}-{guid}"

    /// 新しいスプリントID生成
    let generateSprintId () =
        let timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
        $"sprint-{timestamp}"

    /// タスク分解とエージェント割り当て
    let decomposeAndAssignTasks (instructions: string list) =
        instructions
        |> List.mapi (fun i instruction ->
            let taskId = $"task-{i + 1}"

            let agentId =
                match i % config.DefaultAgentCount with
                | 0 -> "dev1"
                | 1 -> "dev2"
                | _ -> "dev3"

            (agentId, taskId, instruction))
        |> List.groupBy (fun (agentId, _, _) -> agentId)
        |> List.map (fun (agentId, tasks) -> (agentId, tasks |> List.map (fun (_, taskId, _) -> taskId)))
        |> Map.ofList

    /// ワークフロー開始コマンド処理
    member this.StartWorkflow(instructions: string list) =
        async {
            try
                logInfo "WorkflowCommandHandler" $"新ワークフロー開始: {instructions.Length}件の指示"

                let workflowId = generateWorkflowId ()
                let sprintId = generateSprintId ()
                let assignedTasks = decomposeAndAssignTasks instructions

                let newWorkflow =
                    { WorkflowId = workflowId
                      Stage = Instruction
                      SprintId = sprintId
                      StartTime = DateTime.UtcNow
                      Instructions = instructions
                      AssignedTasks = assignedTasks
                      IsCompleted = false
                      LastUpdated = DateTime.UtcNow }

                // リポジトリに保存
                let! saveResult = repository.SaveWorkflow(newWorkflow)

                match saveResult with
                | Ok() ->
                    addSystemActivity "PO" TaskAssignment ("新しいワークフロー開始: " + String.concat "; " instructions)

                    // タスク割り当て通知
                    assignedTasks
                    |> Map.iter (fun agentId taskIds ->
                        taskIds
                        |> List.iter (fun taskId -> addSystemActivity agentId TaskAssignment $"タスク割り当て: {taskId}"))

                    logInfo "WorkflowCommandHandler" $"ワークフロー開始完了: {workflowId}"
                    return Ok workflowId

                | Error error ->
                    logError "WorkflowCommandHandler" $"ワークフロー保存失敗: {error}"
                    return Error $"ワークフロー開始失敗: {error}"

            with ex ->
                logError "WorkflowCommandHandler" $"ワークフロー開始エラー: {ex.Message}"
                return Error $"ワークフロー開始失敗: {ex.Message}"
        }

    /// ワークフロー段階更新コマンド処理
    member this.UpdateStage(workflowId: string, newStage: WorkflowStage) =
        async {
            try
                let! getResult = repository.GetWorkflow(workflowId)

                match getResult with
                | Ok(Some workflow) ->
                    let updatedWorkflow =
                        { workflow with
                            Stage = newStage
                            LastUpdated = DateTime.UtcNow }

                    let! updateResult = repository.UpdateWorkflow(updatedWorkflow)

                    match updateResult with
                    | Ok() ->
                        logInfo "WorkflowCommandHandler" $"ワークフロー段階更新: {workflowId} -> {newStage}"
                        addSystemActivity "system" SystemMessage $"ワークフロー段階更新: {newStage}"
                        return Ok()
                    | Error error -> return Error $"ワークフロー段階更新失敗: {error}"

                | Ok None -> return Error $"ワークフローが見つかりません: {workflowId}"
                | Error error -> return Error $"ワークフロー取得失敗: {error}"

            with ex ->
                logError "WorkflowCommandHandler" $"ワークフロー段階更新エラー: {ex.Message}"
                return Error $"ワークフロー段階更新失敗: {ex.Message}"
        }

    /// ワークフロー完了コマンド処理
    member this.CompleteWorkflow(workflowId: string) =
        async {
            try
                let! getResult = repository.GetWorkflow(workflowId)

                match getResult with
                | Ok(Some workflow) ->
                    let completedWorkflow =
                        { workflow with
                            Stage = Completion
                            IsCompleted = true
                            LastUpdated = DateTime.UtcNow }

                    let! updateResult = repository.UpdateWorkflow(completedWorkflow)

                    match updateResult with
                    | Ok() ->
                        logInfo "WorkflowCommandHandler" $"ワークフロー完了: {workflowId}"
                        addSystemActivity "PO" SystemMessage "ワークフロー正常完了 - 成果確認済み"
                        return Ok()
                    | Error error -> return Error $"ワークフロー完了処理失敗: {error}"

                | Ok None -> return Error $"ワークフローが見つかりません: {workflowId}"
                | Error error -> return Error $"ワークフロー取得失敗: {error}"

            with ex ->
                logError "WorkflowCommandHandler" $"ワークフロー完了エラー: {ex.Message}"
                return Error $"ワークフロー完了失敗: {ex.Message}"
        }

    /// 緊急停止コマンド処理
    member this.EmergencyStop(workflowId: string, reason: string) =
        async {
            try
                logInfo "WorkflowCommandHandler" $"緊急停止実行: {workflowId}, 理由: {reason}"

                let! deleteResult = repository.DeleteWorkflow(workflowId)

                match deleteResult with
                | Ok() ->
                    addSystemActivity "system" SystemMessage $"緊急停止: {reason}"
                    logInfo "WorkflowCommandHandler" $"緊急停止完了: {workflowId}"
                    return Ok()
                | Error error -> return Error $"緊急停止失敗: {error}"

            with ex ->
                logError "WorkflowCommandHandler" $"緊急停止エラー: {ex.Message}"
                return Error $"緊急停止失敗: {ex.Message}"
        }

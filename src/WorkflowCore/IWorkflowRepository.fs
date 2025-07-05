module FCode.WorkflowCore.IWorkflowRepository

open System
open FCode.WorkflowCore.WorkflowTypes

/// ワークフロー状態管理リポジトリインターフェース
type IWorkflowRepository =
    abstract member SaveWorkflow: WorkflowState -> Async<WorkflowResult<unit>>
    abstract member GetWorkflow: string -> Async<WorkflowResult<WorkflowState option>>
    abstract member GetActiveWorkflows: unit -> Async<WorkflowResult<WorkflowState list>>
    abstract member UpdateWorkflow: WorkflowState -> Async<WorkflowResult<unit>>
    abstract member DeleteWorkflow: string -> Async<WorkflowResult<unit>>
    abstract member GetWorkflowHistory: string -> Async<WorkflowResult<WorkflowState list>>

/// インメモリワークフロー状態リポジトリ（開発・テスト用）
type InMemoryWorkflowRepository() =
    let workflowLock = obj ()
    let mutable workflows: Map<string, WorkflowState> = Map.empty
    let mutable history: Map<string, WorkflowState list> = Map.empty

    /// スレッドセーフなワークフロー操作
    let withLock f = lock workflowLock f

    /// 履歴に追加
    let addToHistory (workflow: WorkflowState) =
        let currentHistory =
            history |> Map.tryFind workflow.WorkflowId |> Option.defaultValue []

        let updatedHistory = workflow :: currentHistory
        history <- history |> Map.add workflow.WorkflowId updatedHistory

    interface IWorkflowRepository with
        member this.SaveWorkflow(workflow: WorkflowState) =
            async {
                try
                    withLock (fun () ->
                        workflows <- workflows |> Map.add workflow.WorkflowId workflow
                        addToHistory workflow)

                    return Ok()
                with ex ->
                    return Error(sprintf "ワークフロー保存失敗: %s" ex.Message)
            }

        member this.GetWorkflow(workflowId: string) =
            async {
                try
                    let result = withLock (fun () -> workflows |> Map.tryFind workflowId)
                    return Ok result
                with ex ->
                    return Error(sprintf "ワークフロー取得失敗: %s" ex.Message)
            }

        member this.GetActiveWorkflows() =
            async {
                try
                    let activeWorkflows =
                        withLock (fun () ->
                            workflows |> Map.values |> Seq.filter (fun w -> not w.IsCompleted) |> Seq.toList)

                    return Ok activeWorkflows
                with ex ->
                    return Error(sprintf "アクティブワークフロー取得失敗: %s" ex.Message)
            }

        member this.UpdateWorkflow(workflow: WorkflowState) =
            async {
                try
                    let updatedWorkflow =
                        { workflow with
                            LastUpdated = DateTime.UtcNow }

                    withLock (fun () ->
                        workflows <- workflows |> Map.add workflow.WorkflowId updatedWorkflow
                        addToHistory updatedWorkflow)

                    return Ok()
                with ex ->
                    return Error(sprintf "ワークフロー更新失敗: %s" ex.Message)
            }

        member this.DeleteWorkflow(workflowId: string) =
            async {
                try
                    withLock (fun () -> workflows <- workflows |> Map.remove workflowId)
                    return Ok()
                with ex ->
                    return Error(sprintf "ワークフロー削除失敗: %s" ex.Message)
            }

        member this.GetWorkflowHistory(workflowId: string) =
            async {
                try
                    let workflowHistory =
                        withLock (fun () -> history |> Map.tryFind workflowId |> Option.defaultValue [] |> List.rev // 時系列順
                        )

                    return Ok workflowHistory
                with ex ->
                    return Error(sprintf "ワークフロー履歴取得失敗: %s" ex.Message)
            }

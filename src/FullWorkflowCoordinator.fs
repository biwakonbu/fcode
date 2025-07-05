module FCode.FullWorkflowCoordinator

open System
open FCode.Logger
open FCode.UnifiedActivityView

/// ワークフロー段階
type WorkflowStage =
    | Instruction // PO指示受付
    | TaskDecomposition // タスク分解・エージェント配分
    | SprintExecution // 18分自走実行
    | QualityAssessment // 品質評価・完成度判定
    | ContinuationDecision // 継続・完了・承認判断
    | Completion // 完了・成果確認

/// ワークフロー状態
type WorkflowState =
    { Stage: WorkflowStage
      SprintId: string
      StartTime: DateTime
      Instructions: string list
      AssignedTasks: Map<string, string list> // AgentId -> TaskIds
      IsCompleted: bool }

/// フルワークフローコーディネーター（簡易版）
type FullWorkflowCoordinator() =

    let mutable currentWorkflow: WorkflowState option = None

    /// ワークフロー開始: PO指示受付
    member this.StartWorkflow(instructions: string list) =
        async {
            try
                logInfo "FullWorkflowCoordinator" ("新ワークフロー開始: " + string instructions.Length + "件の指示")

                let sprintId = "sprint-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")

                let newWorkflow =
                    { Stage = Instruction
                      SprintId = sprintId
                      StartTime = DateTime.UtcNow
                      Instructions = instructions
                      AssignedTasks = Map.empty
                      IsCompleted = false }

                currentWorkflow <- Some newWorkflow

                // 会話ペインに開始通知
                addSystemActivity "PO" TaskAssignment ("新しいワークフロー開始: " + String.concat "; " instructions)

                // 基本的なワークフロー処理
                addSystemActivity "system" TaskAssignment "タスク分解・エージェント配分開始"

                return Result.Ok "ワークフロー開始完了"

            with ex ->
                logError "FullWorkflowCoordinator" ("ワークフロー開始エラー: " + ex.Message)
                return Result.Error("ワークフロー開始失敗: " + ex.Message)
        }

    /// 現在のワークフロー状態取得
    member this.GetCurrentWorkflowState() = currentWorkflow

    /// 緊急停止
    member this.EmergencyStop(reason: string) =
        async {
            try
                logInfo "FullWorkflowCoordinator" ("緊急停止実行: " + reason)

                addSystemActivity "system" SystemMessage ("緊急停止: " + reason)

                currentWorkflow <- None

                return Result.Ok "緊急停止完了"

            with ex ->
                logError "FullWorkflowCoordinator" ("緊急停止エラー: " + ex.Message)
                return Result.Error("緊急停止失敗: " + ex.Message)
        }

    /// リソース解放
    member this.Dispose() =
        try
            logInfo "FullWorkflowCoordinator" "FullWorkflowCoordinator disposed"
        with ex ->
            logError "FullWorkflowCoordinator" ("Dispose例外: " + ex.Message)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

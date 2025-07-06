module FCode.WorkflowCore.WorkflowTypes

open System

/// ワークフロー設定
type WorkflowConfig =
    { SprintDurationMinutes: int
      QualityThresholdHigh: int
      QualityThresholdLow: int
      TaskTimeoutMinutes: int
      DefaultAgentCount: int
      MaxRetryAttempts: int
      RetryDelayMs: int }

/// ワークフロー段階
type WorkflowStage =
    | Instruction // PO指示受付
    | TaskDecomposition // タスク分解・エージェント配分
    | SprintExecution // スプリント実行
    | QualityAssessment // 品質評価・完成度判定
    | ContinuationDecision // 継続・完了・承認判断
    | Completion // 完了・成果確認

/// ワークフロー状態
type WorkflowState =
    { WorkflowId: string
      Stage: WorkflowStage
      SprintId: string
      StartTime: DateTime
      Instructions: string list
      AssignedTasks: Map<string, string list> // AgentId -> TaskIds
      IsCompleted: bool
      LastUpdated: DateTime }

/// ワークフロー操作結果
type WorkflowResult<'T> = Result<'T, string>

/// ワークフロー操作コマンド
type WorkflowCommand =
    | StartWorkflow of instructions: string list
    | UpdateStage of workflowId: string * stage: WorkflowStage
    | AssignTasks of workflowId: string * tasks: Map<string, string list>
    | CompleteWorkflow of workflowId: string
    | EmergencyStop of workflowId: string * reason: string

/// ワークフロー操作クエリ
type WorkflowQuery =
    | GetWorkflowState of workflowId: string
    | GetActiveWorkflows
    | GetWorkflowHistory of workflowId: string

/// タスク情報
type TaskInfo =
    { TaskId: string
      Title: string
      Description: string
      AssignedAgent: string option
      EstimatedDuration: TimeSpan option
      ActualDuration: TimeSpan option
      CreatedAt: DateTime
      UpdatedAt: DateTime }

/// 品質評価結果
type QualityAssessment =
    { WorkflowId: string
      QualityScore: int
      AssessmentDate: DateTime
      Details: string
      Recommendation: string }

/// デフォルト設定
module DefaultConfig =
    let create () =
        { SprintDurationMinutes = 18
          QualityThresholdHigh = 80
          QualityThresholdLow = 60
          TaskTimeoutMinutes = 6
          DefaultAgentCount = 3
          MaxRetryAttempts = 5
          RetryDelayMs = 1000 }

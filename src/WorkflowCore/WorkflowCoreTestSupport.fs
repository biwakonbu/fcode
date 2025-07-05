module FCode.WorkflowCore.WorkflowCoreTestSupport

open FCode.WorkflowCore.WorkflowTypes

/// テスト用WorkflowConfig生成
let createTestWorkflowConfig (isCI: bool) =
    { SprintDurationMinutes = if isCI then 1 else 18
      QualityThresholdHigh = 80
      QualityThresholdLow = 60
      TaskTimeoutMinutes = if isCI then 1 else 6
      DefaultAgentCount = 3
      MaxRetryAttempts = if isCI then 2 else 5
      RetryDelayMs = if isCI then 500 else 1000 }

/// テスト用ワークフロー状態生成
let createTestWorkflowState (workflowId: string) =
    { WorkflowId = workflowId
      Stage = WorkflowStage.Instruction
      SprintId = $"test-sprint-{workflowId}"
      StartTime = System.DateTime.UtcNow
      Instructions = [ "テスト用指示1"; "テスト用指示2" ]
      AssignedTasks = Map.ofList [ ("dev1", [ "task1"; "task2" ]); ("qa1", [ "task3" ]) ]
      IsCompleted = false
      LastUpdated = System.DateTime.UtcNow }

module FCode.WorkflowCore.TaskExecutionEngine

open System
open FCode.Logger
open FCode.UnifiedActivityView
open FCode.WorkflowCore.WorkflowTypes
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.RealtimeCollaborationFacade

/// タスク実行状態
type TaskExecutionState =
    { TaskId: string
      AgentId: string
      Status: string
      StartTime: DateTime
      Progress: int }

/// タスク実行エンジン
type TaskExecutionEngine(collaborationFacade: RealtimeCollaborationFacade, config: WorkflowConfig) =

    let taskExecutionLock = obj ()
    let mutable executingTasks: Map<string, TaskExecutionState> = Map.empty

    /// スレッドセーフなタスク状態操作
    let withTaskLock f = lock taskExecutionLock f

    /// タスクをCollaboration層に登録
    let registerTaskInCollaboration (taskId: string) (agentId: string) (instruction: string) =
        async {
            try
                let taskInfo =
                    { TaskId = taskId
                      Title = $"Task: {instruction}"
                      Description = instruction
                      Status = TaskStatus.Pending
                      AssignedAgent = Some agentId
                      Priority = TaskPriority.Medium
                      EstimatedDuration = Some(TimeSpan.FromMinutes(float config.TaskTimeoutMinutes))
                      ActualDuration = None
                      RequiredResources = []
                      CreatedAt = DateTime.UtcNow
                      UpdatedAt = DateTime.UtcNow }

                // Collaboration層にタスク登録を試行
                match collaborationFacade.AutoAssignTask(taskId) with
                | Result.Ok _ ->
                    addSystemActivity agentId TaskAssignment $"タスク割り当て: {instruction}"
                    logInfo "TaskExecutionEngine" $"タスク登録成功: {taskId}"
                    return Ok()
                | Result.Error error ->
                    logWarning "TaskExecutionEngine" $"タスク割り当て失敗 {taskId}: {error}"
                    addSystemActivity agentId SystemMessage $"タスク割り当て失敗: {error}"
                    return Error error

            with ex ->
                let errorMsg = $"タスク登録エラー {taskId}: {ex.Message}"
                logError "TaskExecutionEngine" errorMsg
                return Error errorMsg
        }

    /// スプリント実行シミュレーション
    let simulateSprintExecution (assignedTasks: Map<string, string list>) (sprintId: string) =
        async {
            try
                logInfo "TaskExecutionEngine" $"スプリント実行開始: {sprintId}"
                addSystemActivity "system" SystemMessage $"{config.SprintDurationMinutes}分自走実行開始 - エージェント並列作業"

                // 全タスクを並列実行
                let taskExecutions =
                    assignedTasks
                    |> Map.toList
                    |> List.collect (fun (agentId, taskIds) -> taskIds |> List.map (fun taskId -> (agentId, taskId)))

                let! executionResults =
                    taskExecutions
                    |> List.map (fun (agentId, taskId) ->
                        async {
                            // タスク開始状態を記録
                            let executionState =
                                { TaskId = taskId
                                  AgentId = agentId
                                  Status = "実行中"
                                  StartTime = DateTime.UtcNow
                                  Progress = 0 }

                            withTaskLock (fun () -> executingTasks <- executingTasks |> Map.add taskId executionState)

                            // 短時間の実行シミュレーション
                            do! Async.Sleep(1000)

                            // 進捗更新シミュレーション
                            for progress in [ 25; 50; 75; 100 ] do
                                let updatedState =
                                    { executionState with
                                        Progress = progress }

                                withTaskLock (fun () ->
                                    executingTasks <- executingTasks |> Map.add taskId updatedState)

                                addSystemActivity agentId Progress $"タスク実行中: {taskId} ({progress}%完了)"
                                do! Async.Sleep(200)

                            // 完了状態に更新
                            withTaskLock (fun () -> executingTasks <- executingTasks |> Map.remove taskId)

                            addSystemActivity agentId Progress $"タスク完了: {taskId}"
                            return Ok taskId
                        })
                    |> Async.Parallel

                let successfulTasks =
                    executionResults
                    |> Array.choose (function
                        | Ok taskId -> Some taskId
                        | Error _ -> None)
                    |> Array.length

                let totalTasks = taskExecutions.Length

                logInfo "TaskExecutionEngine" $"スプリント実行完了: {successfulTasks}/{totalTasks}タスク成功"
                addSystemActivity "system" SystemMessage $"スプリント実行完了: {successfulTasks}/{totalTasks}タスク成功"

                return Ok(successfulTasks, totalTasks)

            with ex ->
                let errorMsg = $"スプリント実行エラー: {ex.Message}"
                logError "TaskExecutionEngine" errorMsg
                return Error errorMsg
        }

    /// 品質評価実行
    let performQualityAssessment (sprintId: string) (successfulTasks: int) (totalTasks: int) =
        async {
            try
                logInfo "TaskExecutionEngine" "品質評価・完成度判定開始"
                addSystemActivity "qa1" QualityReview "品質評価開始 - 完成度・品質チェック"

                // 成功率に基づく簡易品質評価
                let successRate =
                    if totalTasks > 0 then
                        (float successfulTasks / float totalTasks) * 100.0
                    else
                        0.0

                let qualityScore = int successRate

                // ランダム要素を追加（実際の品質評価ではより複雑な要因を考慮）
                let random = System.Random()

                let adjustedScore =
                    qualityScore + random.Next(-10, 11) // ±10ポイントの調整
                    |> max 0
                    |> min 100

                addSystemActivity "qa2" QualityReview $"品質評価完了 - 完成度: {adjustedScore}%"

                let assessment =
                    { WorkflowId = sprintId
                      QualityScore = adjustedScore
                      AssessmentDate = DateTime.UtcNow
                      Details = $"成功率: {successRate:F1}%, 調整後スコア: {adjustedScore}%"
                      Recommendation =
                        if adjustedScore >= config.QualityThresholdHigh then
                            "承認推奨"
                        elif adjustedScore >= config.QualityThresholdLow then
                            "継続開発推奨"
                        else
                            "再設計推奨" }

                logInfo "TaskExecutionEngine" $"品質評価完了: {adjustedScore}%"
                return Ok assessment

            with ex ->
                let errorMsg = $"品質評価エラー: {ex.Message}"
                logError "TaskExecutionEngine" errorMsg
                return Error errorMsg
        }

    /// 継続判断実行
    let makeContinuationDecision (assessment: QualityAssessment) =
        async {
            try
                logInfo "TaskExecutionEngine" "継続・完了・承認判断開始"
                addSystemActivity "pm" Decision "継続・完了判断開始"

                let decision =
                    if assessment.QualityScore >= config.QualityThresholdHigh then
                        addSystemActivity "pm" Decision $"品質基準達成({assessment.QualityScore}%) - 完了承認"
                        "completed"
                    elif assessment.QualityScore >= config.QualityThresholdLow then
                        addSystemActivity "pm" Decision $"品質基準未達({assessment.QualityScore}%) - 継続実行要求"
                        "continue"
                    else
                        addSystemActivity "pm" Decision $"品質基準大幅未達({assessment.QualityScore}%) - 再設計要求"
                        "restart"

                logInfo "TaskExecutionEngine" $"継続判断完了: {decision}"
                return Ok decision

            with ex ->
                let errorMsg = $"継続判断エラー: {ex.Message}"
                logError "TaskExecutionEngine" errorMsg
                return Error errorMsg
        }

    /// 完全なワークフロー実行パイプライン
    member this.ExecuteWorkflowPipeline
        (workflowId: string, sprintId: string, assignedTasks: Map<string, string list>, instructions: string list)
        =
        async {
            try
                // 1. タスク分解・エージェント配分段階
                logInfo "TaskExecutionEngine" "タスク分解・エージェント配分開始"

                let taskRegistrations =
                    [ for (agentId, taskIds) in Map.toList assignedTasks do
                          for (i, taskId) in List.indexed taskIds do
                              if i < instructions.Length then
                                  yield registerTaskInCollaboration taskId agentId instructions.[i] ]

                let! registrationResults = taskRegistrations |> Async.Parallel

                let successfulRegistrations =
                    registrationResults
                    |> Array.choose (function
                        | Ok() -> Some()
                        | Error _ -> None)
                    |> Array.length

                logInfo "TaskExecutionEngine" $"タスク登録完了: {successfulRegistrations}/{taskRegistrations.Length}件"

                // 2. スプリント実行段階
                let! sprintResult = simulateSprintExecution assignedTasks sprintId

                match sprintResult with
                | Ok(successfulTasks, totalTasks) ->
                    // 3. 品質評価段階
                    let! assessmentResult = performQualityAssessment sprintId successfulTasks totalTasks

                    match assessmentResult with
                    | Ok assessment ->
                        // 4. 継続判断段階
                        let! decisionResult = makeContinuationDecision assessment

                        match decisionResult with
                        | Ok decision -> return Ok(assessment, decision)
                        | Error error -> return Error $"継続判断失敗: {error}"
                    | Error error -> return Error $"品質評価失敗: {error}"
                | Error error -> return Error $"スプリント実行失敗: {error}"

            with ex ->
                let errorMsg = $"ワークフロー実行パイプラインエラー: {ex.Message}"
                logError "TaskExecutionEngine" errorMsg
                return Error errorMsg
        }

    /// 実行中タスクの状態取得
    member this.GetExecutingTaskStates() =
        withTaskLock (fun () -> executingTasks |> Map.toList)

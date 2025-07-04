module FCode.Collaboration.CompletionAssessmentManager

open System
open FCode.Collaboration.CollaborationTypes

// 優先度から品質スコア変換（テスト用簡易実装）
let private priorityToScore priority =
    match priority with
    | TaskPriority.Critical -> 0.95
    | TaskPriority.High -> 0.90
    | TaskPriority.Medium -> 0.75
    | TaskPriority.Low -> 0.60
    | _ -> 0.50 // 未知の優先度に対するデフォルト値

/// 完成度評価マネージャー
type CompletionAssessmentManager() =

    /// タスク状態集計
    member private this.AggregateTaskStates(tasks: TaskInfo list) =
        let completedTasks = tasks |> List.filter (fun t -> t.Status = TaskStatus.Completed)

        let inProgressTasks =
            tasks |> List.filter (fun t -> t.Status = TaskStatus.InProgress)

        let blockedTasks =
            tasks
            |> List.filter (fun t -> t.Status = TaskStatus.Failed || t.Status = TaskStatus.Cancelled)

        (completedTasks, inProgressTasks, blockedTasks)

    /// 完了率計算
    member private this.CalculateCompletionRate(completedTasks: TaskInfo list, totalTasks: int) =
        if totalTasks = 0 then
            0.0
        else
            float completedTasks.Length / float totalTasks

    /// 品質スコア計算
    member private this.CalculateQualityScore(completedTasks: TaskInfo list) =
        if completedTasks.IsEmpty then
            0.0
        else
            let totalQuality =
                completedTasks |> List.sumBy (fun t -> priorityToScore t.Priority)

            totalQuality / float completedTasks.Length

    /// 受け入れ基準とPO承認要否判定
    member private this.EvaluateAcceptanceAndApproval
        (completionRate: float, qualityScore: float, blockedTasks: TaskInfo list)
        =
        let acceptanceCriteriaMet = completionRate = 1.0 && qualityScore >= 0.8
        let requiresPOApproval = not acceptanceCriteriaMet || blockedTasks.Length > 0
        (acceptanceCriteriaMet, requiresPOApproval)

    /// 完成度評価実行
    member this.EvaluateCompletion(tasks: TaskInfo list, acceptanceCriteria: string list) : CompletionAssessment =
        let (completedTasks, inProgressTasks, blockedTasks) =
            this.AggregateTaskStates(tasks)

        let completionRate = this.CalculateCompletionRate(completedTasks, tasks.Length)
        let qualityScore = this.CalculateQualityScore(completedTasks)

        let (acceptanceCriteriaMet, requiresPOApproval) =
            this.EvaluateAcceptanceAndApproval(completionRate, qualityScore, blockedTasks)

        { TasksCompleted = completedTasks.Length
          TasksInProgress = inProgressTasks.Length
          TasksBlocked = blockedTasks.Length
          OverallCompletionRate = completionRate
          QualityScore = qualityScore
          AcceptanceCriteriaMet = acceptanceCriteriaMet
          RequiresPOApproval = requiresPOApproval }

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

    /// 完成度評価実行
    member this.EvaluateCompletion(tasks: TaskInfo list, acceptanceCriteria: string list) : CompletionAssessment =
        // タスク状態集計
        let completedTasks = tasks |> List.filter (fun t -> t.Status = TaskStatus.Completed)

        let inProgressTasks =
            tasks |> List.filter (fun t -> t.Status = TaskStatus.InProgress)

        let blockedTasks =
            tasks
            |> List.filter (fun t -> t.Status = TaskStatus.Failed || t.Status = TaskStatus.Cancelled)

        // 完了率計算
        let totalTasks = tasks.Length

        let completionRate =
            if totalTasks = 0 then
                0.0
            else
                float completedTasks.Length / float totalTasks

        // 品質スコア計算（完了タスクの平均品質）
        let qualityScore =
            if completedTasks.IsEmpty then
                0.0
            else
                let totalQuality =
                    completedTasks |> List.sumBy (fun t -> priorityToScore t.Priority)

                totalQuality / float completedTasks.Length

        // 受け入れ基準判定（完了率100% かつ 品質スコア0.8以上）
        let acceptanceCriteriaMet = completionRate = 1.0 && qualityScore >= 0.8

        // PO承認要否判定（受け入れ基準未達成 または 品質課題あり）
        let requiresPOApproval = not acceptanceCriteriaMet || blockedTasks.Length > 0

        { TasksCompleted = completedTasks.Length
          TasksInProgress = inProgressTasks.Length
          TasksBlocked = blockedTasks.Length
          OverallCompletionRate = completionRate
          QualityScore = qualityScore
          AcceptanceCriteriaMet = acceptanceCriteriaMet
          RequiresPOApproval = requiresPOApproval }

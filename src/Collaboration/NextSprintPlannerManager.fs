module FCode.Collaboration.NextSprintPlannerManager

open System
open FCode.Collaboration.CollaborationTypes

/// 次スプリント計画マネージャー
type NextSprintPlannerManager() =

    /// 次スプリント計画生成
    member this.PlanNextSprint
        (completion: CompletionAssessment, quality: QualityEvaluationSummary, resources: string list)
        : NextSprintPlan =
        // スプリントID生成
        let sprintId = sprintf "SPRINT-%s" (DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"))

        // 標準3日スプリント
        let duration = VirtualDay 3

        // 優先タスク決定
        let priorityTasks = this.DeterminePriorityTasks(completion, quality)

        // リソース配分
        let resourceAllocation = this.AllocateResources(resources, priorityTasks)

        // 依存関係分析
        let dependencies = this.AnalyzeDependencies(completion, quality)

        // リスク軽減策
        let riskMitigation = this.PlanRiskMitigation(completion, quality, resources)

        { SprintId = sprintId
          Duration = duration
          PriorityTasks = priorityTasks
          ResourceAllocation = resourceAllocation
          Dependencies = dependencies
          RiskMitigation = riskMitigation }

    /// 優先タスク決定
    member private this.DeterminePriorityTasks
        (completion: CompletionAssessment, quality: QualityEvaluationSummary)
        : string list =
        let mutable tasks = []

        // 品質課題が多い場合は品質改善を優先
        if completion.QualityScore < 0.7 || not quality.SecurityCompliance then
            tasks <- "品質改善" :: tasks

        // セキュリティ課題がある場合
        if not quality.SecurityCompliance then
            tasks <- "セキュリティ強化" :: tasks

        // テストカバレッジが低い場合
        if quality.TestCoverage < 0.8 then
            tasks <- "テスト強化" :: tasks

        // ドキュメントが不足している場合
        if quality.DocumentationScore < 0.7 then
            tasks <- "ドキュメント改善" :: tasks

        // 高品質で完成度が高い場合は新機能開発
        if completion.OverallCompletionRate = 1.0 && completion.QualityScore >= 0.9 then
            tasks <- "新機能開発" :: tasks
            tasks <- "品質維持" :: tasks

        // ブロックされたタスクがある場合
        if completion.TasksBlocked > 0 then
            tasks <- "ブロッカー解消" :: tasks

        // 進行中タスクがある場合
        if completion.TasksInProgress > 0 then
            tasks <- "進行中タスク完了" :: tasks

        // 効率化が推奨されている場合
        if quality.RecommendedImprovements |> List.contains "効率化" then
            tasks <- "効率化" :: tasks

        // 最低限のタスクを保証
        if tasks.IsEmpty then
            tasks <- [ "継続的改善" ]

        tasks |> List.rev |> List.distinct

    /// リソース配分
    member private this.AllocateResources(resources: string list, priorityTasks: string list) : (string * int) list =
        resources
        |> List.mapi (fun i resource ->
            let allocation =
                if priorityTasks.Length > 0 then
                    // タスク数に応じて配分調整
                    min 100 (80 + (priorityTasks.Length * 5))
                else
                    80 // 基本配分

            (resource, allocation))

    /// 依存関係分析
    member private this.AnalyzeDependencies
        (completion: CompletionAssessment, quality: QualityEvaluationSummary)
        : string list =
        let mutable dependencies = []

        // 品質課題がある場合の依存関係
        if completion.QualityScore < 0.8 then
            dependencies <- "品質基準達成後に新機能着手" :: dependencies

        // セキュリティ課題がある場合
        if not quality.SecurityCompliance then
            dependencies <- "セキュリティ対応完了が必須" :: dependencies

        // ブロックされたタスクがある場合
        if completion.TasksBlocked > 0 then
            dependencies <- "ブロッカー解消が前提" :: dependencies

        dependencies |> List.rev

    /// リスク軽減策計画
    member private this.PlanRiskMitigation
        (completion: CompletionAssessment, quality: QualityEvaluationSummary, resources: string list)
        : string list =
        let mutable mitigations = []

        // リソース制約リスク
        if resources.Length <= 1 then
            mitigations <- "リソース制約" :: mitigations
            mitigations <- "外部リソース検討" :: mitigations

        // 品質リスク
        if completion.QualityScore < 0.8 then
            mitigations <- "品質ゲート強化" :: mitigations

        // セキュリティリスク
        if not quality.SecurityCompliance then
            mitigations <- "セキュリティレビュー強化" :: mitigations

        // スケジュールリスク
        if completion.TasksBlocked > 0 || completion.OverallCompletionRate < 0.7 then
            mitigations <- "スケジュール遅延対策" :: mitigations

        // 技術的負債リスク
        if quality.CodeQuality < 0.7 then
            mitigations <- "技術的負債解消" :: mitigations

        // 最低限のリスク軽減策
        if mitigations.IsEmpty then
            mitigations <- [ "継続的監視" ]

        mitigations |> List.rev |> List.distinct

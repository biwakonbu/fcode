module FCode.QualityGateManager

open System
open FCode.Collaboration.CollaborationTypes
open FCode.TaskAssignmentManager

/// 品質評価レベル
type QualityLevel =
    | Excellent = 5
    | Good = 4
    | Acceptable = 3
    | Poor = 2
    | Unacceptable = 1

/// 品質評価分野
type QualityDimension =
    | CodeQuality
    | FunctionalQuality
    | ProcessQuality
    | UserExperience
    | TechnicalCompleteness

/// 品質メトリクス
type QualityMetrics =
    { Dimension: QualityDimension
      Score: float
      MaxScore: float
      Details: string
      Timestamp: DateTime }

/// 品質評価結果
type QualityEvaluationResult =
    { TaskId: string
      OverallScore: float
      QualityLevel: QualityLevel
      Metrics: QualityMetrics list
      Recommendations: string list
      EvaluatedBy: string
      EvaluatedAt: DateTime
      PassesThreshold: bool }

/// レビュアー情報
type Reviewer =
    { ReviewerId: string
      Role: AgentSpecialization
      Expertise: string list
      AvailabilityScore: float }

/// レビューコメント
type ReviewComment =
    { CommentId: string
      ReviewerId: string
      TaskId: string
      Dimension: QualityDimension
      Score: float
      Comment: string
      Suggestions: string list
      Timestamp: DateTime
      Priority: TaskPriority }

/// レビュー結果
type ReviewResult =
    { TaskId: string
      Comments: ReviewComment list
      ConsensusScore: float
      RequiredImprovements: string list
      Approved: bool
      ReviewedAt: DateTime }

/// 代替提案
type AlternativeProposal =
    { ProposalId: string
      TaskId: string
      Title: string
      Description: string
      TechnicalApproach: string
      EstimatedEffort: TimeSpan
      DifficultyScore: float
      FeasibilityScore: float
      GeneratedBy: string
      GeneratedAt: DateTime }

/// 品質閾値設定
type QualityThreshold =
    { Dimension: QualityDimension
      MinimumScore: float
      CriticalThreshold: float
      ExcellenceThreshold: float
      WeightFactor: float }

/// 品質評価エンジン
type QualityEvaluationEngine() =

    // デフォルト品質閾値
    let defaultThresholds =
        [ { Dimension = CodeQuality
            MinimumScore = 0.6
            CriticalThreshold = 0.4
            ExcellenceThreshold = 0.85
            WeightFactor = 0.25 }
          { Dimension = FunctionalQuality
            MinimumScore = 0.7
            CriticalThreshold = 0.5
            ExcellenceThreshold = 0.9
            WeightFactor = 0.30 }
          { Dimension = ProcessQuality
            MinimumScore = 0.6
            CriticalThreshold = 0.4
            ExcellenceThreshold = 0.8
            WeightFactor = 0.15 }
          { Dimension = UserExperience
            MinimumScore = 0.65
            CriticalThreshold = 0.45
            ExcellenceThreshold = 0.85
            WeightFactor = 0.20 }
          { Dimension = TechnicalCompleteness
            MinimumScore = 0.75
            CriticalThreshold = 0.6
            ExcellenceThreshold = 0.95
            WeightFactor = 0.10 } ]

    /// 品質スコア計算
    member this.CalculateQualityScore(metrics: QualityMetrics list) : float =
        let totalWeightedScore =
            metrics
            |> List.sumBy (fun metric ->
                let threshold =
                    defaultThresholds |> List.find (fun t -> t.Dimension = metric.Dimension)

                let normalizedScore = metric.Score / metric.MaxScore
                normalizedScore * threshold.WeightFactor)

        let totalWeight = defaultThresholds |> List.sumBy (fun t -> t.WeightFactor)
        totalWeightedScore / totalWeight

    /// 品質レベル判定
    member this.DetermineQualityLevel(overallScore: float) : QualityLevel =
        if overallScore >= 0.9 then QualityLevel.Excellent
        elif overallScore >= 0.75 then QualityLevel.Good
        elif overallScore >= 0.6 then QualityLevel.Acceptable
        elif overallScore >= 0.4 then QualityLevel.Poor
        else QualityLevel.Unacceptable

    /// 品質閾値チェック
    member this.PassesThreshold(overallScore: float) : bool = overallScore >= 0.6

    /// 改善推奨事項生成
    member this.GenerateRecommendations(metrics: QualityMetrics list) : string list =
        metrics
        |> List.choose (fun metric ->
            let threshold =
                defaultThresholds |> List.find (fun t -> t.Dimension = metric.Dimension)

            let normalizedScore = metric.Score / metric.MaxScore

            if normalizedScore < threshold.MinimumScore then
                match metric.Dimension with
                | CodeQuality -> Some "コード品質改善: 複雑度削減・リファクタリング・命名規約統一"
                | FunctionalQuality -> Some "機能品質向上: 要件再確認・テストケース追加・エッジケース対応"
                | ProcessQuality -> Some "プロセス改善: レビュー手順見直し・ドキュメント整備・コミュニケーション強化"
                | UserExperience -> Some "UX改善: ユーザビリティテスト・インターフェース見直し・アクセシビリティ対応"
                | TechnicalCompleteness -> Some "技術完全性向上: エラーハンドリング強化・パフォーマンス最適化・セキュリティ対策"
            else
                None)

    /// 包括的品質評価
    member this.EvaluateQuality
        (taskId: string, metrics: QualityMetrics list, evaluatedBy: string)
        : QualityEvaluationResult =
        let overallScore = this.CalculateQualityScore(metrics)
        let qualityLevel = this.DetermineQualityLevel(overallScore)
        let passesThreshold = this.PassesThreshold(overallScore)
        let recommendations = this.GenerateRecommendations(metrics)

        { TaskId = taskId
          OverallScore = overallScore
          QualityLevel = qualityLevel
          Metrics = metrics
          Recommendations = recommendations
          EvaluatedBy = evaluatedBy
          EvaluatedAt = DateTime.UtcNow
          PassesThreshold = passesThreshold }

/// 上流下流レビューシステム
type UpstreamDownstreamReviewer() =

    /// レビュアー選定
    member this.SelectReviewers(task: ParsedTask) : Reviewer list =
        let baseReviewers =
            [
              // pdm (プロダクトマネージャー) - 上流レビュー
              { ReviewerId = "pdm"
                Role = ProjectManagement [ "product-management"; "strategy" ]
                Expertise = [ "user-requirements"; "business-logic"; "product-strategy" ]
                AvailabilityScore = 0.9 }

              // dev2 - 技術レビュー
              { ReviewerId = "dev2"
                Role = Development [ "F#"; ".NET"; "architecture" ]
                Expertise = [ "code-quality"; "architecture"; "performance" ]
                AvailabilityScore = 0.85 } ]

        // タスクの専門分野に応じて追加レビュアー選定
        let additionalReviewers =
            match task.RequiredSpecialization with
            | Testing _ ->
                [ { ReviewerId = "qa1"
                    Role = Testing [ "quality-assurance"; "test-strategy" ]
                    Expertise = [ "test-coverage"; "quality-metrics"; "defect-prevention" ]
                    AvailabilityScore = 0.9 } ]
            | UXDesign _ ->
                [ { ReviewerId = "ux"
                    Role = UXDesign [ "ui-design"; "user-experience" ]
                    Expertise = [ "usability"; "accessibility"; "user-interface" ]
                    AvailabilityScore = 0.8 } ]
            | _ -> []

        baseReviewers @ additionalReviewers

    /// 協調レビュー実行
    member this.ConductCollaborativeReview(task: ParsedTask, reviewers: Reviewer list) : ReviewResult =
        let comments =
            reviewers
            |> List.mapi (fun i reviewer ->
                let score = this.GenerateReviewScore(task, reviewer)
                let comment = this.GenerateReviewComment(task, reviewer, score)
                let suggestions = this.GenerateSuggestions(task, reviewer, score)

                { CommentId = $"comment_{task.TaskId}_{i}"
                  ReviewerId = reviewer.ReviewerId
                  TaskId = task.TaskId
                  Dimension = this.GetReviewDimension(reviewer.Role)
                  Score = score
                  Comment = comment
                  Suggestions = suggestions
                  Timestamp = DateTime.UtcNow
                  Priority =
                    if score < 0.6 then
                        TaskPriority.High
                    else
                        TaskPriority.Medium })

        let consensusScore = comments |> List.averageBy (fun c -> c.Score)
        let improvements = this.ExtractRequiredImprovements(comments)
        let approved = consensusScore >= 0.65 && improvements.Length <= 3

        { TaskId = task.TaskId
          Comments = comments
          ConsensusScore = consensusScore
          RequiredImprovements = improvements
          Approved = approved
          ReviewedAt = DateTime.UtcNow }

    /// レビュー次元取得
    member this.GetReviewDimension(role: AgentSpecialization) : QualityDimension =
        match role with
        | Development _ -> CodeQuality
        | Testing _ -> FunctionalQuality
        | UXDesign _ -> UserExperience
        | ProjectManagement _ -> ProcessQuality

    /// レビュースコア生成
    member this.GenerateReviewScore(task: ParsedTask, reviewer: Reviewer) : float =
        let baseScore = 0.7 // ベーススコア

        // 専門性適合度による調整
        let expertiseBonus =
            if
                reviewer.Expertise
                |> List.exists (fun e -> task.Description.ToLower().Contains(e.ToLower()))
            then
                0.1
            else
                0.0

        // タスク複雑度による調整
        let complexityPenalty =
            if task.EstimatedDuration.TotalHours > 6.0 then
                -0.1
            else
                0.0

        // ランダム要素（実際のレビューでは実装品質による）
        let variance = Random().NextDouble() * 0.2 - 0.1

        Math.Max(0.0, Math.Min(1.0, baseScore + expertiseBonus + complexityPenalty + variance))

    /// レビューコメント生成
    member this.GenerateReviewComment(task: ParsedTask, reviewer: Reviewer, score: float) : string =
        let qualityDescription =
            if score >= 0.8 then "優秀な実装"
            elif score >= 0.65 then "良好な実装"
            elif score >= 0.5 then "改善が必要"
            else "大幅な改善が必要"

        $"{reviewer.ReviewerId}による{qualityDescription}: {task.Title}のレビュー結果 (スコア: {score:F2})"

    /// 改善提案生成
    member this.GenerateSuggestions(task: ParsedTask, reviewer: Reviewer, score: float) : string list =
        if score < 0.7 then
            match reviewer.Role with
            | Development _ -> [ "コード構造の改善"; "エラーハンドリング強化"; "パフォーマンス最適化" ]
            | Testing _ -> [ "テストケース追加"; "エッジケース対応"; "品質メトリクス改善" ]
            | UXDesign _ -> [ "ユーザビリティ向上"; "アクセシビリティ対応"; "インターフェース改善" ]
            | ProjectManagement _ -> [ "要件明確化"; "プロセス改善"; "ステークホルダー調整" ]
        else
            [ "現在の実装品質は良好です" ]

    /// 必要改善事項抽出
    member this.ExtractRequiredImprovements(comments: ReviewComment list) : string list =
        let improvements =
            comments
            |> List.filter (fun c -> c.Score < 0.65)
            |> List.collect (fun c -> c.Suggestions)
            |> List.distinct

        let maxItems = Math.Min(5, Math.Max(1, improvements.Length))

        if improvements.Length > maxItems then
            improvements |> List.take maxItems
        else
            improvements

/// 3案出しシステム
type AlternativeProposalGenerator() =

    /// 実装困難度分析
    member this.AnalyzeImplementationDifficulty(task: ParsedTask) : float =
        let baseComplexity =
            match task.RequiredSpecialization with
            | Development _ -> 0.5
            | Testing _ -> 0.3
            | UXDesign _ -> 0.4
            | ProjectManagement _ -> 0.2

        let durationFactor = Math.Min(1.0, task.EstimatedDuration.TotalHours / 8.0)
        let dependencyFactor = float task.Dependencies.Length * 0.1

        Math.Min(1.0, baseComplexity + durationFactor + dependencyFactor)

    /// 代替案生成が必要かチェック
    member this.NeedsAlternativeProposals(difficulty: float, qualityResult: QualityEvaluationResult option) : bool =
        let difficultyThreshold = difficulty > 0.7

        let qualityThreshold =
            match qualityResult with
            | Some result -> not result.PassesThreshold
            | None -> false

        difficultyThreshold || qualityThreshold

    /// 3つの代替案生成
    member this.GenerateAlternativeProposals(task: ParsedTask) : AlternativeProposal list =
        let baseId = Guid.NewGuid().ToString("N").[0..7]

        [
          // 案1: 簡略化アプローチ
          { ProposalId = $"alt1_{baseId}"
            TaskId = task.TaskId
            Title = $"{task.Title} - 簡略化実装"
            Description = "最小限の機能で要件を満たす軽量実装"
            TechnicalApproach = "既存パターンの活用・ライブラリ依存最小化・段階的実装"
            EstimatedEffort = TimeSpan.FromHours(task.EstimatedDuration.TotalHours * 0.6)
            DifficultyScore = 0.3
            FeasibilityScore = 0.9
            GeneratedBy = "alternative_proposal_generator"
            GeneratedAt = DateTime.UtcNow }

          // 案2: 標準アプローチ
          { ProposalId = $"alt2_{baseId}"
            TaskId = task.TaskId
            Title = $"{task.Title} - 標準実装"
            Description = "バランスの取れた標準的な実装アプローチ"
            TechnicalApproach = "設計パターン適用・適度な抽象化・将来拡張考慮"
            EstimatedEffort = task.EstimatedDuration
            DifficultyScore = 0.5
            FeasibilityScore = 0.8
            GeneratedBy = "alternative_proposal_generator"
            GeneratedAt = DateTime.UtcNow }

          // 案3: 高機能アプローチ
          { ProposalId = $"alt3_{baseId}"
            TaskId = task.TaskId
            Title = $"{task.Title} - 高機能実装"
            Description = "将来拡張性・保守性を重視した包括的実装"
            TechnicalApproach = "アーキテクチャパターン適用・高度な抽象化・包括的テスト"
            EstimatedEffort = TimeSpan.FromHours(task.EstimatedDuration.TotalHours * 1.5)
            DifficultyScore = 0.8
            FeasibilityScore = 0.6
            GeneratedBy = "alternative_proposal_generator"
            GeneratedAt = DateTime.UtcNow } ]

    /// 最適代替案選択
    member this.SelectBestAlternative
        (proposals: AlternativeProposal list, constraints: Map<string, float>)
        : AlternativeProposal =
        let timeConstraint = constraints.TryFind("time_pressure") |> Option.defaultValue 0.5

        let qualityConstraint =
            constraints.TryFind("quality_requirement") |> Option.defaultValue 0.7

        proposals
        |> List.map (fun p ->
            let timeScore = 1.0 - (p.EstimatedEffort.TotalHours / 24.0) // 時間効率
            let feasibilityScore = p.FeasibilityScore
            let difficultyPenalty = 1.0 - p.DifficultyScore

            let overallScore =
                (timeScore * timeConstraint)
                + (feasibilityScore * 0.3)
                + (difficultyPenalty * qualityConstraint * 0.3)

            (p, overallScore))
        |> List.maxBy snd
        |> fst

/// QualityGateManagerメインクラス
type QualityGateManager
    (
        evaluationEngine: QualityEvaluationEngine,
        reviewer: UpstreamDownstreamReviewer,
        proposalGenerator: AlternativeProposalGenerator
    ) =

    /// 包括的品質ゲート実行
    member this.ExecuteQualityGate(task: ParsedTask) : Result<ReviewResult * AlternativeProposal list option, string> =
        try
            // 1. 初期品質評価
            let sampleMetrics = this.GenerateSampleMetrics(task)

            let qualityResult =
                evaluationEngine.EvaluateQuality(task.TaskId, sampleMetrics, "quality_gate_manager")

            // 2. 上流下流レビュー実行
            let reviewers = reviewer.SelectReviewers(task)
            let reviewResult = reviewer.ConductCollaborativeReview(task, reviewers)

            // 3. 代替案生成判定
            let difficulty = proposalGenerator.AnalyzeImplementationDifficulty(task)

            let needsAlternatives =
                proposalGenerator.NeedsAlternativeProposals(difficulty, Some qualityResult)

            let alternatives =
                if needsAlternatives then
                    Some(proposalGenerator.GenerateAlternativeProposals(task))
                else
                    None

            Result.Ok(reviewResult, alternatives)
        with ex ->
            Result.Error $"品質ゲート実行エラー: {ex.Message}"

    /// サンプルメトリクス生成（実際の実装では実装品質分析）
    member this.GenerateSampleMetrics(task: ParsedTask) : QualityMetrics list =
        let random = Random()

        [ { Dimension = CodeQuality
            Score = random.NextDouble() * 80.0 + 20.0
            MaxScore = 100.0
            Details = "コード複雑度・可読性・保守性評価"
            Timestamp = DateTime.UtcNow }

          { Dimension = FunctionalQuality
            Score = random.NextDouble() * 85.0 + 15.0
            MaxScore = 100.0
            Details = "要件適合性・機能完全性評価"
            Timestamp = DateTime.UtcNow }

          { Dimension = ProcessQuality
            Score = random.NextDouble() * 75.0 + 25.0
            MaxScore = 100.0
            Details = "開発プロセス・レビュー品質評価"
            Timestamp = DateTime.UtcNow }

          { Dimension = UserExperience
            Score = random.NextDouble() * 70.0 + 30.0
            MaxScore = 100.0
            Details = "ユーザビリティ・UX品質評価"
            Timestamp = DateTime.UtcNow }

          { Dimension = TechnicalCompleteness
            Score = random.NextDouble() * 90.0 + 10.0
            MaxScore = 100.0
            Details = "技術的完全性・堅牢性評価"
            Timestamp = DateTime.UtcNow } ]

    /// 品質ゲート結果レポート生成
    member this.GenerateQualityReport
        (task: ParsedTask, reviewResult: ReviewResult, alternatives: AlternativeProposal list option)
        : string =
        let approvalStatus = if reviewResult.Approved then "承認" else "要改善"

        let alternativeInfo =
            match alternatives with
            | Some alts -> $"\n代替案: {alts.Length}件提案"
            | None -> ""

        $"品質ゲート結果 - {task.Title}\n"
        + $"総合スコア: {reviewResult.ConsensusScore:F2}\n"
        + $"判定: {approvalStatus}\n"
        + $"改善要求: {reviewResult.RequiredImprovements.Length}件{alternativeInfo}"

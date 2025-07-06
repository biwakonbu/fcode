module FCode.Tests.QualityGateManagerTests

open System
open NUnit.Framework
open FCode.QualityGateManager
open FCode.TaskAssignmentManager
open FCode.Collaboration.CollaborationTypes

[<Test>]
[<Category("Unit")>]
let ``QualityEvaluationEngine - 品質スコア計算テスト`` () =
    // Arrange
    let engine = QualityEvaluationEngine()

    let metrics =
        [ { Dimension = CodeQuality
            Score = 80.0
            MaxScore = 100.0
            Details = "コード品質"
            Timestamp = DateTime.UtcNow }
          { Dimension = FunctionalQuality
            Score = 85.0
            MaxScore = 100.0
            Details = "機能品質"
            Timestamp = DateTime.UtcNow }
          { Dimension = ProcessQuality
            Score = 70.0
            MaxScore = 100.0
            Details = "プロセス品質"
            Timestamp = DateTime.UtcNow }
          { Dimension = UserExperience
            Score = 75.0
            MaxScore = 100.0
            Details = "UX品質"
            Timestamp = DateTime.UtcNow }
          { Dimension = TechnicalCompleteness
            Score = 90.0
            MaxScore = 100.0
            Details = "技術完全性"
            Timestamp = DateTime.UtcNow } ]

    // Act
    let score = engine.CalculateQualityScore(metrics)

    // Assert
    Assert.True(score > 0.7, $"品質スコア {score:F2} が期待値0.7を下回っています")
    Assert.True(score <= 1.0, $"品質スコア {score:F2} が上限1.0を超えています")

[<Test>]
[<Category("Unit")>]
let ``QualityEvaluationEngine - 品質レベル判定テスト`` () =
    // Arrange
    let engine = QualityEvaluationEngine()

    // Act & Assert
    Assert.AreEqual(QualityLevel.Excellent, engine.DetermineQualityLevel(0.95))
    Assert.AreEqual(QualityLevel.Good, engine.DetermineQualityLevel(0.8))
    Assert.AreEqual(QualityLevel.Acceptable, engine.DetermineQualityLevel(0.65))
    Assert.AreEqual(QualityLevel.Poor, engine.DetermineQualityLevel(0.5))
    Assert.AreEqual(QualityLevel.Unacceptable, engine.DetermineQualityLevel(0.3))

[<Test>]
[<Category("Unit")>]
let ``QualityEvaluationEngine - 品質閾値チェックテスト`` () =
    // Arrange
    let engine = QualityEvaluationEngine()

    // Act & Assert
    Assert.True(engine.PassesThreshold(0.7)) // 閾値以上
    Assert.True(engine.PassesThreshold(0.6)) // 閾値ちょうど
    Assert.False(engine.PassesThreshold(0.5)) // 閾値未満

[<Test>]
[<Category("Unit")>]
let ``QualityEvaluationEngine - 改善推奨事項生成テスト`` () =
    // Arrange
    let engine = QualityEvaluationEngine()

    let lowQualityMetrics =
        [ { Dimension = CodeQuality
            Score = 50.0
            MaxScore = 100.0
            Details = "低品質コード"
            Timestamp = DateTime.UtcNow }
          { Dimension = FunctionalQuality
            Score = 40.0
            MaxScore = 100.0
            Details = "機能不完全"
            Timestamp = DateTime.UtcNow } ]

    // Act
    let recommendations = engine.GenerateRecommendations(lowQualityMetrics)

    // Assert
    Assert.True(recommendations.Length >= 2, $"推奨事項が {recommendations.Length} 件しかありません（期待値: 2件以上）")
    Assert.True(recommendations |> List.exists (fun r -> r.Contains("コード品質改善")))
    Assert.True(recommendations |> List.exists (fun r -> r.Contains("機能品質向上")))

[<Test>]
[<Category("Unit")>]
let ``QualityEvaluationEngine - 包括的品質評価テスト`` () =
    // Arrange
    let engine = QualityEvaluationEngine()

    let metrics =
        [ { Dimension = CodeQuality
            Score = 75.0
            MaxScore = 100.0
            Details = "良好なコード"
            Timestamp = DateTime.UtcNow }
          { Dimension = FunctionalQuality
            Score = 80.0
            MaxScore = 100.0
            Details = "機能完全"
            Timestamp = DateTime.UtcNow } ]

    // Act
    let result = engine.EvaluateQuality("task123", metrics, "test_evaluator")

    // Assert
    Assert.AreEqual("task123", result.TaskId)
    Assert.AreEqual("test_evaluator", result.EvaluatedBy)
    Assert.True(result.OverallScore > 0.0)
    Assert.AreEqual(2, result.Metrics.Length)
    Assert.True(result.EvaluatedAt <= DateTime.UtcNow)

[<Test>]
[<Category("Unit")>]
let ``UpstreamDownstreamReviewer - レビュアー選定テスト`` () =
    // Arrange
    let reviewer = UpstreamDownstreamReviewer()

    let devTask =
        { TaskId = "dev_task_1"
          Title = "F#機能実装"
          Description = "新しいF#機能を実装する"
          RequiredSpecialization = Development [ "F#" ]
          EstimatedDuration = TimeSpan.FromHours(4.0)
          Priority = TaskPriority.High
          Dependencies = [] }

    // Act
    let reviewers = reviewer.SelectReviewers(devTask)

    // Assert
    Assert.True(reviewers.Length >= 2, $"レビュアー数が {reviewers.Length} 人では不足です（期待値: 2人以上）")
    Assert.True(reviewers |> List.exists (fun r -> r.ReviewerId = "pdm"))
    Assert.True(reviewers |> List.exists (fun r -> r.ReviewerId = "dev2"))

[<Test>]
[<Category("Unit")>]
let ``UpstreamDownstreamReviewer - UXタスクレビュアー選定テスト`` () =
    // Arrange
    let reviewer = UpstreamDownstreamReviewer()

    let uxTask =
        { TaskId = "ux_task_1"
          Title = "UI改善"
          Description = "ユーザーインターフェースを改善する"
          RequiredSpecialization = UXDesign [ "UI-design" ]
          EstimatedDuration = TimeSpan.FromHours(3.0)
          Priority = TaskPriority.Medium
          Dependencies = [] }

    // Act
    let reviewers = reviewer.SelectReviewers(uxTask)

    // Assert
    Assert.True(reviewers.Length >= 3, $"UXタスクのレビュアー数が {reviewers.Length} 人では不足です")
    Assert.True(reviewers |> List.exists (fun r -> r.ReviewerId = "ux"))

[<Test>]
[<Category("Unit")>]
let ``UpstreamDownstreamReviewer - 協調レビュー実行テスト`` () =
    // Arrange
    let reviewer = UpstreamDownstreamReviewer()

    let task =
        { TaskId = "test_task"
          Title = "テストタスク"
          Description = "レビューテスト用のタスク"
          RequiredSpecialization = Development [ "F#" ]
          EstimatedDuration = TimeSpan.FromHours(2.0)
          Priority = TaskPriority.Medium
          Dependencies = [] }

    let reviewers =
        [ { ReviewerId = "pdm"
            Role = ProjectManagement [ "planning" ]
            Expertise = [ "requirements" ]
            AvailabilityScore = 0.9 }
          { ReviewerId = "dev2"
            Role = Development [ "F#" ]
            Expertise = [ "architecture" ]
            AvailabilityScore = 0.8 } ]

    // Act
    let result = reviewer.ConductCollaborativeReview(task, reviewers)

    // Assert
    Assert.AreEqual("test_task", result.TaskId)
    Assert.AreEqual(2, result.Comments.Length)
    Assert.True(result.ConsensusScore >= 0.0 && result.ConsensusScore <= 1.0)
    Assert.True(result.ReviewedAt <= DateTime.UtcNow)

[<Test>]
[<Category("Unit")>]
let ``UpstreamDownstreamReviewer - レビュー次元取得テスト`` () =
    // Arrange
    let reviewer = UpstreamDownstreamReviewer()

    // Act & Assert
    Assert.AreEqual(CodeQuality, reviewer.GetReviewDimension(Development [ "F#" ]))
    Assert.AreEqual(FunctionalQuality, reviewer.GetReviewDimension(Testing [ "unit-testing" ]))
    Assert.AreEqual(UserExperience, reviewer.GetReviewDimension(UXDesign [ "UI-design" ]))
    Assert.AreEqual(ProcessQuality, reviewer.GetReviewDimension(ProjectManagement [ "planning" ]))

[<Test>]
[<Category("Unit")>]
let ``UpstreamDownstreamReviewer - レビュースコア生成テスト`` () =
    // Arrange
    let reviewer = UpstreamDownstreamReviewer()

    let task =
        { TaskId = "score_test"
          Title = "スコアテスト"
          Description = "architecture を含むタスク"
          RequiredSpecialization = Development [ "F#" ]
          EstimatedDuration = TimeSpan.FromHours(3.0)
          Priority = TaskPriority.Medium
          Dependencies = [] }

    let testReviewer =
        { ReviewerId = "dev2"
          Role = Development [ "F#" ]
          Expertise = [ "architecture"; "performance" ]
          AvailabilityScore = 0.9 }

    // Act
    let score = reviewer.GenerateReviewScore(task, testReviewer)

    // Assert
    Assert.True(score >= 0.0 && score <= 1.0, $"レビュースコア {score} が範囲外です")

[<Test>]
[<Category("Unit")>]
let ``AlternativeProposalGenerator - 実装困難度分析テスト`` () =
    // Arrange
    let generator = AlternativeProposalGenerator()

    let complexTask =
        { TaskId = "complex_task"
          Title = "複雑なタスク"
          Description = "高度な実装が必要なタスク"
          RequiredSpecialization = Development [ "F#"; "architecture" ]
          EstimatedDuration = TimeSpan.FromHours(10.0)
          Priority = TaskPriority.High
          Dependencies = [ "dep1"; "dep2"; "dep3" ] }

    // Act
    let difficulty = generator.AnalyzeImplementationDifficulty(complexTask)

    // Assert
    Assert.True(difficulty > 0.5, $"複雑なタスクの困難度 {difficulty} が低すぎます")
    Assert.True(difficulty <= 1.0, $"困難度 {difficulty} が上限を超えています")

[<Test>]
[<Category("Unit")>]
let ``AlternativeProposalGenerator - 代替案生成必要性判定テスト`` () =
    // Arrange
    let generator = AlternativeProposalGenerator()
    let highDifficulty = 0.8

    let lowQualityResult =
        { TaskId = "test"
          OverallScore = 0.4
          QualityLevel = QualityLevel.Poor
          Metrics = []
          Recommendations = []
          EvaluatedBy = "test"
          EvaluatedAt = DateTime.UtcNow
          PassesThreshold = false }

    // Act & Assert
    Assert.True(generator.NeedsAlternativeProposals(highDifficulty, None))
    Assert.True(generator.NeedsAlternativeProposals(0.5, Some lowQualityResult))
    Assert.False(generator.NeedsAlternativeProposals(0.5, None))

[<Test>]
[<Category("Unit")>]
let ``AlternativeProposalGenerator - 3案生成テスト`` () =
    // Arrange
    let generator = AlternativeProposalGenerator()

    let task =
        { TaskId = "proposal_test"
          Title = "提案テスト"
          Description = "代替案生成テスト用タスク"
          RequiredSpecialization = Development [ "F#" ]
          EstimatedDuration = TimeSpan.FromHours(4.0)
          Priority = TaskPriority.Medium
          Dependencies = [] }

    // Act
    let proposals = generator.GenerateAlternativeProposals(task)

    // Assert
    Assert.AreEqual(3, proposals.Length)

    // 簡略化案
    let simple = proposals.[0]
    Assert.AreEqual("簡略化", simple.Title)
    Assert.True(simple.EstimatedEffort < task.EstimatedDuration)
    Assert.True(simple.DifficultyScore < 0.5)
    Assert.True(simple.FeasibilityScore > 0.8)

    // 標準案
    let standard = proposals.[1]
    Assert.AreEqual("標準", standard.Title)
    Assert.AreEqual(task.EstimatedDuration, standard.EstimatedEffort)

    // 高機能案
    let advanced = proposals.[2]
    Assert.AreEqual("高機能", advanced.Title)
    Assert.True(advanced.EstimatedEffort > task.EstimatedDuration)
    Assert.True(advanced.DifficultyScore > 0.7)

[<Test>]
[<Category("Unit")>]
let ``AlternativeProposalGenerator - 最適代替案選択テスト`` () =
    // Arrange
    let generator = AlternativeProposalGenerator()

    let proposals =
        [ { ProposalId = "1"
            TaskId = "test"
            Title = "案1"
            Description = ""
            TechnicalApproach = ""
            EstimatedEffort = TimeSpan.FromHours(2.0)
            DifficultyScore = 0.3
            FeasibilityScore = 0.9
            GeneratedBy = "test"
            GeneratedAt = DateTime.UtcNow }
          { ProposalId = "2"
            TaskId = "test"
            Title = "案2"
            Description = ""
            TechnicalApproach = ""
            EstimatedEffort = TimeSpan.FromHours(6.0)
            DifficultyScore = 0.7
            FeasibilityScore = 0.6
            GeneratedBy = "test"
            GeneratedAt = DateTime.UtcNow } ]

    let constraints =
        Map.ofList [ ("time_pressure", 0.8); ("quality_requirement", 0.6) ]

    // Act
    let best = generator.SelectBestAlternative(proposals, constraints)

    // Assert
    Assert.AreEqual("案1", best.Title) // 時間制約が強い場合、簡略案が選択される

[<Test>]
[<Category("Integration")>]
let ``QualityGateManager - 包括的品質ゲート実行テスト`` () =
    // Arrange
    let evaluationEngine = QualityEvaluationEngine()
    let reviewer = UpstreamDownstreamReviewer()
    let proposalGenerator = AlternativeProposalGenerator()
    let manager = QualityGateManager(evaluationEngine, reviewer, proposalGenerator)

    let task =
        { TaskId = "integration_test"
          Title = "統合テストタスク"
          Description = "QualityGateManager統合テスト用"
          RequiredSpecialization = Development [ "F#" ]
          EstimatedDuration = TimeSpan.FromHours(8.0) // 高難易度でも代替案生成をトリガー
          Priority = TaskPriority.High
          Dependencies = [ "dep1"; "dep2" ] }

    // Act
    let result = manager.ExecuteQualityGate(task)

    // Assert
    match result with
    | Result.Ok(reviewResult, alternatives) ->
        Assert.AreEqual("integration_test", reviewResult.TaskId)
        Assert.True(reviewResult.Comments.Length >= 2)
        Assert.True(reviewResult.ConsensusScore >= 0.0)

        // 高難易度タスクなので代替案が生成される可能性が高い
        match alternatives with
        | Some alts -> Assert.AreEqual(3, alts.Length)
        | None -> () // 代替案が不要と判定された場合

    | Result.Error error -> Assert.True(false, $"品質ゲート実行がエラーになりました: {error}")

[<Test>]
[<Category("Unit")>]
let ``QualityGateManager - 品質レポート生成テスト`` () =
    // Arrange
    let evaluationEngine = QualityEvaluationEngine()
    let reviewer = UpstreamDownstreamReviewer()
    let proposalGenerator = AlternativeProposalGenerator()
    let manager = QualityGateManager(evaluationEngine, reviewer, proposalGenerator)

    let task =
        { TaskId = "report_test"
          Title = "レポートテスト"
          Description = "レポート生成テスト"
          RequiredSpecialization = Development [ "F#" ]
          EstimatedDuration = TimeSpan.FromHours(2.0)
          Priority = TaskPriority.Medium
          Dependencies = [] }

    let reviewResult =
        { TaskId = "report_test"
          Comments = []
          ConsensusScore = 0.75
          RequiredImprovements = [ "改善1"; "改善2" ]
          Approved = true
          ReviewedAt = DateTime.UtcNow }

    let alternatives =
        Some
            [ { ProposalId = "alt1"
                TaskId = "report_test"
                Title = "代替案1"
                Description = ""
                TechnicalApproach = ""
                EstimatedEffort = TimeSpan.FromHours(1.0)
                DifficultyScore = 0.3
                FeasibilityScore = 0.9
                GeneratedBy = "test"
                GeneratedAt = DateTime.UtcNow } ]

    // Act
    let report = manager.GenerateQualityReport(task, reviewResult, alternatives)

    // Assert
    Assert.IsTrue(report.Contains("レポートテスト"))
    Assert.IsTrue(report.Contains("0.75"))
    Assert.IsTrue(report.Contains("承認"))
    Assert.IsTrue(report.Contains("代替案: 1件"))

[<Test>]
[<Category("Performance")>]
let ``QualityGateManager - 大量タスク処理性能テスト`` () =
    // Arrange
    let evaluationEngine = QualityEvaluationEngine()
    let reviewer = UpstreamDownstreamReviewer()
    let proposalGenerator = AlternativeProposalGenerator()
    let manager = QualityGateManager(evaluationEngine, reviewer, proposalGenerator)

    let stopwatch = System.Diagnostics.Stopwatch.StartNew()

    // Act
    let results =
        [ 1..10 ]
        |> List.map (fun i ->
            let task =
                { TaskId = $"perf_test_{i}"
                  Title = $"性能テスト{i}"
                  Description = "性能テスト用タスク"
                  RequiredSpecialization = Development [ "F#" ]
                  EstimatedDuration = TimeSpan.FromHours(2.0)
                  Priority = TaskPriority.Medium
                  Dependencies = [] }

            manager.ExecuteQualityGate(task))

    stopwatch.Stop()

    // Assert
    Assert.AreEqual(10, results.Length)

    Assert.True(
        results
        |> List.forall (function
            | Result.Ok _ -> true
            | _ -> false)
    )

    Assert.True(
        stopwatch.ElapsedMilliseconds < 5000,
        $"10タスク処理に {stopwatch.ElapsedMilliseconds}ms かかりました（期待値: <5000ms）"
    )

[<Test>]
[<Category("Unit")>]
let ``QualityMetrics - 品質メトリクス構造検証`` () =
    // Arrange & Act
    let metric =
        { Dimension = CodeQuality
          Score = 85.0
          MaxScore = 100.0
          Details = "コード品質評価詳細"
          Timestamp = DateTime.UtcNow }

    // Assert
    Assert.AreEqual(CodeQuality, metric.Dimension)
    Assert.AreEqual(85.0, metric.Score)
    Assert.AreEqual(100.0, metric.MaxScore)
    Assert.AreEqual("コード品質評価詳細", metric.Details)

[<Test>]
[<Category("Unit")>]
let ``QualityLevel - 品質レベル列挙型検証`` () =
    // Act & Assert
    Assert.AreEqual(5, int QualityLevel.Excellent)
    Assert.AreEqual(4, int QualityLevel.Good)
    Assert.AreEqual(3, int QualityLevel.Acceptable)
    Assert.AreEqual(2, int QualityLevel.Poor)
    Assert.AreEqual(1, int QualityLevel.Unacceptable)

[<Test>]
[<Category("Unit")>]
let ``ReviewComment - レビューコメント構造検証`` () =
    // Arrange & Act
    let comment =
        { CommentId = "comment_123"
          ReviewerId = "pdm"
          TaskId = "task_456"
          Dimension = ProcessQuality
          Score = 0.8
          Comment = "良好なプロセス品質です"
          Suggestions = [ "提案1"; "提案2" ]
          Timestamp = DateTime.UtcNow
          Priority = TaskPriority.Medium }

    // Assert
    Assert.AreEqual("comment_123", comment.CommentId)
    Assert.AreEqual("pdm", comment.ReviewerId)
    Assert.AreEqual("task_456", comment.TaskId)
    Assert.AreEqual(ProcessQuality, comment.Dimension)
    Assert.AreEqual(0.8, comment.Score)
    Assert.AreEqual(2, comment.Suggestions.Length)

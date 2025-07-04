module FCode.Tests.CompletionAssessmentTests

open System
open Xunit
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.CompletionAssessmentManager

// t_wada TDD: Red - まずは失敗するテストを書く

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``CompletionAssessmentManager - 基本的なタスク完成度評価テスト`` () =
    // Arrange: テストデータ準備
    let now = DateTime.UtcNow

    let tasks =
        [ { TaskId = "task1"
            Title = "Task 1"
            Description = ""
            Status = TaskStatus.Completed
            AssignedAgent = None
            Priority = TaskPriority.High
            EstimatedDuration = None
            ActualDuration = None
            RequiredResources = []
            CreatedAt = now
            UpdatedAt = now }
          { TaskId = "task2"
            Title = "Task 2"
            Description = ""
            Status = TaskStatus.InProgress
            AssignedAgent = None
            Priority = TaskPriority.Medium
            EstimatedDuration = None
            ActualDuration = None
            RequiredResources = []
            CreatedAt = now
            UpdatedAt = now }
          { TaskId = "task3"
            Title = "Task 3"
            Description = ""
            Status = TaskStatus.Failed
            AssignedAgent = None
            Priority = TaskPriority.Low
            EstimatedDuration = None
            ActualDuration = None
            RequiredResources = []
            CreatedAt = now
            UpdatedAt = now } ]

    let acceptanceCriteria = [ "ユニットテスト95%カバレッジ"; "性能要件満たす"; "セキュリティチェック完了" ]

    let manager = new CompletionAssessmentManager()

    // Act: 完成度評価実行
    let assessment = manager.EvaluateCompletion(tasks, acceptanceCriteria)

    // Assert: 期待値検証
    Assert.Equal(1, assessment.TasksCompleted) // 1完了
    Assert.Equal(1, assessment.TasksInProgress) // 1進行中
    Assert.Equal(1, assessment.TasksBlocked) // 1ブロック
    Assert.Equal(0.33, assessment.OverallCompletionRate, 2) // 33%完了
    Assert.Equal(0.90, assessment.QualityScore, 2) // High優先度品質スコア
    Assert.False(assessment.AcceptanceCriteriaMet) // 受け入れ基準未達
    Assert.True(assessment.RequiresPOApproval) // PO承認必要

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``CompletionAssessmentManager - 高品質完成ケースでの評価テスト`` () =
    // Arrange: 高品質完成シナリオ
    let now = DateTime.UtcNow

    let tasks =
        [ { TaskId = "task1"
            Title = "Task 1"
            Description = ""
            Status = TaskStatus.Completed
            AssignedAgent = None
            Priority = TaskPriority.Critical
            EstimatedDuration = None
            ActualDuration = None
            RequiredResources = []
            CreatedAt = now
            UpdatedAt = now }
          { TaskId = "task2"
            Title = "Task 2"
            Description = ""
            Status = TaskStatus.Completed
            AssignedAgent = None
            Priority = TaskPriority.Critical
            EstimatedDuration = None
            ActualDuration = None
            RequiredResources = []
            CreatedAt = now
            UpdatedAt = now }
          { TaskId = "task3"
            Title = "Task 3"
            Description = ""
            Status = TaskStatus.Completed
            AssignedAgent = None
            Priority = TaskPriority.Critical
            EstimatedDuration = None
            ActualDuration = None
            RequiredResources = []
            CreatedAt = now
            UpdatedAt = now } ]

    let acceptanceCriteria = [ "ユニットテスト95%カバレッジ"; "性能要件満たす" ]

    let manager = new CompletionAssessmentManager()

    // Act: 完成度評価実行
    let assessment = manager.EvaluateCompletion(tasks, acceptanceCriteria)

    // Assert: 高品質完成の検証
    Assert.Equal(3, assessment.TasksCompleted) // 全完了
    Assert.Equal(0, assessment.TasksInProgress) // 進行中なし
    Assert.Equal(0, assessment.TasksBlocked) // ブロックなし
    Assert.Equal(1.0, assessment.OverallCompletionRate, 2) // 100%完了
    Assert.Equal(0.95, assessment.QualityScore, 2) // Critical優先度品質スコア
    Assert.True(assessment.AcceptanceCriteriaMet) // 受け入れ基準達成
    Assert.False(assessment.RequiresPOApproval) // PO承認不要

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``CompletionAssessmentManager - エッジケース処理テスト`` () =
    // Arrange: エッジケース（タスクなし）
    let emptyTasks = []
    let acceptanceCriteria = [ "基本要件" ]
    let manager = new CompletionAssessmentManager()

    // Act: 空タスクでの評価
    let assessment = manager.EvaluateCompletion(emptyTasks, acceptanceCriteria)

    // Assert: エッジケース動作検証
    Assert.Equal(0, assessment.TasksCompleted)
    Assert.Equal(0, assessment.TasksInProgress)
    Assert.Equal(0, assessment.TasksBlocked)
    Assert.Equal(0.0, assessment.OverallCompletionRate)
    Assert.Equal(0.0, assessment.QualityScore)
    Assert.False(assessment.AcceptanceCriteriaMet)
    Assert.True(assessment.RequiresPOApproval) // 空でもPO確認必要

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``CompletionAssessmentManager - 品質閾値境界値テスト`` () =
    // Arrange: 境界値テスト（品質閾値0.8）
    let now = DateTime.UtcNow

    let tasks =
        [ { TaskId = "task1"
            Title = "Task 1"
            Description = ""
            Status = TaskStatus.Completed
            AssignedAgent = None
            Priority = TaskPriority.High
            EstimatedDuration = None
            ActualDuration = None
            RequiredResources = []
            CreatedAt = now
            UpdatedAt = now } // High=0.90
          { TaskId = "task2"
            Title = "Task 2"
            Description = ""
            Status = TaskStatus.Completed
            AssignedAgent = None
            Priority = TaskPriority.Medium
            EstimatedDuration = None
            ActualDuration = None
            RequiredResources = []
            CreatedAt = now
            UpdatedAt = now } ] // Medium=0.75

    let acceptanceCriteria = [ "品質基準達成" ]
    let manager = new CompletionAssessmentManager()

    // Act: 境界値での評価
    let assessment = manager.EvaluateCompletion(tasks, acceptanceCriteria)

    // Assert: 境界値動作検証
    Assert.Equal(2, assessment.TasksCompleted)
    Assert.Equal(1.0, assessment.OverallCompletionRate)
    Assert.Equal(0.825, assessment.QualityScore, 3) // 平均品質 (0.90+0.75)/2
    Assert.True(assessment.AcceptanceCriteriaMet) // 品質基準達成
    Assert.False(assessment.RequiresPOApproval) // PO承認不要（品質基準達成済み）

// [<Fact>]
// [<Trait("TestCategory", "Integration")>]
// let ``CompletionAssessmentManager - リアルタイム評価統合テスト`` () =
//     // 統合テストは後で実装

// 既存のCollaborationTypes.fsの型定義を使用

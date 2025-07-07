module FCode.Tests.AutoContinuationEngineTests

open System
open NUnit.Framework
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.AutoContinuationEngineManager

[<Test>]
[<Category("Unit")>]
let ``AutoContinuationEngine - 自動継続判定テスト`` () =
    // Arrange
    let assessment =
        { TasksCompleted = 10
          TasksInProgress = 0
          TasksBlocked = 0
          OverallCompletionRate = 1.0
          QualityScore = 0.95
          AcceptanceCriteriaMet = true
          RequiresPOApproval = false }

    let manager = new AutoContinuationEngineManager()

    // Act
    let decision = manager.DecideContinuation(assessment)

    // Assert
    match decision with
    | AutoContinue reason -> Assert.AreEqual(reason, "高品質完成")
    | _ -> Assert.True(false, "Should be AutoContinue")

[<Test>]
[<Category("Unit")>]
let ``AutoContinuationEngine - PO承認要求判定テスト`` () =
    // Arrange
    let assessment =
        { TasksCompleted = 5
          TasksInProgress = 2
          TasksBlocked = 1
          OverallCompletionRate = 0.6
          QualityScore = 0.70
          AcceptanceCriteriaMet = false
          RequiresPOApproval = true }

    let manager = new AutoContinuationEngineManager()

    // Act
    let decision = manager.DecideContinuation(assessment)

    // Assert
    match decision with
    | RequirePOApproval reason -> Assert.AreEqual(reason, "品質")
    | _ -> Assert.True(false, "Should be RequirePOApproval")

[<Test>]
[<Category("Unit")>]
let ``AutoContinuationEngine - 低品質シナリオテスト`` () =
    // Arrange
    let lowQualityAssessment =
        { TasksCompleted = 3
          TasksInProgress = 4
          TasksBlocked = 1
          OverallCompletionRate = 0.4 // 40%完成度
          QualityScore = 0.3 // 低品質スコア
          AcceptanceCriteriaMet = false
          RequiresPOApproval = true }

    let manager = new AutoContinuationEngineManager()

    // Act
    let decision = manager.DecideContinuation(lowQualityAssessment)

    // Assert
    match decision with
    | RequirePOApproval reason -> Assert.AreEqual(reason, "品質")
    | StopExecution reason -> Assert.AreEqual(reason, "品質")
    | _ -> Assert.True(false, "Should be RequirePOApproval or StopExecution for low quality")

[<Test>]
[<Category("Unit")>]
let ``AutoContinuationEngine - ブロックタスク多数シナリオテスト`` () =
    // Arrange
    let blockedTasksAssessment =
        { TasksCompleted = 2
          TasksInProgress = 3
          TasksBlocked = 5 // 完了タスクより多いブロック
          OverallCompletionRate = 0.3
          QualityScore = 0.7
          AcceptanceCriteriaMet = false
          RequiresPOApproval = true }

    let manager = new AutoContinuationEngineManager()

    // Act
    let decision = manager.DecideContinuation(blockedTasksAssessment)

    // Assert
    match decision with
    | RequirePOApproval reason -> Assert.AreEqual(reason, "品質")
    | _ -> Assert.True(false, "Should be RequirePOApproval for blocked tasks scenario")

[<Test>]
[<Category("Unit")>]
let ``AutoContinuationEngine - 重大遅延シナリオテスト`` () =
    // Arrange
    let severeDelayAssessment =
        { TasksCompleted = 1
          TasksInProgress = 2
          TasksBlocked = 2
          OverallCompletionRate = 0.15 // 15%完成度（重大遅延）
          QualityScore = 0.8
          AcceptanceCriteriaMet = false
          RequiresPOApproval = true }

    let manager = new AutoContinuationEngineManager()

    // Act
    let decision = manager.DecideContinuation(severeDelayAssessment)

    // Assert
    match decision with
    | RequirePOApproval reason -> Assert.AreEqual(reason, "品質")
    | _ -> Assert.True(false, "Should be RequirePOApproval for severe delay scenario")

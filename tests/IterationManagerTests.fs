module FCode.Tests.IterationManagerTests

open System
open NUnit.Framework
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IterationManagerCore

[<Test>]
[<Category("Unit")>]
let ``IterationManager - 反復開発管理テスト`` () =
    // Arrange
    let iterationPlan =
        { IterationId = "ITER-001"
          Phases = [ "分析"; "設計"; "実装"; "テスト" ]
          CurrentPhase = "実装"
          CompletionRate = 0.60 }

    let manager = new IterationManagerCore()

    // Act
    let nextPhase = manager.AdvanceToNextPhase(iterationPlan)

    // Assert
    Assert.AreEqual(nextPhase.CurrentPhase, "テスト")
    Assert.True(nextPhase.CompletionRate > iterationPlan.CompletionRate)

[<Test>]
[<Category("Unit")>]
let ``IterationManager - 段階的完成管理テスト`` () =
    // Arrange
    let iterationPlan =
        { IterationId = "ITER-002"
          Phases = [ "Phase1"; "Phase2"; "Phase3" ]
          CurrentPhase = "Phase3"
          CompletionRate = 0.95 }

    let manager = new IterationManagerCore()

    // Act
    let result = manager.CheckIterationCompletion(iterationPlan)

    // Assert
    Assert.True(result.IsComplete)
    Assert.AreEqual(result.FinalCompletionRate, 1.0)

// テスト用反復計画型
type IterationPlan =
    { IterationId: string
      Phases: string list
      CurrentPhase: string
      CompletionRate: float }

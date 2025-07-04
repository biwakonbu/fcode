module FCode.Tests.IterationManagerTests

open System
open Xunit
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IterationManagerCore

[<Fact>]
[<Trait("TestCategory", "Unit")>]
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
    Assert.Equal("テスト", nextPhase.CurrentPhase)
    Assert.True(nextPhase.CompletionRate > iterationPlan.CompletionRate)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
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
    Assert.Equal(1.0, result.FinalCompletionRate)

// テスト用反復計画型
type IterationPlan =
    { IterationId: string
      Phases: string list
      CurrentPhase: string
      CompletionRate: float }

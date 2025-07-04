module FCode.Tests.POApprovalRequirementAnalyzerTests

open System
open Xunit
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.POApprovalRequirementAnalyzerManager

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``POApprovalRequirementAnalyzer - PO承認不要ケーステスト`` () =
    // Arrange
    let assessment =
        { TasksCompleted = 10
          TasksInProgress = 0
          TasksBlocked = 0
          OverallCompletionRate = 1.0
          QualityScore = 0.95
          AcceptanceCriteriaMet = true
          RequiresPOApproval = false }

    let manager = new POApprovalRequirementAnalyzerManager()

    // Act
    let result = manager.AnalyzePOApprovalRequirement(assessment)

    // Assert
    Assert.False(result.RequiresPOApproval)
    Assert.Equal("自動承認", result.RecommendedAction)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``POApprovalRequirementAnalyzer - PO承認必要ケーステスト`` () =
    // Arrange
    let assessment =
        { TasksCompleted = 5
          TasksInProgress = 2
          TasksBlocked = 3
          OverallCompletionRate = 0.5
          QualityScore = 0.65
          AcceptanceCriteriaMet = false
          RequiresPOApproval = true }

    let manager = new POApprovalRequirementAnalyzerManager()

    // Act
    let result = manager.AnalyzePOApprovalRequirement(assessment)

    // Assert
    Assert.True(result.RequiresPOApproval)
    Assert.Equal("PO承認要求", result.RecommendedAction)

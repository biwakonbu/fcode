module FCode.Tests.AutoContinuationEngineTests

open System
open Xunit
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.AutoContinuationEngineManager

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``AutoContinuationEngine - 自動継続判定テスト`` () =
    // Arrange
    let assessment = {
        TasksCompleted = 10
        TasksInProgress = 0
        TasksBlocked = 0
        OverallCompletionRate = 1.0
        QualityScore = 0.95
        AcceptanceCriteriaMet = true
        RequiresPOApproval = false
    }
    
    let manager = new AutoContinuationEngineManager()
    
    // Act
    let decision = manager.DecideContinuation(assessment)
    
    // Assert
    match decision with
    | AutoContinue reason -> Assert.Contains("高品質完成", reason)
    | _ -> Assert.True(false, "Should be AutoContinue")

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``AutoContinuationEngine - PO承認要求判定テスト`` () =
    // Arrange
    let assessment = {
        TasksCompleted = 5
        TasksInProgress = 2
        TasksBlocked = 1
        OverallCompletionRate = 0.6
        QualityScore = 0.70
        AcceptanceCriteriaMet = false
        RequiresPOApproval = true
    }
    
    let manager = new AutoContinuationEngineManager()
    
    // Act
    let decision = manager.DecideContinuation(assessment)
    
    // Assert
    match decision with
    | RequirePOApproval reason -> Assert.Contains("品質基準", reason)
    | _ -> Assert.True(false, "Should be RequirePOApproval")
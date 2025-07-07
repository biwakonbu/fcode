module FCode.Tests.CompletionCriteriaCheckerTests

open System
open NUnit.Framework
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.CompletionCriteriaCheckerManager

[<Test>]
[<Category("Unit")>]
let ``CompletionCriteriaCheckerManager - 基本的な受け入れ基準チェックテスト`` () =
    // Arrange
    let criteria = [ "ユニットテスト95%カバレッジ"; "性能要件満たす"; "セキュリティチェック完了" ]

    let completionData =
        { TestCoverage = 0.96
          PerformanceScore = 0.88
          SecurityCompliance = true
          CodeQuality = 0.90 }

    let manager = new CompletionCriteriaCheckerManager()

    // Act
    let result = manager.CheckCriteria(criteria, completionData)

    // Assert
    Assert.True(result.AllCriteriaMet)
    Assert.AreEqual(result.MetCriteria.Length, 3)
    Assert.IsEmpty(result.UnmetCriteria)

[<Test>]
[<Category("Unit")>]
let ``CompletionCriteriaCheckerManager - 基準未達成時のテスト`` () =
    // Arrange
    let criteria = [ "ユニットテスト95%カバレッジ"; "性能要件満たす"; "セキュリティチェック完了" ]

    let completionData =
        { TestCoverage = 0.85 // 基準未達
          PerformanceScore = 0.60 // 基準未達
          SecurityCompliance = false // 基準未達
          CodeQuality = 0.90 }

    let manager = new CompletionCriteriaCheckerManager()

    // Act
    let result = manager.CheckCriteria(criteria, completionData)

    // Assert
    Assert.False(result.AllCriteriaMet)
    Assert.IsEmpty(result.MetCriteria)
    Assert.AreEqual(result.UnmetCriteria.Length, 3)

// テスト用完成データ型
type CompletionData =
    { TestCoverage: float
      PerformanceScore: float
      SecurityCompliance: bool
      CodeQuality: float }

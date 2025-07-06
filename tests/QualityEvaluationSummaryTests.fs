module FCode.Tests.QualityEvaluationSummaryTests

open System
open NUnit.Framework
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.QualityEvaluationSummaryManager

// t_wada TDD: Red - まずは失敗するテストを書く

[<Test>]
[<Category("Unit")>]
let ``QualityEvaluationSummaryManager - 基本的な品質評価テスト`` () =
    // Arrange: 品質データ準備
    let qualityMetrics =
        { CodeCoverage = 0.95
          CodeComplexity = 2.3
          SecurityVulnerabilities = 0
          PerformanceScore = 0.88
          DocumentationScore = 0.92 }

    let testResults =
        [ "Unit tests: 142/142 passed"
          "Integration tests: 23/23 passed"
          "Performance tests: 8/8 passed" ]

    let codeReviewFindings = [ "コード構造良好"; "適切なエラーハンドリング"; "軽微: 変数名の改善余地" ]

    let manager = new QualityEvaluationSummaryManager()

    // Act: 品質評価実行
    let summary =
        manager.GenerateQualitySummary(qualityMetrics, testResults, codeReviewFindings)

    // Assert: 品質評価結果検証
    Assert.AreEqual(0.965, summary.CodeQuality, 0.01) // コード品質（0.95*0.4 + 0.95*0.3 + 1.0*0.3 = 0.965）
    Assert.AreEqual(0.95, summary.TestCoverage, 0.01) // テストカバレッジ
    Assert.AreEqual(0.92, summary.DocumentationScore, 0.01) // ドキュメント品質
    Assert.True(summary.SecurityCompliance) // セキュリティ準拠
    Assert.AreEqual(summary.PerformanceMetrics.Length, 1) // パフォーマンス指標
    Assert.AreEqual(summary.IssuesFound.Length, 1) // 発見課題数
    Assert.AreEqual(summary.RecommendedImprovements.Length, 1) // 改善提案数（高品質なので最低限のみ）

[<Test>]
[<Category("Unit")>]
let ``QualityEvaluationSummaryManager - 低品質ケースの評価テスト`` () =
    // Arrange: 低品質シナリオ
    let qualityMetrics =
        { CodeCoverage = 0.65
          CodeComplexity = 8.5
          SecurityVulnerabilities = 3
          PerformanceScore = 0.45
          DocumentationScore = 0.30 }

    let testResults =
        [ "Unit tests: 89/120 passed (31 failed)"
          "Integration tests: 12/20 passed (8 failed)" ]

    let codeReviewFindings =
        [ "重大: セキュリティ脆弱性発見"; "重大: パフォーマンス劣化"; "中程度: コード複雑度過多"; "軽微: ドキュメント不足" ]

    let manager = new QualityEvaluationSummaryManager()

    // Act: 低品質評価実行
    let summary =
        manager.GenerateQualitySummary(qualityMetrics, testResults, codeReviewFindings)

    // Assert: 低品質評価結果検証
    Assert.True(summary.CodeQuality < 0.7) // 低コード品質
    Assert.AreEqual(0.65, summary.TestCoverage, 0.01) // 低テストカバレッジ
    Assert.AreEqual(0.30, summary.DocumentationScore, 0.01) // 低ドキュメント品質
    Assert.False(summary.SecurityCompliance) // セキュリティ非準拠
    Assert.AreEqual(summary.PerformanceMetrics.Length, 1) // パフォーマンス指標
    Assert.AreEqual(summary.IssuesFound.Length, 4) // 多数の課題
    Assert.True(summary.RecommendedImprovements.Length >= 3) // 多数の改善提案

[<Test>]
[<Category("Unit")>]
let ``QualityEvaluationSummaryManager - 空データでのエラーハンドリングテスト`` () =
    // Arrange: 空データシナリオ
    let emptyQualityMetrics =
        { CodeCoverage = 0.0
          CodeComplexity = 0.0
          SecurityVulnerabilities = 0
          PerformanceScore = 0.0
          DocumentationScore = 0.0 }

    let emptyTestResults = []
    let emptyCodeReviewFindings = []
    let manager = new QualityEvaluationSummaryManager()

    // Act: 空データでの評価
    let summary =
        manager.GenerateQualitySummary(emptyQualityMetrics, emptyTestResults, emptyCodeReviewFindings)

    // Assert: 適切なデフォルト値
    Assert.AreEqual(summary.CodeQuality, 0.0)
    Assert.AreEqual(summary.TestCoverage, 0.0)
    Assert.AreEqual(summary.DocumentationScore, 0.0)
    Assert.False(summary.SecurityCompliance) // データ不足時は非準拠
    Assert.IsEmpty(summary.PerformanceMetrics)
    Assert.IsEmpty(summary.IssuesFound)
    Assert.True(summary.RecommendedImprovements.Length >= 1) // 最低限の改善提案

[<Test>]
[<Category("Unit")>]
let ``QualityEvaluationSummaryManager - 品質改善提案生成テスト`` () =
    // Arrange: 改善余地ありシナリオ
    let qualityMetrics =
        { CodeCoverage = 0.78
          CodeComplexity = 5.2
          SecurityVulnerabilities = 1
          PerformanceScore = 0.72
          DocumentationScore = 0.68 }

    let testResults =
        [ "Unit tests: 95/110 passed (15 failed)"
          "Performance tests: 6/10 passed (4 failed)" ]

    let codeReviewFindings = [ "中程度: コードレビュー指摘事項"; "軽微: 命名規約改善" ]

    let manager = new QualityEvaluationSummaryManager()

    // Act: 改善提案生成実行
    let summary =
        manager.GenerateQualitySummary(qualityMetrics, testResults, codeReviewFindings)

    // Assert: 改善提案内容検証
    Assert.True(summary.RecommendedImprovements.Length >= 3) // 複数改善提案
    Assert.True(summary.RecommendedImprovements |> List.contains "テストカバレッジ向上") // カバレッジ改善
    Assert.True(summary.RecommendedImprovements |> List.contains "セキュリティ対応") // セキュリティ改善
    Assert.True(summary.RecommendedImprovements |> List.contains "ドキュメント充実") // ドキュメント改善
    Assert.AreEqual(summary.IssuesFound.Length, 2) // 適切な課題抽出

// テスト用品質メトリクス型定義
type QualityMetrics =
    { CodeCoverage: float
      CodeComplexity: float
      SecurityVulnerabilities: int
      PerformanceScore: float
      DocumentationScore: float }

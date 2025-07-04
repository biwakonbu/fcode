module FCode.Collaboration.QualityEvaluationSummaryManager

open System
open FCode.Collaboration.CollaborationTypes

/// 品質評価サマリーマネージャー
type QualityEvaluationSummaryManager() =

    /// 品質評価サマリー生成
    member this.GenerateQualitySummary
        (metrics: QualityMetrics, testResults: string list, codeReviewFindings: string list)
        : QualityEvaluationSummary =
        // コード品質計算（複雑度、カバレッジ、セキュリティを総合）
        let codeQuality = this.CalculateCodeQuality(metrics)

        // テストカバレッジ
        let testCoverage = metrics.CodeCoverage

        // ドキュメントスコア
        let documentationScore = metrics.DocumentationScore

        // セキュリティ準拠判定（脆弱性0かつ最低限の品質）
        let securityCompliance = metrics.SecurityVulnerabilities = 0 && codeQuality >= 0.6

        // パフォーマンス指標
        let performanceMetrics =
            if metrics.PerformanceScore > 0.0 then
                [ ("OverallPerformance", metrics.PerformanceScore) ]
            else
                []

        // 課題抽出
        let issuesFound = this.ExtractIssues(codeReviewFindings)

        // 改善提案生成
        let recommendations =
            this.GenerateRecommendations(metrics, testResults, codeReviewFindings)

        { CodeQuality = codeQuality
          TestCoverage = testCoverage
          DocumentationScore = documentationScore
          SecurityCompliance = securityCompliance
          PerformanceMetrics = performanceMetrics
          IssuesFound = issuesFound
          RecommendedImprovements = recommendations }

    /// コード品質計算
    member private this.CalculateCodeQuality(metrics: QualityMetrics) : float =
        // 空データの場合は0を返す
        if
            metrics.CodeCoverage = 0.0
            && metrics.CodeComplexity = 0.0
            && metrics.SecurityVulnerabilities = 0
        then
            0.0
        else
            let coverageWeight = 0.4
            let complexityWeight = 0.3
            let securityWeight = 0.3

            // カバレッジスコア
            let coverageScore = metrics.CodeCoverage

            // 複雑度スコア（複雑度が低いほど高スコア）
            let complexityScore =
                if metrics.CodeComplexity <= 0.0 then
                    1.0
                elif metrics.CodeComplexity <= 2.5 then
                    0.95 // 高品質コード
                else
                    Math.Max(0.0, 1.0 - (metrics.CodeComplexity - 1.0) / 10.0)

            // セキュリティスコア
            let securityScore =
                if metrics.SecurityVulnerabilities = 0 then
                    1.0
                else
                    Math.Max(0.0, 1.0 - float metrics.SecurityVulnerabilities / 10.0)

            coverageScore * coverageWeight
            + complexityScore * complexityWeight
            + securityScore * securityWeight

    /// 課題抽出
    member private this.ExtractIssues(codeReviewFindings: string list) : string list =
        codeReviewFindings
        |> List.filter (fun finding -> finding.Contains("重大") || finding.Contains("中程度") || finding.Contains("軽微"))
        |> List.map (fun finding -> finding.Trim())

    /// 改善提案生成
    member private this.GenerateRecommendations
        (metrics: QualityMetrics, testResults: string list, codeReviewFindings: string list)
        : string list =
        let mutable recommendations = []

        // テストカバレッジ改善
        if metrics.CodeCoverage < 0.8 then
            recommendations <- "テストカバレッジ向上" :: recommendations

        // セキュリティ対応
        if metrics.SecurityVulnerabilities > 0 then
            recommendations <- "セキュリティ対応" :: recommendations

        // ドキュメント改善
        if metrics.DocumentationScore < 0.7 then
            recommendations <- "ドキュメント充実" :: recommendations

        // パフォーマンス改善
        if metrics.PerformanceScore < 0.7 then
            recommendations <- "パフォーマンス最適化" :: recommendations

        // 複雑度改善
        if metrics.CodeComplexity > 5.0 then
            recommendations <- "コード複雑度削減" :: recommendations

        // 最低限の改善提案
        if recommendations.IsEmpty then
            recommendations <- "継続的品質向上" :: recommendations

        recommendations |> List.rev

/// テスト用品質メトリクス型定義
and QualityMetrics =
    { CodeCoverage: float
      CodeComplexity: float
      SecurityVulnerabilities: int
      PerformanceScore: float
      DocumentationScore: float }

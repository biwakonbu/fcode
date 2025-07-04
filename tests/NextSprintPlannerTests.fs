module FCode.Tests.NextSprintPlannerTests

open System
open Xunit
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.NextSprintPlannerManager

// t_wada TDD: Red - まずは失敗するテストを書く

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``NextSprintPlannerManager - 基本的な次スプリント計画テスト`` () =
    // Arrange: 現スプリント状況
    let completionAssessment =
        { TasksCompleted = 8
          TasksInProgress = 2
          TasksBlocked = 1
          OverallCompletionRate = 0.8
          QualityScore = 0.85
          AcceptanceCriteriaMet = true
          RequiresPOApproval = false }

    let qualitySummary =
        { CodeQuality = 0.85
          TestCoverage = 0.90
          DocumentationScore = 0.75
          SecurityCompliance = true
          PerformanceMetrics = [ ("Response", 0.88) ]
          IssuesFound = [ "軽微: ドキュメント改善" ]
          RecommendedImprovements = [ "継続的改善" ] }

    let availableResources = [ "dev1"; "dev2"; "qa1" ]
    let manager = new NextSprintPlannerManager()

    // Act: 次スプリント計画生成
    let sprintPlan =
        manager.PlanNextSprint(completionAssessment, qualitySummary, availableResources)

    // Assert: 計画内容検証
    Assert.NotNull(sprintPlan)
    Assert.NotEmpty(sprintPlan.SprintId)
    Assert.Equal(VirtualDay 3, sprintPlan.Duration) // 標準3日スプリント
    Assert.True(sprintPlan.PriorityTasks.Length >= 1) // 優先タスクあり
    Assert.Equal(3, sprintPlan.ResourceAllocation.Length) // リソース配分
    Assert.True(sprintPlan.Dependencies.Length >= 0) // 依存関係
    Assert.True(sprintPlan.RiskMitigation.Length >= 1) // リスク軽減策

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``NextSprintPlannerManager - 高品質完成時の計画テスト`` () =
    // Arrange: 高品質完成シナリオ
    let completionAssessment =
        { TasksCompleted = 10
          TasksInProgress = 0
          TasksBlocked = 0
          OverallCompletionRate = 1.0
          QualityScore = 0.95
          AcceptanceCriteriaMet = true
          RequiresPOApproval = false }

    let qualitySummary =
        { CodeQuality = 0.95
          TestCoverage = 0.98
          DocumentationScore = 0.92
          SecurityCompliance = true
          PerformanceMetrics = [ ("Response", 0.95); ("Throughput", 0.90) ]
          IssuesFound = []
          RecommendedImprovements = [ "継続的品質向上" ] }

    let availableResources = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2" ]
    let manager = new NextSprintPlannerManager()

    // Act: 高品質完成時の計画
    let sprintPlan =
        manager.PlanNextSprint(completionAssessment, qualitySummary, availableResources)

    // Assert: 新機能重視の計画
    Assert.Contains("新機能開発", sprintPlan.PriorityTasks)
    Assert.Contains("品質維持", sprintPlan.PriorityTasks)
    Assert.Equal(5, sprintPlan.ResourceAllocation.Length) // 全リソース活用
    Assert.True(sprintPlan.RiskMitigation.Length >= 1)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``NextSprintPlannerManager - 品質課題時の計画テスト`` () =
    // Arrange: 品質課題ありシナリオ
    let completionAssessment =
        { TasksCompleted = 5
          TasksInProgress = 3
          TasksBlocked = 2
          OverallCompletionRate = 0.5
          QualityScore = 0.65
          AcceptanceCriteriaMet = false
          RequiresPOApproval = true }

    let qualitySummary =
        { CodeQuality = 0.65
          TestCoverage = 0.70
          DocumentationScore = 0.45
          SecurityCompliance = false
          PerformanceMetrics = [ ("Response", 0.60) ]
          IssuesFound = [ "重大: セキュリティ課題"; "中程度: パフォーマンス劣化" ]
          RecommendedImprovements = [ "セキュリティ強化"; "テスト強化"; "ドキュメント改善" ] }

    let availableResources = [ "dev1"; "qa1" ]
    let manager = new NextSprintPlannerManager()

    // Act: 品質課題時の計画
    let sprintPlan =
        manager.PlanNextSprint(completionAssessment, qualitySummary, availableResources)

    // Assert: 品質改善重視の計画
    Assert.Contains("品質改善", sprintPlan.PriorityTasks)
    Assert.Contains("セキュリティ強化", sprintPlan.PriorityTasks)
    Assert.Contains("テスト強化", sprintPlan.PriorityTasks)
    Assert.True(sprintPlan.RiskMitigation.Length >= 2) // 多くのリスク軽減策

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``NextSprintPlannerManager - リソース制約時の計画テスト`` () =
    // Arrange: リソース制約シナリオ
    let completionAssessment =
        { TasksCompleted = 6
          TasksInProgress = 2
          TasksBlocked = 1
          OverallCompletionRate = 0.75
          QualityScore = 0.80
          AcceptanceCriteriaMet = true
          RequiresPOApproval = false }

    let qualitySummary =
        { CodeQuality = 0.80
          TestCoverage = 0.85
          DocumentationScore = 0.70
          SecurityCompliance = true
          PerformanceMetrics = [ ("Response", 0.75) ]
          IssuesFound = [ "軽微: ドキュメント不足" ]
          RecommendedImprovements = [ "効率化" ] }

    let limitedResources = [ "dev1" ] // 限定リソース
    let manager = new NextSprintPlannerManager()

    // Act: リソース制約時の計画
    let sprintPlan =
        manager.PlanNextSprint(completionAssessment, qualitySummary, limitedResources)

    // Assert: 効率重視の計画
    Assert.Equal(1, sprintPlan.ResourceAllocation.Length) // 限定リソース反映
    Assert.Contains("効率化", sprintPlan.PriorityTasks)
    Assert.True(sprintPlan.RiskMitigation.Length >= 1)
    Assert.Contains("リソース制約", sprintPlan.RiskMitigation)

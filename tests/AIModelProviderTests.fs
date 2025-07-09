namespace FCode.Tests

open NUnit.Framework
open System
open FCode.AIModelProvider
open FCode.AgentCLI

[<TestFixture>]
[<Category("Unit")>]
type AIModelProviderTests() =

    [<Test>]
    [<Category("Unit")>]
    member _.``ModelSelectionEngineモデル適合度計算テスト``() =
        let engine = ModelSelectionEngine()

        let claude3Sonnet =
            { ModelType = Claude3Sonnet
              MaxTokens = 200000
              CostPerInputToken = 0.003
              CostPerOutputToken = 0.015
              ResponseSpeed = Medium
              CodeGeneration = 0.9
              Reasoning = 0.95
              CreativeWriting = 0.85
              TechnicalAccuracy = 0.9
              ContextRetention = 0.95
              MultilingualSupport = 0.8 }

        let codeTask =
            { RequiredCapabilities = [ "CodeGeneration" ]
              CodeGeneration = true
              ComplexReasoning = false
              CreativeElements = false
              TechnicalDepth = 0.8
              ExpectedTokens = 5000
              Urgency = Normal }

        let reasoningTask =
            { RequiredCapabilities = [ "ArchitectureDesign" ]
              CodeGeneration = false
              ComplexReasoning = true
              CreativeElements = false
              TechnicalDepth = 0.9
              ExpectedTokens = 8000
              Urgency = Normal }

        let codeFitness = engine.CalculateModelFitness claude3Sonnet codeTask
        let reasoningFitness = engine.CalculateModelFitness claude3Sonnet reasoningTask

        // Claude 3 Sonnetはコード生成・推論両方に優れているため、両方で高スコア期待
        // 重み正規化により適合度が調整されたため、期待値を更新
        Assert.Greater(codeFitness, 0.7)
        Assert.Greater(reasoningFitness, 0.65) // 正規化により0.674程度になる

    [<Test>]
    [<Category("Unit")>]
    member _.``ModelSelectionEngineコスト効率計算テスト``() =
        let engine = ModelSelectionEngine()

        let expensiveModel =
            { ModelType = Claude3Opus
              MaxTokens = 200000
              CostPerInputToken = 0.015
              CostPerOutputToken = 0.075
              ResponseSpeed = Slow
              CodeGeneration = 0.95
              Reasoning = 0.98
              CreativeWriting = 0.9
              TechnicalAccuracy = 0.95
              ContextRetention = 0.98
              MultilingualSupport = 0.85 }

        let cheapModel =
            { ModelType = Claude3Haiku
              MaxTokens = 200000
              CostPerInputToken = 0.00025
              CostPerOutputToken = 0.00125
              ResponseSpeed = VeryFast
              CodeGeneration = 0.7
              Reasoning = 0.75
              CreativeWriting = 0.7
              TechnicalAccuracy = 0.75
              ContextRetention = 0.8
              MultilingualSupport = 0.7 }

        let smallTask =
            { RequiredCapabilities = [ "Documentation" ]
              CodeGeneration = false
              ComplexReasoning = false
              CreativeElements = true
              TechnicalDepth = 0.3
              ExpectedTokens = 2000
              Urgency = Normal }

        let expensiveEfficiency = engine.CalculateCostEfficiency expensiveModel smallTask
        let cheapEfficiency = engine.CalculateCostEfficiency cheapModel smallTask

        // 小さなタスクでは安価なモデルの方がコスト効率が良い
        Assert.Greater(cheapEfficiency, expensiveEfficiency)

    [<Test>]
    [<Category("Unit")>]
    member _.``ModelSelectionEngine最適モデル選択テスト``() =
        let engine = ModelSelectionEngine()

        // 高速応答が必要なタスク
        let urgentTask =
            { RequiredCapabilities = [ "Testing" ]
              CodeGeneration = true
              ComplexReasoning = false
              CreativeElements = false
              TechnicalDepth = 0.6
              ExpectedTokens = 3000
              Urgency = Immediate }

        // 複雑な推論が必要なタスク
        let complexTask =
            { RequiredCapabilities = [ "ArchitectureDesign" ]
              CodeGeneration = false
              ComplexReasoning = true
              CreativeElements = false
              TechnicalDepth = 0.9
              ExpectedTokens = 15000
              Urgency = LowPriority }

        let urgentResult = engine.SelectOptimalModel urgentTask
        let complexResult = engine.SelectOptimalModel complexTask

        Assert.IsTrue(urgentResult.IsSome)
        Assert.IsTrue(complexResult.IsSome)

        // 緊急タスクでは高速モデル（Haiku等）が選ばれる可能性が高い
        // 複雑タスクでは高性能モデル（Opus等）が選ばれる可能性が高い
        Assert.IsNotEmpty(urgentResult.Value.SelectionReason)
        Assert.IsNotEmpty(complexResult.Value.SelectionReason)
        Assert.Greater(urgentResult.Value.Confidence, 0.0)
        Assert.Greater(complexResult.Value.Confidence, 0.0)

    [<Test>]
    [<Category("Unit")>]
    member _.``MultiModelManagerタスク特性自動分析テスト``() =
        let manager = MultiModelManager()

        let codeDescription = "F#でAPIエンドポイントを実装してください。認証とエラーハンドリングを含む"
        let codeCapabilities = [ CodeGeneration; Testing ]

        let designDescription = "マイクロサービスアーキテクチャの設計と最適化戦略を分析"
        let designCapabilities = [ ArchitectureDesign; Documentation ]

        let uxDescription = "ユーザーインターフェースの創造的なアイデア提案"
        let uxCapabilities = [ UserExperience; Documentation ]

        let codeTask = manager.AnalyzeTaskCharacteristics codeDescription codeCapabilities

        let designTask =
            manager.AnalyzeTaskCharacteristics designDescription designCapabilities

        let uxTask = manager.AnalyzeTaskCharacteristics uxDescription uxCapabilities

        // コードタスクはCodeGeneration = true
        Assert.IsTrue(codeTask.CodeGeneration)
        Assert.Greater(codeTask.TechnicalDepth, 0.5)

        // 設計タスクはComplexReasoning = true
        Assert.IsTrue(designTask.ComplexReasoning)
        Assert.Greater(designTask.TechnicalDepth, 0.8)

        // UXタスクはCreativeElements = true
        Assert.IsTrue(uxTask.CreativeElements)

    [<Test>]
    [<Category("Unit")>]
    member _.``MultiModelManagerモデル推奨テスト``() =
        let manager = MultiModelManager()

        let taskDescription = "Dockerコンテナ設定の最適化とセキュリティ強化"
        let capabilities = [ ArchitectureDesign; CodeGeneration ]
        let urgency = TaskUrgency.Urgent

        match manager.RecommendModel taskDescription capabilities urgency with
        | Some result ->
            Assert.IsNotNull(result.SelectedModel)
            Assert.IsNotEmpty(result.SelectionReason)
            Assert.Greater(result.EstimatedCost, 0.0)
            Assert.Greater(result.EstimatedTime.TotalSeconds, 0.0)
            Assert.IsNotEmpty(result.AlternativeModels)
        | None -> Assert.Fail("モデル推奨が失敗しました")

    [<Test>]
    [<Category("Unit")>]
    member _.``MultiModelManager使用統計更新テスト``() =
        let manager = MultiModelManager()

        let responseTime = TimeSpan.FromSeconds(5.0)
        let cost = 0.05

        // 成功と失敗のケースで統計更新
        manager.UpdateUsageStats Claude3Sonnet responseTime cost true
        manager.UpdateUsageStats Claude3Sonnet responseTime (cost * 2.0) false
        manager.UpdateUsageStats GPT4Turbo (TimeSpan.FromSeconds(8.0)) (cost * 1.5) true

        let report = manager.GenerateUsageReport()

        Assert.IsNotEmpty(report)
        Assert.IsTrue(report.Contains("AIモデル使用統計レポート"))
        Assert.IsTrue(report.Contains("Claude3Sonnet"))
        Assert.IsTrue(report.Contains("GPT4Turbo"))
        Assert.IsTrue(report.Contains("成功率"))

[<TestFixture>]
[<Category("Performance")>]
type AIModelProviderPerformanceTests() =

    [<Test>]
    [<Category("Performance")>]
    member _.``ModelSelectionEngine大量モデル選択性能テスト``() =
        let engine = ModelSelectionEngine()

        let tasks =
            [ for i in 1..100 ->
                  { RequiredCapabilities = [ "CodeGeneration"; "Testing" ]
                    CodeGeneration = i % 2 = 0
                    ComplexReasoning = i % 3 = 0
                    CreativeElements = i % 5 = 0
                    TechnicalDepth = float (i % 10) / 10.0
                    ExpectedTokens = 1000 + (i * 100)
                    Urgency =
                      match i % 4 with
                      | 0 -> Immediate
                      | 1 -> Urgent
                      | 2 -> Normal
                      | _ -> LowPriority } ]

        let startTime = DateTime.Now

        let results = tasks |> List.map engine.SelectOptimalModel |> List.choose id

        let endTime = DateTime.Now
        let duration = endTime - startTime

        // 100タスクの処理が5秒以内に完了することを確認
        Assert.Less(duration.TotalSeconds, 5.0)
        Assert.AreEqual(100, results.Length)

        // 全ての結果が有効な選択であることを確認
        results
        |> List.iter (fun result ->
            Assert.Greater(result.Confidence, 0.0)
            Assert.Greater(result.EstimatedCost, 0.0)
            Assert.IsNotEmpty(result.SelectionReason))

[<TestFixture>]
[<Category("Integration")>]
type AIModelProviderIntegrationTests() =

    [<Test>]
    [<Category("Integration")>]
    member _.``AIモデル統合フローテスト``() =
        let manager = MultiModelManager()

        // 複数の異なるタスクタイプでのフルフロー
        let testCases =
            [ ("緊急バグ修正", [ CodeGeneration; Debugging ], Immediate)
              ("新機能アーキテクチャ設計", [ ArchitectureDesign; Documentation ], Normal)
              ("E2Eテスト実装", [ Testing; QualityAssurance ], Urgent)
              ("UX改善提案", [ UserExperience ], LowPriority)
              ("セキュリティ監査", [ CodeReview; QualityAssurance ], TaskUrgency.Urgent) ]

        let results =
            testCases
            |> List.map (fun (description, capabilities, urgency) ->
                let recommendation = manager.RecommendModel description capabilities urgency
                (description, recommendation))

        // 全てのタスクで適切なモデルが推奨されることを確認
        results
        |> List.iter (fun (description, recommendation) ->
            Assert.IsTrue(recommendation.IsSome, $"推奨失敗: {description}")

            let result = recommendation.Value
            Assert.IsNotNull(result.SelectedModel)
            Assert.Greater(result.Confidence, 0.3, $"低信頼度: {description}")

            // 使用統計を更新（シミュレーション）
            manager.UpdateUsageStats result.SelectedModel result.EstimatedTime result.EstimatedCost true)

        // 統合レポート生成
        let finalReport = manager.GenerateUsageReport()
        Assert.IsNotEmpty(finalReport)

        // レポートに複数のモデルが含まれていることを確認
        Assert.IsTrue(finalReport.Contains("モデル別統計"))
        Assert.Greater(finalReport.Length, 100)

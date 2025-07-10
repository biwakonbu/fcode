namespace FCode.Tests

open NUnit.Framework
open System
open FCode.SpecialistAgentManager
open FCode.AgentCLI

[<TestFixture>]
[<Category("Unit")>]
type SpecialistAgentManagerTests() =

    let createMockAgentCLI name =
        { new IAgentCLI with
            member _.Name = name
            member _.StartCommand(_) = System.Diagnostics.ProcessStartInfo()

            member _.ParseOutput(_) =
                { Status = Success
                  Content = "Mock output"
                  Metadata = Map.empty
                  Timestamp = DateTime.Now
                  SourceAgent = name
                  Capabilities = [ CodeGeneration ] }

            member _.SupportedCapabilities = [ CodeGeneration; Testing ]

            member _.Config =
                { Name = name
                  CliPath = "mock"
                  DefaultArgs = []
                  OutputFormat = "text"
                  Timeout = TimeSpan.FromMinutes(1.0)
                  MaxRetries = 1
                  SupportedCapabilities = [ CodeGeneration; Testing ]
                  EnvironmentVariables = Map.empty } }

    [<Test>]
    [<Category("Unit")>]
    member _.``SpecialistMatchingEngine専門性スコア計算テスト``() =
        let engine = SpecialistMatchingEngine()

        let databaseAgent =
            { Name = "DB Expert"
              SpecializedCapabilities = [ DatabaseDesign; QueryOptimization ]
              GeneralCapabilities = [ CodeGeneration ]
              CliIntegration = createMockAgentCLI "DB Expert"
              ExpertiseLevel = Senior
              CostPerHour = 120.0
              AvailabilityStatus = Available }

        let requirement =
            { TaskDescription = "データベーススキーマ設計"
              RequiredCapabilities = [ DatabaseDesign ]
              MinExpertiseLevel = Mid
              EstimatedDuration = TimeSpan.FromHours(2.0)
              Priority = High
              Budget = Some 300.0 }

        let score = engine.CalculateExpertiseScore databaseAgent requirement

        // DatabaseDesignを持つSeniorエージェントなので高スコア期待
        Assert.Greater(score, 0.8)
        Assert.LessOrEqual(score, 1.0)

    [<Test>]
    [<Category("Unit")>]
    member _.``SpecialistMatchingEngineコスト効率計算テスト``() =
        let engine = SpecialistMatchingEngine()

        let expensiveAgent =
            { Name = "Expensive Expert"
              SpecializedCapabilities = [ APIDesign ]
              GeneralCapabilities = [ CodeGeneration ]
              CliIntegration = createMockAgentCLI "Expensive Expert"
              ExpertiseLevel = Expert
              CostPerHour = 200.0
              AvailabilityStatus = Available }

        let budgetRequirement =
            { TaskDescription = "API設計"
              RequiredCapabilities = [ APIDesign ]
              MinExpertiseLevel = Senior
              EstimatedDuration = TimeSpan.FromHours(1.0)
              Priority = Medium
              Budget = Some 150.0 }

        let overBudgetRequirement =
            { budgetRequirement with
                Budget = Some 100.0 }

        let withinBudgetScore =
            engine.CalculateCostEfficiency expensiveAgent budgetRequirement

        let overBudgetScore =
            engine.CalculateCostEfficiency expensiveAgent overBudgetRequirement

        // 予算内の場合は高スコア、予算超過の場合は低スコア
        Assert.Greater(withinBudgetScore, overBudgetScore)

    [<Test>]
    [<Category("Unit")>]
    member _.``SpecialistMatchingEngine利用可能性スコア計算テスト``() =
        let engine = SpecialistMatchingEngine()

        let availableAgent =
            { Name = "Available Agent"
              SpecializedCapabilities = [ TestFrameworkSetup ]
              GeneralCapabilities = [ Testing ]
              CliIntegration = createMockAgentCLI "Available Agent"
              ExpertiseLevel = Mid
              CostPerHour = 80.0
              AvailabilityStatus = Available }

        let busyAgent =
            { availableAgent with
                AvailabilityStatus = Busy }

        let offlineAgent =
            { availableAgent with
                AvailabilityStatus = Offline }

        let availableScore = engine.CalculateAvailabilityScore availableAgent
        let busyScore = engine.CalculateAvailabilityScore busyAgent
        let offlineScore = engine.CalculateAvailabilityScore offlineAgent

        Assert.AreEqual(1.0, availableScore)
        Assert.Greater(busyScore, 0.0)
        Assert.Less(busyScore, availableScore)
        Assert.AreEqual(0.0, offlineScore)

    [<Test>]
    [<Category("Unit")>]
    member _.``SpecialistMatchingEngine最適エージェント選択テスト``() =
        let engine = SpecialistMatchingEngine()

        let perfectMatch =
            { Name = "Perfect Match"
              SpecializedCapabilities = [ SecurityAudit; VulnerabilityAssessment ]
              GeneralCapabilities = [ CodeReview ]
              CliIntegration = createMockAgentCLI "Perfect Match"
              ExpertiseLevel = Expert
              CostPerHour = 100.0
              AvailabilityStatus = Available }

        let partialMatch =
            { Name = "Partial Match"
              SpecializedCapabilities = [ SecurityAudit ]
              GeneralCapabilities = [ CodeReview ]
              CliIntegration = createMockAgentCLI "Partial Match"
              ExpertiseLevel = Mid
              CostPerHour = 80.0
              AvailabilityStatus = Available }

        let agents = [ perfectMatch; partialMatch ]

        let requirement =
            { TaskDescription = "セキュリティ監査"
              RequiredCapabilities = [ SecurityAudit; VulnerabilityAssessment ]
              MinExpertiseLevel = Senior
              EstimatedDuration = TimeSpan.FromHours(3.0)
              Priority = Critical
              Budget = Some 400.0 }

        match engine.FindBestAgent agents requirement with
        | Some result ->
            Assert.AreEqual("Perfect Match", result.Agent.Name)
            Assert.Greater(result.MatchScore, 0.5)
            Assert.IsNotEmpty(result.ReasoningExplanation)
        | None -> Assert.Fail("最適エージェントが見つかりませんでした")

    [<Test>]
    [<Category("Unit")>]
    member _.``SpecialistAgentManagerエージェント登録・検索テスト``() =
        let manager = SpecialistAgentManager()

        let apiAgent =
            StandardSpecialistAgents.createAPISpecialist (createMockAgentCLI "API Specialist")

        let dbAgent =
            StandardSpecialistAgents.createDatabaseSpecialist (createMockAgentCLI "DB Specialist")

        manager.RegisterAgent apiAgent
        manager.RegisterAgent dbAgent

        let allAgents = manager.GetAllAgents()
        Assert.AreEqual(2, allAgents.Length)

        let apiAgents = manager.GetAgentsByCapability APIDesign
        Assert.AreEqual(1, apiAgents.Length)
        Assert.AreEqual("API Development Specialist", apiAgents.Head.Name)

        let dbAgents = manager.GetAgentsByCapability DatabaseDesign
        Assert.AreEqual(1, dbAgents.Length)
        Assert.AreEqual("Database Specialist", dbAgents.Head.Name)

    [<Test>]
    [<Category("Unit")>]
    member _.``SpecialistAgentManagerタスク推奨テスト``() =
        let manager = SpecialistAgentManager()

        let testAgent =
            StandardSpecialistAgents.createTestAutomationSpecialist (createMockAgentCLI "Test Specialist")

        manager.RegisterAgent testAgent

        let testRequirement =
            { TaskDescription = "E2Eテスト自動化"
              RequiredCapabilities = [ E2ETesting; TestFrameworkSetup ]
              MinExpertiseLevel = Mid
              EstimatedDuration = TimeSpan.FromHours(4.0)
              Priority = High
              Budget = Some 500.0 }

        match manager.RecommendAgent testRequirement with
        | Some result ->
            Assert.AreEqual("Test Automation Specialist", result.Agent.Name)
            Assert.Greater(result.MatchScore, 0.0)
            Assert.Greater(result.Confidence, 0.0)
        | None -> Assert.Fail("テスト要件に適合するエージェントが見つかりませんでした")

    [<Test>]
    [<Category("Unit")>]
    member _.``SpecialistAgentManager状態更新テスト``() =
        let manager = SpecialistAgentManager()

        let devopsAgent =
            StandardSpecialistAgents.createDevOpsSpecialist (createMockAgentCLI "DevOps Specialist")

        manager.RegisterAgent devopsAgent

        let updateSuccess = manager.UpdateAgentStatus "DevOps Specialist" Busy
        Assert.IsTrue(updateSuccess)

        let availableAgents = manager.GetAvailableAgents()
        Assert.AreEqual(0, availableAgents.Length)

        let updateBackSuccess = manager.UpdateAgentStatus "DevOps Specialist" Available
        Assert.IsTrue(updateBackSuccess)

        let availableAgentsAfter = manager.GetAvailableAgents()
        Assert.AreEqual(1, availableAgentsAfter.Length)

    [<Test>]
    [<Category("Unit")>]
    member _.``SpecialistAgentManager専門分野レポート生成テスト``() =
        let manager = SpecialistAgentManager()

        let apiAgent =
            StandardSpecialistAgents.createAPISpecialist (createMockAgentCLI "API Specialist")

        let securityAgent =
            StandardSpecialistAgents.createSecuritySpecialist (createMockAgentCLI "Security Specialist")

        manager.RegisterAgent apiAgent
        manager.RegisterAgent securityAgent

        let report = manager.GenerateCapabilityReport()

        Assert.IsNotEmpty(report)
        Assert.IsTrue(report.Contains("専門エージェント能力レポート"))
        Assert.IsTrue(report.Contains("APIDesign"))
        Assert.IsTrue(report.Contains("SecurityAudit"))
        Assert.IsTrue(report.Contains("総専門エージェント数: 2"))

    [<Test>]
    [<Category("Unit")>]
    member _.``StandardSpecialistAgents組み込みエージェント生成テスト``() =
        let mockCLI = createMockAgentCLI "Test CLI"

        let dbSpecialist = StandardSpecialistAgents.createDatabaseSpecialist mockCLI
        Assert.AreEqual("Database Specialist", dbSpecialist.Name)
        Assert.IsTrue(dbSpecialist.SpecializedCapabilities |> List.contains DatabaseDesign)
        Assert.AreEqual(Senior, dbSpecialist.ExpertiseLevel)

        let apiSpecialist = StandardSpecialistAgents.createAPISpecialist mockCLI
        Assert.AreEqual("API Development Specialist", apiSpecialist.Name)
        Assert.IsTrue(apiSpecialist.SpecializedCapabilities |> List.contains APIDesign)

        let testSpecialist = StandardSpecialistAgents.createTestAutomationSpecialist mockCLI
        Assert.AreEqual("Test Automation Specialist", testSpecialist.Name)
        Assert.IsTrue(testSpecialist.SpecializedCapabilities |> List.contains E2ETesting)

        let devopsSpecialist = StandardSpecialistAgents.createDevOpsSpecialist mockCLI
        Assert.AreEqual("DevOps Specialist", devopsSpecialist.Name)
        Assert.AreEqual(Expert, devopsSpecialist.ExpertiseLevel)
        Assert.IsTrue(devopsSpecialist.SpecializedCapabilities |> List.contains ContainerOrchestration)

        let securitySpecialist = StandardSpecialistAgents.createSecuritySpecialist mockCLI
        Assert.AreEqual("Security Specialist", securitySpecialist.Name)
        Assert.AreEqual(Expert, securitySpecialist.ExpertiseLevel)
        Assert.IsTrue(securitySpecialist.SpecializedCapabilities |> List.contains SecurityAudit)

[<TestFixture>]
[<Category("Integration")>]
type SpecialistAgentIntegrationTests() =

    let createMockAgentCLI name =
        { new IAgentCLI with
            member _.Name = name
            member _.StartCommand(_) = System.Diagnostics.ProcessStartInfo()

            member _.ParseOutput(_) =
                { Status = Success
                  Content = $"Mock response from {name}"
                  Metadata = Map.empty.Add("test_mode", "true")
                  Timestamp = DateTime.Now
                  SourceAgent = name
                  Capabilities = [ CodeGeneration; Testing ] }

            member _.SupportedCapabilities = [ CodeGeneration; Testing; Documentation ]

            member _.Config =
                { Name = name
                  CliPath = "mock"
                  DefaultArgs = []
                  OutputFormat = "text"
                  Timeout = TimeSpan.FromMinutes(2.0)
                  MaxRetries = 2
                  SupportedCapabilities = [ CodeGeneration; Testing; Documentation ]
                  EnvironmentVariables = Map.empty } }

    [<Test>]
    [<Category("Integration")>]
    member _.``専門エージェント統合フローテスト``() =
        let manager = SpecialistAgentManager()

        // 複数の専門エージェントを登録
        let agents =
            [ StandardSpecialistAgents.createDatabaseSpecialist (createMockAgentCLI "DB Agent")
              StandardSpecialistAgents.createAPISpecialist (createMockAgentCLI "API Agent")
              StandardSpecialistAgents.createTestAutomationSpecialist (createMockAgentCLI "Test Agent")
              StandardSpecialistAgents.createDevOpsSpecialist (createMockAgentCLI "DevOps Agent")
              StandardSpecialistAgents.createSecuritySpecialist (createMockAgentCLI "Security Agent") ]

        agents |> List.iter manager.RegisterAgent

        // 複雑なタスク要件（複数専門分野にまたがる）
        let complexRequirement =
            { TaskDescription = "マイクロサービスAPIの設計・実装・テスト・デプロイ・セキュリティ監査"
              RequiredCapabilities = [ APIDesign; TestFrameworkSetup; ContainerOrchestration; SecurityAudit ]
              MinExpertiseLevel = Senior
              EstimatedDuration = TimeSpan.FromHours(8.0)
              Priority = Critical
              Budget = Some 1200.0 }

        // 各専門分野での最適エージェント推奨
        let apiRequirement =
            { complexRequirement with
                RequiredCapabilities = [ APIDesign ]
                EstimatedDuration = TimeSpan.FromHours(2.0) }

        let testRequirement =
            { complexRequirement with
                RequiredCapabilities = [ TestFrameworkSetup ]
                EstimatedDuration = TimeSpan.FromHours(2.0) }

        let devopsRequirement =
            { complexRequirement with
                RequiredCapabilities = [ ContainerOrchestration ]
                EstimatedDuration = TimeSpan.FromHours(2.0) }

        let securityRequirement =
            { complexRequirement with
                RequiredCapabilities = [ SecurityAudit ]
                EstimatedDuration = TimeSpan.FromHours(2.0) }

        let apiResult = manager.RecommendAgent apiRequirement
        let testResult = manager.RecommendAgent testRequirement
        let devopsResult = manager.RecommendAgent devopsRequirement
        let securityResult = manager.RecommendAgent securityRequirement

        // 各専門分野で適切なエージェントが推奨されることを確認
        Assert.IsTrue(apiResult.IsSome)
        Assert.IsTrue(testResult.IsSome)
        Assert.IsTrue(devopsResult.IsSome)
        Assert.IsTrue(securityResult.IsSome)

        Assert.AreEqual("API Development Specialist", apiResult.Value.Agent.Name)
        Assert.AreEqual("Test Automation Specialist", testResult.Value.Agent.Name)
        Assert.AreEqual("DevOps Specialist", devopsResult.Value.Agent.Name)
        Assert.AreEqual("Security Specialist", securityResult.Value.Agent.Name)

        // 全エージェントが高い信頼度を持つことを確認
        Assert.Greater(apiResult.Value.Confidence, 0.7)
        Assert.Greater(testResult.Value.Confidence, 0.7)
        Assert.Greater(devopsResult.Value.Confidence, 0.7)
        Assert.Greater(securityResult.Value.Confidence, 0.7)

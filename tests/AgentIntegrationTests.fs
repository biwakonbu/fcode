namespace FCode.Tests

open System
open System.Threading.Tasks
open NUnit.Framework
open FCode.ISpecializedAgent
open FCode.SpecialistAgentManager
open FCode.WorkflowOrchestrator
open FCode.ModelSwitchingStrategy
open FCode.AIModelProvider
open FCode.FCodeError

// エージェント統合テスト用のモックエージェント
type IntegrationTestAgent(agentId: string, specialization: AgentSpecialization) =
    interface ISpecializedAgent with
        member _.AgentId = agentId
        member _.Specialization = specialization
        member _.Capabilities = [ CodeGeneration; Debugging ]
        member _.CurrentState = Available

        member _.ExecuteTask(context: AgentExecutionContext) =
            task {
                // 統合テスト用のより詳細な実行結果
                let result =
                    { Success = true
                      Output = $"Integration test executed by {agentId} for task: {context.Task}"
                      Error = None
                      ExecutionTime = TimeSpan.FromMilliseconds(150.0)
                      Timestamp = DateTime.Now }

                return Result.Ok(result)
            }

[<TestFixture>]
[<Category("Unit")>]
type AgentIntegrationTests() =

    [<Test>]
    [<Category("Unit")>]
    member this.``Agent registration and workflow execution should work together``() =
        async {
            // Arrange
            let manager = SpecialistAgentManager() :> ISpecializedAgentManager
            let modelProvider = MultiModelManager()
            let orchestrator = WorkflowOrchestrator(manager, modelProvider)

            let agent = IntegrationTestAgent("integration-agent", DevOps) :> ISpecializedAgent
            let! _ = manager.RegisterAgent(agent) |> Async.AwaitTask

            let context =
                { ExecutionId = "integration-test"
                  ProjectPath = "/test/integration"
                  UserId = "test-user"
                  StartTime = DateTime.Now }

            // Act
            let! workflowResult = orchestrator.StartWorkflow("integration-workflow", context)

            // Assert
            match workflowResult with
            | Result.Ok taskId ->
                Assert.IsNotEmpty(taskId)

                // Verify agent is available for task execution
                let! agentsResult = manager.GetAvailableAgents() |> Async.AwaitTask

                match agentsResult with
                | Result.Ok agents ->
                    Assert.AreEqual(1, agents.Length)
                    Assert.AreEqual("integration-agent", agents.Head.AgentId)
                | Result.Error _ -> Assert.Fail("Should retrieve available agents")

            | Result.Error _ -> Assert.Fail("Workflow start should succeed")
        }

    [<Test>]
    [<Category("Unit")>]
    member this.``Model switching with agent execution should work together``() =
        async {
            // Arrange
            let config = ModelSwitchingUtils.createDefaultConfiguration ()
            let engine = ModelSwitchingEngine(config)

            let manager = SpecialistAgentManager() :> ISpecializedAgentManager
            let agent = IntegrationTestAgent("model-test-agent", API) :> ISpecializedAgent
            let! _ = manager.RegisterAgent(agent) |> Async.AwaitTask

            // Act
            let! modelResult = engine.RecommendModelSwitch("API development task")

            // Execute task with recommended model
            let taskContext =
                { RequestId = "model-integration-test"
                  UserId = "test-user"
                  ProjectPath = "/test/model"
                  Task = "API development with recommended model"
                  Context = Map.empty
                  Timestamp = DateTime.Now
                  Timeout = TimeSpan.FromMinutes(5.0)
                  Priority = 5 }

            let! executionResult = agent.ExecuteTask(taskContext) |> Async.AwaitTask

            // Assert
            match modelResult, executionResult with
            | Result.Ok model, Result.Ok execResult ->
                Assert.AreEqual(Claude3Sonnet, model)
                Assert.IsTrue(execResult.Success)
                Assert.IsTrue(execResult.Output.Contains("API development"))
            | _ -> Assert.Fail("Both model recommendation and task execution should succeed")
        }

    [<Test>]
    [<Category("Unit")>]
    member this.``Multiple agents with different specializations should be managed correctly``() =
        task {
            // Arrange
            let manager = SpecialistAgentManager() :> ISpecializedAgentManager

            let devAgent = IntegrationTestAgent("dev-agent", Backend) :> ISpecializedAgent
            let qaAgent = IntegrationTestAgent("qa-agent", TestAutomation) :> ISpecializedAgent

            let secAgent =
                IntegrationTestAgent("security-agent", AgentSpecialization.Security) :> ISpecializedAgent

            // Act
            let! devResult = manager.RegisterAgent(devAgent) |> Async.AwaitTask
            let! qaResult = manager.RegisterAgent(qaAgent) |> Async.AwaitTask
            let! secResult = manager.RegisterAgent(secAgent) |> Async.AwaitTask

            let! allAgents = manager.GetAvailableAgents() |> Async.AwaitTask

            // Assert
            match devResult, qaResult, secResult, allAgents with
            | Result.Ok _, Result.Ok _, Result.Ok _, Result.Ok agents ->
                Assert.AreEqual(3, agents.Length)

                // Verify each agent is registered with correct specialization
                let devAgentFound =
                    agents
                    |> List.exists (fun a -> a.AgentId = "dev-agent" && a.Specialization = Backend)

                let qaAgentFound =
                    agents
                    |> List.exists (fun a -> a.AgentId = "qa-agent" && a.Specialization = TestAutomation)

                let secAgentFound =
                    agents
                    |> List.exists (fun a ->
                        a.AgentId = "security-agent" && a.Specialization = AgentSpecialization.Security)

                Assert.IsTrue(devAgentFound, "Development agent should be found")
                Assert.IsTrue(qaAgentFound, "QA agent should be found")
                Assert.IsTrue(secAgentFound, "Security agent should be found")

            | _ -> Assert.Fail("All agent registrations should succeed")
        }

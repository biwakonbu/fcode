namespace FCode.Tests

open System
open System.Threading.Tasks
open NUnit.Framework
open FCode.ISpecializedAgent
open FCode.SpecialistAgentManager
open FCode.FCodeError

// テスト用のモックエージェント
type TestAgent(agentId: string, specialization: AgentSpecialization) =
    interface ISpecializedAgent with
        member _.AgentId = agentId
        member _.Specialization = specialization
        member _.Capabilities = [ CodeGeneration; Debugging ]
        member _.CurrentState = Available

        member _.ExecuteTask(context: AgentExecutionContext) =
            task {
                let result =
                    { Success = true
                      Output = "Test output"
                      Error = None
                      ExecutionTime = TimeSpan.FromMilliseconds(100.0)
                      Timestamp = DateTime.Now }

                return Result.Ok(result)
            }

[<TestFixture>]
[<Category("Unit")>]
type SpecialistAgentManagerTests() =

    [<Test>]
    [<Category("Unit")>]
    member this.``RegisterAgent should register agent successfully``() =
        task {
            // Arrange
            let manager = SpecialistAgentManager() :> ISpecializedAgentManager
            let agent = TestAgent("test-agent", Database) :> ISpecializedAgent

            // Act
            let! result = manager.RegisterAgent(agent)

            // Assert
            match result with
            | Result.Ok _ -> Assert.Pass()
            | Result.Error _ -> Assert.Fail("Registration should succeed")
        }

    [<Test>]
    [<Category("Unit")>]
    member this.``GetAgent should return registered agent``() =
        task {
            // Arrange
            let manager = SpecialistAgentManager() :> ISpecializedAgentManager
            let agent = TestAgent("test-agent", Database) :> ISpecializedAgent
            let! _ = manager.RegisterAgent(agent)

            // Act
            let! result = manager.GetAgent("test-agent")

            // Assert
            match result with
            | Result.Ok(Some retrievedAgent) -> Assert.AreEqual("test-agent", retrievedAgent.AgentId)
            | Result.Ok None -> Assert.Fail("Agent should be found")
            | Result.Error _ -> Assert.Fail("GetAgent should succeed")
        }

    [<Test>]
    [<Category("Unit")>]
    member this.``GetAvailableAgents should return available agents``() =
        task {
            // Arrange
            let manager = SpecialistAgentManager() :> ISpecializedAgentManager
            let agent = TestAgent("test-agent", Database) :> ISpecializedAgent
            let! _ = manager.RegisterAgent(agent)

            // Act
            let! result = manager.GetAvailableAgents()

            // Assert
            match result with
            | Result.Ok agents ->
                Assert.AreEqual(1, agents.Length)
                Assert.AreEqual("test-agent", agents.[0].AgentId)
            | Result.Error _ -> Assert.Fail("GetAvailableAgents should succeed")
        }

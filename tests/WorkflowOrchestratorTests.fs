namespace FCode.Tests

open System
open NUnit.Framework
open FCode.WorkflowOrchestrator
open FCode.ISpecializedAgent
open FCode.SpecialistAgentManager
open FCode.AIModelProvider

[<TestFixture>]
[<Category("Unit")>]
type WorkflowOrchestratorTests() =

    [<Test>]
    [<Category("Unit")>]
    member this.``StartWorkflow should create workflow successfully``() =
        async {
            // Arrange
            let manager = SpecialistAgentManager() :> ISpecializedAgentManager
            let modelProvider = MultiModelManager()
            let orchestrator = WorkflowOrchestrator(manager, modelProvider)

            let context =
                { ExecutionId = "test-execution"
                  ProjectPath = "/test/path"
                  UserId = "test-user"
                  StartTime = DateTime.Now }

            // Act
            let! result = orchestrator.StartWorkflow("test-workflow", context)

            // Assert
            match result with
            | Result.Ok taskId ->
                Assert.IsNotEmpty(taskId)
                Assert.Pass()
            | Result.Error _ -> Assert.Fail("StartWorkflow should succeed")
        }

    [<Test>]
    [<Category("Unit")>]
    member this.``GetWorkflowStatus should return progress``() =
        async {
            // Arrange
            let manager = SpecialistAgentManager() :> ISpecializedAgentManager
            let modelProvider = MultiModelManager()
            let orchestrator = WorkflowOrchestrator(manager, modelProvider)

            // Act
            let! result = orchestrator.GetWorkflowStatus("test-workflow")

            // Assert
            match result with
            | Result.Ok progress ->
                Assert.GreaterOrEqual(progress, 0.0)
                Assert.LessOrEqual(progress, 1.0)
            | Result.Error _ -> Assert.Fail("GetWorkflowStatus should succeed")
        }

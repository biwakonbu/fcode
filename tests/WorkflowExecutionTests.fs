namespace FCode.Tests

open System
open System.Threading.Tasks
open NUnit.Framework
open FCode.ISpecializedAgent
open FCode.SpecialistAgentManager
open FCode.WorkflowOrchestrator
open FCode.AIModelProvider
open FCode.FCodeError

// ワークフロー実行テスト用のモックエージェント
type WorkflowTestAgent(agentId: string, specialization: AgentSpecialization, executionDelay: int) =
    interface ISpecializedAgent with
        member _.AgentId = agentId
        member _.Specialization = specialization
        member _.Capabilities = [ CodeGeneration; Debugging ]
        member _.CurrentState = Available

        member _.ExecuteTask(context: AgentExecutionContext) =
            task {
                // 実行遅延をシミュレート
                do! Task.Delay(executionDelay)

                let result =
                    { Success = true
                      Output = $"Workflow task completed by {agentId}: {context.Task}"
                      Error = None
                      ExecutionTime = TimeSpan.FromMilliseconds(float executionDelay)
                      Timestamp = DateTime.Now }

                return Result.Ok(result)
            }

[<TestFixture>]
[<Category("Unit")>]
type WorkflowExecutionTests() =

    [<Test>]
    [<Category("Unit")>]
    member this.``Workflow execution from start to completion should work``() =
        async {
            // Arrange
            let manager = SpecialistAgentManager() :> ISpecializedAgentManager
            let modelProvider = MultiModelManager()
            let orchestrator = WorkflowOrchestrator(manager, modelProvider)

            let context =
                { ExecutionId = "workflow-execution-test"
                  ProjectPath = "/test/workflow"
                  UserId = "test-user"
                  StartTime = DateTime.Now }

            // Act - Start workflow
            let! startResult = orchestrator.StartWorkflow("execution-test", context)

            match startResult with
            | Result.Ok taskId ->
                // Check initial status
                let! statusResult = orchestrator.GetWorkflowStatus("execution-test")

                match statusResult with
                | Result.Ok progress ->
                    Assert.GreaterOrEqual(progress, 0.0)
                    Assert.LessOrEqual(progress, 1.0)

                    // Execute the task
                    let! execResult = orchestrator.ExecuteTask(taskId)

                    match execResult with
                    | Result.Ok completedTask ->
                        Assert.AreEqual(TaskCompleted, completedTask.Status)

                        // Check final status
                        let! finalStatusResult = orchestrator.GetWorkflowStatus("execution-test")

                        match finalStatusResult with
                        | Result.Ok finalProgress ->
                            Assert.GreaterOrEqual(finalProgress, 0.0)
                            Assert.LessOrEqual(finalProgress, 1.0)
                        | Result.Error _ -> Assert.Fail("Final status check should succeed")

                    | Result.Error _ -> Assert.Fail("Task execution should succeed")

                | Result.Error _ -> Assert.Fail("Status check should succeed")

            | Result.Error _ -> Assert.Fail("Workflow start should succeed")
        }

    [<Test>]
    [<Category("Unit")>]
    member this.``Multiple workflow tasks should be managed correctly``() =
        async {
            // Arrange
            let manager = SpecialistAgentManager() :> ISpecializedAgentManager
            let modelProvider = MultiModelManager()
            let orchestrator = WorkflowOrchestrator(manager, modelProvider)

            let context1 =
                { ExecutionId = "workflow-1"
                  ProjectPath = "/test/workflow1"
                  UserId = "test-user-1"
                  StartTime = DateTime.Now }

            let context2 =
                { ExecutionId = "workflow-2"
                  ProjectPath = "/test/workflow2"
                  UserId = "test-user-2"
                  StartTime = DateTime.Now }

            // Act - Start multiple workflows
            let! result1 = orchestrator.StartWorkflow("multi-test-1", context1)
            let! result2 = orchestrator.StartWorkflow("multi-test-2", context2)

            // Assert
            match result1, result2 with
            | Result.Ok taskId1, Result.Ok taskId2 ->
                Assert.AreNotEqual(taskId1, taskId2, "Task IDs should be unique")

                // Both workflows should have valid status
                let! status1 = orchestrator.GetWorkflowStatus("multi-test-1")
                let! status2 = orchestrator.GetWorkflowStatus("multi-test-2")

                match status1, status2 with
                | Result.Ok progress1, Result.Ok progress2 ->
                    Assert.GreaterOrEqual(progress1, 0.0)
                    Assert.GreaterOrEqual(progress2, 0.0)
                | _ -> Assert.Fail("Both workflow statuses should be retrievable")

            | _ -> Assert.Fail("Both workflows should start successfully")
        }

    [<Test>]
    [<Category("Unit")>]
    member this.``Workflow with agent integration should execute tasks``() =
        async {
            // Arrange
            let manager = SpecialistAgentManager() :> ISpecializedAgentManager
            let modelProvider = MultiModelManager()
            let orchestrator = WorkflowOrchestrator(manager, modelProvider)

            // Register test agents
            let devAgent = WorkflowTestAgent("workflow-dev", Backend, 50) :> ISpecializedAgent

            let qaAgent =
                WorkflowTestAgent("workflow-qa", TestAutomation, 30) :> ISpecializedAgent

            let! _ = manager.RegisterAgent(devAgent) |> Async.AwaitTask
            let! _ = manager.RegisterAgent(qaAgent) |> Async.AwaitTask

            let context =
                { ExecutionId = "agent-workflow-test"
                  ProjectPath = "/test/agent-workflow"
                  UserId = "test-user"
                  StartTime = DateTime.Now }

            // Act
            let! workflowResult = orchestrator.StartWorkflow("agent-integration", context)

            match workflowResult with
            | Result.Ok taskId ->
                // Verify agents are available
                let! agentsResult = manager.GetAvailableAgents() |> Async.AwaitTask

                match agentsResult with
                | Result.Ok agents ->
                    Assert.AreEqual(2, agents.Length)

                    // Execute task with one of the agents
                    let testContext =
                        { RequestId = "workflow-task"
                          UserId = "test-user"
                          ProjectPath = "/test/agent-workflow"
                          Task = "Workflow integration test task"
                          Context = Map.empty
                          Timestamp = DateTime.Now
                          Timeout = TimeSpan.FromMinutes(1.0)
                          Priority = 3 }

                    let! agentExecResult = devAgent.ExecuteTask(testContext) |> Async.AwaitTask

                    match agentExecResult with
                    | Result.Ok execResult ->
                        Assert.IsTrue(execResult.Success)
                        Assert.IsTrue(execResult.Output.Contains("workflow-dev"))
                    | Result.Error _ -> Assert.Fail("Agent task execution should succeed")

                | Result.Error _ -> Assert.Fail("Should retrieve available agents")

            | Result.Error _ -> Assert.Fail("Workflow with agents should start successfully")
        }

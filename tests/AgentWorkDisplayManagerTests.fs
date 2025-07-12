/// SC-1-2: エージェント作業表示機能テスト
module FCode.Tests.AgentWorkDisplayManagerTests

open System
open NUnit.Framework
open FCode

[<TestFixture>]
[<Category("Unit")>]
type AgentWorkDisplayManagerTests() =

    [<Test>]
    member _.AgentWorkDisplayManager_InitializeAgent_SetsIdleStatus() =
        // Arrange
        let manager = new AgentWorkDisplayManager()
        let agentId = "test-agent"

        // Act
        manager.InitializeAgent(agentId)

        // Assert
        match manager.GetAgentWorkInfo(agentId) with
        | Some workInfo ->
            Assert.AreEqual(agentId, workInfo.AgentId)

            match workInfo.CurrentStatus with
            | Idle _ -> Assert.Pass("Agent initialized with Idle status")
            | _ -> Assert.Fail("Agent should be initialized with Idle status")
        | None -> Assert.Fail("Agent work info should be available after initialization")

    [<Test>]
    member _.AgentWorkDisplayManager_StartTask_UpdatesToWorkingStatus() =
        // Arrange
        let manager = new AgentWorkDisplayManager()
        let agentId = "test-agent"
        let taskTitle = "Test Task"
        let duration = TimeSpan.FromMinutes(30.0)

        manager.InitializeAgent(agentId)

        // Act
        manager.StartTask(agentId, taskTitle, duration)

        // Assert
        match manager.GetAgentWorkInfo(agentId) with
        | Some workInfo ->
            match workInfo.CurrentStatus with
            | Working(title, _, progress) ->
                Assert.AreEqual(taskTitle, title)
                Assert.AreEqual(0.0, progress)
            | _ -> Assert.Fail("Agent should be in Working status after task start")
        | None -> Assert.Fail("Agent work info should be available")

    [<Test>]
    member _.AgentWorkDisplayManager_UpdateProgress_ModifiesProgressValue() =
        // Arrange
        let manager = new AgentWorkDisplayManager()
        let agentId = "test-agent"
        let taskTitle = "Test Task"
        let duration = TimeSpan.FromMinutes(30.0)

        manager.InitializeAgent(agentId)
        manager.StartTask(agentId, taskTitle, duration)

        // Act
        manager.UpdateProgress(agentId, 50.0, "Half way done")

        // Assert
        match manager.GetAgentWorkInfo(agentId) with
        | Some workInfo ->
            Assert.AreEqual(50.0, workInfo.ProgressPercentage)

            match workInfo.CurrentStatus with
            | Working(_, _, progress) -> Assert.AreEqual(50.0, progress)
            | _ -> Assert.Fail("Agent should remain in Working status")
        | None -> Assert.Fail("Agent work info should be available")

    [<Test>]
    member _.AgentWorkDisplayManager_CompleteTask_SetsCompletedStatus() =
        // Arrange
        let manager = new AgentWorkDisplayManager()
        let agentId = "test-agent"
        let taskTitle = "Test Task"
        let duration = TimeSpan.FromMinutes(30.0)
        let result = "Task completed successfully"

        manager.InitializeAgent(agentId)
        manager.StartTask(agentId, taskTitle, duration)

        // Act
        manager.CompleteTask(agentId, result)

        // Assert
        match manager.GetAgentWorkInfo(agentId) with
        | Some workInfo ->
            Assert.AreEqual(100.0, workInfo.ProgressPercentage)

            match workInfo.CurrentStatus with
            | Completed(title, _, resultMsg) ->
                Assert.AreEqual(taskTitle, title)
                Assert.AreEqual(result, resultMsg)
            | _ -> Assert.Fail("Agent should be in Completed status")
        | None -> Assert.Fail("Agent work info should be available")

    [<Test>]
    member _.AgentWorkDisplayManager_ReportError_SetsErrorStatus() =
        // Arrange
        let manager = new AgentWorkDisplayManager()
        let agentId = "test-agent"
        let taskTitle = "Test Task"
        let duration = TimeSpan.FromMinutes(30.0)
        let errorMessage = "Test error occurred"

        manager.InitializeAgent(agentId)
        manager.StartTask(agentId, taskTitle, duration)

        // Act
        manager.ReportError(agentId, errorMessage)

        // Assert
        match manager.GetAgentWorkInfo(agentId) with
        | Some workInfo ->
            match workInfo.CurrentStatus with
            | Error(title, message, _) ->
                Assert.AreEqual(taskTitle, title)
                Assert.AreEqual(errorMessage, message)
            | _ -> Assert.Fail("Agent should be in Error status")
        | None -> Assert.Fail("Agent work info should be available")

    [<Test>]
    member _.AgentWorkDisplayManager_FormatWorkStatus_ReturnsFormattedString() =
        // Arrange
        let manager = new AgentWorkDisplayManager()
        let agentId = "test-agent"
        let taskTitle = "Test Task"
        let duration = TimeSpan.FromMinutes(30.0)

        manager.InitializeAgent(agentId)
        manager.StartTask(agentId, taskTitle, duration)
        manager.UpdateProgress(agentId, 25.0, "Quarter done")

        // Act
        match manager.GetAgentWorkInfo(agentId) with
        | Some workInfo ->
            let formatted = manager.FormatWorkStatus(workInfo)

            // Assert
            Assert.IsTrue(formatted.Contains(agentId))
            Assert.IsTrue(formatted.Contains(taskTitle))
            Assert.IsTrue(formatted.Contains("25.0%"))
        | None -> Assert.Fail("Agent work info should be available")

    [<Test>]
    member _.AgentWorkDisplayManager_TaskHistory_MaintainsRecentEntries() =
        // Arrange
        let manager = new AgentWorkDisplayManager()
        let agentId = "test-agent"

        manager.InitializeAgent(agentId)

        // Act - Perform multiple operations
        manager.StartTask(agentId, "Task 1", TimeSpan.FromMinutes(10.0))
        manager.UpdateProgress(agentId, 50.0, "Progress update")
        manager.CompleteTask(agentId, "Task 1 completed")

        // Assert
        match manager.GetAgentWorkInfo(agentId) with
        | Some workInfo ->
            Assert.GreaterOrEqual(workInfo.TaskHistory.Length, 3)
            // 最新のエントリが最初に来ることを確認
            let (_, latestNote, _) = workInfo.TaskHistory.Head
            Assert.IsTrue(latestNote.Contains("Task completed"))
        | None -> Assert.Fail("Agent work info should be available")

    [<Test>]
    member _.AgentWorkDisplayManager_StartReview_SetsReviewingStatus() =
        // Arrange
        let manager = new AgentWorkDisplayManager()
        let agentId = "qa-agent"
        let reviewTarget = "Feature X implementation"
        let reviewer = "QA Lead"

        manager.InitializeAgent(agentId)

        // Act
        manager.StartReview(agentId, reviewTarget, reviewer)

        // Assert
        match manager.GetAgentWorkInfo(agentId) with
        | Some workInfo ->
            match workInfo.CurrentStatus with
            | Reviewing(target, rev, _) ->
                Assert.AreEqual(reviewTarget, target)
                Assert.AreEqual(reviewer, rev)
            | _ -> Assert.Fail("Agent should be in Reviewing status")
        | None -> Assert.Fail("Agent work info should be available")

[<TestFixture>]
[<Category("Integration")>]
type AgentWorkDisplayManagerIntegrationTests() =

    [<Test>]
    member _.AgentWorkDisplayManager_DisplayUpdateHandler_ReceivesNotifications() =
        // Arrange
        let manager = new AgentWorkDisplayManager()
        let agentId = "test-agent"
        let mutable handlerCalled = false
        let mutable receivedAgentId = ""
        let mutable receivedWorkInfo = None

        // Act - Register handler
        manager.RegisterDisplayUpdateHandler(fun id info ->
            handlerCalled <- true
            receivedAgentId <- id
            receivedWorkInfo <- Some info)

        manager.InitializeAgent(agentId)

        // Small delay to ensure async handler execution
        System.Threading.Thread.Sleep(100)

        // Assert
        Assert.IsTrue(handlerCalled)
        Assert.AreEqual(agentId, receivedAgentId)
        Assert.IsTrue(receivedWorkInfo.IsSome)

    [<Test>]
    member _.AgentWorkDisplayManager_GetAllAgentWorkInfos_ReturnsAllAgents() =
        // Arrange
        let manager = new AgentWorkDisplayManager()
        let agentIds = [ "dev1"; "qa1"; "ux" ]

        // Act
        for agentId in agentIds do
            manager.InitializeAgent(agentId)

        let allInfos = manager.GetAllAgentWorkInfos()

        // Assert
        Assert.AreEqual(agentIds.Length, allInfos.Length)

        for agentId in agentIds do
            Assert.IsTrue(allInfos |> List.exists (fun (id, _) -> id = agentId))

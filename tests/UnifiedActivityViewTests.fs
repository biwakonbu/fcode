module FCode.Tests.UnifiedActivityViewTests

open System
open System.Threading
open NUnit.Framework
open Terminal.Gui
open FCode.UnifiedActivityView
open FCode.AgentMessaging
open FCode.Logger

[<TestFixture>]
type UnifiedActivityViewTests() =

    [<SetUp>]
    member _.Setup() =
        // テスト用ログ設定
        ()

    [<Test>]
    [<Category("Unit")>]
    member _.``UnifiedActivityManager Basic Creation Test``() =
        // 統合活動管理の基本生成テスト
        let manager = new UnifiedActivityManager()

        Assert.AreEqual(0, manager.GetActivityCount())
        Assert.AreEqual(0, manager.GetAllActivities().Length)

    [<Test>]
    [<Category("Unit")>]
    member _.``AddSystemActivity Basic Test``() =
        // システム活動追加基本テスト
        let manager = new UnifiedActivityManager()

        manager.AddSystemActivity("test-agent", CodeGeneration, "Test code generation activity")

        Assert.AreEqual(1, manager.GetActivityCount())

        let activities = manager.GetAllActivities()
        Assert.AreEqual(1, activities.Length)
        Assert.AreEqual("test-agent", activities.[0].AgentId)
        Assert.AreEqual(CodeGeneration, activities.[0].ActivityType)
        Assert.AreEqual("Test code generation activity", activities.[0].Message)
        Assert.AreEqual("system", activities.[0].Status)

    [<Test>]
    [<Category("Unit")>]
    member _.``AddActivityFromMessage AgentMessage Conversion Test``() =
        // AgentMessageからの活動変換テスト
        let manager = new UnifiedActivityManager()

        let testMessage =
            MessageBuilder()
                .From("dev1")
                .To("qa1")
                .OfType(MessageType.TaskAssignment)
                .WithPriority(High)
                .WithContent("Please review the implementation")
                .WithMetadata("task_id", "TASK-001")
                .Build()

        manager.AddActivityFromMessage(testMessage)

        Assert.AreEqual(1, manager.GetActivityCount())

        let activities = manager.GetAllActivities()
        let activity = activities.[0]
        Assert.AreEqual("dev1", activity.AgentId)
        Assert.AreEqual(ActivityType.TaskAssignment, activity.ActivityType)
        Assert.AreEqual("Please review the implementation", activity.Message)
        Assert.AreEqual(High, activity.Priority)
        Assert.AreEqual(Some "TASK-001", activity.RelatedTaskId)
        Assert.AreEqual("received", activity.Status)

    [<Test>]
    [<Category("Unit")>]
    member _.``GetLatestActivitiesByAgent Filter Test``() =
        // エージェント別活動フィルタテスト
        let manager = new UnifiedActivityManager()

        manager.AddSystemActivity("agent1", CodeGeneration, "Agent1 activity 1")
        manager.AddSystemActivity("agent2", Testing, "Agent2 activity 1")
        manager.AddSystemActivity("agent1", ActivityType.QualityReview, "Agent1 activity 2")
        manager.AddSystemActivity("agent1", Documentation, "Agent1 activity 3")

        let agent1Activities = manager.GetLatestActivitiesByAgent("agent1", 5)
        let agent2Activities = manager.GetLatestActivitiesByAgent("agent2", 5)

        Assert.AreEqual(3, agent1Activities.Length)
        Assert.AreEqual(1, agent2Activities.Length)
        Assert.IsTrue(agent1Activities |> Array.forall (fun a -> a.AgentId = "agent1"))
        Assert.IsTrue(agent2Activities |> Array.forall (fun a -> a.AgentId = "agent2"))

    [<Test>]
    [<Category("Unit")>]
    member _.``GetLatestActivitiesByType Filter Test``() =
        // 活動種別フィルタテスト
        let manager = new UnifiedActivityManager()

        manager.AddSystemActivity("agent1", CodeGeneration, "Code generation 1")
        manager.AddSystemActivity("agent2", Testing, "Testing 1")
        manager.AddSystemActivity("agent3", CodeGeneration, "Code generation 2")
        manager.AddSystemActivity("agent1", ActivityType.QualityReview, "QA review 1")

        let codeGenActivities = manager.GetLatestActivitiesByType(CodeGeneration, 5)
        let testActivities = manager.GetLatestActivitiesByType(Testing, 5)
        let qaActivities = manager.GetLatestActivitiesByType(ActivityType.QualityReview, 5)

        Assert.AreEqual(2, codeGenActivities.Length)
        Assert.AreEqual(1, testActivities.Length)
        Assert.AreEqual(1, qaActivities.Length)
        Assert.IsTrue(codeGenActivities |> Array.forall (fun a -> a.ActivityType = CodeGeneration))

    [<Test>]
    [<Category("Unit")>]
    member _.``MessageType to ActivityType Conversion Test``() =
        // メッセージ種別から活動種別への変換テスト
        let manager = new UnifiedActivityManager()

        let testCases =
            [ (MessageType.TaskAssignment, ActivityType.TaskAssignment)
              (MessageType.Progress, ActivityType.Progress)
              (MessageType.QualityReview, ActivityType.QualityReview)
              (MessageType.Escalation, ActivityType.Escalation)
              (MessageType.StateUpdate, ActivityType.SystemMessage)
              (MessageType.ResourceRequest, ActivityType.SystemMessage)
              (MessageType.Collaboration, ActivityType.Decision)
              (MessageType.Notification, ActivityType.SystemMessage) ]

        for (messageType, expectedActivityType) in testCases do
            let message =
                MessageBuilder().From("test-agent").OfType(messageType).WithContent($"Test {messageType}").Build()

            manager.AddActivityFromMessage(message)

        let activities = manager.GetAllActivities()
        Assert.AreEqual(testCases.Length, activities.Length)

        for i, (_, expectedActivityType) in testCases |> List.indexed do
            Assert.AreEqual(expectedActivityType, activities.[i].ActivityType)

    [<Test>]
    [<Category("Unit")>]
    member _.``ClearActivities Test``() =
        // 活動クリアテスト
        let manager = new UnifiedActivityManager()

        manager.AddSystemActivity("agent1", CodeGeneration, "Activity 1")
        manager.AddSystemActivity("agent2", Testing, "Activity 2")

        Assert.AreEqual(2, manager.GetActivityCount())

        manager.ClearActivities()

        Assert.AreEqual(0, manager.GetActivityCount())
        Assert.AreEqual(0, manager.GetAllActivities().Length)

    [<Test>]
    [<Category("Unit")>]
    member _.``ActivityType Display Format Test``() =
        // 活動種別表示フォーマットテスト
        let manager = new UnifiedActivityManager()

        // プライベートメソッドのテストのため、実際の表示を確認
        manager.AddSystemActivity("agent1", CodeGeneration, "Code activity")
        manager.AddSystemActivity("agent2", Testing, "Test activity")
        manager.AddSystemActivity("agent3", ActivityType.QualityReview, "QA activity")
        manager.AddSystemActivity("agent4", ActivityType.Escalation, "Escalation activity")

        Assert.AreEqual(4, manager.GetActivityCount())

        let activities = manager.GetAllActivities()
        Assert.IsTrue(activities |> Array.exists (fun a -> a.ActivityType = CodeGeneration))
        Assert.IsTrue(activities |> Array.exists (fun a -> a.ActivityType = Testing))

        Assert.IsTrue(
            activities
            |> Array.exists (fun a -> a.ActivityType = ActivityType.QualityReview)
        )

        Assert.IsTrue(activities |> Array.exists (fun a -> a.ActivityType = ActivityType.Escalation))

    [<Test>]
    [<Category("Unit")>]
    member _.``Priority Types Mapping Test``() =
        // 優先度マッピングテスト
        let manager = new UnifiedActivityManager()

        manager.AddSystemActivity("agent1", CodeGeneration, "Critical activity", Critical)
        manager.AddSystemActivity("agent2", Testing, "High activity", High)
        manager.AddSystemActivity("agent3", Documentation, "Normal activity", Normal)
        manager.AddSystemActivity("agent4", SystemMessage, "Low activity", Low)

        let activities = manager.GetAllActivities()
        Assert.AreEqual(4, activities.Length)

        Assert.IsTrue(activities |> Array.exists (fun a -> a.Priority = Critical))
        Assert.IsTrue(activities |> Array.exists (fun a -> a.Priority = High))
        Assert.IsTrue(activities |> Array.exists (fun a -> a.Priority = Normal))
        Assert.IsTrue(activities |> Array.exists (fun a -> a.Priority = Low))

    [<Test>]
    [<Category("Integration")>]
    member _.``Global UnifiedActivityManager Usage Test``() =
        // グローバル統合活動管理使用テスト
        let initialCount = globalUnifiedActivityManager.GetActivityCount()

        addSystemActivity "global-test" CodeGeneration "Global system activity"

        let newCount = globalUnifiedActivityManager.GetActivityCount()
        Assert.AreEqual(initialCount + 1, newCount)

        let activities = globalUnifiedActivityManager.GetAllActivities()
        let latestActivity = activities |> Array.maxBy (fun a -> a.Timestamp)
        Assert.AreEqual("global-test", latestActivity.AgentId)
        Assert.AreEqual(CodeGeneration, latestActivity.ActivityType)
        Assert.AreEqual("Global system activity", latestActivity.Message)

    [<Test>]
    [<Category("Integration")>]
    member _.``Global addActivityFromMessage Usage Test``() =
        // グローバル関数によるAgentMessage追加テスト
        let initialCount = globalUnifiedActivityManager.GetActivityCount()

        let testMessage =
            MessageBuilder()
                .From("global-agent")
                .OfType(MessageType.Progress)
                .WithPriority(Normal)
                .WithContent("Global message activity")
                .Build()

        addActivityFromMessage testMessage

        let newCount = globalUnifiedActivityManager.GetActivityCount()
        Assert.AreEqual(initialCount + 1, newCount)

        let activities = globalUnifiedActivityManager.GetAllActivities()
        let latestActivity = activities |> Array.maxBy (fun a -> a.Timestamp)
        Assert.AreEqual("global-agent", latestActivity.AgentId)
        Assert.AreEqual(ActivityType.Progress, latestActivity.ActivityType)
        Assert.AreEqual("Global message activity", latestActivity.Message)

    [<TearDown>]
    member _.TearDown() =
        // テスト終了時のクリーンアップ
        ()

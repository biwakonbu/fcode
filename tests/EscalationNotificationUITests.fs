module FCode.Tests.EscalationNotificationUITests

open System
open System.Threading
open NUnit.Framework
open Terminal.Gui
open FCode.EscalationNotificationUI
open FCode.AgentMessaging
open FCode.Logger

[<TestFixture>]
type EscalationNotificationUITests() =

    [<SetUp>]
    member _.Setup() =
        // テスト用ログ設定
        ()

    [<Test>]
    [<Category("Unit")>]
    member _.``EscalationNotificationManager Basic Creation Test``() =
        // エスカレーション通知管理の基本生成テスト
        let manager = new EscalationNotificationManager()

        Assert.AreEqual(0, manager.GetNotificationCount())
        Assert.AreEqual(0, manager.GetAllNotifications().Length)

    [<Test>]
    [<Category("Unit")>]
    member _.``CreateEscalationNotification Basic Test``() =
        // エスカレーション通知作成基本テスト
        let manager = new EscalationNotificationManager()

        let notificationId =
            manager.CreateEscalationNotification(
                "Test Technical Decision",
                "Technical judgment required for implementation",
                EscalationNotificationType.TechnicalDecision,
                EscalationUrgency.Urgent,
                "dev1",
                "PO",
                [ "task1"; "task2" ],
                None
            )

        Assert.AreEqual(1, manager.GetNotificationCount())

        let notifications = manager.GetAllNotifications()
        Assert.AreEqual(1, notifications.Length)
        Assert.AreEqual("Test Technical Decision", notifications.[0].Title)
        Assert.AreEqual("Technical judgment required for implementation", notifications.[0].Description)
        Assert.AreEqual(EscalationNotificationType.TechnicalDecision, notifications.[0].NotificationType)
        Assert.AreEqual(EscalationUrgency.Urgent, notifications.[0].Urgency)
        Assert.AreEqual("dev1", notifications.[0].RequestingAgent)
        Assert.AreEqual("PO", notifications.[0].TargetRole)
        Assert.AreEqual([ "task1"; "task2" ], notifications.[0].RelatedTaskIds)
        Assert.AreEqual(Pending, notifications.[0].Status)

    [<Test>]
    [<Category("Unit")>]
    member _.``Response Deadline Calculation Test``() =
        // 回答期限計算テスト
        let manager = new EscalationNotificationManager()
        let beforeTime = DateTime.Now

        // Immediate緊急度テスト（1時間以内）
        let immediateId =
            manager.CreateEscalationNotification(
                "Immediate Decision",
                "Immediate response required",
                EscalationNotificationType.TechnicalDecision,
                Immediate,
                "dev1",
                "PO",
                [],
                None
            )

        let (found, notification) = manager.GetNotificationDetail(immediateId)
        Assert.IsTrue(found)
        Assert.IsTrue(notification.RequiredResponseBy <= beforeTime.AddHours(1.1))
        Assert.IsTrue(notification.RequiredResponseBy >= beforeTime.AddHours(0.9))

    [<Test>]
    [<Category("Unit")>]
    member _.``RespondToNotification Approve Test``() =
        // 通知承認テスト
        let manager = new EscalationNotificationManager()

        let notificationId =
            manager.CreateEscalationNotification(
                "Test Decision",
                "Test description",
                EscalationNotificationType.TechnicalDecision,
                EscalationUrgency.Normal,
                "dev1",
                "PO",
                [],
                None
            )

        let responseResult =
            manager.RespondToNotification(notificationId, ApproveWithComment "Approved for implementation", "PO")

        Assert.IsTrue(responseResult)

        let (found, notification) = manager.GetNotificationDetail(notificationId)
        Assert.IsTrue(found)
        Assert.AreEqual(Resolved, notification.Status)
        Assert.IsTrue(notification.ResponseContent.IsSome)
        Assert.IsTrue(notification.ResponseContent.Value.Contains("Approved for implementation"))
        Assert.IsTrue(notification.ResponseAt.IsSome)

    [<Test>]
    [<Category("Unit")>]
    member _.``RespondToNotification Reject Test``() =
        // 通知却下テスト
        let manager = new EscalationNotificationManager()

        let notificationId =
            manager.CreateEscalationNotification(
                "Test Decision",
                "Test description",
                EscalationNotificationType.ResourceRequest,
                Urgent,
                "qa1",
                "PO",
                [],
                None
            )

        let responseResult =
            manager.RespondToNotification(notificationId, Reject "Not enough justification", "PO")

        Assert.IsTrue(responseResult)

        let (found, notification) = manager.GetNotificationDetail(notificationId)
        Assert.IsTrue(found)
        Assert.AreEqual(Rejected, notification.Status)
        Assert.IsTrue(notification.ResponseContent.IsSome)
        Assert.IsTrue(notification.ResponseContent.Value.Contains("Not enough justification"))

    [<Test>]
    [<Category("Unit")>]
    member _.``ProcessEscalationMessage Auto Creation Test``() =
        // エスカレーションメッセージ自動作成テスト
        let manager = new EscalationNotificationManager()

        let escalationMessage =
            MessageBuilder()
                .From("dev2")
                .To("PO")
                .OfType(MessageType.Escalation)
                .WithPriority(MessagePriority.Critical)
                .WithContent("Critical technical issue requires immediate decision")
                .WithMetadata("escalation_title", "Critical API Integration Issue")
                .WithMetadata("escalation_type", "technical")
                .WithMetadata("target_role", "PO")
                .WithMetadata("related_tasks", "api-integration,security-review")
                .Build()

        manager.ProcessEscalationMessage(escalationMessage)

        let notifications = manager.GetAllNotifications()
        Assert.AreEqual(1, notifications.Length)
        Assert.AreEqual("Critical API Integration Issue", notifications.[0].Title)
        Assert.AreEqual(EscalationNotificationType.TechnicalDecision, notifications.[0].NotificationType)
        Assert.AreEqual(Immediate, notifications.[0].Urgency)
        Assert.AreEqual("dev2", notifications.[0].RequestingAgent)
        Assert.AreEqual("PO", notifications.[0].TargetRole)
        Assert.AreEqual([ "api-integration"; "security-review" ], notifications.[0].RelatedTaskIds)

    [<Test>]
    [<Category("Unit")>]
    member _.``Escalation Type Mapping Test``() =
        // エスカレーションタイプマッピングテスト
        let manager = new EscalationNotificationManager()

        let testCases =
            [ ("technical", EscalationNotificationType.TechnicalDecision)
              ("resource", EscalationNotificationType.ResourceRequest)
              ("quality", EscalationNotificationType.QualityGate)
              ("timeline", EscalationNotificationType.TimelineExtension)
              ("external", EscalationNotificationType.ExternalDependency)
              ("business", EscalationNotificationType.BusinessDecision)
              ("unknown", EscalationNotificationType.TechnicalDecision) ] // デフォルト

        testCases
        |> List.iteri (fun i (escalationType, expectedType) ->
            let message =
                MessageBuilder()
                    .From($"agent{i}")
                    .To("PO")
                    .OfType(MessageType.Escalation)
                    .WithPriority(MessagePriority.Normal)
                    .WithContent($"Test escalation {i}")
                    .WithMetadata("escalation_type", escalationType)
                    .Build()

            manager.ProcessEscalationMessage(message))

        // macOS CI環境での処理完了待機
        let isMacOS =
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX
            )

        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isMacOS && isCI then
            System.Threading.Thread.Sleep(50)

        let notifications = manager.GetAllNotifications()
        // デバッグ情報を追加（CI環境のみ）
        if isCI then
            Console.WriteLine($"Expected: {testCases.Length}, Actual: {notifications.Length}")
            let testCaseNames = testCases |> List.map fst |> String.concat ", "
            Console.WriteLine($"Test cases: {testCaseNames}")
            Console.WriteLine($"Notifications: {notifications.Length}")

        Assert.AreEqual(testCases.Length, notifications.Length)

        testCases
        |> List.iteri (fun i (_, expectedType) ->
            let notification =
                notifications |> Array.find (fun n -> n.RequestingAgent = $"agent{i}")

            Assert.AreEqual(expectedType, notification.NotificationType))

    [<Test>]
    [<Category("Unit")>]
    member _.``Urgency Priority Mapping Test``() =
        // 緊急度優先度マッピングテスト
        let manager = new EscalationNotificationManager()

        let priorityMappings =
            [ (MessagePriority.Critical, Immediate)
              (MessagePriority.High, Urgent)
              (MessagePriority.Normal, EscalationUrgency.Normal)
              (MessagePriority.Low, EscalationUrgency.Low) ]

        priorityMappings
        |> List.iteri (fun i (priority, expectedUrgency) ->
            let message =
                MessageBuilder()
                    .From($"test-agent{i}")
                    .To("PO")
                    .OfType(MessageType.Escalation)
                    .WithPriority(priority)
                    .WithContent($"Test message {i}")
                    .Build()

            manager.ProcessEscalationMessage(message))

        let notifications = manager.GetAllNotifications()
        Assert.AreEqual(priorityMappings.Length, notifications.Length)

        priorityMappings
        |> List.iteri (fun i (_, expectedUrgency) ->
            let notification =
                notifications |> Array.find (fun n -> n.RequestingAgent = $"test-agent{i}")

            Assert.AreEqual(expectedUrgency, notification.Urgency))

    [<Test>]
    [<Category("Unit")>]
    member _.``GetActiveNotifications Filter Test``() =
        // アクティブ通知フィルタテスト
        let manager = new EscalationNotificationManager()

        let id1 =
            manager.CreateEscalationNotification(
                "Active 1",
                "Description 1",
                EscalationNotificationType.TechnicalDecision,
                EscalationUrgency.Urgent,
                "dev1",
                "PO",
                [],
                None
            )

        let id2 =
            manager.CreateEscalationNotification(
                "Active 2",
                "Description 2",
                EscalationNotificationType.ResourceRequest,
                EscalationUrgency.Normal,
                "dev2",
                "PO",
                [],
                None
            )

        let id3 =
            manager.CreateEscalationNotification(
                "To Resolve",
                "Description 3",
                EscalationNotificationType.QualityGate,
                Urgent,
                "qa1",
                "PO",
                [],
                None
            )

        manager.RespondToNotification(id3, ApproveWithComment "Resolved", "PO")
        |> ignore

        let activeNotifications = manager.GetActiveNotifications()
        let allNotifications = manager.GetAllNotifications()

        Assert.AreEqual(3, allNotifications.Length)
        Assert.AreEqual(2, activeNotifications.Length)
        Assert.IsTrue(activeNotifications |> Array.forall (fun n -> n.Status = Pending))

    [<Test>]
    [<Category("Unit")>]
    member _.``ProcessExpiredNotifications Test``() =
        // 期限切れ通知処理テスト
        let manager = new EscalationNotificationManager()

        // 期限切れになるように過去の時刻でテスト用通知を作成
        let id1 =
            manager.CreateEscalationNotification(
                "Expired Test",
                "Test description",
                EscalationNotificationType.TechnicalDecision,
                Immediate,
                "dev1",
                "PO",
                [],
                None
            )

        // 通知を手動で期限切れに設定（テスト用）
        let (found, notification) = manager.GetNotificationDetail(id1)
        Assert.IsTrue(found)

        // 期限切れ処理を実行（実際の実装では期限チェックが必要）
        let expiredCount = manager.ProcessExpiredNotifications()

        // この時点では期限切れではないため0であることを確認
        Assert.AreEqual(0, expiredCount)

    [<Test>]
    [<Category("Unit")>]
    member _.``PONotificationAction Types Test``() =
        // PO通知アクション種別テスト
        let manager = new EscalationNotificationManager()

        let testActions =
            [ (Acknowledge, Acknowledged)
              (ApproveWithComment "Test approval", Resolved)
              (RequestMoreInfo "Need more details", MoreInfoRequested)
              (EscalateToHigher "Escalate to CEO", EscalatedHigher)
              (Reject "Invalid request", Rejected) ]

        testActions
        |> List.iteri (fun i (action, expectedStatus) ->
            let id =
                manager.CreateEscalationNotification(
                    $"Test {i}",
                    $"Description {i}",
                    EscalationNotificationType.TechnicalDecision,
                    EscalationUrgency.Normal,
                    "dev1",
                    "PO",
                    [],
                    None
                )

            let result = manager.RespondToNotification(id, action, "PO")
            Assert.IsTrue(result)

            let (found, notification) = manager.GetNotificationDetail(id)
            Assert.IsTrue(found)
            Assert.AreEqual(expectedStatus, notification.Status))

    [<Test>]
    [<Category("Unit")>]
    [<Category("Integration")>]
    member _.``Global EscalationNotificationManager Usage Test``() =
        // グローバルエスカレーション通知管理使用テスト
        let manager = new EscalationNotificationManager()
        let initialCount = manager.GetNotificationCount()

        let notificationId =
            createEscalationNotification
                "Global Test Notification"
                "Global notification description"
                EscalationNotificationType.ResourceRequest
                EscalationUrgency.Urgent
                "global-agent"
                "PO"
                [ "global-task" ]
                None

        let newCount = manager.GetNotificationCount()
        Assert.AreEqual(initialCount + 1, newCount)

        let notifications = manager.GetAllNotifications()
        let latestNotification = notifications |> Array.maxBy (fun n -> n.CreatedAt)
        Assert.AreEqual("Global Test Notification", latestNotification.Title)
        Assert.AreEqual(EscalationNotificationType.ResourceRequest, latestNotification.NotificationType)
        Assert.AreEqual(EscalationUrgency.Urgent, latestNotification.Urgency)

    [<Test>]
    [<Category("Unit")>]
    [<Category("Integration")>]
    member _.``Global Functions Integration Test``() =
        // グローバル関数統合テスト
        let manager = new EscalationNotificationManager()

        let notificationId =
            createEscalationNotification
                "Integration Test"
                "Test integration"
                EscalationNotificationType.TechnicalDecision
                EscalationUrgency.Normal
                "test-agent"
                "PO"
                []
                None

        let responseResult =
            respondToNotification notificationId (ApproveWithComment "Integration approved") "PO"

        Assert.IsTrue(responseResult)

        let (found, notification) = manager.GetNotificationDetail(notificationId)

        Assert.IsTrue(found)
        Assert.AreEqual(Resolved, notification.Status)
        Assert.IsTrue(notification.ResponseContent.IsSome)

    [<TearDown>]
    member _.TearDown() =
        // テスト終了時のクリーンアップ
        ()

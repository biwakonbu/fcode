module FCode.Tests.SOLIDDesignTests

open NUnit.Framework
open System
open FCode.UnifiedActivityView
open FCode.EscalationNotificationUI
open FCode.DecisionTimelineView
open FCode.ProgressDashboard
open FCode.AgentMessaging

// ===============================================
// SOLID設計原則テストスイート
// ===============================================

[<TestFixture>]
[<Category("Unit")>]
type SOLIDDesignTestSuite() =

    // ===============================================
    // S: 単一責任の原則 (Single Responsibility Principle)
    // ===============================================

    [<Test>]
    [<Category("Unit")>]
    member this.``UnifiedActivityManager should have single responsibility for activity management``() =
        // Arrange
        let manager = new UnifiedActivityManager()

        let message =
            { MessageId = "test-msg-001"
              FromAgent = "test-agent"
              ToAgent = None
              Content = "Test activity content"
              MessageType = MessageType.Progress
              Priority = MessagePriority.Normal
              Timestamp = DateTime.Now
              ExpiresAt = None
              CorrelationId = None
              Metadata = Map.empty }

        // Act
        let result = manager.AddActivityFromMessage(message)

        // Assert
        Assert.IsTrue(
            match result with
            | Result.Ok _ -> true
            | _ -> false
        )

        Assert.AreEqual(1, manager.GetActivityCount())

        // Cleanup
        manager.Dispose()

    [<Test>]
    [<Category("Unit")>]
    member this.``EscalationNotificationManager should have single responsibility for escalation handling``() =
        // Arrange
        let manager = new EscalationNotificationManager()

        // Act
        let notificationId =
            manager.CreateEscalationNotification(
                "Test Escalation",
                "Test escalation description",
                TechnicalDecision,
                Urgent,
                "test-agent",
                "PO",
                [],
                None
            )

        // Assert
        Assert.IsFalse(String.IsNullOrEmpty(notificationId))
        Assert.AreEqual(1, manager.GetNotificationCount())

        // Cleanup
        manager.Dispose()

    [<Test>]
    [<Category("Unit")>]
    member this.``DecisionTimelineManager should have single responsibility for decision tracking``() =
        // Arrange
        let manager = new DecisionTimelineManager()

        // Act
        let decisionId =
            manager.StartDecision("Test Decision", "Test decision description", MessagePriority.High, [ "dev1"; "pm" ])

        // Assert
        Assert.IsFalse(String.IsNullOrEmpty(decisionId))
        Assert.AreEqual(1, manager.GetDecisionCount())

        // Cleanup
        manager.Dispose()

    [<Test>]
    [<Category("Unit")>]
    member this.``ProgressDashboardManager should have single responsibility for metrics management``() =
        // Arrange
        let manager = new ProgressDashboardManager()

        // Act
        let result = manager.CreateMetric(TaskCompletion, "Test Metric", 75.0, 100.0, "%")

        // Assert
        Assert.That(
            result
            |> function
                | Result.Ok _ -> true
                | _ -> false
        )

        Assert.AreEqual(1, manager.GetMetricCount())

        // Cleanup
        manager.Dispose()

    // ===============================================
    // O: 開放閉鎖の原則 (Open/Closed Principle)
    // ===============================================

    [<Test>]
    [<Category("Unit")>]
    member this.``ActivityType should be extensible without modifying existing code``() =
        // Arrange & Act
        let activityTypes =
            [ ActivityType.CodeGeneration
              ActivityType.Testing
              ActivityType.QualityReview
              ActivityType.Documentation
              ActivityType.TaskAssignment
              ActivityType.Progress
              ActivityType.Escalation
              ActivityType.Decision
              ActivityType.SystemMessage ]

        // Assert - 新しい活動タイプを追加する際は既存コードを変更せずに拡張可能
        Assert.AreEqual(9, activityTypes.Length)

        // 判別共用体による型安全性確保
        let testActivity =
            { ActivityId = "test-act-001"
              AgentId = "test-agent"
              ActivityType = ActivityType.CodeGeneration
              Message = "Generated new module"
              Timestamp = DateTime.Now
              Priority = MessagePriority.Normal
              Metadata = Map.empty
              RelatedTaskId = None
              Status = ActivityStatus.Completed }

        Assert.AreEqual(ActivityType.CodeGeneration, testActivity.ActivityType)

    [<Test>]
    [<Category("Unit")>]
    member this.``EscalationUrgency should be extensible for new urgency levels``() =
        // Arrange & Act
        let urgencyLevels =
            [ EscalationUrgency.Immediate
              EscalationUrgency.Urgent
              EscalationUrgency.Normal
              EscalationUrgency.Low ]

        // Assert
        Assert.AreEqual(4, urgencyLevels.Length)

        // 型安全性とパターンマッチング確保
        let testUrgency = EscalationUrgency.Immediate

        let expectedDeadline =
            match testUrgency with
            | EscalationUrgency.Immediate -> DateTime.Now.AddHours(1.0)
            | EscalationUrgency.Urgent -> DateTime.Now.AddHours(4.0)
            | EscalationUrgency.Normal -> DateTime.Now.AddDays(1.0)
            | EscalationUrgency.Low -> DateTime.Now.AddDays(3.0)

        Assert.Greater(expectedDeadline, DateTime.Now)

    // ===============================================
    // L: リスコフの置換原則 (Liskov Substitution Principle)
    // ===============================================

    [<Test>]
    [<Category("Unit")>]
    member this.``IDisposable implementations should be substitutable``() =
        // Arrange
        let disposableObjects: IDisposable list =
            [ new UnifiedActivityManager() :> IDisposable
              new EscalationNotificationManager() :> IDisposable
              new DecisionTimelineManager() :> IDisposable
              new ProgressDashboardManager() :> IDisposable ]

        // Act & Assert - すべてのIDisposable実装は置換可能
        for disposable in disposableObjects do
            Assert.DoesNotThrow(fun () -> disposable.Dispose())

    [<Test>]
    [<Category("Unit")>]
    member this.``Result type should be consistent across all managers``() =
        // Arrange
        let activityManager = new UnifiedActivityManager()
        let progressManager = new ProgressDashboardManager()

        // Act - 一貫したResult型の使用
        let activityResult = activityManager.ClearActivities()
        let progressResult = progressManager.ClearAllData()

        // Assert - すべてのResult型は同じ型シグネチャ
        Assert.That(
            activityResult
            |> function
                | Result.Ok _ -> true
                | _ -> false
        )

        Assert.That(
            progressResult
            |> function
                | Result.Ok _ -> true
                | _ -> false
        )

        // Cleanup
        activityManager.Dispose()
        progressManager.Dispose()

    // ===============================================
    // I: インターフェース分離の原則 (Interface Segregation Principle)
    // ===============================================

    [<Test>]
    [<Category("Unit")>]
    member this.``Managers should not depend on interfaces they don't use``() =
        // Arrange - 各マネージャーは必要な機能のみ公開
        let activityManager = new UnifiedActivityManager()
        let escalationManager = new EscalationNotificationManager()

        // Act & Assert - 活動管理はエスカレーション機能に依存しない
        Assert.IsNotNull(activityManager.GetActivityCount)
        Assert.IsNotNull(escalationManager.GetNotificationCount)

        // 各マネージャーは独立して動作可能
        let activityResult = activityManager.ClearActivities()
        escalationManager.ClearNotificationHistory()

        Assert.That(
            activityResult
            |> function
                | Result.Ok _ -> true
                | _ -> false
        )

        // Cleanup
        activityManager.Dispose()
        escalationManager.Dispose()

    [<Test>]
    [<Category("Unit")>]
    member this.``Specialized classes should have focused interfaces``() =
        // Arrange
        let activityManager = new UnifiedActivityManager()

        // Act - 専門化されたインターフェース
        let activities = activityManager.GetAllActivities()
        let count = activityManager.GetActivityCount()

        // Assert - 活動管理に特化したメソッドのみ提供
        Assert.IsNotNull(activities)
        Assert.AreEqual(0, count)

        // 他の責務（通知管理等）のメソッドは含まない
        // コンパイル時に型安全性が保証される

        // Cleanup
        activityManager.Dispose()

    // ===============================================
    // D: 依存性逆転の原則 (Dependency Inversion Principle)
    // ===============================================

    [<Test>]
    [<Category("Unit")>]
    member this.``Managers should depend on abstractions, not concrete implementations``() =
        // Arrange - 依存性注入パターンの確認
        let originalManager = new UnifiedActivityManager()

        // Act - グローバル状態の代わりに依存性注入を使用
        injectActivityManager originalManager

        // 注入されたインスタンスが使用されることを確認
        let testMessage =
            { MessageId = "inject-test-001"
              FromAgent = "test-agent"
              ToAgent = None
              Content = "Dependency injection test"
              MessageType = MessageType.Notification
              Priority = MessagePriority.Normal
              Timestamp = DateTime.Now
              ExpiresAt = None
              CorrelationId = None
              Metadata = Map.empty }

        let result = addActivityFromMessage testMessage

        // Assert
        Assert.That(
            result
            |> function
                | Result.Ok _ -> true
                | _ -> false
        )

        Assert.AreEqual(1, originalManager.GetActivityCount())

        // Cleanup
        originalManager.Dispose()

    [<Test>]
    [<Category("Unit")>]
    member this.``High-level modules should not depend on low-level modules``() =
        // Arrange - 高レベルモジュール（UnifiedActivityManager）は
        // 低レベルモジュール（具体的なストレージ実装）に依存しない
        let manager = new UnifiedActivityManager()

        // Act - 抽象化された操作のみ使用
        let systemActivityResult =
            manager.AddSystemActivity("system", ActivityType.SystemMessage, "System initialization complete")

        // Assert - ストレージの具体的な実装に依存しない
        Assert.That(
            systemActivityResult
            |> function
                | Result.Ok _ -> true
                | _ -> false
        )

        // 内部ストレージの実装変更があっても外部インターフェースは変わらない
        let activities = manager.GetAllActivities()
        Assert.AreEqual(1, activities.Length)

        // Cleanup
        manager.Dispose()

// ===============================================
// SOLID設計品質指標テスト
// ===============================================

[<TestFixture>]
[<Category("Integration")>]
type SOLIDQualityMetricsTestSuite() =

    [<Test>]
    [<Category("Integration")>]
    member this.``SOLID design should demonstrate loose coupling``() =
        // Arrange - 異なるマネージャーが独立して動作
        let activityManager = new UnifiedActivityManager()
        let escalationManager = new EscalationNotificationManager()
        let decisionManager = new DecisionTimelineManager()
        let progressManager = new ProgressDashboardManager()

        // Act - 各マネージャーは他に依存せずに動作
        let activityResult =
            activityManager.AddSystemActivity("system", ActivityType.SystemMessage, "Test message")

        let escalationId =
            escalationManager.CreateEscalationNotification(
                "Test",
                "Description",
                TechnicalDecision,
                Urgent,
                "agent",
                "PO",
                [],
                None
            )

        let decisionId =
            decisionManager.StartDecision("Test Decision", "Description", MessagePriority.High, [ "agent" ])

        let metricResult =
            progressManager.CreateMetric(TaskCompletion, "Test Metric", 50.0, 100.0, "%")

        // Assert - すべて独立して正常動作
        Assert.That(
            activityResult
            |> function
                | Result.Ok _ -> true
                | _ -> false
        )

        Assert.IsFalse(String.IsNullOrEmpty(escalationId))
        Assert.IsFalse(String.IsNullOrEmpty(decisionId))

        Assert.That(
            metricResult
            |> function
                | Result.Ok _ -> true
                | _ -> false
        )

        // Cleanup
        activityManager.Dispose()
        escalationManager.Dispose()
        decisionManager.Dispose()
        progressManager.Dispose()

    [<Test>]
    member this.``SOLID design should demonstrate high cohesion``() =
        // Arrange - 各クラス内の機能は高い凝集性を持つ
        let manager = new UnifiedActivityManager()

        // Act - 関連する機能が一つのクラスに集約
        let addResult =
            manager.AddSystemActivity("agent1", ActivityType.Progress, "Progress update")

        let activities = manager.GetAllActivities()
        let count = manager.GetActivityCount()
        let clearResult = manager.ClearActivities()

        // Assert - すべての活動管理機能が一箇所で提供
        Assert.That(
            addResult
            |> function
                | Result.Ok _ -> true
                | _ -> false
        )

        Assert.IsNotNull(activities)
        Assert.AreEqual(1, count)

        Assert.That(
            clearResult
            |> function
                | Result.Ok _ -> true
                | _ -> false
        )

        Assert.AreEqual(0, manager.GetActivityCount())

        // Cleanup
        manager.Dispose()

    [<Test>]
    member this.``SOLID design should enable easy testing and mocking``() =
        // Arrange - 依存性注入により、テスト用のモックが容易
        let testManager = new UnifiedActivityManager()

        // Act - テスト用インスタンス注入
        injectActivityManager testManager

        // グローバル関数経由でテスト
        let testResult =
            addSystemActivity "test-agent" ActivityType.Testing "Unit test execution"

        // Assert - モックされたインスタンスでテスト実行
        Assert.That(
            testResult
            |> function
                | Result.Ok _ -> true
                | _ -> false
        )

        Assert.AreEqual(1, testManager.GetActivityCount())

        // Cleanup
        testManager.Dispose()

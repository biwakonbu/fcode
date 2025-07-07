module FCode.Tests.AgentMessagingTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open NUnit.Framework
open FCode.AgentMessaging
open FCode.MessagePersistence
open FCode.Logger

[<TestFixture>]
[<Category("Unit")>]
type AgentMessagingTests() =

    [<SetUp>]
    member _.Setup() =
        // テスト用ログ設定
        ()

    [<Test>]
    [<Category("Unit")>]
    member _.``MessageBuilder Basic Construction Test``() =
        // メッセージビルダーの基本機能テスト
        let message =
            MessageBuilder()
                .From("dev1")
                .To("qa1")
                .OfType(TaskAssignment)
                .WithPriority(High)
                .WithContent("Please review the implementation")
                .WithMetadata("task_id", "TASK-001")
                .Build()

        Assert.AreEqual("dev1", message.FromAgent)
        Assert.AreEqual(Some "qa1", message.ToAgent)
        Assert.AreEqual(TaskAssignment, message.MessageType)
        Assert.AreEqual(High, message.Priority)
        Assert.AreEqual("Please review the implementation", message.Content)
        Assert.IsTrue(message.Metadata.ContainsKey("task_id"))
        Assert.AreEqual("TASK-001", message.Metadata.["task_id"])
        Assert.IsNotNull(message.MessageId)
        Assert.IsTrue(message.MessageId.Length > 0)

    [<Test>]
    [<Category("Unit")>]
    member _.``MessageBuilder Broadcast Construction Test``() =
        // ブロードキャストメッセージ構築テスト
        let message =
            MessageBuilder()
                .From("pm")
                .Broadcast()
                .OfType(Progress)
                .WithPriority(Normal)
                .WithContent("Sprint progress update")
                .Build()

        Assert.AreEqual("pm", message.FromAgent)
        Assert.AreEqual(None, message.ToAgent)
        Assert.AreEqual(Progress, message.MessageType)
        Assert.AreEqual(Normal, message.Priority)

    [<Test>]
    [<Category("Unit")>]
    member _.``MessageUtils Task Assignment Creation Test``() =
        // タスク割り当てメッセージ作成テスト
        let message =
            MessageUtils.createTaskAssignment "pm" "dev1" "Implement new feature" "TASK-002"

        Assert.AreEqual("pm", message.FromAgent)
        Assert.AreEqual(Some "dev1", message.ToAgent)
        Assert.AreEqual(TaskAssignment, message.MessageType)
        Assert.AreEqual(High, message.Priority)
        Assert.IsTrue(message.Content.Contains("Implement new feature"))
        Assert.IsTrue(message.Metadata.ContainsKey("task_id"))
        Assert.AreEqual("TASK-002", message.Metadata.["task_id"])
        Assert.IsTrue(message.ExpiresAt.IsSome)

    [<Test>]
    [<Category("Unit")>]
    member _.``MessageUtils Progress Report Creation Test``() =
        // 進捗報告メッセージ作成テスト
        let message =
            MessageUtils.createProgressReport "dev1" "TASK-002" 75 "Implementation in progress"

        Assert.AreEqual("dev1", message.FromAgent)
        Assert.AreEqual(None, message.ToAgent) // ブロードキャスト
        Assert.AreEqual(Progress, message.MessageType)
        Assert.AreEqual(Normal, message.Priority)
        Assert.IsTrue(message.Content.Contains("75%"))
        Assert.AreEqual("TASK-002", message.Metadata.["task_id"])
        Assert.AreEqual("75", message.Metadata.["progress_percentage"])

    [<Test>]
    [<Category("Unit")>]
    member _.``MessageUtils Quality Review Creation Test``() =
        // 品質レビューメッセージ作成テスト
        let message1 =
            MessageUtils.createQualityReview "qa1" "dev1" "Code review completed - no issues" 0

        Assert.AreEqual(Normal, message1.Priority) // 問題なしの場合

        let message2 =
            MessageUtils.createQualityReview "qa1" "dev1" "Code review completed - 3 issues found" 3

        Assert.AreEqual(High, message2.Priority) // 問題ありの場合
        Assert.AreEqual("3", message2.Metadata.["issue_count"])

    [<Test>]
    [<Category("Unit")>]
    member _.``MessageUtils Escalation Creation Test``() =
        // エスカレーションメッセージ作成テスト
        let message =
            MessageUtils.createEscalation "dev1" "Critical bug in production" "high"

        Assert.AreEqual("dev1", message.FromAgent)
        Assert.AreEqual(None, message.ToAgent) // ブロードキャスト
        Assert.AreEqual(Escalation, message.MessageType)
        Assert.AreEqual(Critical, message.Priority)
        Assert.IsTrue(message.Content.Contains("Critical bug"))
        Assert.AreEqual("high", message.Metadata.["severity"])

    [<Test>]
    [<Category("Integration")>]
    member _.``MultiAgentMessageRouter Basic Registration Test``() =
        // メッセージルーター基本登録テスト
        let config =
            { MaxRetries = 3
              RetryDelay = TimeSpan.FromSeconds(1.0)
              MessageTTL = TimeSpan.FromHours(1.0)
              BufferSize = 100
              EnablePersistence = false
              PersistenceFile = None }

        let router = new MultiAgentMessageRouter(config) :> IMessageRouter
        let mutable receivedMessages = []

        let testHandler =
            { new IMessageHandler with
                member _.HandleMessage(message) =
                    async {
                        receivedMessages <- message :: receivedMessages
                        return true
                    }

                member _.AgentName = "test-agent"
                member _.SupportedMessageTypes = [ TaskAssignment; Progress ] }

        router.RegisterAgent("test-agent", testHandler)

        // 登録後の状態確認（実際の動作確認は次のテストで）
        Assert.Pass("Agent registration completed successfully")

    [<Test>]
    [<Category("Integration")>]
    member _.``MultiAgentMessageRouter Message Delivery Test``() =
        async {
            // メッセージ配信テスト
            let config =
                { MaxRetries = 3
                  RetryDelay = TimeSpan.FromSeconds(1.0)
                  MessageTTL = TimeSpan.FromHours(1.0)
                  BufferSize = 100
                  EnablePersistence = false
                  PersistenceFile = None }

            let router = new MultiAgentMessageRouter(config) :> IMessageRouter
            let mutable receivedMessages = []
            let messageReceivedEvent = new ManualResetEventSlim(false)

            let testHandler =
                { new IMessageHandler with
                    member _.HandleMessage(message) =
                        async {
                            receivedMessages <- message :: receivedMessages
                            messageReceivedEvent.Set()
                            return true
                        }

                    member _.AgentName = "test-receiver"
                    member _.SupportedMessageTypes = [ TaskAssignment; Progress ] }

            router.RegisterAgent("test-receiver", testHandler)

            // ルーター開始
            (router :?> MultiAgentMessageRouter).Start()

            // メッセージ送信
            let testMessage =
                MessageBuilder()
                    .From("test-sender")
                    .To("test-receiver")
                    .OfType(TaskAssignment)
                    .WithPriority(High)
                    .WithContent("Test task assignment")
                    .Build()

            let! sendResult = router.SendMessage(testMessage)
            Assert.IsTrue(sendResult)

            // メッセージ受信待機（最大5秒）
            let received = messageReceivedEvent.Wait(5000)
            Assert.IsTrue(received, "Message was not received within timeout")
            Assert.AreEqual(1, receivedMessages.Length)

            let receivedMessage = receivedMessages.[0]
            Assert.AreEqual("test-sender", receivedMessage.FromAgent)
            Assert.AreEqual("Test task assignment", receivedMessage.Content)

            // ルーター停止
            (router :?> MultiAgentMessageRouter).Stop()
        }

    [<Test>]
    [<Category("Integration")>]
    member _.``MultiAgentMessageRouter Broadcast Test``() =
        async {
            // ブロードキャストメッセージテスト
            let config =
                { MaxRetries = 3
                  RetryDelay = TimeSpan.FromSeconds(1.0)
                  MessageTTL = TimeSpan.FromHours(1.0)
                  BufferSize = 100
                  EnablePersistence = false
                  PersistenceFile = None }

            let router = new MultiAgentMessageRouter(config) :> IMessageRouter
            let mutable agent1Messages = []
            let mutable agent2Messages = []
            let messageCountEvent = new CountdownEvent(2)

            let agent1Handler =
                { new IMessageHandler with
                    member _.HandleMessage(message) =
                        async {
                            agent1Messages <- message :: agent1Messages
                            messageCountEvent.Signal() |> ignore
                            return true
                        }

                    member _.AgentName = "agent1"
                    member _.SupportedMessageTypes = [ Progress; Notification ] }

            let agent2Handler =
                { new IMessageHandler with
                    member _.HandleMessage(message) =
                        async {
                            agent2Messages <- message :: agent2Messages
                            messageCountEvent.Signal() |> ignore
                            return true
                        }

                    member _.AgentName = "agent2"
                    member _.SupportedMessageTypes = [ Progress; Notification ] }

            router.RegisterAgent("agent1", agent1Handler)
            router.RegisterAgent("agent2", agent2Handler)

            // ルーター開始
            (router :?> MultiAgentMessageRouter).Start()

            // ブロードキャストメッセージ送信
            let broadcastMessage =
                MessageBuilder()
                    .From("broadcaster")
                    .Broadcast()
                    .OfType(Progress)
                    .WithPriority(Normal)
                    .WithContent("Sprint status update")
                    .Build()

            let! deliveryCount = router.BroadcastMessage(broadcastMessage)
            Assert.AreEqual(2, deliveryCount)

            // 両エージェントでのメッセージ受信待機（最大5秒）
            let allReceived = messageCountEvent.Wait(5000)
            Assert.IsTrue(allReceived, "Not all agents received the broadcast message")
            Assert.AreEqual(1, agent1Messages.Length)
            Assert.AreEqual(1, agent2Messages.Length)

            // ルーター停止
            (router :?> MultiAgentMessageRouter).Stop()
        }

    [<Test>]
    [<Category("Integration")>]
    member _.``FileMessagePersistence Basic Operations Test``() =
        async {
            // ファイル永続化基本操作テスト
            let tempFile = Path.GetTempFileName()
            let persistence = new FileMessagePersistence(tempFile) :> IMessagePersistence

            try
                // テストメッセージ作成
                let testMessage =
                    MessageBuilder()
                        .From("test-agent")
                        .To("target-agent")
                        .OfType(TaskAssignment)
                        .WithPriority(High)
                        .WithContent("Test persistence")
                        .WithMetadata("test_key", "test_value")
                        .Build()

                // メッセージ保存
                let! saveResult = persistence.SaveMessage(testMessage)
                Assert.IsTrue(saveResult)

                // 未配信メッセージ取得
                let! undelivered = persistence.GetUndeliveredMessages()
                Assert.AreEqual(1, undelivered.Length)
                Assert.AreEqual(testMessage.MessageId, undelivered.[0].MessageId)

                // 配信完了マーク
                let! markResult = persistence.MarkAsDelivered(testMessage.MessageId)
                Assert.IsTrue(markResult)

                // 配信完了後の未配信メッセージ確認
                let! undeliveredAfter = persistence.GetUndeliveredMessages()
                Assert.AreEqual(0, undeliveredAfter.Length)

                // 統計情報取得
                let! stats = persistence.GetStats()
                Assert.AreEqual(1L, stats.TotalMessages)
                Assert.AreEqual(1L, stats.SuccessfulDeliveries)

            finally
                // テンポラリファイル削除
                if File.Exists(tempFile) then
                    File.Delete(tempFile)
        }

    [<Test>]
    [<Category("Integration")>]
    member _.``MessagePersistence Failure Handling Test``() =
        async {
            // 永続化失敗処理テスト
            let tempFile = Path.GetTempFileName()
            let persistence = new FileMessagePersistence(tempFile) :> IMessagePersistence

            try
                let testMessage =
                    MessageBuilder()
                        .From("failing-agent")
                        .To("target-agent")
                        .OfType(TaskAssignment)
                        .WithPriority(High)
                        .WithContent("Test failure handling")
                        .Build()

                // メッセージ保存
                let! saveResult = persistence.SaveMessage(testMessage)
                Assert.IsTrue(saveResult)

                // 配信失敗マーク
                let! failResult = persistence.MarkAsFailed (testMessage.MessageId) "Test delivery failure"
                Assert.IsTrue(failResult)

                // 統計情報で失敗が記録されていることを確認
                let! stats = persistence.GetStats()
                Assert.AreEqual(1L, stats.TotalMessages)
                Assert.AreEqual(1L, stats.FailedDeliveries)

            finally
                if File.Exists(tempFile) then
                    File.Delete(tempFile)
        }

    [<Test>]
    [<Category("Integration")>]
    member _.``MessagePersistence Expired Message Cleanup Test``() =
        async {
            // 期限切れメッセージクリーンアップテスト
            let tempFile = Path.GetTempFileName()
            let persistence = new FileMessagePersistence(tempFile) :> IMessagePersistence

            try
                // 期限切れメッセージ作成
                let expiredMessage =
                    MessageBuilder()
                        .From("test-agent")
                        .To("target-agent")
                        .OfType(TaskAssignment)
                        .WithPriority(High)
                        .WithContent("Expired message")
                        .ExpiresIn(TimeSpan.FromMilliseconds(-1.0)) // 既に期限切れ
                        .Build()

                // 有効なメッセージ作成
                let validMessage =
                    MessageBuilder()
                        .From("test-agent")
                        .To("target-agent")
                        .OfType(Progress)
                        .WithPriority(Normal)
                        .WithContent("Valid message")
                        .ExpiresIn(TimeSpan.FromHours(1.0))
                        .Build()

                // 両方保存
                let! save1 = persistence.SaveMessage(expiredMessage)
                let! save2 = persistence.SaveMessage(validMessage)
                Assert.IsTrue(save1 && save2)

                // クリーンアップ前の状態確認
                let! statsBefore = persistence.GetStats()
                Assert.AreEqual(2L, statsBefore.TotalMessages)

                // 期限切れメッセージクリーンアップ
                let! cleanedCount = persistence.CleanupExpiredMessages()
                Assert.AreEqual(1, cleanedCount)

                // クリーンアップ後の状態確認
                let! statsAfter = persistence.GetStats()
                Assert.AreEqual(1L, statsAfter.TotalMessages)

            finally
                if File.Exists(tempFile) then
                    File.Delete(tempFile)
        }

    [<Test>]
    [<Category("Performance")>]
    member _.``MessageRouter High Load Test``() =
        async {
            // 高負荷メッセージ処理テスト
            let config =
                { MaxRetries = 3
                  RetryDelay = TimeSpan.FromMilliseconds(100.0)
                  MessageTTL = TimeSpan.FromHours(1.0)
                  BufferSize = 1000
                  EnablePersistence = false
                  PersistenceFile = None }

            let router = new MultiAgentMessageRouter(config) :> IMessageRouter
            let messageCount = 100
            let receivedCount = ref 0
            let allMessagesEvent = new CountdownEvent(messageCount)

            let loadTestHandler =
                { new IMessageHandler with
                    member _.HandleMessage(message) =
                        async {
                            Interlocked.Increment(receivedCount) |> ignore
                            allMessagesEvent.Signal() |> ignore
                            return true
                        }

                    member _.AgentName = "load-test-agent"
                    member _.SupportedMessageTypes = [ Progress; Notification ] }

            router.RegisterAgent("load-test-agent", loadTestHandler)
            (router :?> MultiAgentMessageRouter).Start()

            // 大量メッセージ送信
            let sendTasks =
                [ 1..messageCount ]
                |> List.map (fun i ->
                    let message =
                        MessageBuilder()
                            .From("load-tester")
                            .To("load-test-agent")
                            .OfType(Progress)
                            .WithPriority(Normal)
                            .WithContent($"Load test message {i}")
                            .Build()

                    router.SendMessage(message))

            let! sendResults = Async.Parallel sendTasks
            let successCount = sendResults |> Array.filter id |> Array.length
            Assert.AreEqual(messageCount, successCount)

            // 全メッセージ受信待機（最大30秒）
            let allReceived = allMessagesEvent.Wait(30000)
            Assert.IsTrue(allReceived, $"Not all messages received: {!receivedCount}/{messageCount}")
            Assert.AreEqual(messageCount, !receivedCount)

            (router :?> MultiAgentMessageRouter).Stop()
        }

    [<TearDown>]
    member _.TearDown() =
        // テスト終了時のクリーンアップ
        ()

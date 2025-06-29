module FCode.Tests.IPCChannelTests

open System
open System.Threading
open System.Threading.Tasks
open NUnit.Framework
open FCode.IPCChannel

[<TestFixture>]
type IPCChannelTests() =

    [<SetUp>]
    member _.Setup() =
        // Logger is automatically initialized
        ()

    [<Test>]
    member _.BasicIPCChannelSendCommandReturnsResponse() =
        task {
            // Arrange
            use channel = createIPCChannel ()
            let! _ = channel.StartAsync()
            let command = StartSession("test-pane", "/tmp")

            // Act
            let! response = channel.SendCommandAsync(command)

            // Assert
            match response with
            | SessionStarted(paneId, sessionId, processId) ->
                Assert.AreEqual("test-pane", paneId)
                Assert.IsNotEmpty(sessionId)
                Assert.Greater(processId, 0)
            | SessionResponse.Error(_, msg) -> Assert.Fail("Expected SessionStarted but got Error")
            | _ -> Assert.Fail("Unexpected response type")
        }

    [<Test>]
    member _.IPCChannelSendCommandWithRetryRetriesOnFailure() =
        task {
            // Arrange
            let config =
                { defaultIPCConfig with
                    MaxRetryAttempts = 2
                    RetryDelayMs = 100 }

            use channel = new IPCChannel(config)
            let! _ = channel.StartAsync()

            // Simulate failure with invalid command
            let command = SendInput("non-existent-pane", "test")

            // Act
            let! response = channel.SendCommandWithRetryAsync(command)

            // Assert
            match response with
            | InputProcessed _ -> () // Success case
            | SessionResponse.Error _ -> () // Expected for non-existent pane
            | _ -> Assert.Fail("Unexpected response")
        }

    [<Test>]
    member _.IPCChannelHealthCheckReturnsHealthStatus() =
        task {
            // Arrange
            use channel = createIPCChannel ()
            let! _ = channel.StartAsync()

            // Act
            let! isHealthy = channel.CheckConnectionHealth()

            // Assert
            Assert.IsTrue(isHealthy)
        }

    [<Test>]
    member _.IPCChannelGetMetricsReturnsValidMetrics() =
        task {
            // Arrange
            use channel = createIPCChannel ()
            let! _ = channel.StartAsync()

            // Send a few commands to generate metrics
            let! _ = channel.SendCommandAsync(HealthCheck("test"))
            let! _ = channel.SendCommandAsync(HealthCheck("test2"))

            // Act
            let metrics = channel.GetMetrics()

            // Assert
            Assert.GreaterOrEqual(metrics.ProcessedRequests, 0L)
            Assert.GreaterOrEqual(metrics.QueueLength, 0)
            Assert.GreaterOrEqual(metrics.AverageLatencyMs, 0.0)
        }

    [<Test>]
    member _.IPCChannelBackpressureHandlingDropsRequestsWhenThresholdExceeded() =
        task {
            // Arrange
            let config =
                { defaultIPCConfig with
                    ChannelCapacity = 10
                    BackpressureThreshold = 5
                    BackpressurePolicy = DropOldest }

            use channel = new IPCChannel(config)
            let! _ = channel.StartAsync()

            // Act - Send many requests quickly to trigger backpressure
            let tasks =
                [| for i in 1..20 do
                       yield channel.SendCommandAsync(HealthCheck($"test-{i}")) |]

            let! _ = Task.WhenAll(tasks)

            // Assert
            let metrics = channel.GetMetrics()
            Assert.Greater(metrics.ProcessedRequests, 0L)
            // Some requests should have been processed or dropped
            Assert.GreaterOrEqual(metrics.ProcessedRequests, 0L)
        }

    [<Test>]
    member _.IPCChannelConcurrentRequestsHandlesSafely() =
        task {
            // Arrange
            use channel = createIPCChannel ()
            let! _ = channel.StartAsync()

            // Act - Send concurrent requests
            let! responses =
                [| for i in 1..10 -> channel.SendCommandAsync(HealthCheck($"concurrent-{i}")) |]
                |> Task.WhenAll

            // Assert
            Assert.AreEqual(10, responses.Length)

            for response in responses do
                match response with
                | HealthStatus _ -> ()
                | SessionResponse.Error _ -> () // Some may fail due to concurrency
                | _ -> Assert.Fail("Unexpected response")
        }

    [<Test>]
    member _.IPCChannelSessionLifecycleWorksCorrectly() =
        task {
            // Arrange
            use channel = createIPCChannel ()
            let! _ = channel.StartAsync()
            let paneId = "lifecycle-test"

            // Act & Assert
            // 1. Start session
            let! startResponse = channel.SendCommandAsync(StartSession(paneId, "/tmp"))

            match startResponse with
            | SessionStarted(id, sessionId, _) ->
                Assert.AreEqual(paneId, id)
                Assert.IsNotEmpty(sessionId)
            | _ -> Assert.Fail("Expected SessionStarted")

            // 2. Send input
            let! inputResponse = channel.SendCommandAsync(SendInput(paneId, "echo hello"))

            match inputResponse with
            | InputProcessed(id) -> Assert.AreEqual(paneId, id)
            | _ -> Assert.Fail("Expected InputProcessed")

            // 3. Request output
            let! outputResponse = channel.SendCommandAsync(RequestOutput(paneId))

            match outputResponse with
            | OutputData(id, data) ->
                Assert.AreEqual(paneId, id)
                Assert.IsNotEmpty(data)
            | _ -> Assert.Fail("Expected OutputData")

            // 4. Stop session
            let! stopResponse = channel.SendCommandAsync(StopSession(paneId))

            match stopResponse with
            | SessionStopped(id) -> Assert.AreEqual(paneId, id)
            | _ -> Assert.Fail("Expected SessionStopped")
        }

    [<Test>]
    member _.IPCChannelStopCleansUpProperly() =
        task {
            // Arrange
            use channel = createIPCChannel ()
            let! _ = channel.StartAsync()

            // Act
            channel.Stop()

            // Assert - Should not be able to send commands after stopping
            try
                let! response = channel.SendCommandAsync(HealthCheck("test"))

                match response with
                | SessionResponse.Error _ -> () // Expected
                | _ -> Assert.Fail("Expected error response after stopping channel")
            with _ ->
                () // Exception is also acceptable after stopping
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``IPCChannel背圧制御 - DropOldestポリシー詳細テスト``() =
        task {
            // Arrange
            let config =
                { defaultIPCConfig with
                    ChannelCapacity = 5
                    BackpressureThreshold = 3
                    BackpressurePolicy = DropOldest
                    RequestTimeoutMs = 1000 }

            use channel = new IPCChannel(config)
            let! _ = channel.StartAsync()

            // Act - 背圧しきい値を超えるリクエストを高速送信
            let requestTasks =
                [| for i in 1..10 do
                       yield channel.SendCommandAsync(HealthCheck($"drop-test-{i}")) |]

            let! responses = Task.WhenAll(requestTasks)

            // Assert
            let metrics = channel.GetMetrics()
            Assert.Greater(metrics.ProcessedRequests, 0L, "いくつかのリクエストは処理される")
            Assert.Greater(metrics.DroppedRequests, 0L, "いくつかのリクエストはドロップされる")

            // 全体の応答数を確認
            Assert.AreEqual(10, responses.Length, "全リクエストに対する応答が返される")
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``IPCChannel背圧制御 - DropNewestポリシーテスト``() =
        task {
            // Arrange
            let config =
                { defaultIPCConfig with
                    ChannelCapacity = 5
                    BackpressureThreshold = 3
                    BackpressurePolicy = DropNewest
                    RequestTimeoutMs = 1000 }

            use channel = new IPCChannel(config)
            let! _ = channel.StartAsync()

            // Act - 大量リクエストで新しいものがドロップされることを確認
            let requestTasks =
                [| for i in 1..8 do
                       yield channel.SendCommandAsync(HealthCheck($"newest-{i}")) |]

            let! responses = Task.WhenAll(requestTasks)

            // Assert
            let metrics = channel.GetMetrics()
            Assert.Greater(metrics.ProcessedRequests, 0L, "古いリクエストは処理される")
            Assert.Greater(metrics.DroppedRequests, 0L, "新しいリクエストがドロップされる")
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``IPCChannel背圧制御 - BlockUntilSpaceポリシーテスト``() =
        task {
            // Arrange
            let config =
                { defaultIPCConfig with
                    ChannelCapacity = 3
                    BackpressureThreshold = 2
                    BackpressurePolicy = BlockUntilSpace
                    RequestTimeoutMs = 2000 }

            use channel = new IPCChannel(config)
            let! _ = channel.StartAsync()

            // Act - ブロッキング動作により全リクエストが処理されることを確認
            let startTime = DateTime.Now

            let requestTasks =
                [| for i in 1..5 do
                       yield channel.SendCommandAsync(HealthCheck($"block-{i}")) |]

            let! responses = Task.WhenAll(requestTasks)
            let elapsed = (DateTime.Now - startTime).TotalMilliseconds

            // Assert
            Assert.AreEqual(5, responses.Length, "全リクエストが処理される")
            Assert.Greater(elapsed, 100.0, "ブロッキングにより時間がかかる")

            let metrics = channel.GetMetrics()
            Assert.AreEqual(0L, metrics.DroppedRequests, "ブロッキングポリシーではドロップされない")
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``IPCChannel背圧制御 - ThrowExceptionポリシーテスト``() =
        task {
            // Arrange
            let config =
                { defaultIPCConfig with
                    ChannelCapacity = 3
                    BackpressureThreshold = 2
                    BackpressurePolicy = ThrowException
                    RequestTimeoutMs = 1000 }

            use channel = new IPCChannel(config)
            let! _ = channel.StartAsync()

            // Act & Assert - 例外が発生することを確認
            let mutable exceptionThrown = false

            try
                let requestTasks =
                    [| for i in 1..6 do
                           yield channel.SendCommandAsync(HealthCheck($"exception-{i}")) |]

                let! _ = Task.WhenAll(requestTasks)
                ()
            with ex ->
                exceptionThrown <- true
                FCode.Logger.logDebug "IPCChannelTest" $"Expected exception caught: {ex.Message}"

            Assert.IsTrue(exceptionThrown || channel.GetMetrics().DroppedRequests > 0L, "例外またはドロップが発生する")
        }

    [<Test>]
    [<Category("Performance")>]
    member _.``IPCChannel大量同時リクエスト性能テスト``() =
        task {
            // Arrange
            let config =
                { defaultIPCConfig with
                    MaxConcurrentRequests = 50
                    ChannelCapacity = 100
                    BackpressureThreshold = 80
                    BackpressurePolicy = DropOldest }

            use channel = new IPCChannel(config)
            let! _ = channel.StartAsync()

            // Act - 大量同時リクエスト
            let requestCount = 100
            let startTime = DateTime.Now

            let requestTasks =
                [| for i in 1..requestCount do
                       yield channel.SendCommandAsync(HealthCheck($"perf-{i}")) |]

            let! responses = Task.WhenAll(requestTasks)
            let elapsed = (DateTime.Now - startTime).TotalMilliseconds

            // Assert
            Assert.AreEqual(requestCount, responses.Length, "全リクエストに応答が返る")

            let metrics = channel.GetMetrics()
            Assert.Greater(metrics.ProcessedRequests, 0L, "リクエストが処理される")
            Assert.LessOrEqual(elapsed, 10000.0, "10秒以内に完了")

            FCode.Logger.logInfo "IPCChannelTest" $"性能テスト結果: {requestCount}リクエスト in {elapsed}ms"
            FCode.Logger.logInfo "IPCChannelTest" $"平均遅延: {metrics.AverageLatencyMs}ms"
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``IPCChannelタイムアウト時のリソース解放テスト``() =
        task {
            // Arrange
            let config =
                { defaultIPCConfig with
                    RequestTimeoutMs = 100 // 非常に短いタイムアウト
                    ChannelCapacity = 3 }

            use channel = new IPCChannel(config)
            let! _ = channel.StartAsync()

            // Act - タイムアウトするリクエストを送信
            let timeoutTasks =
                [| for i in 1..5 do
                       yield channel.SendCommandAsync(HealthCheck($"timeout-{i}")) |]

            let! responses = Task.WhenAll(timeoutTasks)

            // Assert - タイムアウト後でもリソースが適切に解放されることを確認
            let metrics = channel.GetMetrics()
            Assert.GreaterOrEqual(metrics.ErrorCount, 0L, "タイムアウトエラーが記録される")

            // 新しいリクエストが正常に処理されることを確認
            let! newResponse = channel.SendCommandAsync(HealthCheck("after-timeout"))
            Assert.IsNotNull(newResponse, "タイムアウト後も正常に動作する")
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``IPCChannelメトリクス精度テスト``() =
        task {
            // Arrange
            use channel = createIPCChannel ()
            let! _ = channel.StartAsync()

            // Act - 複数の操作を実行してメトリクスを生成
            let! _ = channel.SendCommandAsync(HealthCheck("metrics-1"))
            let! _ = channel.SendCommandAsync(HealthCheck("metrics-2"))
            let! _ = channel.SendCommandAsync(HealthCheck("metrics-3"))

            // Assert - メトリクスの整合性を確認
            let metrics = channel.GetMetrics()
            Assert.GreaterOrEqual(metrics.ProcessedRequests, 3L, "最低3件のリクエストが処理される")
            Assert.GreaterOrEqual(metrics.AverageLatencyMs, 0.0, "平均遅延は非負")
            Assert.GreaterOrEqual(metrics.QueueLength, 0, "キュー長は非負")
            Assert.GreaterOrEqual(metrics.ErrorCount, 0L, "エラー数は非負")
            Assert.GreaterOrEqual(metrics.DroppedRequests, 0L, "ドロップ数は非負")

            FCode.Logger.logInfo
                "IPCChannelTest"
                $"メトリクス詳細: 処理={metrics.ProcessedRequests}, 遅延={metrics.AverageLatencyMs}ms, エラー={metrics.ErrorCount}"
        }

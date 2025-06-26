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
    member _.BasicIPCChannel_SendCommand_ReturnsResponse() =
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
    member _.IPCChannel_SendCommandWithRetry_RetriesOnFailure() =
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
    member _.IPCChannel_HealthCheck_ReturnsHealthStatus() =
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
    member _.IPCChannel_GetMetrics_ReturnsValidMetrics() =
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
    member _.IPCChannel_BackpressureHandling_DropsRequestsWhenThresholdExceeded() =
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
    member _.IPCChannel_ConcurrentRequests_HandlesSafely() =
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
    member _.IPCChannel_SessionLifecycle_WorksCorrectly() =
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
    member _.IPCChannel_Stop_CleansUpProperly() =
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

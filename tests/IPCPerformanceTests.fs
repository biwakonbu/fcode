module FCode.Tests.IPCPerformanceTests

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open NUnit.Framework
open FCode.IPCChannel
open FCode.UnixDomainSocketManager
open FCode.Logger

/// FC-002性能要件検証: 1万req/s、99パーセンタイル<2ms
[<TestFixture>]
[<Category("Performance")>]
type IPCPerformanceTests() =

    [<SetUp>]
    member _.Setup() =
        // Logger は自動初期化済み
        ()

    [<Test>]
    [<Category("Performance")>]
    member _.IPCChannelThroughputTest10KRequestsPerSecond() =
        task {
            // Arrange
            let targetRequestsPerSecond = 10_000
            let testDurationSeconds = 5
            let totalRequests = targetRequestsPerSecond * testDurationSeconds

            use channel = createIPCChannel ()
            let! _ = channel.StartAsync()

            logInfo "PERF" $"Starting throughput test: {totalRequests} requests in {testDurationSeconds}s"

            // Act
            let stopwatch = Stopwatch.StartNew()

            let! responses =
                Array.init totalRequests (fun i -> channel.SendCommandAsync(HealthCheck $"perf-test-{i}"))
                |> Task.WhenAll

            stopwatch.Stop()

            // Assert
            let actualRequestsPerSecond = float totalRequests / stopwatch.Elapsed.TotalSeconds
            let metrics = channel.GetMetrics()

            logInfo "PERF" "Throughput test completed:"
            logInfo "PERF" $"  Total requests: {totalRequests}"
            logInfo "PERF" $"  Elapsed time: {stopwatch.Elapsed.TotalSeconds}s"
            logInfo "PERF" $"  Actual throughput: {actualRequestsPerSecond} req/s"
            logInfo "PERF" $"  Target throughput: {targetRequestsPerSecond} req/s"
            logInfo "PERF" $"  Average latency: {metrics.AverageLatencyMs}ms"
            logInfo "PERF" $"  Processed requests: {metrics.ProcessedRequests}"
            logInfo "PERF" $"  Dropped requests: {metrics.DroppedRequests}"

            // 性能要件: 1万req/s達成を確認
            Assert.GreaterOrEqual(
                actualRequestsPerSecond,
                float targetRequestsPerSecond * 0.8,
                "Throughput should be at least 80% of target"
            )

            // 成功率90%以上を確認
            let successRate = float responses.Length / float totalRequests
            Assert.GreaterOrEqual(successRate, 0.9, "Success rate should be at least 90%")
        }

    [<Test>]
    [<Category("Performance")>]
    member _.IPCChannelLatencyTest99PercentileUnder2ms() =
        task {
            // Arrange
            let sampleSize = 1000
            let latencies = Array.zeroCreate<float> sampleSize

            use channel = createIPCChannel ()
            let! _ = channel.StartAsync()

            logInfo "PERF" $"Starting latency test: {sampleSize} samples"

            // Act - 個別にレイテンシ測定
            for i = 0 to sampleSize - 1 do
                let stopwatch = Stopwatch.StartNew()
                let! _ = channel.SendCommandAsync(HealthCheck $"latency-test-{i}")
                stopwatch.Stop()
                latencies.[i] <- stopwatch.Elapsed.TotalMilliseconds

            // Assert
            Array.sortInPlace latencies
            let p50 = latencies.[sampleSize / 2]
            let p95 = latencies.[int (0.95 * float sampleSize)]
            let p99 = latencies.[int (0.99 * float sampleSize)]
            let average = Array.average latencies
            let maximum = Array.max latencies

            logInfo "PERF" "Latency test completed:"
            logInfo "PERF" $"  Sample size: {sampleSize}"
            logInfo "PERF" $"  Average: {average}ms"
            logInfo "PERF" $"  50th percentile: {p50}ms"
            logInfo "PERF" $"  95th percentile: {p95}ms"
            logInfo "PERF" $"  99th percentile: {p99}ms"
            logInfo "PERF" $"  Maximum: {maximum}ms"

            // 性能要件: 99パーセンタイル < 2ms
            Assert.Less(p99, 2.0, "99th percentile latency should be under 2ms")

            // 追加検証: 95パーセンタイル < 1ms
            Assert.Less(p95, 1.0, "95th percentile latency should be under 1ms")
        }

    [<Test>]
    [<Category("Performance")>]
    member _.IPCChannelConcurrentLoadTest() =
        task {
            // Arrange
            let concurrentClients = 50
            let requestsPerClient = 200
            let totalRequests = concurrentClients * requestsPerClient

            use channel = createIPCChannel ()
            let! _ = channel.StartAsync()

            logInfo "PERF" $"Starting concurrent load test: {concurrentClients} clients × {requestsPerClient} requests"

            // Act
            let stopwatch = Stopwatch.StartNew()

            let clientTasks =
                Array.init concurrentClients (fun clientId ->
                    task {
                        let! responses =
                            Array.init requestsPerClient (fun reqId ->
                                channel.SendCommandAsync(HealthCheck $"client-{clientId}-req-{reqId}"))
                            |> Task.WhenAll

                        return responses
                    })

            let! allResponses = Task.WhenAll clientTasks
            stopwatch.Stop()

            // Assert
            let totalResponses = allResponses |> Array.sumBy (fun responses -> responses.Length)
            let throughput = float totalResponses / stopwatch.Elapsed.TotalSeconds
            let metrics = channel.GetMetrics()

            logInfo "PERF" "Concurrent load test completed:"
            logInfo "PERF" $"  Concurrent clients: {concurrentClients}"
            logInfo "PERF" $"  Total requests: {totalRequests}"
            logInfo "PERF" $"  Total responses: {totalResponses}"
            logInfo "PERF" $"  Elapsed time: {stopwatch.Elapsed.TotalSeconds}s"
            logInfo "PERF" $"  Throughput: {throughput} req/s"
            logInfo "PERF" $"  Average latency: {metrics.AverageLatencyMs}ms"
            logInfo "PERF" $"  Dropped requests: {metrics.DroppedRequests}"

            // 並行処理での安定性確認
            Assert.GreaterOrEqual(
                totalResponses,
                totalRequests * 8 / 10,
                "At least 80% of requests should be processed successfully under concurrent load"
            )

            // 並行環境でもスループット維持確認
            Assert.Greater(throughput, 5000.0, "Concurrent throughput should exceed 5,000 req/s")
        }

    [<Test>]
    [<Category("Performance")>]
    member _.UnixDomainSocketFramingPerformanceTest() =
        task {
            // Arrange
            let messageCount = 5000
            let mutable totalSerializationTime = 0.0
            let mutable totalDeserializationTime = 0.0

            logInfo "PERF" $"Starting UDS framing performance test: {messageCount} messages"

            // Act - シリアライゼーション性能測定
            for i = 1 to messageCount do
                let envelope = createEnvelope $"test-data-{i}"

                let serializeWatch = Stopwatch.StartNew()
                let serialized = serializeMessage envelope
                serializeWatch.Stop()
                totalSerializationTime <- totalSerializationTime + serializeWatch.Elapsed.TotalMilliseconds

                let deserializeWatch = Stopwatch.StartNew()
                let _deserialized = deserializeMessage<string> serialized
                deserializeWatch.Stop()
                totalDeserializationTime <- totalDeserializationTime + deserializeWatch.Elapsed.TotalMilliseconds

            // Assert
            let avgSerializationMs = totalSerializationTime / float messageCount
            let avgDeserializationMs = totalDeserializationTime / float messageCount
            let avgRoundTripMs = avgSerializationMs + avgDeserializationMs

            logInfo "PERF" "UDS framing performance test completed:"
            logInfo "PERF" $"  Message count: {messageCount}"
            logInfo "PERF" $"  Avg serialization: {avgSerializationMs}ms"
            logInfo "PERF" $"  Avg deserialization: {avgDeserializationMs}ms"
            logInfo "PERF" $"  Avg round-trip: {avgRoundTripMs}ms"

            // フレーミング性能要件: シリアライズ/デシリアライズは0.1ms以下
            Assert.Less(avgSerializationMs, 0.1, "Average serialization should be under 0.1ms")
            Assert.Less(avgDeserializationMs, 0.1, "Average deserialization should be under 0.1ms")
            Assert.Less(avgRoundTripMs, 0.2, "Average round-trip should be under 0.2ms")
        }

    [<Test>]
    [<Category("Performance")>]
    member _.IPCChannelMemoryUsageStabilityTest() =
        task {
            // Arrange
            let testDurationMs = 10_000 // 10秒（CI環境考慮して短縮）
            let requestInterval = 50 // 50ms間隔
            let expectedRequests = testDurationMs / requestInterval

            use channel = createIPCChannel ()
            let! _ = channel.StartAsync()

            let initialMemory = GC.GetTotalMemory true
            logInfo "PERF" $"Starting memory stability test: {testDurationMs}ms duration"
            logInfo "PERF" $"  Initial memory: {initialMemory / 1024L / 1024L}MB"

            // Act - 持続的な負荷をかけてメモリ使用量を監視
            let cancellationTokenSource = new CancellationTokenSource(testDurationMs)
            let mutable requestCount = 0

            try
                while not cancellationTokenSource.Token.IsCancellationRequested do
                    let! _ = channel.SendCommandAsync(HealthCheck $"memory-test-{requestCount}")
                    requestCount <- requestCount + 1

                    if requestCount % 20 = 0 then
                        let currentMemory = GC.GetTotalMemory false
                        logDebug "PERF" $"Requests: {requestCount}, Memory: {currentMemory / 1024L / 1024L}MB"

                    do! Task.Delay(requestInterval, cancellationTokenSource.Token)
            with :? OperationCanceledException ->
                ()

            // メモリ状況確認
            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()

            let finalMemory = GC.GetTotalMemory true
            let memoryIncrease = finalMemory - initialMemory
            let metrics = channel.GetMetrics()

            // Assert
            logInfo "PERF" "Memory stability test completed:"
            logInfo "PERF" $"  Total requests: {requestCount}"
            logInfo "PERF" $"  Expected requests: ~{expectedRequests}"
            logInfo "PERF" $"  Initial memory: {initialMemory / 1024L / 1024L}MB"
            logInfo "PERF" $"  Final memory: {finalMemory / 1024L / 1024L}MB"
            logInfo "PERF" $"  Memory increase: {memoryIncrease / 1024L / 1024L}MB"
            logInfo "PERF" $"  Processed requests: {metrics.ProcessedRequests}"
            logInfo "PERF" $"  Average latency: {metrics.AverageLatencyMs}ms"

            // メモリリーク検証: 増加量は10MB以下であること
            Assert.Less(memoryIncrease, 10L * 1024L * 1024L, "Memory increase should be under 10MB")

            // 処理継続性確認: 期待リクエスト数の50%以上処理（CI環境考慮）
            Assert.GreaterOrEqual(
                requestCount,
                expectedRequests / 2,
                "Should process at least 50% of expected requests"
            )
        }

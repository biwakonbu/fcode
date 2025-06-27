module ProcessSupervisorTests

open NUnit.Framework
open System
open System.Threading
open FCode.ProcessSupervisor

[<TestFixture>]
type ProcessSupervisorTests() =

    [<Test>]
    member _.``ProcessSupervisor初期化テスト``() =
        // Arrange & Act
        let config = defaultConfig
        use supervisor = new ProcessSupervisor(config)

        // Assert
        Assert.That(config.HeartbeatIntervalMs, Is.EqualTo(2000))
        Assert.That(config.MemoryLimitMB, Is.EqualTo(512.0))
        Assert.That(config.CpuLimitPercent, Is.EqualTo(50.0))

    [<Test>]
    member _.``WorkerStatus型のテスト``() =
        // Arrange
        let statuses = [ Starting; Running; Unhealthy; Crashed; Stopping ]

        // Act & Assert
        for status in statuses do
            match status with
            | Starting -> Assert.Pass("Starting status exists")
            | Running -> Assert.Pass("Running status exists")
            | Unhealthy -> Assert.Pass("Unhealthy status exists")
            | Crashed -> Assert.Pass("Crashed status exists")
            | Stopping -> Assert.Pass("Stopping status exists")

    [<Test>]
    member _.``RecoveryStrategy選択テスト - 少ない再起動回数``() =
        // Arrange
        let error = StartupFailure "Test failure"
        let restartCount = 1

        // Act
        let strategy = selectRecoveryStrategy error restartCount

        // Assert
        match strategy with
        | DelayedRestart delayMs -> Assert.That(delayMs, Is.EqualTo(5000))
        | _ -> Assert.Fail($"Expected DelayedRestart, got {strategy}")

    [<Test>]
    member _.``RecoveryStrategy選択テスト - 最大再起動回数超過``() =
        // Arrange
        let error = StartupFailure "Test failure"
        let restartCount = 6

        // Act
        let strategy = selectRecoveryStrategy error restartCount

        // Assert
        match strategy with
        | ManualIntervention reason -> Assert.That(reason, Is.EqualTo("Max restart limit exceeded"))
        | _ -> Assert.Fail($"Expected ManualIntervention, got {strategy}")

    [<Test>]
    member _.``RecoveryStrategy選択テスト - リソース枯渇``() =
        // Arrange
        let error = ResourceExhaustion "Memory"
        let restartCount = 3

        // Act
        let strategy = selectRecoveryStrategy error restartCount

        // Assert
        match strategy with
        | ImmediateRestart -> Assert.Pass("Correct immediate restart strategy")
        | _ -> Assert.Fail($"Expected ImmediateRestart, got {strategy}")

    [<Test>]
    member _.``RecoveryStrategy選択テスト - 無応答プロセス``() =
        // Arrange
        let error = UnresponsiveProcess 10000
        let restartCount = 2

        // Act
        let strategy = selectRecoveryStrategy error restartCount

        // Assert
        match strategy with
        | ImmediateRestart -> Assert.Pass("Correct immediate restart strategy")
        | _ -> Assert.Fail($"Expected ImmediateRestart, got {strategy}")

    [<Test>]
    member _.``ProcessError型の詳細テスト``() =
        // Arrange & Act
        let errors =
            [ StartupFailure "Test reason"
              CommunicationFailure "Last known state"
              ResourceExhaustion "Memory"
              UnresponsiveProcess 5000
              CorruptedSession "session-123"
              NetworkConnectivityLoss ]

        // Assert
        Assert.That(errors.Length, Is.EqualTo(6))

        for error in errors do
            match error with
            | StartupFailure reason -> Assert.That(reason, Is.EqualTo("Test reason"))
            | CommunicationFailure state -> Assert.That(state, Is.EqualTo("Last known state"))
            | ResourceExhaustion resType -> Assert.That(resType, Is.EqualTo("Memory"))
            | UnresponsiveProcess duration -> Assert.That(duration, Is.EqualTo(5000))
            | CorruptedSession sessionId -> Assert.That(sessionId, Is.EqualTo("session-123"))
            | NetworkConnectivityLoss -> Assert.Pass("Network connectivity loss error exists")

    [<Test>]
    member _.``IPCMessage型のテスト``() =
        // Arrange
        let paneId = "test-pane"
        let workingDir = "/tmp/test"
        let input = "test input"
        let output = "test output"
        let timestamp = DateTime.Now

        // Act
        let messages =
            [ StartSession(paneId, workingDir)
              StopSession(paneId)
              SendInput(paneId, input)
              ReceiveOutput(paneId, output)
              Heartbeat(paneId, timestamp)
              ProcessCrashed(paneId, 1)
              ResourceAlert(paneId, "Memory", 80.5)
              HealthCheck(paneId) ]

        // Assert
        Assert.That(messages.Length, Is.EqualTo(8))

        match messages.[0] with
        | StartSession(id, dir) ->
            Assert.That(id, Is.EqualTo(paneId))
            Assert.That(dir, Is.EqualTo(workingDir))
        | _ -> Assert.Fail("First message should be StartSession")

    [<Test>]
    member _.``IPCResponse型のテスト``() =
        // Arrange
        let paneId = "test-pane"
        let sessionId = "session-123"
        let output = "test output"
        let timestamp = DateTime.Now

        let metrics =
            { ProcessUptime = TimeSpan.FromMinutes(5.0)
              MemoryUsageMB = 256.0
              CpuUsagePercent = 25.0
              ResponseTimeMs = 100
              LastActivity = timestamp
              ErrorCount = 0
              RestartCount = 1
              // 新しいメトリクス（FC-004対応）
              AverageResponseTimeMs = 120.0
              CpuUsageHistory = Array.empty
              ErrorRate = 0.0
              MemoryTrend = "stable"
              LastCpuMeasurement = timestamp }

        // Act
        let responses =
            [ SessionStarted(paneId, sessionId)
              SessionStopped(paneId)
              InputReceived(paneId)
              OutputSent(paneId, output)
              HeartbeatAck(paneId, timestamp)
              HealthStatus(paneId, metrics)
              Error(paneId, "Test error") ]

        // Assert
        Assert.That(responses.Length, Is.EqualTo(7))

        match responses.[5] with
        | HealthStatus(id, m) ->
            Assert.That(id, Is.EqualTo(paneId))
            Assert.That(m.MemoryUsageMB, Is.EqualTo(256.0))
            Assert.That(m.CpuUsagePercent, Is.EqualTo(25.0))
        | _ -> Assert.Fail("Sixth response should be HealthStatus")

    [<Test>]
    member _.``HealthMetrics構造体のテスト``() =
        // Arrange
        let uptime = TimeSpan.FromHours(2.0)
        let memoryMB = 128.0
        let cpuPercent = 15.5
        let responseMs = 250
        let lastActivity = DateTime.Now
        let errorCount = 3
        let restartCount = 1

        // Act
        let metrics =
            { ProcessUptime = uptime
              MemoryUsageMB = memoryMB
              CpuUsagePercent = cpuPercent
              ResponseTimeMs = responseMs
              LastActivity = lastActivity
              ErrorCount = errorCount
              RestartCount = restartCount
              // 新しいメトリクス（FC-004対応）
              AverageResponseTimeMs = 300.0
              CpuUsageHistory = [| 15.5; 12.3; 18.7 |]
              ErrorRate = 1.5
              MemoryTrend = "increasing"
              LastCpuMeasurement = lastActivity }

        // Assert
        Assert.That(metrics.ProcessUptime, Is.EqualTo(uptime))
        Assert.That(metrics.MemoryUsageMB, Is.EqualTo(memoryMB))
        Assert.That(metrics.CpuUsagePercent, Is.EqualTo(cpuPercent))
        Assert.That(metrics.ResponseTimeMs, Is.EqualTo(responseMs))
        Assert.That(metrics.ErrorCount, Is.EqualTo(errorCount))
        Assert.That(metrics.RestartCount, Is.EqualTo(restartCount))

    [<Test>]
    member _.``SupervisorConfig設定値テスト``() =
        // Arrange & Act
        let config = defaultConfig

        // Assert
        Assert.That(config.HeartbeatIntervalMs, Is.EqualTo(2000))
        Assert.That(config.MemoryLimitMB, Is.EqualTo(512.0))
        Assert.That(config.CpuLimitPercent, Is.EqualTo(50.0))
        Assert.That(config.MaxRestarts, Is.EqualTo(5))
        Assert.That(config.RestartCooldownMs, Is.EqualTo(10000))
        Assert.That(config.HealthCheckTimeoutMs, Is.EqualTo(5000))
        Assert.That(config.PreventiveRestartIntervalMs, Is.EqualTo(3600000))
        Assert.That(config.SessionPersistenceEnabled, Is.True)

    [<Test>]
    member _.``WorkerProcess初期化テスト``() =
        // Arrange
        let paneId = "dev1"
        let processId = 12345
        let sessionId = Guid.NewGuid().ToString()
        let startTime = DateTime.Now

        let healthMetrics =
            { ProcessUptime = TimeSpan.Zero
              MemoryUsageMB = 0.0
              CpuUsagePercent = 0.0
              ResponseTimeMs = 0
              LastActivity = startTime
              ErrorCount = 0
              RestartCount = 0
              // 新しいメトリクス（FC-004対応）
              AverageResponseTimeMs = 0.0
              CpuUsageHistory = Array.empty
              ErrorRate = 0.0
              MemoryTrend = "stable"
              LastCpuMeasurement = startTime }

        // Act
        let worker =
            { PaneId = paneId
              ProcessId = Some processId
              Status = Starting
              LastHeartbeat = startTime
              RestartCount = 0
              SessionId = sessionId
              Process = None // テスト用にNone
              HealthMetrics = healthMetrics
              StartTime = startTime
              WorkingDirectory = "/tmp/test"
              // 新しいメトリクス追跡機能（FC-004対応）
              ProcessMetrics = None // テスト用にNone
              ResponseTimeTracker = createResponseTimeTracker ()
              ErrorCounter = createErrorCounter () }

        // Assert
        Assert.That(worker.PaneId, Is.EqualTo(paneId))
        Assert.That(worker.ProcessId, Is.EqualTo(Some processId))
        Assert.That(worker.Status, Is.EqualTo(Starting))
        Assert.That(worker.RestartCount, Is.EqualTo(0))
        Assert.That(worker.SessionId, Is.EqualTo(sessionId))
        Assert.That(worker.HealthMetrics.ProcessUptime, Is.EqualTo(TimeSpan.Zero))

    [<Test>]
    member _.``グローバル関数の存在確認テスト``() =
        // CI環境ではスキップ（System.Management依存）
        let isCI = System.Environment.GetEnvironmentVariable("CI") <> null

        if isCI then
            Assert.Ignore("Skipped in CI environment due to System.Management dependencies")
        else
            // Act & Assert - 関数が存在することを確認
            Assert.DoesNotThrow(fun () ->
                startSupervisor ()
                let workers = getAllWorkers ()
                Assert.That(workers, Is.Not.Null)
                stopSupervisor ())

    [<Test>]
    member _.``Worker管理関数のテスト``() =
        // CI環境ではスキップ（System.Management依存）
        let isCI = System.Environment.GetEnvironmentVariable("CI") <> null

        if isCI then
            Assert.Ignore("Skipped in CI environment due to System.Management dependencies")
        else
            // Arrange
            let paneId = "test-pane"

            // Act & Assert - 基本的な関数呼び出しが例外を投げないことを確認
            Assert.DoesNotThrow(fun () ->
                let status = getWorkerStatus paneId
                Assert.That(status, Is.EqualTo(None)) // 存在しないワーカー

                let metrics = getWorkerMetrics paneId
                Assert.That(metrics, Is.EqualTo(None)) // 存在しないワーカー
            )

    // ===============================================
    // FC-004 新機能のテスト
    // ===============================================

    [<Test>]
    member _.``CircularBuffer基本機能テスト``() =
        // Arrange
        let buffer = CircularBuffer<int>(3)

        // Act & Assert - 空の状態
        Assert.That(buffer.Count, Is.EqualTo(0))
        Assert.That(buffer.IsEmpty, Is.True)

        // Act - 要素追加
        buffer.Add(1)
        buffer.Add(2)
        buffer.Add(3)

        // Assert
        Assert.That(buffer.Count, Is.EqualTo(3))
        Assert.That(buffer.IsEmpty, Is.False)

        let items = buffer.GetLast(3)
        Assert.That(items, Is.EqualTo([| 3; 2; 1 |]))

    [<Test>]
    member _.``CircularBuffer容量超過テスト``() =
        // Arrange
        let buffer = CircularBuffer<int>(3)

        // Act - 容量を超えて追加
        buffer.Add(1)
        buffer.Add(2)
        buffer.Add(3)
        buffer.Add(4) // 容量超過
        buffer.Add(5) // 容量超過

        // Assert - 最新3件が保持される
        Assert.That(buffer.Count, Is.EqualTo(3))
        let items = buffer.GetLast(3)
        Assert.That(items, Is.EqualTo([| 5; 4; 3 |]))

    [<Test>]
    member _.``CircularBuffer平均値計算テスト``() =
        // Arrange
        let buffer = CircularBuffer<float>(5)

        // Act
        buffer.Add(10.0)
        buffer.Add(20.0)
        buffer.Add(30.0)

        // Assert
        let average = buffer.GetAverage(fun x -> x)
        Assert.That(average, Is.EqualTo(20.0).Within(0.01))

    [<Test>]
    member _.``ErrorCounter機能テスト``() =
        // Arrange
        let counter = createErrorCounter ()

        // Act
        incrementErrorCount counter "ipc"
        incrementErrorCount counter "crash"
        incrementErrorCount counter "timeout"
        incrementErrorCount counter "ipc"

        // Assert
        Assert.That(counter.TotalErrors, Is.EqualTo(4))
        Assert.That(counter.IPCErrors, Is.EqualTo(2))
        Assert.That(counter.ProcessCrashes, Is.EqualTo(1))
        Assert.That(counter.TimeoutErrors, Is.EqualTo(1))
        Assert.That(!counter.LastErrorTime, Is.Not.EqualTo(DateTime.MinValue))

    [<Test>]
    member _.``ResponseTimeTracker機能テスト``() =
        // Arrange
        let tracker = createResponseTimeTracker ()
        let requestId = "test-request-1"

        // Act
        startResponseTimeMeasurement tracker requestId
        Thread.Sleep(50) // 50ms待機
        let responseTime = completeResponseTimeMeasurement tracker requestId

        // Assert
        Assert.That(responseTime, Is.GreaterThan(40))
        Assert.That(responseTime, Is.LessThan(100))
        Assert.That(tracker.ResponseHistory.Count, Is.EqualTo(1))

    [<Test>]
    member _.``新しいメトリクス型のテスト``() =
        // Arrange & Act
        let cpuStats =
            { Current = 15.5
              Average = 20.0
              History = [| 15.5; 18.2; 22.3 |] }

        let responseStats =
            { AverageMs = 150.0
              RecentHistory = [| 120; 180; 140 |]
              PendingRequests = 2 }

        let errorStats =
            { TotalErrors = 5
              IPCErrors = 2
              ProcessCrashes = 1
              TimeoutErrors = 2
              LastErrorTime = DateTime.Now
              ErrorRate = 1.5 }

        // Assert
        Assert.That(cpuStats.Current, Is.EqualTo(15.5))
        Assert.That(cpuStats.History.Length, Is.EqualTo(3))
        Assert.That(responseStats.AverageMs, Is.EqualTo(150.0))
        Assert.That(responseStats.PendingRequests, Is.EqualTo(2))
        Assert.That(errorStats.TotalErrors, Is.EqualTo(5))
        Assert.That(errorStats.ErrorRate, Is.EqualTo(1.5))

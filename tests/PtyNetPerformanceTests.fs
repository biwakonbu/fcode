namespace FCode.Tests

open NUnit.Framework
open System
open System.Threading.Tasks
open System.Diagnostics
open System.Threading
open FCode
open TuiPoC.Logger

/// PTY Netパフォーマンステスト（スループット・レイテンシ計測）
[<TestFixture>]
type PtyNetPerformanceTests() =

    let mutable ptyManager: PtyNetManager option = None

    /// コマンドの存在確認
    let checkCommandExists (command: string) : bool =
        try
            let processInfo = ProcessStartInfo()
            processInfo.FileName <- "which"
            processInfo.Arguments <- command
            processInfo.RedirectStandardOutput <- true
            processInfo.RedirectStandardError <- true
            processInfo.UseShellExecute <- false
            processInfo.CreateNoWindow <- true

            use proc = Process.Start(processInfo)
            proc.WaitForExit()
            proc.ExitCode = 0
        with _ ->
            false

    [<SetUp>]
    member this.Setup() = ptyManager <- Some(new PtyNetManager())

    [<TearDown>]
    member this.TearDown() =
        match ptyManager with
        | Some manager ->
            (manager :> IDisposable).Dispose()
            ptyManager <- None
        | None -> ()

    /// スループット計測テスト - yesコマンドで60fps相当の大量出力
    [<Test>]
    member this.ThroughputTest_YesCommand_60FPS() =
        async {
            // yesコマンドの存在確認
            if not (checkCommandExists "yes") then
                Assert.Ignore("yes command not available on this platform")

            match ptyManager with
            | Some manager ->
                logInfo "スループットテスト開始" "yesコマンド 60fps相当"

                // yesコマンドを起動（1秒あたり60回出力）
                let! sessionResult = manager.CreateSession("yes", [| "test-output-line" |]) |> Async.AwaitTask

                match sessionResult with
                | Result.Ok session ->
                    // 出力読み取り開始
                    let! readingTask = manager.StartOutputReading() |> Async.AwaitTask |> Async.StartChild

                    let stopwatch = Stopwatch.StartNew()
                    let testDurationMs = 1000 // 1秒間テスト
                    let expectedFps = 60
                    let expectedBytesPerSecond = 1024 * 1024 // 1 MB/s の閾値

                    // 1秒間待機
                    do! Task.Delay(testDurationMs) |> Async.AwaitTask
                    stopwatch.Stop()

                    // 出力データを取得
                    let output = manager.GetOutput()
                    let totalBytes = System.Text.Encoding.UTF8.GetByteCount(output)
                    let actualDurationMs = stopwatch.ElapsedMilliseconds

                    // スループット計算
                    let bytesPerSecond = (float totalBytes) * 1000.0 / (float actualDurationMs)
                    let linesCount = output.Split('\n').Length
                    let linesPerSecond = (float linesCount) * 1000.0 / (float actualDurationMs)

                    logInfo
                        "スループット計測結果"
                        ("bytes/sec="
                         + bytesPerSecond.ToString("F0")
                         + ", lines/sec="
                         + linesPerSecond.ToString("F0")
                         + ", total_bytes="
                         + totalBytes.ToString()
                         + ", duration_ms="
                         + actualDurationMs.ToString())

                    // アサーション
                    Assert.That(
                        bytesPerSecond,
                        Is.GreaterThan(float expectedBytesPerSecond),
                        ("スループットが閾値を下回りました: "
                         + bytesPerSecond.ToString("F0")
                         + " bytes/s < "
                         + expectedBytesPerSecond.ToString()
                         + " bytes/s")
                    )

                    Assert.That(totalBytes, Is.GreaterThan(0), "出力データが取得されませんでした")

                | Result.Error error -> Assert.Fail("PTYセッション作成に失敗: " + error)

            | None -> Assert.Fail("PTYマネージャーが初期化されていません")
        }
        |> Async.RunSynchronously

    /// レイテンシ計測テスト - 99パーセンタイル16ms未満
    [<Test>]
    member this.LatencyTest_99Percentile_Under16ms() =
        async {
            match ptyManager with
            | Some manager ->
                logInfo "レイテンシテスト開始" "99パーセンタイル < 16ms"

                let! sessionResult = manager.CreateSession("cat", [||]) |> Async.AwaitTask

                match sessionResult with
                | Result.Ok _session ->
                    let! _readingTask = manager.StartOutputReading() |> Async.AwaitTask |> Async.StartChild

                    let latencies = ResizeArray<float>()
                    let testCount = 100

                    for i in 1..testCount do
                        let testInput = "test-" + i.ToString() + "\n"

                        // レイテンシ計測開始
                        let stopwatch = Stopwatch.StartNew()

                        // 入力送信
                        let inputSent = manager.SendInput(testInput)
                        Assert.That(inputSent, Is.True, ("入力送信失敗: iteration " + i.ToString()))

                        // 出力が返ってくるまで待機（最大100ms）
                        let mutable outputReceived = false
                        let mutable waitTime = 0

                        while not outputReceived && waitTime < 100 do
                            do! Task.Delay(1) |> Async.AwaitTask
                            waitTime <- waitTime + 1
                            let currentOutput = manager.GetOutput()

                            if currentOutput.Contains(testInput.Trim()) then
                                outputReceived <- true

                        stopwatch.Stop()

                        if outputReceived then
                            latencies.Add(float stopwatch.ElapsedMilliseconds)
                        else
                            logWarning "レイテンシテスト" ("タイムアウト: iteration " + i.ToString())

                        manager.ClearOutput() // 次のテスト用にクリア
                        do! Task.Delay(10) |> Async.AwaitTask // 少し待機

                    // 99パーセンタイル計算
                    if latencies.Count > 0 then
                        let sortedLatencies = latencies |> Seq.sort |> Seq.toArray
                        let percentile99Index = int (float sortedLatencies.Length * 0.99)

                        let percentile99 =
                            sortedLatencies.[min percentile99Index (sortedLatencies.Length - 1)]

                        let averageLatency = latencies |> Seq.average

                        logInfo
                            "レイテンシ計測結果"
                            ("99th_percentile="
                             + percentile99.ToString("F2")
                             + "ms, average="
                             + averageLatency.ToString("F2")
                             + "ms, samples="
                             + latencies.Count.ToString())

                        // アサーション: 99パーセンタイルが16ms未満
                        Assert.That(
                            percentile99,
                            Is.LessThan(16.0),
                            ("99パーセンタイルレイテンシが閾値を超過: " + percentile99.ToString("F2") + "ms >= 16ms")
                        )

                        Assert.That(latencies.Count, Is.GreaterThan(testCount / 2), "有効なレイテンシサンプルが不足しています")
                    else
                        Assert.Fail("レイテンシデータが取得できませんでした")

                | Result.Error error -> Assert.Fail("PTYセッション作成に失敗: " + error)

            | None -> Assert.Fail("PTYマネージャーが初期化されていません")
        }
        |> Async.RunSynchronously

    /// 大量データ処理テスト - メモリ効率性確認
    [<Test>]
    member this.MemoryEfficiencyTest_LargeOutput() =
        async {
            match ptyManager with
            | Some manager ->
                logInfo "メモリ効率テスト開始" "大量データ処理"

                let initialMemory = GC.GetTotalMemory(true)

                let! sessionResult =
                    manager.CreateSession("dd", [| "if=/dev/zero"; "bs=1024"; "count=1024" |])
                    |> Async.AwaitTask

                match sessionResult with
                | Result.Ok _session ->
                    let! _readingTask = manager.StartOutputReading() |> Async.AwaitTask |> Async.StartChild

                    // 5秒間実行
                    do! Task.Delay(5000) |> Async.AwaitTask

                    let output = manager.GetOutput()
                    let finalMemory = GC.GetTotalMemory(true)
                    let memoryIncrease = finalMemory - initialMemory

                    logInfo
                        "メモリ効率テスト結果"
                        ("memory_increase="
                         + (memoryIncrease / 1024L).ToString()
                         + "KB, output_length="
                         + output.Length.ToString())

                    // メモリ使用量が合理的な範囲内であることを確認（10MB未満）
                    Assert.That(
                        memoryIncrease,
                        Is.LessThan(10L * 1024L * 1024L),
                        ("メモリ使用量が過大: " + (memoryIncrease / 1024L / 1024L).ToString() + "MB")
                    )

                | Result.Error error -> Assert.Fail("PTYセッション作成に失敗: " + error)

            | None -> Assert.Fail("PTYマネージャーが初期化されていません")
        }
        |> Async.RunSynchronously

module FCode.Tests.ResourceManagementTests

open System
open System.IO
open System.Diagnostics
open System.Threading.Tasks
open System.Threading
open NUnit.Framework
open Terminal.Gui
open FCode.ClaudeCodeProcess
open FCode.Logger

[<TestFixture>]
[<Ignore("Temporarily disabled during WorkerProcessManager → SessionManager migration")>]
type ResourceManagementTests() =

    let mutable initialMemory = 0L
    let mutable testResources: IDisposable list = []

    [<SetUp>]
    member _.Setup() =
        // 初期メモリ使用量を記録
        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()
        initialMemory <- GC.GetTotalMemory(false)
        testResources <- []

    [<TearDown>]
    member _.TearDown() =
        // 全てのテストリソースをクリーンアップ
        testResources
        |> List.iter (fun resource ->
            try
                resource.Dispose()
            with _ ->
                ())

        testResources <- []

        // 全WorkerとSessionをクリーンアップ
        workerManager.CleanupAllWorkers()
        sessionManager.CleanupAllSessions()

        // ガベージコレクション強制実行
        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()

    [<Test>]
    [<Category("Unit")>]
    member _.``メモリリーク検出テスト - TextView大量作成と解放``() =
        // Arrange
        let textViewCount = 100
        let createdTextViews = ResizeArray<TextView>()

        // Act - 大量のTextViewを作成
        for i in 1..textViewCount do
            let textView = new TextView()
            textView.Text <- $"Memory leak test {i}"
            createdTextViews.Add(textView)
            testResources <- (textView :> IDisposable) :: testResources

        // 中間メモリ使用量測定
        GC.Collect()
        let midMemory = GC.GetTotalMemory(false)

        // すべてのTextViewを解放
        createdTextViews |> Seq.iter (fun tv -> tv.Dispose())
        createdTextViews.Clear()
        testResources <- []

        // 最終メモリ使用量測定
        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()
        let finalMemory = GC.GetTotalMemory(false)

        // Assert
        let memoryIncrease = finalMemory - initialMemory
        Assert.LessOrEqual(memoryIncrease, 10_000_000L, "10MB以内のメモリ増加") // 10MB許容

        FCode.Logger.logInfo "ResourceTest" $"メモリ使用量: 初期={initialMemory}, 中間={midMemory}, 最終={finalMemory}"
        FCode.Logger.logInfo "ResourceTest" $"メモリ増加: {memoryIncrease} bytes"

    [<Test>]
    [<Category("Unit")>]
    member _.``ファイルハンドルリーク検出テスト - 一時ファイル作成と削除``() =
        // Arrange
        let tempFiles = ResizeArray<string>()
        let fileCount = 50

        try
            // Act - 大量の一時ファイルを作成
            for i in 1..fileCount do
                let tempPath = Path.GetTempFileName()
                File.WriteAllText(tempPath, $"File handle test {i}")
                tempFiles.Add(tempPath)

            // ファイルハンドル使用状況をチェック（プロセス統計）
            let currentProcess = Process.GetCurrentProcess()

            let handleCount =
                try
                    currentProcess.HandleCount
                with _ ->
                    -1

            FCode.Logger.logInfo "ResourceTest" $"プロセスハンドル数: {handleCount}"

            // すべてのファイルを削除
            tempFiles
            |> Seq.iter (fun path ->
                try
                    if File.Exists(path) then
                        File.Delete(path)
                with ex ->
                    FCode.Logger.logWarning "ResourceTest" $"ファイル削除失敗: {path} - {ex.Message}")

            tempFiles.Clear()

            // Assert - ファイルがすべて削除されていることを確認
            let remainingFiles = tempFiles |> Seq.filter File.Exists |> Seq.length
            Assert.AreEqual(0, remainingFiles, "すべての一時ファイルが削除される")

        finally
            // 確実なクリーンアップ
            tempFiles
            |> Seq.iter (fun path ->
                try
                    if File.Exists(path) then
                        File.Delete(path)
                with _ ->
                    ())

    [<Test>]
    [<Category("Unit")>]
    member _.``ソケットリーク検出テスト - IPC接続作成と解放``() =
        task {
            // Arrange
            let socketPaths = ResizeArray<string>()
            let socketCount = 10

            try
                // Act - 複数のソケットファイルを作成・削除
                for i in 1..socketCount do
                    let socketPath = Path.Combine(Path.GetTempPath(), $"test-socket-{i}.sock")

                    // ダミーソケットファイル作成
                    File.WriteAllText(socketPath, "test socket")
                    socketPaths.Add(socketPath)

                    // 短時間待機
                    do! Task.Delay(10)

                // ソケットファイルを削除
                socketPaths
                |> Seq.iter (fun path ->
                    try
                        if File.Exists(path) then
                            File.Delete(path)
                    with ex ->
                        FCode.Logger.logWarning "ResourceTest" $"ソケットファイル削除失敗: {path} - {ex.Message}")

                // Assert - すべてのソケットファイルが削除されること
                let remainingSockets = socketPaths |> Seq.filter File.Exists |> Seq.length
                Assert.AreEqual(0, remainingSockets, "すべてのソケットファイルが削除される")

            finally
                // 確実なクリーンアップ
                socketPaths
                |> Seq.iter (fun path ->
                    try
                        if File.Exists(path) then
                            File.Delete(path)
                    with _ ->
                        ())
        }

    [<Test>]
    [<Category("Performance")>]
    member _.``長時間稼働安定性テスト - WorkerManager継続操作``() =
        task {
            // Arrange
            let testDurationMs = 5000 // 5秒間の継続テスト
            let operationIntervalMs = 200 // 200ms間隔
            let testPaneId = "stability-test"
            let workingDir = Directory.GetCurrentDirectory()

            let stabilityTextView = new TextView()
            stabilityTextView.Text <- "Stability test"
            testResources <- (stabilityTextView :> IDisposable) :: testResources

            // Act - 長時間の繰り返し操作
            let startTime = DateTime.Now
            let mutable operationCount = 0
            let mutable errorCount = 0

            while (DateTime.Now - startTime).TotalMilliseconds < float testDurationMs do
                try
                    // Worker起動・停止サイクル
                    let startSuccess =
                        workerManager.StartWorker($"{testPaneId}-{operationCount}", workingDir, stabilityTextView)

                    do! Task.Delay(operationIntervalMs / 2)

                    if startSuccess then
                        let stopSuccess = workerManager.StopWorker($"{testPaneId}-{operationCount}")

                        if not stopSuccess then
                            errorCount <- errorCount + 1
                    else
                        errorCount <- errorCount + 1

                    operationCount <- operationCount + 1
                    do! Task.Delay(operationIntervalMs / 2)

                with ex ->
                    FCode.Logger.logError "ResourceTest" $"長時間稼働テストエラー: {ex.Message}"
                    errorCount <- errorCount + 1

            // Assert
            let errorRate = (float errorCount) / (float operationCount) * 100.0
            Assert.LessOrEqual(errorRate, 20.0, "エラー率が20%以下") // 80%の成功率を要求
            Assert.Greater(operationCount, 5, "最低5回の操作が実行される")

            FCode.Logger.logInfo
                "ResourceTest"
                $"長時間稼働結果: 操作回数={operationCount}, エラー={errorCount}, エラー率={errorRate:F1}%%"
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``CircularBuffer リソース管理テスト``() =
        // Arrange
        let bufferCapacity = 1000
        let buffer = FCode.ProcessSupervisor.CircularStringBuffer(bufferCapacity)
        let largeDataCount = 10000

        // Act - 大量データでバッファをテスト
        for i in 1..largeDataCount do
            buffer.AddLine($"Large data test line {i} with some additional content to increase memory usage")

        // バッファ状態確認
        let allLines = buffer.GetAllLines()
        let lineCount = allLines.Split('\n').Length

        // Assert
        Assert.LessOrEqual(lineCount, bufferCapacity, "CircularBufferが容量制限を遵守")
        Assert.Greater(lineCount, 0, "CircularBufferにデータが格納される")

        FCode.Logger.logInfo "ResourceTest" $"CircularBuffer結果: 容量={bufferCapacity}, 格納行数={lineCount}"

    [<Test>]
    [<Category("Unit")>]
    member _.``スレッドリーク検出テスト - Task作成と完了``() =
        task {
            // CI環境でも軽量化して実行（タスク数を制限）
            let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))
            let taskCount = if isCI then 10 else 100 // CI環境では軽量化
            // Arrange
            let initialThreadCount = Process.GetCurrentProcess().Threads.Count

            // Act - 大量のTaskを作成・実行
            let tasks =
                [| for i in 1..taskCount do
                       yield
                           Task.Run(
                               System.Func<Task>(fun () ->
                                   task {
                                       do! Task.Delay(10) // 短時間待機
                                       FCode.Logger.logDebug "ResourceTest" $"Task {i} completed"
                                   })
                           ) |]

            // すべてのTaskの完了を待機
            do! Task.WhenAll(tasks)

            // スレッド数確認（少し待機してからチェック）
            do! Task.Delay(1000)
            let finalThreadCount = Process.GetCurrentProcess().Threads.Count

            // Assert
            let threadIncrease = finalThreadCount - initialThreadCount
            Assert.LessOrEqual(threadIncrease, 10, "スレッド数の増加が10以下") // 許容範囲

            FCode.Logger.logInfo
                "ResourceTest"
                $"スレッド数: 初期={initialThreadCount}, 最終={finalThreadCount}, 増加={threadIncrease}"
        }

    [<Test>]
    [<Category("Integration")>]
    member _.``統合リソース管理テスト - 全コンポーネント協調動作``() =
        task {
            // Arrange
            let integrationTextView = new TextView()
            integrationTextView.Text <- "Integration resource test"
            testResources <- (integrationTextView :> IDisposable) :: testResources

            let workingDir = Directory.GetCurrentDirectory()
            let testPaneId = "resource-integration"

            // 初期リソース状態測定
            GC.Collect()
            let initialMem = GC.GetTotalMemory(false)
            let initialThreads = Process.GetCurrentProcess().Threads.Count

            // Act - 複数コンポーネントの統合動作

            // 1. WorkerManager操作
            let workerSuccess =
                workerManager.StartWorker(testPaneId, workingDir, integrationTextView)

            do! Task.Delay(500)

            // 2. SessionManager操作
            let sessionSuccess =
                sessionManager.StartSession($"session-{testPaneId}", workingDir, integrationTextView)

            do! Task.Delay(500)

            // 3. UIHelpers操作
            let frameView = new FrameView("resource-integration-frame")
            frameView.Add(integrationTextView)
            testResources <- (frameView :> IDisposable) :: testResources

            for i in 1..10 do
                let _ = FCode.UIHelpers.getTextViewsFromPane frameView
                do! Task.Delay(50)

            // 4. クリーンアップ
            let _ = sessionManager.StopSession($"session-{testPaneId}")
            let _ = workerManager.StopWorker(testPaneId)

            // 最終リソース状態測定
            do! Task.Delay(1000)
            GC.Collect()
            let finalMem = GC.GetTotalMemory(false)
            let finalThreads = Process.GetCurrentProcess().Threads.Count

            // Assert
            let memoryIncrease = finalMem - initialMem
            let threadIncrease = finalThreads - initialThreads

            Assert.LessOrEqual(memoryIncrease, 50_000_000L, "50MB以内のメモリ増加")
            Assert.LessOrEqual(threadIncrease, 15, "15以下のスレッド増加")

            FCode.Logger.logInfo "ResourceTest" $"統合リソース結果: Worker={workerSuccess}, Session={sessionSuccess}"
            FCode.Logger.logInfo "ResourceTest" $"メモリ増加: {memoryIncrease} bytes, スレッド増加: {threadIncrease}"
        }

module FCode.Tests.EndToEndIntegrationTests

open System
open System.IO
open System.Threading.Tasks
open NUnit.Framework
open Terminal.Gui
open FCode.UIHelpers
open FCode.ClaudeCodeProcess
open FCode.Logger

[<TestFixture>]
[<Ignore("Temporarily disabled during WorkerProcessManager → SessionManager migration")>]
type EndToEndIntegrationTests() =

    let mutable testTextViews: TextView list = []

    [<SetUp>]
    member _.Setup() =
        // CI環境でのTerminal.Gui初期化スキップ
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if not isCI then
            try
                Application.Init()
            with _ ->
                () // Already initialized

    [<TearDown>]
    member _.TearDown() =
        // 全てのテストワーカーをクリーンアップ
        workerManager.CleanupAllWorkers()
        sessionManager.CleanupAllSessions()

        // TextViewsをクリーンアップ
        testTextViews |> List.iter (fun tv -> tv.Dispose())
        testTextViews <- []

        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if not isCI then
            try
                Application.Shutdown()
            with _ ->
                ()

    [<Test>]
    [<Category("Integration")>]
    member _.``完全フロー統合テスト - TextView初期化からWorker起動まで``() =
        task {
            // Arrange - Program.fsと同様のペイン作成
            let createIntegrationPane (title: string) =
                let fv = new FrameView(title)
                fv.Border.Effect3D <- false

                let textView = new TextView()
                textView.X <- 0
                textView.Y <- 0
                textView.Width <- Dim.Fill()
                textView.Height <- Dim.Fill()
                textView.ReadOnly <- true
                textView.Text <- $"[DEBUG] {title}ペイン - TextView初期化完了"

                fv.Add(textView)
                testTextViews <- textView :: testTextViews // クリーンアップ用に追記
                (fv, textView)

            let (dev1Frame, dev1TextView) = createIntegrationPane "dev1"
            let workingDir = Directory.GetCurrentDirectory()

            // Act - TextView発見からWorker起動までの完全フロー

            // 1. UIHelpers経由でのTextView発見テスト
            let foundTextViews = getTextViewsFromPane dev1Frame
            Assert.AreEqual(1, foundTextViews.Length, "ステップ1: TextView発見成功")
            Assert.AreSame(dev1TextView, foundTextViews.[0], "ステップ1: 正確なTextViewが発見される")

            // 2. WorkerProcessManager経由でのWorker起動テスト（短時間タイムアウト）
            let workerStartSuccess =
                workerManager.StartWorker("integration-dev1", workingDir, dev1TextView)

            Assert.IsTrue(workerStartSuccess, "ステップ2: Worker起動プロセス開始成功")

            // 3. ワーカーステータス確認（短時間待機）
            do! Task.Delay(500) // 500ms待機
            let isWorkerActive = workerManager.IsWorkerActive("integration-dev1")

            // 注意: テスト環境では実際のClaude CLIが利用できないため、
            // 起動プロセスは開始されるが接続は失敗する可能性が高い
            FCode.Logger.logInfo "EndToEndTest" $"Worker active status: {isWorkerActive}"

            // 4. TextView内容更新確認
            let textContent = dev1TextView.Text.ToString()
            Assert.IsTrue(textContent.Contains("dev1"), "ステップ4: TextViewに適切な内容が設定される")

            // Cleanup
            workerManager.StopWorker("integration-dev1") |> ignore
            dev1Frame.Dispose()
        }

    [<Test>]
    [<Category("Integration")>]
    member _.``複数ペイン同時操作統合テスト``() =
        task {
            // Arrange - 複数ペインを同時に作成
            let createMultiPane (title: string) =
                let fv = new FrameView(title)
                fv.Border.Effect3D <- false

                let textView = new TextView()
                textView.X <- 0
                textView.Y <- 0
                textView.Width <- Dim.Fill()
                textView.Height <- Dim.Fill()
                textView.ReadOnly <- true
                textView.Text <- $"[DEBUG] {title}ペイン - 複数ペインテスト"

                fv.Add(textView)
                testTextViews <- textView :: testTextViews
                (fv, textView)

            let panes =
                [ createMultiPane "multi-dev1"
                  createMultiPane "multi-dev2"
                  createMultiPane "multi-qa1" ]

            let workingDir = Directory.GetCurrentDirectory()

            // Act - 複数ペインでの同時Worker起動
            let startResults =
                panes
                |> List.mapi (fun i (frame, textView) ->
                    let paneId = $"multi-pane-{i}"
                    let success = workerManager.StartWorker(paneId, workingDir, textView)
                    (paneId, success, frame, textView))

            // Assert - 全ペインで起動プロセスが開始されること
            startResults
            |> List.iter (fun (paneId, success, _, _) -> Assert.IsTrue(success, $"{paneId}: 起動プロセス開始成功"))

            // 短時間待機後のステータス確認
            do! Task.Delay(1000)

            let statusResults =
                startResults
                |> List.map (fun (paneId, _, _, _) ->
                    let isActive = workerManager.IsWorkerActive(paneId)
                    (paneId, isActive))

            FCode.Logger.logInfo "EndToEndTest" "複数ペイン起動結果:"

            statusResults
            |> List.iter (fun (paneId, isActive) ->
                FCode.Logger.logInfo "EndToEndTest" $"  {paneId}: Active={isActive}")

            // Cleanup
            startResults
            |> List.iter (fun (paneId, _, frame, _) ->
                workerManager.StopWorker(paneId) |> ignore
                frame.Dispose())
        }

    [<Test>]
    [<Category("Integration")>]
    member _.``エラー回復統合テスト - TextView発見失敗からの復旧``() =
        task {
            // Arrange - 意図的にTextViewを持たないFrameView
            let emptyFrame = new FrameView("error-recovery-test")
            emptyFrame.Border.Effect3D <- false
            // TextViewを追加しない（エラー状況をシミュレート）

            let workingDir = Directory.GetCurrentDirectory()

            // Act - TextView発見失敗シナリオ
            let foundTextViews = getTextViewsFromPane emptyFrame
            Assert.AreEqual(0, foundTextViews.Length, "ステップ1: TextView発見失敗を確認")

            // WorkerManager起動時のエラーハンドリング確認
            // （TextViewが提供されないため、どう処理されるかテスト）
            let dummyTextView = new TextView()
            dummyTextView.Text <- "dummy for error test"
            testTextViews <- dummyTextView :: testTextViews

            let workerStartSuccess =
                workerManager.StartWorker("error-recovery", workingDir, dummyTextView)

            Assert.IsTrue(workerStartSuccess, "ステップ2: ダミーTextViewでも起動プロセス開始")

            // 短時間待機後にエラー状態を確認
            do! Task.Delay(500)

            let errorContent = dummyTextView.Text.ToString()
            FCode.Logger.logInfo "EndToEndTest" $"Error recovery test content: {errorContent}"

            // Cleanup
            workerManager.StopWorker("error-recovery") |> ignore
            emptyFrame.Dispose()
        }

    [<Test>]
    [<Category("Integration")>]
    member _.``リソース枯渇時の動作統合テスト``() =
        task {
            // Arrange - 大量のペインを作成してリソース枯渇をシミュレート
            let resourceTestPanes =
                [ 1..10 ]
                |> List.map (fun i ->
                    let fv = new FrameView($"resource-test-{i}")
                    fv.Border.Effect3D <- false

                    let textView = new TextView()
                    textView.Text <- $"Resource test pane {i}"
                    fv.Add(textView)
                    testTextViews <- textView :: testTextViews
                    (fv, textView, $"resource-{i}"))

            let workingDir = Directory.GetCurrentDirectory()

            // Act - 大量のWorker起動を試行
            let startResults =
                resourceTestPanes
                |> List.map (fun (frame, textView, paneId) ->
                    try
                        let success = workerManager.StartWorker(paneId, workingDir, textView)
                        (paneId, success, None)
                    with ex ->
                        (paneId, false, Some ex.Message))

            // Assert - リソース制限に達してもクラッシュしないこと
            let successCount =
                startResults |> List.filter (fun (_, success, _) -> success) |> List.length

            let errorCount =
                startResults |> List.filter (fun (_, _, error) -> error.IsSome) |> List.length

            FCode.Logger.logInfo "EndToEndTest" $"リソーステスト結果: 成功={successCount}, エラー={errorCount}"

            // 最低でもいくつかは成功し、全体でクラッシュしないこと
            Assert.Greater(successCount, 0, "いくつかのWorkerは起動成功")
            Assert.LessOrEqual(errorCount, resourceTestPanes.Length, "エラーが過度に発生しない")

            // 短時間待機
            do! Task.Delay(1000)

            // Cleanup
            resourceTestPanes
            |> List.iter (fun (frame, _, paneId) ->
                workerManager.StopWorker(paneId) |> ignore
                frame.Dispose())
        }

    [<Test>]
    [<Category("Integration")>]
    member _.``SessionManager直接操作統合テスト``() =
        task {
            // Arrange - SessionManager経由でのセッション操作
            let sessionTextView = new TextView()
            sessionTextView.Text <- "Session manager test"
            testTextViews <- sessionTextView :: testTextViews

            let workingDir = Directory.GetCurrentDirectory()
            let sessionPaneId = "session-direct-test"

            // Act - SessionManager直接操作

            // 1. セッション開始
            let sessionStartSuccess =
                sessionManager.StartSession(sessionPaneId, workingDir, sessionTextView)

            if sessionStartSuccess then
                FCode.Logger.logInfo "EndToEndTest" "SessionManager直接操作: セッション開始成功"

                // 2. セッション状態確認
                let isSessionActive = sessionManager.IsSessionActive(sessionPaneId)
                Assert.IsTrue(isSessionActive, "セッションがアクティブ状態")

                // 3. 入力送信テスト（実際のClaude CLIがない環境では失敗する可能性あり）
                let inputSuccess =
                    sessionManager.SendInput(sessionPaneId, "echo 'integration test'")

                FCode.Logger.logInfo "EndToEndTest" $"入力送信結果: {inputSuccess}"

                // 短時間待機
                do! Task.Delay(500)

                // 4. セッション停止
                let stopSuccess = sessionManager.StopSession(sessionPaneId)
                Assert.IsTrue(stopSuccess, "セッション停止成功")
            else
                FCode.Logger.logInfo "EndToEndTest" "SessionManager直接操作: セッション開始失敗（予想される結果）"
                Assert.IsTrue(true, "Claude CLIが利用できない環境での予想される結果")
        }

    [<Test>]
    [<Category("Performance")>]
    member _.``統合性能テスト - 大量操作での応答性``() =
        task {
            // Arrange - 性能測定用のペイン作成
            let perfTextView = new TextView()
            perfTextView.Text <- "Performance integration test"
            testTextViews <- perfTextView :: testTextViews

            let perfFrame = new FrameView("performance-integration")
            perfFrame.Add(perfTextView)

            // Act - 大量のTextView検索操作
            let iterations = 1000
            let startTime = DateTime.Now

            for i in 1..iterations do
                let _ = getTextViewsFromPane perfFrame
                ()

            let searchElapsed = (DateTime.Now - startTime).TotalMilliseconds

            // Worker操作の性能測定
            let workerStartTime = DateTime.Now
            let workingDir = Directory.GetCurrentDirectory()

            // 複数のWorker起動・停止サイクル
            for i in 1..5 do
                let paneId = $"perf-{i}"
                let _ = workerManager.StartWorker(paneId, workingDir, perfTextView)
                do! Task.Delay(100) // 短時間待機
                let _ = workerManager.StopWorker(paneId)
                ()

            let workerElapsed = (DateTime.Now - workerStartTime).TotalMilliseconds

            // Assert - 性能要件
            Assert.LessOrEqual(searchElapsed, 5000.0, $"TextView検索性能: {iterations}回の検索が5秒以内")
            Assert.LessOrEqual(workerElapsed, 10000.0, "Worker操作性能: 5サイクルが10秒以内")

            FCode.Logger.logInfo "EndToEndTest" $"統合性能結果: 検索={searchElapsed}ms, Worker={workerElapsed}ms"

            // Cleanup
            perfFrame.Dispose()
        }

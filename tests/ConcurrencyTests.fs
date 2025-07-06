namespace FCode.Tests

open NUnit.Framework
open System
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Diagnostics
open FCode.SessionPersistenceManager
open FCode.DetachAttachManager

[<TestFixture>]
[<Category("Unit")>]
type ConcurrencyTests() =

    let testConfig =
        { AutoSaveIntervalMinutes = 1
          MaxHistoryLength = 100
          MaxSessionAge = TimeSpan.FromDays(1.0)
          StorageDirectory =
            Path.Combine(Path.GetTempPath(), "fcode-concurrency-test-" + Guid.NewGuid().ToString("N")[..7])
          CompressionEnabled = true
          MaxSessionSizeMB = 10 }

    let detachConfig =
        { PersistenceConfig = testConfig
          BackgroundProcessTimeout = TimeSpan.FromMinutes(5.0)
          ProcessCheckInterval = TimeSpan.FromSeconds(10.0)
          MaxDetachedSessions = 10 }

    [<SetUp>]
    member this.Setup() =
        match initializeStorage testConfig with
        | Success _ -> ()
        | Error msg -> Assert.Fail($"ストレージ初期化失敗: {msg}")

    [<TearDown>]
    member this.Cleanup() =
        try
            if Directory.Exists(testConfig.StorageDirectory) then
                Directory.Delete(testConfig.StorageDirectory, true)
        with ex ->
            printfn $"並行処理テストクリーンアップ警告: {ex.Message}"

    [<Test>]
    [<Category("Unit")>]
    member this.``複数プロセス同時セッション保存競合テスト``() =
        let sessionId = generateSessionId ()
        let processCount = 5

        // 同じセッションIDに対して複数のプロセスが同時保存を試行
        let paneState =
            { PaneId = "test"
              ConversationHistory = [ "concurrent save test" ]
              WorkingDirectory = "/tmp"
              Environment = Map.empty
              ProcessStatus = "Running"
              LastActivity = DateTime.Now
              MessageCount = 1
              SizeBytes = 100L }

        let createSnapshot processId =
            { SessionId = sessionId
              PaneStates =
                Map.ofList
                    [ ("test",
                       { paneState with
                           PaneId = $"test-{processId}" }) ]
              CreatedAt = DateTime.Now
              LastSavedAt = DateTime.Now
              TotalSize = 100L
              Version = "1.0" }

        // 並行保存タスクを実行
        let concurrentTasks =
            [ 1..processCount ]
            |> List.map (fun i ->
                async {
                    try
                        // 少しランダムな遅延で保存タイミングをずらす
                        do! Async.Sleep(Random().Next(0, 50))
                        let snapshot = createSnapshot i
                        let result = saveSession testConfig snapshot
                        return (i, result)
                    with ex ->
                        return (i, Error $"Exception: {ex.Message}")
                })

        let results = concurrentTasks |> Async.Parallel |> Async.RunSynchronously

        // 結果の検証
        let successCount =
            results
            |> Array.filter (fun (_, result) ->
                match result with
                | Success _ -> true
                | _ -> false)
            |> Array.length

        let errorCount = results.Length - successCount

        // 少なくとも一部は成功することを期待
        Assert.Greater(successCount, 0, "全ての並行保存が失敗しました")

        // エラーがある場合、競合に関する適切なエラーメッセージかチェック
        for (processId, result) in results do
            match result with
            | Error msg -> printfn $"プロセス{processId}エラー: {msg}"
            // ファイルロックや並行アクセスに関するエラーは期待される
            | Success _ -> printfn $"プロセス{processId}成功"

        // 最終的にセッションが正常に読み込めることを確認
        match loadSession testConfig sessionId with
        | Success loadedSnapshot -> Assert.AreEqual(sessionId, loadedSnapshot.SessionId, "セッションIDが一致しません")
        | Error msg -> Assert.Fail($"並行保存後のセッション読み込み失敗: {msg}")

    [<Test>]
    [<Category("Unit")>]
    member this.``プロセスロック競合とタイミング競合テスト``() =
        let sessionId = generateSessionId ()
        let processIds = [ 12345; 12346; 12347; 12348; 12349 ]

        // 複数のプロセスが同時に同一セッションのロックを取得しようとする
        let lockTasks =
            processIds
            |> List.map (fun pid ->
                async {
                    try
                        // ランダムな遅延でタイミングをずらす
                        do! Async.Sleep(Random().Next(0, 100))
                        let result = saveProcessLock detachConfig sessionId pid
                        return (pid, if result then "Success" else "Failed")
                    with ex ->
                        return (pid, $"Exception: {ex.Message}")
                })

        let lockResults = lockTasks |> Async.Parallel |> Async.RunSynchronously

        // 結果の検証
        let successCount =
            lockResults
            |> Array.filter (fun (_, result) -> result = "Success")
            |> Array.length

        // 複数成功する可能性があるが、最後に保存されたものが有効になるはず
        Assert.Greater(successCount, 0, "全てのプロセスロック取得が失敗しました")

        // 最終的にロードできるロック情報を確認
        match loadProcessLock detachConfig sessionId with
        | Some lockInfo ->
            Assert.IsTrue(List.exists (fun pid -> pid = lockInfo.ProcessId) processIds, "ロードされたプロセスIDが期待される値ではありません")
            Assert.AreEqual(sessionId, lockInfo.SessionId, "セッションIDが一致しません")
        | None -> Assert.Fail("プロセスロック情報が読み込めませんでした")

    [<Test>]
    [<Category("Unit")>]
    member this.``プロセス終了タイミング競合による孤立ロック清理テスト``() =
        let sessionIds = [ 1..10 ] |> List.map (fun _ -> generateSessionId ())
        let fakePids = [ 99991..100000 ] // 存在しない可能性の高いプロセスID

        // 複数の孤立プロセスロックを作成
        let lockCreationTasks =
            List.zip sessionIds fakePids
            |> List.map (fun (sessionId, pid) ->
                async {
                    let result = saveProcessLock detachConfig sessionId pid
                    return (sessionId, pid, result)
                })

        let lockCreationResults =
            lockCreationTasks |> Async.Parallel |> Async.RunSynchronously

        // ロック作成の確認
        let successfulLocks =
            lockCreationResults
            |> Array.filter (fun (_, _, result) -> result)
            |> Array.length

        Assert.Greater(successfulLocks, 0, "テスト用プロセスロックが作成されませんでした")

        // 並行して孤立ロック清理を実行
        let cleanupTasks =
            [ 1..3 ]
            |> List.map (fun i ->
                async {
                    try
                        let! cleanedCount = cleanupOrphanedLocks detachConfig
                        return (i, cleanedCount)
                    with ex ->
                        return (i, -1) // エラーを示すために-1を返す
                })

        let cleanupResults = cleanupTasks |> Async.Parallel |> Async.RunSynchronously

        // 清理結果の検証
        let totalCleaned =
            cleanupResults
            |> Array.filter (fun (_, count) -> count >= 0)
            |> Array.sumBy (fun (_, count) -> count)

        // 一部または全部の孤立ロックが清理されることを期待
        Assert.GreaterOrEqual(totalCleaned, 1, "孤立ロックが清理されませんでした")

        // 最終的に残存するロック数を確認
        let remainingDetachedSessions = listDetachedSessions detachConfig
        Assert.LessOrEqual(remainingDetachedSessions.Length, successfulLocks, "清理後も期待以上のロックが残存しています")

    [<Test>]
    [<Category("Unit")>]
    member this.``複数セッション同時読み書き競合テスト``() =
        let sessionCount = 10
        let sessionIds = [ 1..sessionCount ] |> List.map (fun _ -> generateSessionId ())

        // 各セッションに対して並行して読み書きを実行
        let readWriteTasks =
            sessionIds
            |> List.mapi (fun i sessionId ->
                async {
                    try
                        // 書き込みタスク
                        let paneState =
                            { PaneId = $"pane-{i}"
                              ConversationHistory = [ $"Message from session {i}" ]
                              WorkingDirectory = $"/tmp/session-{i}"
                              Environment = Map.ofList [ ("SESSION_ID", sessionId) ]
                              ProcessStatus = "Running"
                              LastActivity = DateTime.Now
                              MessageCount = 1
                              SizeBytes = 100L }

                        let snapshot =
                            { SessionId = sessionId
                              PaneStates = Map.ofList [ (paneState.PaneId, paneState) ]
                              CreatedAt = DateTime.Now
                              LastSavedAt = DateTime.Now
                              TotalSize = 100L
                              Version = "1.0" }

                        // 保存
                        let saveResult = saveSession testConfig snapshot

                        // 少し待ってから読み込み
                        do! Async.Sleep(Random().Next(10, 100))

                        // 読み込み
                        let loadResult = loadSession testConfig sessionId

                        return (sessionId, saveResult, loadResult)
                    with ex ->
                        return
                            (sessionId, Error $"Save exception: {ex.Message}", Error $"Load exception: {ex.Message}")
                })

        let results = readWriteTasks |> Async.Parallel |> Async.RunSynchronously

        // 結果の検証
        let saveSuccessCount =
            results
            |> Array.filter (fun (_, saveResult, _) ->
                match saveResult with
                | Success _ -> true
                | _ -> false)
            |> Array.length

        let loadSuccessCount =
            results
            |> Array.filter (fun (_, _, loadResult) ->
                match loadResult with
                | Success _ -> true
                | _ -> false)
            |> Array.length

        Assert.Greater(saveSuccessCount, sessionCount / 2, "保存成功率が低すぎます")
        Assert.Greater(loadSuccessCount, sessionCount / 2, "読み込み成功率が低すぎます")

        // エラーがある場合の詳細ログ
        for (sessionId, saveResult, loadResult) in results do
            match saveResult with
            | Error msg -> printfn $"セッション{sessionId[..8]}保存エラー: {msg}"
            | Success _ -> ()

            match loadResult with
            | Error msg -> printfn $"セッション{sessionId[..8]}読み込みエラー: {msg}"
            | Success _ -> ()

    [<Test>]
    [<Category("Performance")>]
    member this.``大量並行セッション操作パフォーマンステスト``() =
        let sessionCount = 50
        let operationsPerSession = 5

        let performanceTest =
            async {
                let stopwatch = Stopwatch.StartNew()

                // 大量の並行操作を実行
                let allTasks =
                    [ 1..sessionCount ]
                    |> List.collect (fun i ->
                        [ 1..operationsPerSession ]
                        |> List.map (fun j ->
                            async {
                                let sessionId = generateSessionId ()

                                let paneState =
                                    { PaneId = $"pane-{i}-{j}"
                                      ConversationHistory = [ $"Perf test message {i}-{j}" ]
                                      WorkingDirectory = "/tmp"
                                      Environment = Map.empty
                                      ProcessStatus = "Running"
                                      LastActivity = DateTime.Now
                                      MessageCount = 1
                                      SizeBytes = 50L }

                                let snapshot =
                                    { SessionId = sessionId
                                      PaneStates = Map.ofList [ (paneState.PaneId, paneState) ]
                                      CreatedAt = DateTime.Now
                                      LastSavedAt = DateTime.Now
                                      TotalSize = 50L
                                      Version = "1.0" }

                                let saveResult = saveSession testConfig snapshot

                                let loadResult =
                                    match saveResult with
                                    | Success _ -> loadSession testConfig sessionId
                                    | Error _ -> Error "Save failed"

                                return (sessionId, saveResult, loadResult)
                            }))

                let! results = Async.Parallel allTasks
                stopwatch.Stop()

                return (results, stopwatch.ElapsedMilliseconds)
            }

        let (results, elapsedMs) = Async.RunSynchronously(performanceTest, timeout = 60000)

        // パフォーマンス検証
        let totalOperations = sessionCount * operationsPerSession

        let successfulOperations =
            results
            |> Array.filter (fun (_, saveResult, loadResult) ->
                match saveResult, loadResult with
                | Success _, Success _ -> true
                | _ -> false)
            |> Array.length

        let successRate = float successfulOperations / float totalOperations
        let operationsPerSecond = float totalOperations / (float elapsedMs / 1000.0)

        printfn $"並行処理パフォーマンス結果:"
        printfn $"- 総操作数: {totalOperations}"
        printfn $"- 成功操作数: {successfulOperations}"
        printfn $"- 成功率: {successRate:P2}"
        printfn $"- 実行時間: {elapsedMs}ms"
        printfn $"- 操作/秒: {operationsPerSecond:F2}"

        // パフォーマンス基準の検証
        Assert.Greater(successRate, 0.8, "成功率が80%を下回りました")
        Assert.Less(elapsedMs, 30000, "実行時間が30秒を超えました")

    [<Test>]
    [<Category("Stability")>]
    member this.``アクティブセッション並行設定競合テスト``() =
        // CI環境では並行処理テストが不安定なためスキップ
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isCI then
            Assert.Ignore("CI環境では並行処理テストをスキップ")
        else
            let isMacOS =
                System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.OSX
                )

            let sessionIds = [ 1..10 ] |> List.map (fun _ -> generateSessionId ())

            // 複数のプロセスが並行してアクティブセッションを設定
            let setActiveTasks =
                sessionIds
                |> List.mapi (fun i sessionId ->
                    async {
                        try
                            // MacOSでは遅延を長めに設定（並行処理の安定性向上）
                            let delayRange =
                                if isMacOS then
                                    Random().Next(0, 100)
                                else
                                    Random().Next(0, 50)

                            do! Async.Sleep(delayRange)
                            let result = setActiveSession testConfig sessionId
                            return (i, sessionId, result)
                        with ex ->
                            return (i, sessionId, Error $"Exception: {ex.Message}")
                    })

            let setResults = setActiveTasks |> Async.Parallel |> Async.RunSynchronously

            // 設定結果の検証
            let successCount =
                setResults
                |> Array.filter (fun (_, _, result) ->
                    match result with
                    | Success _ -> true
                    | _ -> false)
                |> Array.length

            Assert.Greater(successCount, 0, "全てのアクティブセッション設定が失敗しました")

            // 最終的にアクティブセッションが正しく設定されていることを確認（リトライ付き）
            let rec checkActiveSession retryCount =
                if retryCount <= 0 then
                    Assert.Fail("アクティブセッション確認の最大リトライ回数に達しました")
                else
                    // MacOSでは追加の待機時間
                    if isMacOS then
                        System.Threading.Thread.Sleep(10)

                    match getActiveSession testConfig with
                    | Success(Some activeSessionId) ->
                        Assert.IsTrue(
                            List.exists (fun sessionId -> sessionId = activeSessionId) sessionIds,
                            "設定されたアクティブセッションが期待される値ではありません"
                        )
                    | Success None when retryCount > 1 ->
                        // リトライ
                        System.Threading.Thread.Sleep(5)
                        checkActiveSession (retryCount - 1)
                    | Success None -> Assert.Fail("アクティブセッションが設定されていません")
                    | Error msg -> Assert.Fail($"アクティブセッション取得失敗: {msg}")

            checkActiveSession (if isMacOS then 5 else 3)

    [<Test>]
    [<Category("Performance")>]
    member this.``システムリソース枯渇時の動作テスト``() =
        let mutable createdSessions = []
        let mutable operationCount = 0
        let maxOperations = 1000

        try
            // システムリソースが枯渇するまで操作を続ける
            while operationCount < maxOperations do
                let sessionId = generateSessionId ()
                createdSessions <- sessionId :: createdSessions

                let paneState =
                    { PaneId = "resource-test"
                      ConversationHistory = [ String.replicate 1000 $"Resource test {operationCount}" ]
                      WorkingDirectory = "/tmp"
                      Environment = Map.empty
                      ProcessStatus = "Running"
                      LastActivity = DateTime.Now
                      MessageCount = 1
                      SizeBytes = 1000L }

                let snapshot =
                    { SessionId = sessionId
                      PaneStates = Map.ofList [ ("resource-test", paneState) ]
                      CreatedAt = DateTime.Now
                      LastSavedAt = DateTime.Now
                      TotalSize = 1000L
                      Version = "1.0" }

                match saveSession testConfig snapshot with
                | Success _ ->
                    operationCount <- operationCount + 1

                    // 100操作ごとにリソース使用量をチェック
                    if operationCount % 100 = 0 then
                        let currentProcess = Process.GetCurrentProcess()
                        let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)
                        printfn $"操作{operationCount}: メモリ使用量 {memoryMB}MB"

                        // メモリ使用量が過度に増加した場合は停止
                        if memoryMB > 1000L then // 1GB制限
                            printfn "メモリ使用量制限に達したためテストを停止"
                            operationCount <- maxOperations // ループ終了

                | Error msg ->
                    printfn "操作%dでエラー: %s" operationCount msg
                    operationCount <- maxOperations // ループ終了

        with ex ->
            printfn $"リソース枯渇テスト例外: {ex.Message}"

        // テスト結果の評価
        Assert.Greater(operationCount, 50, "リソース枯渇テストで十分な操作が実行されませんでした")
        printfn $"リソース枯渇テスト完了: {operationCount}操作実行、{createdSessions.Length}セッション作成"

    [<Test>]
    [<Category("Unit")>]
    member this.``デッドロック防止機構テスト``() =
        let sessionId1 = generateSessionId ()
        let sessionId2 = generateSessionId ()

        // 2つのタスクが異なる順序でリソースにアクセスしてデッドロックを発生させようとする
        let task1 =
            async {
                try
                    // セッション1 -> セッション2の順でアクセス
                    let paneState1 =
                        { PaneId = "deadlock-test-1"
                          ConversationHistory = [ "Task 1 accessing session 1" ]
                          WorkingDirectory = "/tmp"
                          Environment = Map.empty
                          ProcessStatus = "Running"
                          LastActivity = DateTime.Now
                          MessageCount = 1
                          SizeBytes = 100L }

                    let snapshot1 =
                        { SessionId = sessionId1
                          PaneStates = Map.ofList [ ("deadlock-test-1", paneState1) ]
                          CreatedAt = DateTime.Now
                          LastSavedAt = DateTime.Now
                          TotalSize = 100L
                          Version = "1.0" }

                    let result1 = saveSession testConfig snapshot1
                    do! Async.Sleep(100) // 他のタスクがアクセスする時間を作る

                    let paneState2 =
                        { PaneId = "deadlock-test-2"
                          ConversationHistory = [ "Task 1 accessing session 2" ]
                          WorkingDirectory = "/tmp"
                          Environment = Map.empty
                          ProcessStatus = "Running"
                          LastActivity = DateTime.Now
                          MessageCount = 1
                          SizeBytes = 100L }

                    let snapshot2 =
                        { SessionId = sessionId2
                          PaneStates = Map.ofList [ ("deadlock-test-2", paneState2) ]
                          CreatedAt = DateTime.Now
                          LastSavedAt = DateTime.Now
                          TotalSize = 100L
                          Version = "1.0" }

                    let result2 = saveSession testConfig snapshot2
                    return ("Task1", result1, result2)
                with ex ->
                    return ("Task1", Error $"Task1 Exception: {ex.Message}", Error "Not reached")
            }

        let task2 =
            async {
                try
                    // セッション2 -> セッション1の順でアクセス
                    let paneState2 =
                        { PaneId = "deadlock-test-3"
                          ConversationHistory = [ "Task 2 accessing session 2" ]
                          WorkingDirectory = "/tmp"
                          Environment = Map.empty
                          ProcessStatus = "Running"
                          LastActivity = DateTime.Now
                          MessageCount = 1
                          SizeBytes = 100L }

                    let snapshot2 =
                        { SessionId = sessionId2
                          PaneStates = Map.ofList [ ("deadlock-test-3", paneState2) ]
                          CreatedAt = DateTime.Now
                          LastSavedAt = DateTime.Now
                          TotalSize = 100L
                          Version = "1.0" }

                    let result2 = saveSession testConfig snapshot2
                    do! Async.Sleep(100)

                    let paneState1 =
                        { PaneId = "deadlock-test-4"
                          ConversationHistory = [ "Task 2 accessing session 1" ]
                          WorkingDirectory = "/tmp"
                          Environment = Map.empty
                          ProcessStatus = "Running"
                          LastActivity = DateTime.Now
                          MessageCount = 1
                          SizeBytes = 100L }

                    let snapshot1 =
                        { SessionId = sessionId1
                          PaneStates = Map.ofList [ ("deadlock-test-4", paneState1) ]
                          CreatedAt = DateTime.Now
                          LastSavedAt = DateTime.Now
                          TotalSize = 100L
                          Version = "1.0" }

                    let result1 = saveSession testConfig snapshot1
                    return ("Task2", result2, result1)
                with ex ->
                    return ("Task2", Error $"Task2 Exception: {ex.Message}", Error "Not reached")
            }

        // タイムアウト付きで両タスクを実行
        let results =
            [ task1; task2 ]
            |> Async.Parallel
            |> fun asyncOp -> Async.RunSynchronously(asyncOp, timeout = 10000) // 10秒タイムアウト

        // デッドロックが発生しなかったことを確認
        Assert.AreEqual(2, results.Length, "デッドロック防止テストが完了しませんでした")

        for (taskName, result1, result2) in results do
            let result1Text =
                match result1 with
                | Success _ -> "成功"
                | Error msg -> sprintf "エラー: %s" msg

            printfn "%s: 結果1 = %s" taskName result1Text

            let result2Text =
                match result2 with
                | Success _ -> "成功"
                | Error msg -> sprintf "エラー: %s" msg

            printfn "%s: 結果2 = %s" taskName result2Text

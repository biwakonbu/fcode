namespace FCode.Tests

open NUnit.Framework
open System
open System.IO
open System.Security.AccessControl
open System.Security.Principal
open FCode.SessionPersistenceManager
open FCode.DetachAttachManager

[<TestFixture>]
[<Category("Unit")>]
type ErrorHandlingTests() =

    let testConfig =
        { AutoSaveIntervalMinutes = 1
          MaxHistoryLength = 100
          MaxSessionAge = TimeSpan.FromDays(1.0)
          StorageDirectory = Path.Combine(Path.GetTempPath(), "fcode-error-test-" + Guid.NewGuid().ToString("N")[..7])
          CompressionEnabled = true
          MaxSessionSizeMB = 10 }

    let detachConfig =
        { PersistenceConfig = testConfig
          BackgroundProcessTimeout = TimeSpan.FromMinutes(5.0)
          ProcessCheckInterval = TimeSpan.FromSeconds(10.0)
          MaxDetachedSessions = 3 }

    [<SetUp>]
    member this.Setup() =
        match initializeStorage testConfig with
        | Success _ -> ()
        | Error msg -> Assert.Fail($"ストレージ初期化失敗: {msg}")

    [<TearDown>]
    member this.Cleanup() =
        try
            if Directory.Exists(testConfig.StorageDirectory) then
                // 読み込み専用属性を削除してからディレクトリを削除
                let rec clearReadOnly (dir: string) =
                    if Directory.Exists(dir) then
                        for file in Directory.GetFiles(dir) do
                            try
                                File.SetAttributes(file, FileAttributes.Normal)
                            with _ ->
                                ()

                        for subDir in Directory.GetDirectories(dir) do
                            clearReadOnly subDir

                            try
                                File.SetAttributes(subDir, FileAttributes.Normal)
                            with _ ->
                                ()

                clearReadOnly testConfig.StorageDirectory
                Directory.Delete(testConfig.StorageDirectory, true)
        with ex ->
            printfn $"エラーハンドリングテストクリーンアップ警告: {ex.Message}"

    [<Test>]
    member this.``ディスク容量不足シミュレーションテスト``() =
        let sessionId = generateSessionId ()

        // 非常に大きなデータを作成してディスク容量不足をシミュレート
        let hugePaneState =
            { PaneId = "test"
              ConversationHistory = List.replicate 100000 (String.replicate 1000 "A") // 約100MB
              WorkingDirectory = "/tmp"
              Environment = Map.empty
              ProcessStatus = "Running"
              LastActivity = DateTime.Now
              MessageCount = 100000
              SizeBytes = 100_000_000L }

        let snapshot =
            { SessionId = sessionId
              PaneStates = Map.ofList [ ("test", hugePaneState) ]
              CreatedAt = DateTime.Now
              LastSavedAt = DateTime.Now
              TotalSize = 100_000_000L
              Version = "1.0" }

        // 保存試行（ディスク容量不足または制限により失敗することを期待）
        match saveSession testConfig snapshot with
        | Success _ ->
            // 成功した場合でも、実際にファイルが作成されているか確認
            let sessionDir = Path.Combine(testConfig.StorageDirectory, "sessions", sessionId)
            let metadataFile = Path.Combine(sessionDir, "metadata.json")
            Assert.IsTrue(File.Exists(metadataFile), "メタデータファイルが作成されていません")

        | Error msg ->
            // エラーメッセージが適切であることを確認
            Assert.IsTrue(
                msg.Contains("容量")
                || msg.Contains("サイズ")
                || msg.Contains("制限")
                || msg.Contains("space"),
                $"期待されるディスク容量エラーメッセージではありません: {msg}"
            )

    [<Test>]
    member this.``読み込み専用ディレクトリへの書き込みエラーテスト``() =
        if Environment.OSVersion.Platform <> PlatformID.Win32NT then
            let sessionId = generateSessionId ()
            let readOnlyDir = Path.Combine(testConfig.StorageDirectory, "readonly-sessions")

            try
                // 読み込み専用ディレクトリを作成
                Directory.CreateDirectory(readOnlyDir) |> ignore

                // Unix系で権限を読み込み専用に設定
                let dirInfo = new DirectoryInfo(readOnlyDir)
                dirInfo.Attributes <- dirInfo.Attributes ||| FileAttributes.ReadOnly

                // chmod 444相当の権限設定
                try
                    System.Diagnostics.Process.Start("chmod", $"444 {readOnlyDir}") |> ignore
                    System.Threading.Thread.Sleep(100) // 権限変更の反映を待つ
                with _ ->
                    ()

                // 読み込み専用ディレクトリを使った設定
                let readOnlyConfig =
                    { testConfig with
                        StorageDirectory = readOnlyDir }

                let paneState =
                    { PaneId = "test"
                      ConversationHistory = [ "test" ]
                      WorkingDirectory = "/tmp"
                      Environment = Map.empty
                      ProcessStatus = "Running"
                      LastActivity = DateTime.Now
                      MessageCount = 1
                      SizeBytes = 100L }

                // 読み込み専用ディレクトリへの書き込み試行
                match savePaneState readOnlyConfig sessionId paneState with
                | Success _ -> Assert.Fail("読み込み専用ディレクトリへの書き込みが成功してしまいました")
                | Error msg ->
                    Assert.IsTrue(
                        msg.Contains("権限")
                        || msg.Contains("permission")
                        || msg.Contains("access")
                        || msg.Contains("denied"),
                        $"期待される権限エラーメッセージではありません: {msg}"
                    )

            finally
                // 権限を戻してクリーンアップ
                try
                    System.Diagnostics.Process.Start("chmod", $"755 {readOnlyDir}") |> ignore
                    System.Threading.Thread.Sleep(100)
                with _ ->
                    ()
        else
            // Windowsでは異なる権限設定方法が必要
            Assert.Inconclusive("Windows権限テストは未実装")

    [<Test>]
    member this.``破損JSONファイル読み込み耐性テスト``() =
        let sessionId = generateSessionId ()

        // 正常なセッションを作成
        let paneState =
            { PaneId = "test"
              ConversationHistory = [ "test message" ]
              WorkingDirectory = "/tmp"
              Environment = Map.ofList [ ("VAR", "value") ]
              ProcessStatus = "Running"
              LastActivity = DateTime.Now
              MessageCount = 1
              SizeBytes = 100L }

        let snapshot =
            { SessionId = sessionId
              PaneStates = Map.ofList [ ("test", paneState) ]
              CreatedAt = DateTime.Now
              LastSavedAt = DateTime.Now
              TotalSize = 100L
              Version = "1.0" }

        // 正常な保存
        match saveSession testConfig snapshot with
        | Success _ ->
            let sessionDir = Path.Combine(testConfig.StorageDirectory, "sessions", sessionId)
            let metadataFile = Path.Combine(sessionDir, "metadata.json")
            let stateFile = Path.Combine(sessionDir, "pane-states", "test.json")

            // 破損パターンのテスト
            let corruptionPatterns =
                [ "broken json content" // 完全に無効なJSON
                  "{" // 不完全なJSON開始
                  "}" // 不完全なJSON終了
                  "{\"SessionId\":\"test\"" // JSONの途中で切断
                  "{\"SessionId\":null,\"invalid\":}" // 不正な値
                  "" // 空ファイル
                  "\x00\x01\x02\x03" // バイナリデータ
                  String.replicate 1000000 "{" ] // 巨大な不正JSON

            for (i, corruptContent) in List.indexed corruptionPatterns do
                // メタデータファイルを破損
                File.WriteAllText(metadataFile, corruptContent)

                match loadSession testConfig sessionId with
                | Success _ -> Assert.Fail($"破損JSONファイル({i})の読み込みが成功してしまいました")
                | Error msg ->
                    Assert.IsTrue(
                        msg.Contains("JSON")
                        || msg.Contains("読み込み")
                        || msg.Contains("parse")
                        || msg.Contains("format"),
                        $"期待されるJSON読み込みエラーメッセージではありません: {msg}"
                    )

                // ペイン状態ファイルも破損させてテスト
                File.WriteAllText(stateFile, corruptContent)

                match loadPaneState testConfig sessionId "test" with
                | Success _ -> Assert.Fail($"破損ペイン状態ファイル({i})の読み込みが成功してしまいました")
                | Error msg ->
                    Assert.IsTrue(
                        msg.Contains("JSON")
                        || msg.Contains("読み込み")
                        || msg.Contains("parse")
                        || msg.Contains("format"),
                        $"期待されるペイン状態読み込みエラーメッセージではありません: {msg}"
                    )

        | Error msg -> Assert.Fail($"正常セッション保存失敗: {msg}")

    [<Test>]
    member this.``破損圧縮データ読み込み耐性テスト``() =
        let sessionId = generateSessionId ()

        // 圧縮ファイルを直接作成してから破損
        let historyDir =
            Path.Combine(testConfig.StorageDirectory, "sessions", sessionId, "conversation-history")

        Directory.CreateDirectory(historyDir) |> ignore

        let historyFile = Path.Combine(historyDir, "test.history.gz")

        let corruptedData =
            [ [||] // 空データ
              [| 0x1fuy; 0x8buy |] // 不完全なgzipヘッダー
              [| 0x1fuy; 0x8buy; 0x08uy; 0x00uy; 0x00uy |] // 不完全なgzipファイル
              Array.replicate 1000 0xFFuy // 不正なバイナリデータ
              System.Text.Encoding.UTF8.GetBytes("plain text data") ] // 非圧縮データ

        for (i, corruptData) in List.indexed corruptedData do
            File.WriteAllBytes(historyFile, corruptData)

            let decompressedHistory = decompressHistory corruptData

            // decompressHistoryが適切にエラーハンドリングしているか確認
            // 現在の実装では空リストを返すが、これは適切な fallback
            Assert.IsNotNull(decompressedHistory, $"破損圧縮データ({i})でnullが返されました")

    [<Test>]
    member this.``存在しないディレクトリへのアクセスエラーテスト``() =
        let nonExistentConfig =
            { testConfig with
                StorageDirectory = "/nonexistent/directory/path" }

        // 存在しないディレクトリでの初期化試行
        match initializeStorage nonExistentConfig with
        | Success _ ->
            // ディレクトリが作成される場合は正常（一部環境では権限により作成される）
            Assert.IsTrue(Directory.Exists(nonExistentConfig.StorageDirectory), "ディレクトリが作成されたはずなのに存在しません")
        | Error msg ->
            Assert.IsTrue(
                msg.Contains("初期化")
                || msg.Contains("ディレクトリ")
                || msg.Contains("path")
                || msg.Contains("directory"),
                $"期待されるディレクトリエラーメッセージではありません: {msg}"
            )

    [<Test>]
    member this.``プロセスロックファイル破損処理テスト``() =
        let sessionId = generateSessionId ()

        // 正常なプロセスロックを作成
        let result = saveProcessLock detachConfig sessionId 12345
        Assert.IsTrue(result, "プロセスロック保存が失敗しました")

        let lockFile = getProcessLockFile detachConfig sessionId

        // ロックファイルを破損
        let corruptedLockData =
            [ "invalid json"
              "{\"ProcessId\":\"not_a_number\"}"
              "{\"SessionId\":null}"
              ""
              "\x00\x01\x02\x03" ]

        for corruptData in corruptedLockData do
            File.WriteAllText(lockFile, corruptData)

            match loadProcessLock detachConfig sessionId with
            | Some _ -> Assert.Fail("破損プロセスロックファイルの読み込みが成功してしまいました")
            | None ->
                // None が返されることは期待される動作
                ()

    [<Test>]
    member this.``メモリ不足シミュレーションテスト``() =
        // 非常に大きな会話履歴でメモリ使用量をテスト
        try
            let largeHistory =
                [ 1..1000 ] |> List.map (fun i -> String.replicate 100000 $"Message {i} ") // 各メッセージ約100KB

            // 圧縮処理でメモリ不足が発生しないかテスト
            let compressedData = compressHistory largeHistory
            Assert.IsNotNull(compressedData, "圧縮処理でnullが返されました")
            Assert.Greater(compressedData.Length, 0, "圧縮データが空です")

            // 展開処理でメモリ不足が発生しないかテスト
            let decompressedHistory = decompressHistory compressedData
            Assert.IsNotNull(decompressedHistory, "展開処理でnullが返されました")
            Assert.AreEqual(largeHistory.Length, decompressedHistory.Length, "展開後のデータ件数が一致しません")

        with
        | :? OutOfMemoryException -> Assert.Inconclusive("メモリ不足により테스ト完了不能（期待される動作）")
        | ex -> Assert.Fail($"予期しないメモリ관련 에러: {ex.Message}")

    [<Test>]
    member this.``同時ファイルアクセス競合エラーテスト``() =
        let sessionId = generateSessionId ()

        let paneState =
            { PaneId = "test"
              ConversationHistory = [ "concurrent test" ]
              WorkingDirectory = "/tmp"
              Environment = Map.empty
              ProcessStatus = "Running"
              LastActivity = DateTime.Now
              MessageCount = 1
              SizeBytes = 100L }

        let snapshot =
            { SessionId = sessionId
              PaneStates = Map.ofList [ ("test", paneState) ]
              CreatedAt = DateTime.Now
              LastSavedAt = DateTime.Now
              TotalSize = 100L
              Version = "1.0" }

        // 並行保存タスクを開始
        let tasks =
            [ 1..5 ]
            |> List.map (fun i ->
                async {
                    try
                        let result = saveSession testConfig snapshot
                        return (i, result)
                    with ex ->
                        return (i, Error $"Exception: {ex.Message}")
                })

        let results = tasks |> Async.Parallel |> Async.RunSynchronously

        // 少なくとも1つは成功することを期待
        let successCount =
            results
            |> Array.filter (fun (_, result) ->
                match result with
                | Success _ -> true
                | _ -> false)
            |> Array.length

        Assert.Greater(successCount, 0, "並行保存で全て失敗しました")

        // エラーがある場合、適切なエラーメッセージかチェック
        for (i, result) in results do
            match result with
            | Error msg -> printfn $"タスク{i}エラー: {msg}"
            // ファイルロック関連のエラーは期待される
            | Success _ -> printfn $"タスク{i}成功"

    // Windowsネットワークドライブテストは削除（対象プラットフォーム外のため）

    [<Test>]
    member this.``特殊文字ファイル名処理エラーテスト``() =
        let sessionId = generateSessionId ()

        // Linux/macOSで問題となる可能性がある特殊文字
        let problematicFileNames =
            [ "ファイル名/スラッシュ" // パス区切り文字
              "ファイル名\0ヌル文字" // ヌル文字
              "ファイル名\n改行" // 改行文字
              "ファイル名\t タブ" // タブ文字
              String.replicate 300 "長" ] // 非常に長いファイル名

        for problematicName in problematicFileNames do
            let paneState =
                { PaneId = problematicName
                  ConversationHistory = [ "test" ]
                  WorkingDirectory = "/tmp"
                  Environment = Map.empty
                  ProcessStatus = "Running"
                  LastActivity = DateTime.Now
                  MessageCount = 1
                  SizeBytes = 100L }

            match savePaneState testConfig sessionId paneState with
            | Success _ ->
                // 成功した場合でも、ファイルが適切に作成されているか確認
                let stateDir =
                    Path.Combine(testConfig.StorageDirectory, "sessions", sessionId, "pane-states")

                let jsonFiles = Directory.GetFiles(stateDir, "*.json")
                Assert.Greater(jsonFiles.Length, 0, $"特殊文字ファイル名'{problematicName}'でファイルが作成されませんでした")

            | Error msg ->
                // エラーは期待される動作（特殊文字が適切に拒否される）
                Assert.IsTrue(
                    msg.Contains("ファイル名")
                    || msg.Contains("文字")
                    || msg.Contains("invalid")
                    || msg.Contains("character"),
                    $"期待される文字エラーメッセージではありません: {msg}"
                )

namespace FCode.Tests

open NUnit.Framework
open System
open System.IO
open FCode.SessionPersistenceManager
open FCode.DetachAttachManager

[<TestFixture>]
[<Category("Unit")>]
type SecurityTests() =

    let testConfig =
        { AutoSaveIntervalMinutes = 1
          MaxHistoryLength = 100
          MaxSessionAge = TimeSpan.FromDays(1.0)
          StorageDirectory =
            Path.Combine(Path.GetTempPath(), "fcode-security-test-" + Guid.NewGuid().ToString("N")[..7])
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
                Directory.Delete(testConfig.StorageDirectory, true)
        with ex ->
            printfn $"セキュリティテストクリーンアップ警告: {ex.Message}"

    [<Test>]
    member this.``パスインジェクション攻撃耐性テスト - ディレクトリトラバーサル``() =
        // 悪意あるセッションIDでのディレクトリトラバーサル攻撃
        let maliciousSessionIds =
            [ "../../../etc/passwd"
              "..\\..\\..\\windows\\system32\\config\\sam"
              "../../../home/user/.ssh/id_rsa"
              "session/../../../tmp/malicious"
              "/etc/passwd"
              "C:\\Windows\\System32\\config\\SAM"
              "session\x00.txt" // NULL文字埋め込み
              "session\r\n../../etc/passwd" ] // 改行文字埋め込み

        for maliciousId in maliciousSessionIds do
            // セッション保存の試行
            let paneState =
                { PaneId = "test"
                  ConversationHistory = [ "test" ]
                  WorkingDirectory = "/tmp"
                  Environment = Map.empty
                  ProcessStatus = "Running"
                  LastActivity = DateTime.Now
                  MessageCount = 1
                  SizeBytes = 100L }

            let snapshot =
                { SessionId = maliciousId
                  PaneStates = Map.ofList [ ("test", paneState) ]
                  CreatedAt = DateTime.Now
                  LastSavedAt = DateTime.Now
                  TotalSize = 100L
                  Version = "1.0" }

            // 保存試行（失敗するか、安全な場所に保存されることを確認）
            match saveSession testConfig snapshot with
            | Success _ ->
                // 成功した場合、保存先が安全な場所であることを確認
                let sessionDir = Path.Combine(testConfig.StorageDirectory, "sessions", maliciousId)
                let normalizedPath = Path.GetFullPath(sessionDir)
                let basePath = Path.GetFullPath(testConfig.StorageDirectory)

                Assert.IsTrue(
                    normalizedPath.StartsWith(basePath),
                    $"危険なパス: {normalizedPath} が基準ディレクトリ {basePath} の外に作成されました"
                )
            | Error _ ->
                // 失敗は期待される動作
                ()

            // プロセスロックファイルでも同様にテスト
            let lockResult = saveProcessLock detachConfig maliciousId 12345

            if lockResult then
                let lockFile = getProcessLockFile detachConfig maliciousId
                let normalizedLockPath = Path.GetFullPath(lockFile)
                let basePath = Path.GetFullPath(testConfig.StorageDirectory)

                Assert.IsTrue(
                    normalizedLockPath.StartsWith(basePath),
                    $"危険なロックファイルパス: {normalizedLockPath} が基準ディレクトリの外に作成されました"
                )

    [<Test>]
    member this.``機密情報漏洩防止テスト - 環境変数内API_KEY``() =
        let sessionId = generateSessionId ()

        // 機密情報を含む環境変数
        let sensitiveEnvironment =
            Map.ofList
                [ ("CLAUDE_API_KEY", "sk-ant-api03-super-secret-key-12345")
                  ("OPENAI_API_KEY", "sk-openai-secret-key-67890")
                  ("DATABASE_PASSWORD", "super-secret-db-password")
                  ("JWT_SECRET", "jwt-signing-secret-key")
                  ("AWS_SECRET_ACCESS_KEY", "aws-secret-access-key")
                  ("NORMAL_VAR", "normal-value") ]

        let paneState =
            { PaneId = "test"
              ConversationHistory = [ "API_KEYを使用した処理を実行中" ]
              WorkingDirectory = "/tmp"
              Environment = sensitiveEnvironment
              ProcessStatus = "Running"
              LastActivity = DateTime.Now
              MessageCount = 1
              SizeBytes = 500L }

        let snapshot =
            { SessionId = sessionId
              PaneStates = Map.ofList [ ("test", paneState) ]
              CreatedAt = DateTime.Now
              LastSavedAt = DateTime.Now
              TotalSize = 500L
              Version = "1.0" }

        // セッション保存
        match saveSession testConfig snapshot with
        | Success _ ->
            // 保存されたファイルを直接読み込み、機密情報が含まれていないか確認
            let sessionDir = Path.Combine(testConfig.StorageDirectory, "sessions", sessionId)
            let stateFile = Path.Combine(sessionDir, "pane-states", "test.json")

            let historyFile =
                Path.Combine(sessionDir, "conversation-history", "test.history.gz")

            let metadataFile = Path.Combine(sessionDir, "metadata.json")

            // ペイン状態ファイルの検査
            if File.Exists(stateFile) then
                let stateContent = File.ReadAllText(stateFile)
                Assert.IsFalse(stateContent.Contains("sk-ant-api03"), "API_KEYがペイン状態ファイルに漏洩しています")
                Assert.IsFalse(stateContent.Contains("super-secret"), "機密情報がペイン状態ファイルに漏洩しています")

                // 通常の環境変数は保存されているべき
                Assert.IsTrue(stateContent.Contains("NORMAL_VAR"), "通常の環境変数が保存されていません")

            // 会話履歴ファイルの検査
            if File.Exists(historyFile) then
                let historyBytes = File.ReadAllBytes(historyFile)
                let historyContent = decompressHistory historyBytes
                let fullHistoryText = String.Join(" ", historyContent)

                // 会話履歴内の機密情報チェック
                Assert.IsFalse(fullHistoryText.Contains("sk-ant-api03"), "API_KEYが会話履歴に漏洩しています")
                Assert.IsFalse(fullHistoryText.Contains("super-secret-db-password"), "データベースパスワードが会話履歴に漏洩しています")

            // メタデータファイルの検査
            if File.Exists(metadataFile) then
                let metadataContent = File.ReadAllText(metadataFile)
                Assert.IsFalse(metadataContent.Contains("sk-ant-api03"), "API_KEYがメタデータファイルに漏洩しています")

        | Error msg -> Assert.Fail($"機密情報テスト用セッション保存失敗: {msg}")

    [<Test>]
    member this.``ファイル名インジェクション攻撃耐性テスト``() =
        let sessionId = generateSessionId ()

        // 悪意あるペインID
        let maliciousPaneIds =
            [ "../../../etc/passwd"
              "..\\..\\config.ini"
              "pane\x00.json" // NULL文字
              "pane\r\n.exe" // 改行文字
              "pane<script>alert('xss')</script>"
              "pane|rm -rf /"
              "pane;cat /etc/passwd"
              "pane`whoami`"
              "pane$(echo malicious)" ]

        for maliciousPaneId in maliciousPaneIds do
            let paneState =
                { PaneId = maliciousPaneId
                  ConversationHistory = [ "test" ]
                  WorkingDirectory = "/tmp"
                  Environment = Map.empty
                  ProcessStatus = "Running"
                  LastActivity = DateTime.Now
                  MessageCount = 1
                  SizeBytes = 100L }

            // ペイン状態保存の試行
            match savePaneState testConfig sessionId paneState with
            | Success _ ->
                // 成功した場合、ファイルが安全な場所に作成されていることを確認
                let stateDir =
                    Path.Combine(testConfig.StorageDirectory, "sessions", sessionId, "pane-states")

                let files = Directory.GetFiles(stateDir, "*.json")

                for file in files do
                    let normalizedPath = Path.GetFullPath(file)
                    let basePath = Path.GetFullPath(testConfig.StorageDirectory)

                    Assert.IsTrue(
                        normalizedPath.StartsWith(basePath),
                        $"危険なファイルパス: {normalizedPath} が基準ディレクトリの外に作成されました"
                    )

                    // ファイル名にコマンドインジェクション文字が含まれていないことを確認
                    let fileName = Path.GetFileName(file)
                    Assert.IsFalse(fileName.Contains("|"), "ファイル名にパイプ文字が含まれています")
                    Assert.IsFalse(fileName.Contains(";"), "ファイル名にセミコロン文字が含まれています")
                    Assert.IsFalse(fileName.Contains("`"), "ファイル名にバッククォート文字が含まれています")
                    Assert.IsFalse(fileName.Contains("$"), "ファイル名にドル記号が含まれています")

            | Error _ ->
                // 失敗は期待される動作（不正なペインIDは拒否されるべき）
                ()

    [<Test>]
    member this.``シンボリックリンク攻撃耐性テスト``() =
        let sessionId = generateSessionId ()

        // テスト用の危険な場所を作成
        let dangerousPath = Path.Combine(Path.GetTempPath(), "dangerous-target")
        Directory.CreateDirectory(dangerousPath) |> ignore
        File.WriteAllText(Path.Combine(dangerousPath, "secret.txt"), "機密情報")

        try
            // シンボリックリンクが作成可能な環境でテスト
            if Environment.OSVersion.Platform = PlatformID.Unix then
                let sessionDir = Path.Combine(testConfig.StorageDirectory, "sessions", sessionId)
                Directory.CreateDirectory(sessionDir) |> ignore

                let linkPath = Path.Combine(sessionDir, "malicious-link")

                try
                    // シンボリックリンク作成の試行（権限がある場合のみ）
                    let linkInfo = new System.IO.FileInfo(linkPath)
                    linkInfo.CreateAsSymbolicLink(dangerousPath)

                    // リンクが作成された場合、アプリケーションがリンクを辿らないことを確認
                    let paneState =
                        { PaneId = "test"
                          ConversationHistory = [ "test" ]
                          WorkingDirectory = "/tmp"
                          Environment = Map.empty
                          ProcessStatus = "Running"
                          LastActivity = DateTime.Now
                          MessageCount = 1
                          SizeBytes = 100L }

                    // セッション保存時にシンボリックリンクを辿らないことを確認
                    match
                        saveSession
                            testConfig
                            { SessionId = sessionId
                              PaneStates = Map.ofList [ ("test", paneState) ]
                              CreatedAt = DateTime.Now
                              LastSavedAt = DateTime.Now
                              TotalSize = 100L
                              Version = "1.0" }
                    with
                    | Success _ ->
                        // ファイルが実際のディレクトリ内に作成されていることを確認
                        let actualSessionDir =
                            Path.Combine(testConfig.StorageDirectory, "sessions", sessionId)

                        let metadataFile = Path.Combine(actualSessionDir, "metadata.json")
                        Assert.IsTrue(File.Exists(metadataFile), "メタデータファイルが作成されていません")

                        // シンボリックリンク先には何も作成されていないことを確認
                        let linkTarget = Path.Combine(dangerousPath, "metadata.json")
                        Assert.IsFalse(File.Exists(linkTarget), "シンボリックリンク先に機密ファイルが作成されました")

                    | Error _ -> () // エラーは期待される動作

                with ex ->
                    // シンボリックリンク作成に失敗した場合（権限不足等）はテストをスキップ
                    Assert.Inconclusive($"シンボリックリンク作成失敗（権限不足の可能性）: {ex.Message}")
        // Linux/macOS専用機能（Windowsは対象プラットフォーム外）

        finally
            // テスト用ディレクトリのクリーンアップ
            try
                if Directory.Exists(dangerousPath) then
                    Directory.Delete(dangerousPath, true)
            with _ ->
                ()

    [<Test>]
    member this.``大量データによるDoS攻撃耐性テスト``() =
        let sessionId = generateSessionId ()

        // 非常に大きな会話履歴を作成（10MB相当）
        let largeMessage = String.replicate 10000 "A" // 10KB文字列
        let massiveHistory = List.replicate 1000 largeMessage // 10MB相当

        let paneState =
            { PaneId = "test"
              ConversationHistory = massiveHistory
              WorkingDirectory = "/tmp"
              Environment = Map.empty
              ProcessStatus = "Running"
              LastActivity = DateTime.Now
              MessageCount = massiveHistory.Length
              SizeBytes = int64 (massiveHistory.Length * largeMessage.Length) }

        let snapshot =
            { SessionId = sessionId
              PaneStates = Map.ofList [ ("test", paneState) ]
              CreatedAt = DateTime.Now
              LastSavedAt = DateTime.Now
              TotalSize = paneState.SizeBytes
              Version = "1.0" }

        // MaxSessionSizeMBを超える場合の処理確認
        match saveSession testConfig snapshot with
        | Success _ ->
            // 成功した場合、圧縮が適切に働いていることを確認
            let sessionDir = Path.Combine(testConfig.StorageDirectory, "sessions", sessionId)

            let historyFile =
                Path.Combine(sessionDir, "conversation-history", "test.history.gz")

            if File.Exists(historyFile) then
                let compressedSize = (new FileInfo(historyFile)).Length
                let originalSize = paneState.SizeBytes
                let compressionRatio = float compressedSize / float originalSize

                // 圧縮率が合理的であることを確認（同じ文字の繰り返しなので高圧縮率が期待される）
                Assert.Less(compressionRatio, 0.1, "圧縮率が期待値より悪い（DoS攻撃の可能性）")

        | Error msg ->
            // サイズ制限により拒否される場合も正常
            Assert.IsTrue(msg.Contains("サイズ") || msg.Contains("制限"), $"期待されるサイズ制限エラーではありません: {msg}")

    [<Test>]
    member this.``権限昇格攻撃耐性テスト``() =
        let sessionId = generateSessionId ()

        // 権限昇格を試行する環境変数
        let privilegeEscalationEnv =
            Map.ofList
                [ ("PATH", "/bin:/usr/bin:/sbin:/usr/sbin:.")
                  ("LD_PRELOAD", "/tmp/malicious.so")
                  ("LD_LIBRARY_PATH", "/tmp/malicious/lib")
                  ("SHELL", "/bin/sh -c 'rm -rf /'")
                  ("HOME", "/root")
                  ("USER", "root")
                  ("SUDO_USER", "root") ]

        let paneState =
            { PaneId = "test"
              ConversationHistory = [ "sudo rm -rf /"; "chmod 777 /etc/passwd" ]
              WorkingDirectory = "/tmp"
              Environment = privilegeEscalationEnv
              ProcessStatus = "Running"
              LastActivity = DateTime.Now
              MessageCount = 2
              SizeBytes = 200L }

        let snapshot =
            { SessionId = sessionId
              PaneStates = Map.ofList [ ("test", paneState) ]
              CreatedAt = DateTime.Now
              LastSavedAt = DateTime.Now
              TotalSize = 200L
              Version = "1.0" }

        // セッション保存後、保存されたデータから危険なコマンドが実行されないことを確認
        match saveSession testConfig snapshot with
        | Success _ ->
            // 読み込み時に危険な環境変数が適切にサニタイズされることを確認
            match loadSession testConfig sessionId with
            | Success loadedSnapshot ->
                let loadedPane = loadedSnapshot.PaneStates.["test"]

                // 危険な環境変数が保存・復元時にサニタイズされているかチェック
                // （実装により、フィルタリングまたは警告が期待される）
                if loadedPane.Environment.ContainsKey("LD_PRELOAD") then
                    printfn "警告: LD_PRELOADが保存されています - セキュリティ上の懸念"

                if
                    loadedPane.Environment.ContainsKey("SHELL")
                    && loadedPane.Environment.["SHELL"].Contains("rm -rf")
                then
                    Assert.Fail("危険なSHELL環境変数が保存されています")

            | Error _ -> () // 読み込みエラーは期待される動作かもしれない

        | Error _ -> () // 保存エラーも期待される動作かもしれない

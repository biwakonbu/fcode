namespace FCode.Tests

open NUnit.Framework
open System
open System.IO
open FCode.SessionPersistenceManager

[<TestFixture>]
[<Category("Unit")>]
type SessionPersistenceTests() =

    let testConfig =
        { AutoSaveIntervalMinutes = 1
          MaxHistoryLength = 100
          MaxSessionAge = TimeSpan.FromDays(1.0)
          StorageDirectory = Path.Combine(Path.GetTempPath(), "fcode-test-" + Guid.NewGuid().ToString("N")[..7])
          CompressionEnabled = true
          MaxSessionSizeMB = 10 }

    [<SetUp>]
    member this.Setup() =
        // テスト用ディレクトリの初期化
        match initializeStorage testConfig with
        | Success _ -> ()
        | Error msg -> Assert.Fail($"ストレージ初期化失敗: {msg}")

    [<TearDown>]
    member this.Cleanup() =
        // テスト用ディレクトリの削除
        try
            if Directory.Exists(testConfig.StorageDirectory) then
                Directory.Delete(testConfig.StorageDirectory, true)
        with ex ->
            printfn $"テストクリーンアップ警告: {ex.Message}"

    [<Test>]
    member this.``ストレージディレクトリ初期化テスト``() =
        // 必要なディレクトリが作成されているか確認
        let sessionsDir = Path.Combine(testConfig.StorageDirectory, "sessions")
        let recoveryDir = Path.Combine(testConfig.StorageDirectory, "recovery")
        let configDir = Path.Combine(testConfig.StorageDirectory, "config")

        Assert.IsTrue(Directory.Exists(sessionsDir), "sessions ディレクトリが作成されていません")
        Assert.IsTrue(Directory.Exists(recoveryDir), "recovery ディレクトリが作成されていません")
        Assert.IsTrue(Directory.Exists(configDir), "config ディレクトリが作成されていません")

    [<Test>]
    member this.``セッションID生成テスト``() =
        let sessionId1 = generateSessionId ()
        let sessionId2 = generateSessionId ()

        // セッションIDが空でないこと
        Assert.IsNotNull(sessionId1)
        Assert.IsNotEmpty(sessionId1)

        // 2つのセッションIDが異なること
        Assert.AreNotEqual(sessionId1, sessionId2)

        // 適切な形式であること (yyyyMMdd-HHmmss-xxxxxxxx)
        Assert.IsTrue(sessionId1.Length > 16, "セッションIDの長さが不適切です")
        Assert.IsTrue(sessionId1.Contains("-"), "セッションIDにハイフンが含まれていません")

    [<Test>]
    member this.``会話履歴圧縮・展開テスト``() =
        let originalHistory =
            [ "こんにちは"; "F#でのプログラミングについて教えてください"; "セッション永続化機能を実装中です"; "テストケースを作成しています" ]

        // 圧縮
        let compressed = compressHistory originalHistory
        Assert.IsNotNull(compressed)
        Assert.Greater(compressed.Length, 0)

        // 展開
        let decompressed = decompressHistory compressed
        Assert.AreEqual(originalHistory.Length, decompressed.Length)
        Assert.AreEqual(originalHistory, decompressed)

    [<Test>]
    member this.``基本的なペイン状態保存・読み込みテスト``() =
        let sessionId = generateSessionId ()

        let paneState =
            { PaneId = "dev1"
              ConversationHistory = [ "Hello"; "World"; "Test" ]
              WorkingDirectory = "/tmp/test"
              Environment = Map.ofList [ ("CLAUDE_ROLE", "dev"); ("TEST_VAR", "value") ]
              ProcessStatus = "Running"
              LastActivity = DateTime.Now
              MessageCount = 3
              SizeBytes = 1024L }

        // 保存
        match savePaneState testConfig sessionId paneState with
        | Success _ -> ()
        | Error msg -> Assert.Fail($"ペイン状態保存失敗: {msg}")

        // 読み込み
        match loadPaneState testConfig sessionId paneState.PaneId with
        | Success loadedState ->
            Assert.AreEqual(paneState.PaneId, loadedState.PaneId)
            Assert.AreEqual(paneState.WorkingDirectory, loadedState.WorkingDirectory)
            Assert.AreEqual(paneState.Environment, loadedState.Environment)
            Assert.AreEqual(paneState.ProcessStatus, loadedState.ProcessStatus)
            Assert.AreEqual(paneState.ConversationHistory, loadedState.ConversationHistory)
        | Error msg -> Assert.Fail($"ペイン状態読み込み失敗: {msg}")

    [<Test>]
    member this.``セッションスナップショット保存・読み込みテスト``() =
        let sessionId = generateSessionId ()

        let paneStates =
            Map.ofList
                [ ("dev1",
                   { PaneId = "dev1"
                     ConversationHistory = [ "dev1 message 1"; "dev1 message 2" ]
                     WorkingDirectory = "/tmp/dev1"
                     Environment = Map.ofList [ ("CLAUDE_ROLE", "dev") ]
                     ProcessStatus = "Running"
                     LastActivity = DateTime.Now.AddMinutes(-5.0)
                     MessageCount = 2
                     SizeBytes = 500L })
                  ("qa1",
                   { PaneId = "qa1"
                     ConversationHistory = [ "qa1 message 1" ]
                     WorkingDirectory = "/tmp/qa1"
                     Environment = Map.ofList [ ("CLAUDE_ROLE", "qa") ]
                     ProcessStatus = "Running"
                     LastActivity = DateTime.Now.AddMinutes(-3.0)
                     MessageCount = 1
                     SizeBytes = 300L }) ]

        let snapshot =
            { SessionId = sessionId
              PaneStates = paneStates
              CreatedAt = DateTime.Now.AddMinutes(-10.0)
              LastSavedAt = DateTime.Now
              TotalSize = 800L
              Version = "1.0" }

        // 保存
        match saveSession testConfig snapshot with
        | Success _ -> ()
        | Error msg -> Assert.Fail($"セッション保存失敗: {msg}")

        // 読み込み
        match loadSession testConfig sessionId with
        | Success loadedSnapshot ->
            Assert.AreEqual(snapshot.SessionId, loadedSnapshot.SessionId)
            Assert.AreEqual(snapshot.PaneStates.Count, loadedSnapshot.PaneStates.Count)
            Assert.AreEqual(snapshot.Version, loadedSnapshot.Version)

            // 各ペインの状態確認
            for KeyValue(paneId, originalState) in snapshot.PaneStates do
                Assert.IsTrue(loadedSnapshot.PaneStates.ContainsKey(paneId))
                let loadedState = loadedSnapshot.PaneStates.[paneId]
                Assert.AreEqual(originalState.PaneId, loadedState.PaneId)
                Assert.AreEqual(originalState.ConversationHistory, loadedState.ConversationHistory)
        | Error msg -> Assert.Fail($"セッション読み込み失敗: {msg}")

    [<Test>]
    member this.``セッション一覧取得テスト``() =
        // 複数のセッションを作成
        let sessions = [ generateSessionId (); generateSessionId (); generateSessionId () ]

        for sessionId in sessions do
            let snapshot =
                { SessionId = sessionId
                  PaneStates =
                    Map.ofList
                        [ ("dev1",
                           { PaneId = "dev1"
                             ConversationHistory = []
                             WorkingDirectory = "/tmp"
                             Environment = Map.empty
                             ProcessStatus = "Running"
                             LastActivity = DateTime.Now
                             MessageCount = 0
                             SizeBytes = 0L }) ]
                  CreatedAt = DateTime.Now
                  LastSavedAt = DateTime.Now
                  TotalSize = 0L
                  Version = "1.0" }

            match saveSession testConfig snapshot with
            | Success _ -> ()
            | Error msg -> Assert.Fail($"テスト用セッション保存失敗: {msg}")

        // セッション一覧取得
        let sessionList = listSessions testConfig
        Assert.GreaterOrEqual(sessionList.Length, sessions.Length)

        // 作成したセッションが含まれているか確認
        let sessionIds = sessionList |> List.map (fun s -> s.SessionId) |> Set.ofList

        for sessionId in sessions do
            Assert.IsTrue(sessionIds.Contains(sessionId), $"セッション {sessionId} が一覧に含まれていません")

    [<Test>]
    member this.``アクティブセッション設定・取得テスト``() =
        let sessionId = generateSessionId ()

        // アクティブセッション設定
        match setActiveSession testConfig sessionId with
        | Success _ -> ()
        | Error msg -> Assert.Fail($"アクティブセッション設定失敗: {msg}")

        // アクティブセッション取得
        match getActiveSession testConfig with
        | Success(Some activeSessionId) -> Assert.AreEqual(sessionId, activeSessionId)
        | Success None -> Assert.Fail("アクティブセッションが取得できませんでした")
        | Error msg -> Assert.Fail($"アクティブセッション取得失敗: {msg}")

    [<Test>]
    member this.``存在しないセッション読み込みエラーテスト``() =
        let nonExistentSessionId = "non-existent-session"

        match loadSession testConfig nonExistentSessionId with
        | Success _ -> Assert.Fail("存在しないセッションの読み込みが成功してしまいました")
        | Error msg -> Assert.IsTrue(msg.Contains("見つかりません"), $"期待されるエラーメッセージではありません: {msg}")

    [<Test>]
    member this.``空の会話履歴での圧縮・展開テスト``() =
        let emptyHistory: string list = []

        let compressed = compressHistory emptyHistory
        Assert.IsNotNull(compressed)

        let decompressed = decompressHistory compressed
        Assert.AreEqual(emptyHistory, decompressed)

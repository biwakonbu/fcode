namespace FCode.Tests

open NUnit.Framework
open System
open System.IO
open System.Diagnostics
open System.Threading.Tasks
open FCode.SessionPersistenceManager
open FCode.DetachAttachManager

[<TestFixture>]
[<Category("Unit")>]
type DetachAttachTests() =

    let testConfig =
        { PersistenceConfig =
            { AutoSaveIntervalMinutes = 1
              MaxHistoryLength = 100
              MaxSessionAge = TimeSpan.FromDays(1.0)
              StorageDirectory =
                Path.Combine(Path.GetTempPath(), "fcode-detach-test-" + Guid.NewGuid().ToString("N")[..7])
              CompressionEnabled = true
              MaxSessionSizeMB = 10 }
          BackgroundProcessTimeout = TimeSpan.FromMinutes(5.0)
          ProcessCheckInterval = TimeSpan.FromSeconds(10.0)
          MaxDetachedSessions = 3 }

    [<SetUp>]
    member this.Setup() =
        // テスト用ディレクトリの初期化
        match initializeStorage testConfig.PersistenceConfig with
        | Success _ -> ()
        | Error msg -> Assert.Fail($"ストレージ初期化失敗: {msg}")

    [<TearDown>]
    member this.Cleanup() =
        // テスト用ディレクトリの削除
        try
            if Directory.Exists(testConfig.PersistenceConfig.StorageDirectory) then
                Directory.Delete(testConfig.PersistenceConfig.StorageDirectory, true)
        with ex ->
            printfn $"テストクリーンアップ警告: {ex.Message}"

    [<Test>]
    [<Category("Unit")>]
    member this.``プロセスロックファイルパス生成テスト``() =
        let sessionId = "test-session-123"
        let lockFile = getProcessLockFile testConfig sessionId

        Assert.IsNotNull(lockFile)
        Assert.IsTrue(lockFile.Contains(sessionId))
        Assert.IsTrue(lockFile.EndsWith(".lock"))

        // ディレクトリ構造が正しいか確認
        let expectedDir =
            Path.Combine(testConfig.PersistenceConfig.StorageDirectory, "locks")

        Assert.IsTrue(lockFile.StartsWith(expectedDir))

    [<Test>]
    [<Category("Unit")>]
    member this.``プロセスロック保存・読み込みテスト``() =
        let sessionId = generateSessionId ()
        let currentPid = Process.GetCurrentProcess().Id

        // プロセスロック保存
        let saveResult = saveProcessLock testConfig sessionId currentPid
        Assert.IsTrue(saveResult, "プロセスロック保存が失敗しました")

        // プロセスロック読み込み
        match loadProcessLock testConfig sessionId with
        | Some lockInfo ->
            Assert.AreEqual(sessionId, lockInfo.SessionId)
            Assert.AreEqual(currentPid, lockInfo.ProcessId)
            Assert.IsTrue(lockInfo.IsDetached)
            Assert.IsTrue(lockInfo.StartTime <= DateTime.Now)
        | None -> Assert.Fail("プロセスロック読み込みが失敗しました")

    [<Test>]
    [<Category("Unit")>]
    member this.``プロセスロック削除テスト``() =
        let sessionId = generateSessionId ()
        let currentPid = Process.GetCurrentProcess().Id

        // プロセスロック保存
        let saveResult = saveProcessLock testConfig sessionId currentPid
        Assert.IsTrue(saveResult)

        // 保存されたことを確認
        match loadProcessLock testConfig sessionId with
        | Some _ -> () // 正常
        | None -> Assert.Fail("プロセスロックが保存されていません")

        // プロセスロック削除
        removeProcessLock testConfig sessionId

        // 削除されたことを確認
        match loadProcessLock testConfig sessionId with
        | Some _ -> Assert.Fail("プロセスロックが削除されていません")
        | None -> () // 正常

    [<Test>]
    [<Category("Unit")>]
    member this.``現在プロセスの生存確認テスト``() =
        let currentPid = Process.GetCurrentProcess().Id
        let isAlive = isProcessAlive currentPid
        Assert.IsTrue(isAlive, "現在のプロセスが生存していないと判定されました")

    [<Test>]
    [<Category("Unit")>]
    member this.``存在しないプロセスの生存確認テスト``() =
        // 存在しない可能性の高いプロセスID
        let nonExistentPid = 999999
        let isAlive = isProcessAlive nonExistentPid
        Assert.IsFalse(isAlive, "存在しないプロセスが生存していると判定されました")

    [<Test>]
    [<Category("Unit")>]
    member this.``デタッチセッション一覧取得テスト``() =
        let sessionIds = [ generateSessionId (); generateSessionId () ]
        let currentPid = Process.GetCurrentProcess().Id

        // 複数のプロセスロックを作成
        for sessionId in sessionIds do
            let saveResult = saveProcessLock testConfig sessionId currentPid
            Assert.IsTrue(saveResult, $"セッション {sessionId} のロック保存が失敗しました")

        // デタッチセッション一覧取得
        let detachedSessions = listDetachedSessions testConfig
        Assert.GreaterOrEqual(detachedSessions.Length, sessionIds.Length)

        // 作成したセッションが含まれているか確認
        let detachedSessionIds =
            detachedSessions |> List.map (fun s -> s.SessionId) |> Set.ofList

        for sessionId in sessionIds do
            Assert.IsTrue(detachedSessionIds.Contains(sessionId), $"デタッチセッション {sessionId} が一覧に含まれていません")

    [<Test>]
    [<Category("Unit")>]
    member this.``存在しないセッションのアタッチテスト``() =
        let nonExistentSessionId = "non-existent-session"

        let attachTask = attachSession testConfig nonExistentSessionId
        let result = Async.RunSynchronously(attachTask, timeout = 5000)

        match result with
        | SessionNotFound sessionId -> Assert.AreEqual(nonExistentSessionId, sessionId)
        | _ -> Assert.Fail("存在しないセッションに対して適切なエラーが返されませんでした")

    [<Test>]
    [<Category("Unit")>]
    member this.``復旧候補セッション検索テスト``() =
        // 最近のセッションを作成
        let recentSessionId = generateSessionId ()

        let recentSnapshot =
            { SessionId = recentSessionId
              PaneStates =
                Map.ofList
                    [ ("dev1",
                       { PaneId = "dev1"
                         ConversationHistory = []
                         WorkingDirectory = "/tmp"
                         Environment = Map.empty
                         ProcessStatus = "Running"
                         LastActivity = DateTime.Now.AddMinutes(-30.0) // 30分前
                         MessageCount = 0
                         SizeBytes = 0L }) ]
              CreatedAt = DateTime.Now.AddHours(-1.0)
              LastSavedAt = DateTime.Now.AddMinutes(-30.0)
              TotalSize = 0L
              Version = "1.0" }

        // 古いセッションを作成
        let oldSessionId = generateSessionId ()

        let oldSnapshot =
            { SessionId = oldSessionId
              PaneStates =
                Map.ofList
                    [ ("dev1",
                       { PaneId = "dev1"
                         ConversationHistory = []
                         WorkingDirectory = "/tmp"
                         Environment = Map.empty
                         ProcessStatus = "Running"
                         LastActivity = DateTime.Now.AddHours(-5.0) // 5時間前
                         MessageCount = 0
                         SizeBytes = 0L }) ]
              CreatedAt = DateTime.Now.AddHours(-6.0)
              LastSavedAt = DateTime.Now.AddHours(-5.0)
              TotalSize = 0L
              Version = "1.0" }

        // セッション保存
        match saveSession testConfig.PersistenceConfig recentSnapshot with
        | Success _ -> ()
        | Error msg -> Assert.Fail($"最近のセッション保存失敗: {msg}")

        match saveSession testConfig.PersistenceConfig oldSnapshot with
        | Success _ -> ()
        | Error msg -> Assert.Fail($"古いセッション保存失敗: {msg}")

        // 復旧候補検索
        let recoverableSessions = findRecoverableSessions testConfig

        // 最近のセッションが含まれ、古いセッションが含まれないことを確認
        let recoverableIds =
            recoverableSessions |> List.map (fun s -> s.SessionId) |> Set.ofList

        Assert.IsTrue(recoverableIds.Contains(recentSessionId), "最近のセッションが復旧候補に含まれていません")
        Assert.IsFalse(recoverableIds.Contains(oldSessionId), "古いセッションが復旧候補に含まれています")

    [<Test>]
    [<Category("Unit")>]
    member this.``孤立プロセスロック清理テスト``() =
        let sessionId = generateSessionId ()
        let fakePid = 999999 // 存在しないプロセスID

        // 孤立したプロセスロックを作成
        let saveResult = saveProcessLock testConfig sessionId fakePid
        Assert.IsTrue(saveResult)

        // 清理前の確認
        match loadProcessLock testConfig sessionId with
        | Some _ -> () // 正常
        | None -> Assert.Fail("テスト用プロセスロックが作成されていません")

        // 直接ロックファイルが存在することを確認
        let lockFile = getProcessLockFile testConfig sessionId
        Assert.IsTrue(System.IO.File.Exists(lockFile), "ロックファイルが作成されていません")

        // 孤立プロセスロック清理実行
        let cleanupTask = cleanupOrphanedLocks testConfig
        let cleanedCount = Async.RunSynchronously(cleanupTask, timeout = 5000)

        // 清理後、ロックファイルが削除されているか確認
        Assert.IsFalse(System.IO.File.Exists(lockFile), "孤立プロセスロックが清理されていません")

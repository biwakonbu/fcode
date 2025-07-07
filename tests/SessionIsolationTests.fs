namespace FCode.Tests

open NUnit.Framework
open FCode
open System
open System.IO

/// FC-010: セッション間独立性基本テストスイート
[<TestFixture>]
[<Category("Unit")>]
type SessionIsolationTests() =

    let mutable testBaseDir = ""

    [<SetUp>]
    member _.Setup() =
        testBaseDir <- Path.Combine(Path.GetTempPath(), sprintf "fcode-test-%s" (Guid.NewGuid().ToString("N").[..7]))

    [<TearDown>]
    member _.TearDown() =
        if Directory.Exists(testBaseDir) then
            Directory.Delete(testBaseDir, true)

    /// 基本的な作業ディレクトリ作成テスト
    [<Test>]
    [<Category("Unit")>]
    member _.BasicWorkspaceCreationTest() =
        let config =
            { WorkingDirectoryManager.defaultConfig with
                SessionsBaseDir = Path.Combine(testBaseDir, "sessions") }

        match WorkingDirectoryManager.createPaneWorkspace config "test-pane" with
        | Ok workspace ->
            Assert.IsTrue(Directory.Exists(workspace.BaseDirectory))
            Assert.IsTrue(Directory.Exists(workspace.WorkingDirectory))
            Assert.IsTrue(Directory.Exists(workspace.TempDirectory))
            Assert.IsTrue(Directory.Exists(workspace.OutputDirectory))
            Assert.AreEqual("test-pane", workspace.PaneId)
        | Error e -> Assert.Fail(sprintf "ワークスペース作成失敗: %s" e)

    /// 基本的な環境変数分離テスト
    [<Test>]
    [<Category("Unit")>]
    member _.BasicEnvironmentIsolationTest() =
        let config = EnvironmentIsolation.defaultConfig

        match EnvironmentIsolation.createIsolatedEnvironment config "test-pane" "dev" testBaseDir with
        | Ok environment ->
            Assert.AreEqual("test-pane", environment.PaneId)
            Assert.AreEqual("dev", environment.ClaudeRole)
            Assert.IsTrue(environment.CustomVars.ContainsKey("PANE_ID"))
            Assert.IsTrue(environment.CustomVars.ContainsKey("SESSION_ID"))
        | Error e -> Assert.Fail(sprintf "環境作成失敗: %s" e)

    /// 基本的なファイルロックテスト
    [<Test>]
    [<Category("Unit")>]
    member _.BasicFileLockTest() =
        let config =
            { FileLockManager.defaultConfig with
                LocksDirectory = Path.Combine(testBaseDir, "locks") }

        let testFile = Path.Combine(testBaseDir, "test-file.txt")
        Directory.CreateDirectory(testBaseDir) |> ignore
        File.WriteAllText(testFile, "test content")

        match FileLockManager.acquireFileLock config testFile FileLockManager.WriteLock "test-pane" None with
        | FileLockManager.LockAcquired lockId ->
            Assert.IsNotNull(lockId)
            Assert.IsTrue(lockId.Length > 0)

            // ロック解放
            match FileLockManager.releaseFileLock config lockId with
            | Ok() -> Assert.Pass("ロック取得・解放成功")
            | Error e -> Assert.Fail(sprintf "ロック解放失敗: %s" e)
        | result -> Assert.Fail(sprintf "ロック取得失敗: %A" result)

    /// 基本的なセッション状態管理テスト
    [<Test>]
    [<Category("Unit")>]
    member _.BasicSessionStateTest() =
        let config =
            { SessionStateManager.defaultConfig with
                StatesBaseDir = Path.Combine(testBaseDir, "sessions") }

        let environment = Map.ofList [ ("TEST_VAR", "test-value") ]

        match SessionStateManager.createNewSessionState config "test-pane" testBaseDir environment with
        | Ok session ->
            Assert.AreEqual("test-pane", session.PaneId)
            Assert.IsTrue(session.SessionId.Contains("test-pane"))
            Assert.AreEqual(0, session.ConversationHistory.Length)
            Assert.AreEqual(testBaseDir, session.WorkingDirectory)
        | Error e -> Assert.Fail(sprintf "セッション作成失敗: %s" e)

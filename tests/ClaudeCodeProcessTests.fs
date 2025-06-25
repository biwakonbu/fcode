module fcode.Tests.ClaudeCodeProcessTests

open NUnit.Framework
open Terminal.Gui
open TuiPoC.ClaudeCodeProcess
open System.IO

[<TestFixture>]
type ClaudeCodeProcessTests() =
    
    let mutable sessionManager: SessionManager option = None
    
    [<SetUp>]
    member _.Setup() =
        // 各テストの前にTerminal.Guiを初期化
        try
            Application.Init()
        with
        | _ -> () // Already initialized
        
        // 新しいSessionManagerインスタンスを作成
        sessionManager <- Some (SessionManager())
    
    [<TearDown>]
    member _.TearDown() =
        // 全セッションをクリーンアップ
        match sessionManager with
        | Some manager -> manager.CleanupAllSessions()
        | None -> ()
        
        try
            Application.Shutdown()
        with
        | _ -> () // Not initialized or already shutdown

    [<Test>]
    member _.``SessionManager初期化テスト``() =
        match sessionManager with
        | Some manager ->
            Assert.That(manager.GetActiveSessionCount(), Is.EqualTo(0), "初期状態でアクティブセッション数が0であること")
        | None ->
            Assert.Fail("SessionManagerが初期化されていない")

    [<Test>]
    member _.``セッション状態確認テスト``() =
        match sessionManager with
        | Some manager ->
            let paneId = "test_pane"
            // 初期状態でセッションが非アクティブ
            Assert.That(manager.IsSessionActive(paneId), Is.False, "初期状態でセッションが非アクティブであること")
        | None ->
            Assert.Fail("SessionManagerが初期化されていない")

    [<Test>]
    member _.``無効なセッション停止テスト``() =
        match sessionManager with
        | Some manager ->
            let paneId = "nonexistent_pane"
            // 存在しないセッションの停止は失敗する
            let result = manager.StopSession(paneId)
            Assert.That(result, Is.False, "存在しないセッションの停止は失敗すること")
        | None ->
            Assert.Fail("SessionManagerが初期化されていない")

    [<Test>]
    member _.``セッション入力送信テスト - 非アクティブセッション``() =
        match sessionManager with
        | Some manager ->
            let paneId = "test_pane"
            let input = "test input"
            // 非アクティブセッションへの入力送信は失敗する
            let result = manager.SendInput(paneId, input)
            Assert.That(result, Is.False, "非アクティブセッションへの入力送信は失敗すること")
        | None ->
            Assert.Fail("SessionManagerが初期化されていない")

    [<Test>]
    member _.``アクティブセッション数カウントテスト``() =
        match sessionManager with
        | Some manager ->
            // 初期状態
            Assert.That(manager.GetActiveSessionCount(), Is.EqualTo(0), "初期状態でアクティブセッション数が0")
            // 注意: 実際のClaude Codeプロセス起動はCI環境では実行できないため、
            // ここでは基本的なカウント機能のみテスト
        | None ->
            Assert.Fail("SessionManagerが初期化されていない")

    [<Test>]
    member _.``全セッションクリーンアップテスト``() =
        match sessionManager with
        | Some manager ->
            // クリーンアップ実行（例外が発生しないことを確認）
            Assert.DoesNotThrow(fun () -> manager.CleanupAllSessions())
            // クリーンアップ後はアクティブセッション数が0
            Assert.That(manager.GetActiveSessionCount(), Is.EqualTo(0), "クリーンアップ後にアクティブセッション数が0であること")
        | None ->
            Assert.Fail("SessionManagerが初期化されていない")

    [<Test>]
    member _.``重複セッション起動テスト - モック``() =
        match sessionManager with
        | Some manager ->
            // 注意: 実際のプロセス起動はテスト環境では困難なため、
            // 基本的なAPI呼び出しのみテスト
            let paneId = "test_pane"
            let workingDir = Directory.GetCurrentDirectory()
            // Claude CLIが存在しない環境でも例外が発生しないことを確認
            let mockTextView = new TextView()
            Assert.DoesNotThrow(fun () -> 
                manager.StartSession(paneId, workingDir, mockTextView) |> ignore)
        | None ->
            Assert.Fail("SessionManagerが初期化されていない")

    [<Test>]
    member _.``無効なディレクトリでのセッション起動テスト``() =
        match sessionManager with
        | Some manager ->
            let paneId = "test_pane"
            let invalidDir = "/nonexistent/directory"
            // 無効なディレクトリでのセッション起動は例外を適切に処理すること
            let mockTextView = new TextView()
            Assert.DoesNotThrow(fun () -> 
                manager.StartSession(paneId, invalidDir, mockTextView) |> ignore)
        | None ->
            Assert.Fail("SessionManagerが初期化されていない")
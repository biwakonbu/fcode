module FCode.Tests.KeyBindingsTests

open NUnit.Framework
open Terminal.Gui
open FCode.KeyBindings
open FCode.ClaudeCodeProcess
open System

[<TestFixture>]
type KeyBindingsTests() =

    let createMockFrameViews () =
        // CI環境でのTerminal.Gui初期化スキップ
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isCI then
            // CI環境ではモックオブジェクトを作成
            Array.init 8 (fun i -> null)
        else
            Application.Init()
            Array.init 8 (fun i -> new FrameView("pane" + i.ToString()))

    let createMockSessionManager () = SessionManager()

    let skipIfCI () =
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isCI then
            Assert.Ignore("CI環境ではTerminal.Guiテストをスキップ")

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
        // テスト後のクリーンアップ
        try
            Application.Shutdown()
        with _ ->
            () // Not initialized or already shutdown

    [<Test>]
    member _.``キーアクション型のテスト``() =
        // キーアクションの型安全性テスト
        let exitAction = Exit
        let nextPaneAction = NextPane
        let focusPaneAction = FocusPane 3

        Assert.That(exitAction, Is.EqualTo(Exit))
        Assert.That(nextPaneAction, Is.EqualTo(NextPane))
        Assert.That(focusPaneAction, Is.EqualTo(FocusPane 3))

    [<Test>]
    member _.``Emacsキーバインド定義のテスト``() =
        // キーバインド定義の存在確認
        let hasExitBinding =
            emacsKeyBindings
            |> List.exists (fun (keys, action) ->
                match keys, action with
                | [ k1; k2 ], Exit when k1 = (Key.CtrlMask ||| Key.X) && k2 = (Key.CtrlMask ||| Key.C) -> true
                | _ -> false)

        let hasNextPaneBinding =
            emacsKeyBindings
            |> List.exists (fun (keys, action) ->
                match keys, action with
                | [ k1; k2 ], NextPane when k1 = (Key.CtrlMask ||| Key.X) && k2 = Key.O -> true
                | _ -> false)

        Assert.That(hasExitBinding, Is.True, "Ctrl+X Ctrl+C による終了バインドが存在すること")
        Assert.That(hasNextPaneBinding, Is.True, "Ctrl+X O による次ペイン移動バインドが存在すること")

    [<Test>]
    member _.``EmacsKeyHandlerの初期化テスト``() =
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        Assert.That(handler.CurrentPaneIndex, Is.EqualTo(0), "初期ペインインデックスは0であること")

    [<Test>]
    member _.``ペインインデックス設定テスト``() =
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // 有効なインデックス設定
        handler.SetCurrentPaneIndex(3)
        Assert.That(handler.CurrentPaneIndex, Is.EqualTo(3), "有効なインデックスが設定されること")

        // 無効なインデックス設定（負の値）
        handler.SetCurrentPaneIndex(-1)
        Assert.That(handler.CurrentPaneIndex, Is.EqualTo(3), "負のインデックスは無視されること")

        // 無効なインデックス設定（範囲外）
        handler.SetCurrentPaneIndex(10)
        Assert.That(handler.CurrentPaneIndex, Is.EqualTo(3), "範囲外のインデックスは無視されること")

    [<Test>]
    member _.``シングルキーバインドテスト``() =
        // CI環境ではスキップ（Terminal.Gui Application.Refresh依存）
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isCI then
            Assert.Ignore("Skipped in CI environment due to Terminal.Gui Application.Refresh dependencies")
        else
            let panes = createMockFrameViews ()
            let handler = EmacsKeyHandler(panes, createMockSessionManager ())

            // Ctrl+L (Refresh) のテスト
            let refreshKey = KeyEvent(Key.CtrlMask ||| Key.L, KeyModifiers())
            let handled = handler.HandleKey(refreshKey)

            Assert.That(handled, Is.True, "Ctrl+L キーが処理されること")

    [<Test>]
    member _.``マルチキーシーケンステスト``() =
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // Ctrl+X の最初のキー
        let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let firstHandled = handler.HandleKey(firstKey)

        Assert.That(firstHandled, Is.True, "最初のキー（Ctrl+X）が処理されること")

        // 次のキー O (NextPane)
        let secondKey = KeyEvent(Key.O, KeyModifiers())
        let secondHandled = handler.HandleKey(secondKey)

        Assert.That(secondHandled, Is.True, "2番目のキー（O）が処理されること")
        Assert.That(handler.CurrentPaneIndex, Is.EqualTo(1), "次のペインに移動していること")

    [<Test>]
    member _.``キーシーケンスタイムアウトテスト``() =
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // Ctrl+X を送信
        let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let firstHandled = handler.HandleKey(firstKey)
        Assert.That(firstHandled, Is.True)

        // 2秒以上待機をシミュレート（実際の待機は行わず、無効なキーで代用）
        let invalidKey = KeyEvent(Key.A, KeyModifiers())
        let invalidHandled = handler.HandleKey(invalidKey)

        Assert.That(invalidHandled, Is.False, "無効なキーシーケンスは処理されないこと")

    [<Test>]
    member _.``ダイレクトペイン移動テスト``() =
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // Ctrl+X 3 でペイン3に移動
        let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let secondKey = KeyEvent(Key.D3, KeyModifiers())

        handler.HandleKey(firstKey) |> ignore
        let handled = handler.HandleKey(secondKey)

        Assert.That(handled, Is.True, "ダイレクト移動キーが処理されること")
        Assert.That(handler.CurrentPaneIndex, Is.EqualTo(3), "指定ペインに移動していること")

    [<Test>]
    member _.``前ペイン移動テスト``() =
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // 初期状態でペイン2に設定
        handler.SetCurrentPaneIndex(2)

        // Ctrl+X Ctrl+O で前のペインに移動
        let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let secondKey = KeyEvent(Key.CtrlMask ||| Key.O, KeyModifiers())

        handler.HandleKey(firstKey) |> ignore
        let handled = handler.HandleKey(secondKey)

        Assert.That(handled, Is.True, "前ペイン移動キーが処理されること")
        Assert.That(handler.CurrentPaneIndex, Is.EqualTo(1), "前のペインに移動していること")

    [<Test>]
    member _.``ペイン移動の循環テスト``() =
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // 最後のペイン(7)に設定
        handler.SetCurrentPaneIndex(7)

        // 次のペインに移動（循環して0に戻る）
        let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let secondKey = KeyEvent(Key.O, KeyModifiers())

        handler.HandleKey(firstKey) |> ignore
        handler.HandleKey(secondKey) |> ignore

        Assert.That(handler.CurrentPaneIndex, Is.EqualTo(0), "最後のペインから最初のペインに循環すること")

        // 前のペインに移動（循環して7に戻る）
        let thirdKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let fourthKey = KeyEvent(Key.CtrlMask ||| Key.O, KeyModifiers())

        handler.HandleKey(thirdKey) |> ignore
        handler.HandleKey(fourthKey) |> ignore

        Assert.That(handler.CurrentPaneIndex, Is.EqualTo(7), "最初のペインから最後のペインに循環すること")

    [<Test>]
    member _.``Ctrl-X Ctrl-C終了コマンドテスト``() =
        // CI環境ではスキップ（Terminal.Gui Application.RequestStop依存）
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isCI then
            Assert.Ignore("Skipped in CI environment due to Terminal.Gui Application.RequestStop dependencies")
        else
            let panes = createMockFrameViews ()
            let handler = EmacsKeyHandler(panes, createMockSessionManager ())

            // Ctrl+X Ctrl+C による終了コマンドをテスト
            let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
            let secondKey = KeyEvent(Key.CtrlMask ||| Key.C, KeyModifiers())

            let firstHandled = handler.HandleKey(firstKey)
            Assert.That(firstHandled, Is.True, "最初のキー（Ctrl+X）が処理されること")

            // 2番目のキーでExitアクションが実行される（Application.RequestStop()が呼ばれる）
            let secondHandled = handler.HandleKey(secondKey)
            Assert.That(secondHandled, Is.True, "2番目のキー（Ctrl+C）が処理されること")

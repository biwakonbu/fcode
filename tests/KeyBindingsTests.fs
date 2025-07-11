module FCode.Tests.KeyBindingsTests

open NUnit.Framework
open Terminal.Gui
open FCode.KeyBindings
open FCode.ClaudeCodeProcess
open System

[<TestFixture>]
[<Category("Unit")>]
type KeyBindingsTests() =

    let createMockFrameViews () =
        // CI環境でのTerminal.Gui初期化スキップ
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isCI then
            // CI環境では実際のFrameViewを作成するが、Terminal.Gui初期化は不要
            Array.init 8 (fun i -> new FrameView("mock-pane" + i.ToString()))
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
    [<Category("Unit")>]
    member _.``キーアクション型のテスト``() =
        // キーアクションの型安全性テスト
        let exitAction = Exit
        let nextPaneAction = NextPane
        let focusPaneAction = FocusPane 3

        Assert.AreEqual(Exit, exitAction)
        Assert.AreEqual(NextPane, nextPaneAction)
        Assert.AreEqual(FocusPane 3, focusPaneAction)

    [<Test>]
    [<Category("Unit")>]
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

        Assert.IsTrue(hasExitBinding, "Ctrl+X Ctrl+C による終了バインドが存在すること")
        Assert.IsTrue(hasNextPaneBinding, "Ctrl+X O による次ペイン移動バインドが存在すること")

    [<Test>]
    [<Category("Unit")>]
    member _.``EmacsKeyHandlerの初期化テスト``() =
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        Assert.AreEqual(0, handler.CurrentPaneIndex, "初期ペインインデックスは0であること")

    [<Test>]
    [<Category("Unit")>]
    member _.``ペインインデックス設定テスト``() =
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // 有効なインデックス設定
        handler.SetCurrentPaneIndex(3)
        Assert.AreEqual(3, handler.CurrentPaneIndex, "有効なインデックスが設定されること")

        // 無効なインデックス設定（負の値）
        handler.SetCurrentPaneIndex(-1)
        Assert.AreEqual(3, handler.CurrentPaneIndex, "負のインデックスは無視されること")

        // 無効なインデックス設定（範囲外）
        handler.SetCurrentPaneIndex(10)
        Assert.AreEqual(3, handler.CurrentPaneIndex, "範囲外のインデックスは無視されること")

    [<Test>]
    [<Category("Unit")>]
    member _.``シングルキーバインドテスト``() =
        // CI環境ではスキップ（Terminal.Gui Application.Refresh依存）
        skipIfCI ()
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // Ctrl+L (Refresh) のテスト
        let refreshKey = KeyEvent(Key.CtrlMask ||| Key.L, KeyModifiers())
        let handled = handler.HandleKey(refreshKey)

        Assert.IsTrue(handled, "Ctrl+L キーが処理されること")

    [<Test>]
    [<Category("Unit")>]
    member _.``マルチキーシーケンステスト``() =
        skipIfCI ()
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // Ctrl+X の最初のキー
        let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let firstHandled = handler.HandleKey(firstKey)

        Assert.IsTrue(firstHandled, "最初のキー（Ctrl+X）が処理されること")

        // 次のキー O (NextPane)
        let secondKey = KeyEvent(Key.O, KeyModifiers())
        let secondHandled = handler.HandleKey(secondKey)

        Assert.IsTrue(secondHandled, "2番目のキー（O）が処理されること")
        Assert.AreEqual(1, handler.CurrentPaneIndex, "次のペインに移動していること")

    [<Test>]
    [<Category("Unit")>]
    member _.``キーシーケンスタイムアウトテスト``() =
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // Ctrl+X を送信
        let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let firstHandled = handler.HandleKey(firstKey)
        Assert.IsTrue(firstHandled)

        // 2秒以上待機をシミュレート（実際の待機は行わず、無効なキーで代用）
        let invalidKey = KeyEvent(Key.A, KeyModifiers())
        let invalidHandled = handler.HandleKey(invalidKey)

        Assert.IsFalse(invalidHandled, "無効なキーシーケンスは処理されないこと")

    [<Test>]
    [<Category("Unit")>]
    member _.``ダイレクトペイン移動テスト``() =
        skipIfCI ()
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // Ctrl+X 3 でペイン3に移動
        let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let secondKey = KeyEvent(Key.D3, KeyModifiers())

        handler.HandleKey(firstKey) |> ignore
        let handled = handler.HandleKey(secondKey)

        Assert.IsTrue(handled, "ダイレクト移動キーが処理されること")
        Assert.AreEqual(3, handler.CurrentPaneIndex, "指定ペインに移動していること")

    [<Test>]
    [<Category("Unit")>]
    member _.``前ペイン移動テスト``() =
        skipIfCI ()
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // 初期状態でペイン2に設定
        handler.SetCurrentPaneIndex(2)

        // Ctrl+X Ctrl+O で前のペインに移動
        let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let secondKey = KeyEvent(Key.CtrlMask ||| Key.O, KeyModifiers())

        handler.HandleKey(firstKey) |> ignore
        let handled = handler.HandleKey(secondKey)

        Assert.IsTrue(handled, "前ペイン移動キーが処理されること")
        Assert.AreEqual(1, handler.CurrentPaneIndex, "前のペインに移動していること")

    [<Test>]
    [<Category("Unit")>]
    member _.``ペイン移動の循環テスト``() =
        skipIfCI ()
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // 最後のペイン(7)に設定
        handler.SetCurrentPaneIndex(7)

        // 次のペインに移動（循環して0に戻る）
        let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let secondKey = KeyEvent(Key.O, KeyModifiers())

        handler.HandleKey(firstKey) |> ignore
        handler.HandleKey(secondKey) |> ignore

        Assert.AreEqual(0, handler.CurrentPaneIndex, "最後のペインから最初のペインに循環すること")

        // 前のペインに移動（循環して7に戻る）
        let thirdKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let fourthKey = KeyEvent(Key.CtrlMask ||| Key.O, KeyModifiers())

        handler.HandleKey(thirdKey) |> ignore
        handler.HandleKey(fourthKey) |> ignore

        Assert.AreEqual(7, handler.CurrentPaneIndex, "最初のペインから最後のペインに循環すること")

    [<Test>]
    [<Category("Unit")>]
    member _.``Ctrl-X Ctrl-C終了コマンドテスト``() =
        // CI環境ではスキップ（Terminal.Gui Application.RequestStop依存）
        skipIfCI ()
        let panes = createMockFrameViews ()
        let handler = EmacsKeyHandler(panes, createMockSessionManager ())

        // Ctrl+X Ctrl+C による終了コマンドをテスト
        let firstKey = KeyEvent(Key.CtrlMask ||| Key.X, KeyModifiers())
        let secondKey = KeyEvent(Key.CtrlMask ||| Key.C, KeyModifiers())

        let firstHandled = handler.HandleKey(firstKey)
        Assert.IsTrue(firstHandled, "最初のキー（Ctrl+X）が処理されること")

        // 2番目のキーでExitアクションが実行される（Application.RequestStop()が呼ばれる）
        let secondHandled = handler.HandleKey(secondKey)
        Assert.IsTrue(secondHandled, "2番目のキー（Ctrl+C）が処理されること")

/// FC-027: MockUI機能拡張テスト
/// Terminal.Gui初期化ハング完全回避のためのモックUI包括的テスト
module FCode.Tests.MockUIEnhancementTests

open System
open NUnit.Framework
open FCode.Tests.CITestHelper
open FCode.Tests.MockUI

[<TestFixture>]
[<Category("Unit")>]
type MockUIEnhancementTests() =

    [<SetUp>]
    member _.Setup() =
        // CI環境強制設定
        CIEnvironment.forceCI true

    [<TearDown>]
    member _.TearDown() = CIEnvironment.forceCI false

    // MockFrameViewテスト
    [<Test>]
    member _.``MockFrameViewが正常に作成される``() =
        let frameView = MockFrameView("Test Frame")
        Assert.AreEqual("Test Frame", frameView.Title)
        Assert.IsNotNull(frameView)

    [<Test>]
    member _.``MockFrameViewにサブビューを追加できる``() =
        let frameView = MockFrameView("Parent")
        let childView = MockTextView()

        frameView.Add(childView)
        Assert.AreEqual(1, frameView.SubViews.Count)

    [<Test>]
    member _.``MockFrameViewからサブビューを削除できる``() =
        let frameView = MockFrameView("Parent")
        let childView = MockTextView()

        frameView.Add(childView)
        let removed = frameView.Remove(childView)

        Assert.IsTrue(removed)
        Assert.AreEqual(0, frameView.SubViews.Count)

    // MockTextViewテスト
    [<Test>]
    member _.``MockTextViewのテキスト操作が正常動作する``() =
        let textView = MockTextView()

        textView.Text <- "Test Content"
        Assert.AreEqual("Test Content", textView.Text)

    [<Test>]
    member _.``MockTextViewの読み取り専用設定が機能する``() =
        let textView = MockTextView()
        textView.ReadOnly <- true

        textView.InsertText("New Text")
        // ReadOnlyの場合、テキストは挿入されない
        Assert.AreEqual("", textView.Text)

    [<Test>]
    member _.``MockTextViewにテキスト挿入できる``() =
        let textView = MockTextView()
        textView.ReadOnly <- false

        textView.Text <- "Initial"
        textView.InsertText(" Added")

        Assert.AreEqual("Initial Added", textView.Text)

    // MockApplicationテスト
    [<Test>]
    member _.``MockApplicationが正常に初期化される``() =
        MockApplication.Init()
        Assert.IsTrue(MockApplication.IsInitialized())

        MockApplication.Shutdown()
        Assert.IsFalse(MockApplication.IsInitialized())

    [<Test>]
    member _.``MockApplicationのRun機能が動作する``() =
        MockApplication.Init()
        let topView = MockFrameView("Top")

        // Run は即座に完了する（実際のイベントループなし）
        Assert.DoesNotThrow(fun () -> MockApplication.Run(topView))

        MockApplication.Shutdown()

    // UIFactoryテスト
    [<Test>]
    member _.``CI環境でUIFactoryがモックオブジェクトを返す``() =
        let frameView = UIFactory.createFrameView ("Test")
        Assert.IsInstanceOf<MockFrameView>(frameView)

    [<Test>]
    member _.``CI環境でUIFactoryがモックTextViewを返す``() =
        let textView = UIFactory.createTextView ()
        Assert.IsInstanceOf<MockTextView>(textView)

    // SafeTerminalGuiInitializerテスト
    [<Test>]
    member _.``CI環境でTerminal.Gui初期化が回避される``() =
        // CI環境で初期化しても例外が発生しない
        Assert.DoesNotThrow(fun () -> SafeTerminalGuiInitializer.safeApplicationInit ())

    [<Test>]
    member _.``CI環境でTerminal.Guiシャットダウンが回避される``() =
        SafeTerminalGuiInitializer.safeApplicationInit ()
        // CI環境でシャットダウンしても例外が発生しない
        Assert.DoesNotThrow(fun () -> SafeTerminalGuiInitializer.safeApplicationShutdown ())

    // MockUITestSetupテスト
    [<Test>]
    member _.``MockUIテスト設定が正常に動作する``() =
        let mutable actionCalled = false

        let result =
            MockUITestSetup.withMockUI (fun () ->
                actionCalled <- true
                "test result")

        Assert.IsTrue(actionCalled)
        Assert.AreEqual("test result", result)

[<TestFixture>]
[<Category("Integration")>]
type MockUIIntegrationTests() =

    [<Test>]
    member _.``非CI環境でMockUIが実際のTerminal.Guiを使用する``() =
        // 非CI環境を設定
        CIEnvironment.forceCI false

        try
            // 実際のTerminal.Guiが使用されることを確認
            // （Terminal.Gui初期化が必要なため、CI環境でのみスキップ）
            if not (CIEnvironment.isCI ()) then
                let frameView = UIFactory.createFrameView ("Real")
                Assert.IsInstanceOf<Terminal.Gui.FrameView>(frameView)
        finally
            CIEnvironment.forceCI true

    [<Test>]
    member _.``CI環境判定が複数の環境変数で動作する``() =
        // 各CI環境変数をテスト
        let testEnvVars = [ "CI"; "GITHUB_ACTIONS"; "GITLAB_CI"; "JENKINS_URL" ]

        for envVar in testEnvVars do
            Environment.SetEnvironmentVariable(envVar, "true")
            Assert.IsTrue(CIEnvironment.isCI (), $"Environment variable {envVar} should trigger CI mode")
            Environment.SetEnvironmentVariable(envVar, null)

    [<Test>]
    member _.``DISPLAY変数なしでヘッドレス環境として判定される``() =
        let originalDisplay = Environment.GetEnvironmentVariable("DISPLAY")

        try
            Environment.SetEnvironmentVariable("DISPLAY", null)
            Assert.IsTrue(CIEnvironment.isCI (), "Missing DISPLAY should trigger CI mode")
        finally
            Environment.SetEnvironmentVariable("DISPLAY", originalDisplay)

[<TestFixture>]
[<Category("Stability")>]
type MockUIStabilityTests() =

    [<Test>]
    member _.``長時間のMockUI操作で安定性を維持する``() =
        let frameView = MockFrameView("Stability Test")

        // 大量の操作を実行
        for i in 1..1000 do
            let childView = MockTextView()
            childView.Text <- $"Content {i}"
            frameView.Add(childView)

            if i % 100 = 0 then
                frameView.SetNeedsDisplay()

        Assert.AreEqual(1000, frameView.SubViews.Count)

    [<Test>]
    member _.``MockApplicationの繰り返し初期化で問題が発生しない``() =
        for _ in 1..10 do
            MockApplication.Init()
            Assert.IsTrue(MockApplication.IsInitialized())

            MockApplication.Shutdown()
            Assert.IsFalse(MockApplication.IsInitialized())

    [<Test>]
    member _.``巨大テキストでMockTextViewが安定動作する``() =
        let textView = MockTextView()
        let largeText = String.replicate 10000 "Large content line\n"

        textView.Text <- largeText
        Assert.AreEqual(largeText, textView.Text)

        // 追加のテキスト挿入
        textView.InsertText("Additional")
        Assert.IsTrue(textView.Text.Contains("Additional"))

[<TestFixture>]
[<Category("Performance")>]
type MockUIPerformanceTests() =

    [<Test>]
    member _.``MockUI作成のパフォーマンスが許容範囲内``() =
        let startTime = DateTime.Now

        // 大量のMockUIオブジェクト作成
        for i in 1..1000 do
            let frameView = MockFrameView($"Frame {i}")
            let textView = MockTextView()
            frameView.Add(textView)

        let elapsed = DateTime.Now - startTime
        Assert.Less(elapsed.TotalSeconds, 1.0, "1000 MockUI objects should be created within 1 second")

    [<Test>]
    member _.``MockUIテキスト操作のパフォーマンスが許容範囲内``() =
        let textView = MockTextView()
        let startTime = DateTime.Now

        // 大量のテキスト操作
        for i in 1..1000 do
            textView.InsertText($"Line {i}\n")

        let elapsed = DateTime.Now - startTime
        Assert.Less(elapsed.TotalSeconds, 2.0, "1000 text operations should complete within 2 seconds")

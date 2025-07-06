module FCode.Tests.MockFrameViewTests

open NUnit.Framework
open FCode.Tests.TestHelpers
open FCode.ColorSchemes

[<TestFixture>]
[<Category("Unit")>]
type MockFrameViewTests() =

    /// MockFrameView基本機能テスト
    [<Test>]
    member _.``MockFrameView basic properties test``() =
        let mockView = new MockFrameView("test-pane")

        Assert.AreEqual("test-pane", mockView.Title)
        Assert.AreEqual(80, mockView.Width)
        Assert.AreEqual(24, mockView.Height)
        Assert.AreEqual(0, mockView.X)
        Assert.AreEqual(0, mockView.Y)
        Assert.IsTrue(mockView.CanFocus)
        Assert.IsFalse(mockView.HasFocus)

    /// MockFrameView ColorScheme変更追跡テスト
    [<Test>]
    member _.``MockFrameView color scheme tracking test``() =
        let mockView = new MockFrameView("test-pane")
        let originalScheme = mockView.ColorScheme

        // 新しいカラースキームを設定
        let newScheme = qaScheme
        mockView.ColorScheme <- newScheme

        Assert.AreEqual(newScheme, mockView.ColorScheme)
        Assert.AreEqual(originalScheme, mockView.LastColorScheme)

    /// ITestableView インターフェース実装テスト
    [<Test>]
    member _.``MockFrameView ITestableView interface test``() =
        let mockView = new MockFrameView("test-pane") :> ITestableView

        Assert.AreEqual("test-pane", mockView.Title)
        Assert.AreEqual(devScheme, mockView.ColorScheme)

        // カラースキーム変更テスト
        mockView.ColorScheme <- uxScheme
        Assert.AreEqual(uxScheme, mockView.ColorScheme)

    /// createMockFrameViewArray テスト
    [<Test>]
    member _.``createMockFrameViewArray creates correct array``() =
        let mockViews = createMockFrameViewArray 5 "test"

        Assert.AreEqual(5, mockViews.Length)
        Assert.AreEqual("test0", mockViews.[0].Title)
        Assert.AreEqual("test4", mockViews.[4].Title)

        // 全てがITestableViewインターフェースを実装
        mockViews
        |> Array.iter (fun view ->
            Assert.IsNotNull(view)
            Assert.IsInstanceOf<ITestableView>(view))

    /// createMockFrameViewSingle テスト
    [<Test>]
    member _.``createMockFrameViewSingle creates single view``() =
        let mockView = createMockFrameViewSingle "single-test"

        Assert.AreEqual("single-test", mockView.Title)
        Assert.IsInstanceOf<ITestableView>(mockView)

    /// TestFrameView下位互換性テスト
    [<Test>]
    member _.``TestFrameView backward compatibility test``() =
        let testView = new TestFrameView("compat-test")

        // MockFrameViewを継承していることを確認
        Assert.IsInstanceOf<MockFrameView>(testView)
        Assert.AreEqual("compat-test", testView.Title)

        // 下位互換メソッドのテスト
        let originalScheme = testView.GetColorScheme()
        testView.SetColorScheme(pmScheme)
        Assert.AreEqual(pmScheme, testView.GetColorScheme())
        Assert.AreEqual(originalScheme, testView.LastColorScheme)

    /// UI依存性分離テスト
    [<Test>]
    member _.``MockFrameView UI independence test``() =
        let testPassed =
            validateUIIndependence (fun () ->
                // CI環境下でMockFrameViewが正常動作することを確認
                let mockView = new MockFrameView("ui-independence-test")
                mockView.ColorScheme <- chatScheme

                let testableView = mockView :> ITestableView
                applySchemeByRoleTestable testableView "qa1"

                Assert.AreEqual(qaScheme, testableView.ColorScheme))

        Assert.IsTrue(testPassed, "UI依存性分離が正常に機能すること")

    /// 並行MockFrameView操作テスト
    [<Test>]
    member _.``MockFrameView concurrent operations test``() =
        let mockViews = createMockFrameViewArray 10 "concurrent"

        let tasks =
            mockViews
            |> Array.mapi (fun i view ->
                async {
                    let scheme = if i % 2 = 0 then qaScheme else uxScheme
                    view.ColorScheme <- scheme
                    return view.ColorScheme = scheme
                })

        let results = Async.Parallel tasks |> Async.RunSynchronously

        // 全ての並行操作が成功
        results |> Array.iter (fun result -> Assert.IsTrue(result))

        // 各ビューが期待されるカラースキームを持つ
        mockViews
        |> Array.iteri (fun i view ->
            let expectedScheme = if i % 2 = 0 then qaScheme else uxScheme
            Assert.AreEqual(expectedScheme, view.ColorScheme))

    /// MockFrameView初期化タイムスタンプテスト
    [<Test>]
    member _.``MockFrameView initialization timestamp test``() =
        let beforeCreation = System.DateTime.UtcNow
        System.Threading.Thread.Sleep(1) // 1ms待機でタイムスタンプ差を確保

        let mockView = new MockFrameView("timestamp-test")

        System.Threading.Thread.Sleep(1) // 1ms待機
        let afterCreation = System.DateTime.UtcNow

        Assert.GreaterOrEqual(mockView.InitializationTime, beforeCreation)
        Assert.LessOrEqual(mockView.InitializationTime, afterCreation)
        Assert.Greater(mockView.InitializationTime, System.DateTime.MinValue)

[<TestFixture>]
[<Category("Performance")>]
type MockFrameViewPerformanceTests() =

    /// 大量MockFrameView作成パフォーマンステスト
    [<Test>]
    member _.``MockFrameView performance test large array``() =
        let startTime = System.DateTime.UtcNow
        let largeArray = createMockFrameViewArray 1000 "perf"
        let endTime = System.DateTime.UtcNow
        let duration = endTime - startTime

        Assert.AreEqual(1000, largeArray.Length)
        Assert.Less(duration.TotalMilliseconds, 100.0, $"大量作成パフォーマンス: 1000個 < 100ms、実際: {duration.TotalMilliseconds}ms")

        // 各MockFrameViewが正常に初期化されていることを確認
        largeArray
        |> Array.iteri (fun i view ->
            Assert.AreEqual($"perf{i}", view.Title)
            Assert.IsNotNull(view.ColorScheme))

[<TestFixture>]
[<Category("Integration")>]
type MockFrameViewIntegrationTests() =

    /// createTestableFrameView CI環境対応テスト
    [<Test>]
    member _.``createTestableFrameView CI environment handling``() =
        // CI環境の状態を保存
        let originalCIValue = System.Environment.GetEnvironmentVariable("CI")

        try
            // CI環境を模擬
            System.Environment.SetEnvironmentVariable("CI", "true")
            let ciView = createTestableFrameView "ci-test"

            Assert.IsNotNull(ciView)
            Assert.AreEqual("ci-test", ciView.Title)
            Assert.IsInstanceOf<ITestableView>(ciView)

            // 非CI環境を模擬（CI変数を削除）
            System.Environment.SetEnvironmentVariable("CI", null)
            let nonCiView = createTestableFrameView "non-ci-test"

            Assert.IsNotNull(nonCiView)
            Assert.AreEqual("non-ci-test", nonCiView.Title)
            Assert.IsInstanceOf<ITestableView>(nonCiView)

        finally
            // 元の環境変数を復元
            System.Environment.SetEnvironmentVariable("CI", originalCIValue)

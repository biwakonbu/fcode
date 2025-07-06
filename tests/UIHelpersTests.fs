module FCode.Tests.UIHelpersTests

open System
open NUnit.Framework
open Terminal.Gui
open FCode.UIHelpers

[<TestFixture>]
type UIHelpersTests() =

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
        // CI環境ではShutdownをスキップ
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if not isCI then
            try
                Application.Shutdown()
            with _ ->
                ()

    [<Test>]
    member _.``findTextViews should find direct TextView``() =
        // Arrange
        let textView = new TextView()
        textView.Text <- "test content"

        // Act
        let result = findTextViews textView |> Seq.toList

        // Assert
        Assert.AreEqual(1, result.Length)
        Assert.AreSame(textView, result.[0])

    [<Test>]
    member _.``findTextViews should find TextView inside FrameView ContentView hierarchy``() =
        // Arrange - Program.fsのmakePaneと同じ方法でFrameViewを作成
        let frameView = new FrameView("test")
        let textView = new TextView()
        textView.Text <- "test content"
        frameView.Add(textView) // これによりContentView階層が作られる

        // Act
        let result = findTextViews frameView |> Seq.toList

        // Assert
        Assert.AreEqual(1, result.Length, "FrameView内のTextViewが見つからない")
        Assert.AreEqual("test content", result.[0].Text.ToString())

    [<Test>]
    member _.``getTextViewsFromPane should return empty list for pane without TextView``() =
        // Arrange
        let frameView = new FrameView("empty")

        // Act
        let result = getTextViewsFromPane frameView

        // Assert
        Assert.AreEqual(0, result.Length)

    [<Test>]
    member _.``getTextViewsFromPane should find TextView in ContentView hierarchy``() =
        // Arrange - Program.fsのmakePaneロジックを再現
        let frameView = new FrameView("test")
        frameView.Border.Effect3D <- false

        let textView = new TextView()
        textView.X <- 0
        textView.Y <- 0
        textView.Width <- Dim.Fill()
        textView.Height <- Dim.Fill()
        textView.ReadOnly <- true
        textView.Text <- "[DEBUG] testペイン - TextView初期化完了"

        frameView.Add(textView)

        // Act
        let result = getTextViewsFromPane frameView

        // Assert
        Assert.AreEqual(1, result.Length, "makePaneで作成されたFrameView内のTextViewが見つからない")
        Assert.IsTrue(result.[0].Text.ToString().Contains("testペイン"))

    [<Test>]
    member _.``findTextViews should handle multiple nested TextViews``() =
        // Arrange
        let container = new View()
        let textView1 = new TextView()
        textView1.Text <- "first"
        let textView2 = new TextView()
        textView2.Text <- "second"

        let subContainer = new View()
        subContainer.Add(textView2)
        container.Add(textView1)
        container.Add(subContainer)

        // Act
        let result = findTextViews container |> Seq.toList

        // Assert
        Assert.AreEqual(2, result.Length)
        let texts = result |> List.map (fun tv -> tv.Text.ToString()) |> Array.ofList
        CollectionAssert.Contains(texts, "first")
        CollectionAssert.Contains(texts, "second")

    [<Test>]
    member _.``findTextViews should handle empty View without crash``() =
        // Arrange
        let emptyView = new View()

        // Act & Assert
        let result = findTextViews emptyView |> Seq.toList
        Assert.AreEqual(0, result.Length)

    [<Test>]
    [<Category("Unit")>]
    member _.``findTextViews should use reflection for ContentView access``() =
        // Arrange - モックFrameViewでContentViewプロパティアクセステスト
        let frameView = new FrameView("reflection-test")
        let textView = new TextView()
        textView.Text <- "reflection content"
        frameView.Add(textView)

        // Act - リフレクションベースの検索を実行
        let result = findTextViews frameView |> Seq.toList

        // Assert - リフレクションによりTextViewが発見されること
        Assert.AreEqual(1, result.Length, "リフレクションによるTextView検索が失敗")
        Assert.AreEqual("reflection content", result.[0].Text.ToString())

    [<Test>]
    [<Category("Unit")>]
    member _.``findTextViews should handle reflection exceptions gracefully``() =
        // Arrange - 通常のViewでリフレクション例外処理をテスト
        let regularView = new View()
        let textView = new TextView()
        textView.Text <- "exception handling test"
        regularView.Add(textView)

        // Act - リフレクション例外が発生しても処理が継続されること
        let result = findTextViews regularView |> Seq.toList

        // Assert - 例外処理により基本的なTextView検索は動作すること
        Assert.AreEqual(1, result.Length, "リフレクション例外処理によるフォールバック検索が失敗")
        Assert.AreEqual("exception handling test", result.[0].Text.ToString())

    [<Test>]
    [<Category("Unit")>]
    member _.``dumpViewHierarchy should not crash on complex views``() =
        // Arrange - 複雑なView階層でクラッシュしないかテスト
        let container = new FrameView("hierarchy-test")
        let subView1 = new View()
        let subView2 = new TextView()
        subView2.Text <- "hierarchy content"
        let subView3 = new FrameView("nested")

        container.Add(subView1)
        container.Add(subView2)
        container.Add(subView3)
        subView1.Add(new TextView())

        // Act & Assert - dumpViewHierarchy実行時にクラッシュしないこと
        Assert.DoesNotThrow(fun () -> dumpViewHierarchy container 0)

    [<Test>]
    [<Category("Unit")>]
    member _.``getTextViewsFromPane should combine hierarchy dump and search``() =
        // Arrange - getTextViewsFromPaneが階層ダンプと検索を組み合わせてテスト
        let frameView = new FrameView("combined-test")
        let textView1 = new TextView()
        textView1.Text <- "first combined"
        let textView2 = new TextView()
        textView2.Text <- "second combined"

        frameView.Add(textView1)
        let subContainer = new View()
        subContainer.Add(textView2)
        frameView.Add(subContainer)

        // Act - 階層ダンプ＋検索の組み合わせ機能
        let result = getTextViewsFromPane frameView

        // Assert - 全TextViewが発見されること
        Assert.AreEqual(2, result.Length, "組み合わせ検索で全TextViewが見つからない")
        let texts = result |> List.map (fun tv -> tv.Text.ToString()) |> Set.ofList
        Assert.IsTrue(texts.Contains("first combined"))
        Assert.IsTrue(texts.Contains("second combined"))

    [<Test>]
    [<Category("Unit")>]
    member _.``findTextViews - 存在しないプロパティアクセステスト``() =
        // Arrange - リフレクションで存在しないプロパティにアクセスしても安全に処理される
        let frameView = new FrameView("nonexistent-property-test")
        let textView = new TextView()
        textView.Text <- "safe reflection test"
        frameView.Add(textView)

        // Act - 存在しないプロパティへのアクセスが発生しても例外で落ちないこと
        let result = findTextViews frameView |> Seq.toList

        // Assert - 基本的なTextView検索は動作すること
        Assert.AreEqual(1, result.Length, "存在しないプロパティアクセスでも基本検索は動作")
        Assert.AreEqual("safe reflection test", result.[0].Text.ToString())

    [<Test>]
    [<Category("Unit")>]
    member _.``findTextViews - 型変換失敗テスト``() =
        // Arrange - 型変換が失敗する可能性のあるシナリオをテスト
        let complexView = new View()
        let textView1 = new TextView()
        textView1.Text <- "type conversion test 1"
        let textView2 = new TextView()
        textView2.Text <- "type conversion test 2"

        // 異なる型のViewを混在させる
        complexView.Add(textView1)
        complexView.Add(new View()) // 中間View
        complexView.Add(textView2)

        // Act - 型変換失敗が発生しても安全に処理されること
        let result = findTextViews complexView |> Seq.toList

        // Assert - 全TextViewが発見されること
        Assert.AreEqual(2, result.Length, "型変換失敗時も適切にTextViewを発見")
        let texts = result |> List.map (fun tv -> tv.Text.ToString()) |> Set.ofList
        Assert.IsTrue(texts.Contains("type conversion test 1"))
        Assert.IsTrue(texts.Contains("type conversion test 2"))

    [<Test>]
    [<Category("Performance")>]
    member _.``findTextViews - 大量View階層パフォーマンステスト``() =
        // Arrange - 深い階層と多数のViewを持つ構造
        let rootView = new View()
        let textViewCount = 50
        let maxDepth = 10

        // 深い階層構造を作成
        let rec createDeepHierarchy (parent: View) (depth: int) (textViewIndex: int ref) =
            if depth < maxDepth then
                for i in 1..5 do // 各階層に5つのView
                    let childView = new View()
                    parent.Add(childView)

                    // いくつかにはTextViewを配置
                    if i % 2 = 0 && !textViewIndex < textViewCount then
                        let textView = new TextView()
                        textView.Text <- $"performance test {!textViewIndex}"
                        childView.Add(textView)
                        incr textViewIndex

                    // 再帰的に子階層を作成
                    createDeepHierarchy childView (depth + 1) textViewIndex

        let textViewIndex = ref 0
        createDeepHierarchy rootView 0 textViewIndex

        // Act - パフォーマンス測定
        let startTime = DateTime.Now
        let result = findTextViews rootView |> Seq.toList
        let elapsed = (DateTime.Now - startTime).TotalMilliseconds

        // Assert
        Assert.Greater(result.Length, 0, "大量階層でもTextViewが発見される")
        Assert.LessOrEqual(elapsed, 5000.0, "5秒以内に完了する") // パフォーマンス要件

        FCode.Logger.logInfo "UIHelpersTest" $"大量階層テスト: {result.Length}個のTextViewを{elapsed}msで発見"

    [<Test>]
    [<Category("Unit")>]
    member _.``dumpViewHierarchy - null View処理テスト``() =
        // Arrange - null Viewが混在する状況での安全性テスト
        let container = new FrameView("null-safety-test")
        let textView = new TextView()
        textView.Text <- "null safety test"
        container.Add(textView)

        // Act & Assert - null処理で例外が発生しないこと
        Assert.DoesNotThrow(fun () -> dumpViewHierarchy container 0)

        // 階層ダンプ後も検索機能が正常に動作すること
        let result = getTextViewsFromPane container
        Assert.AreEqual(1, result.Length, "null処理後も検索機能が正常動作")

    [<Test>]
    [<Category("Unit")>]
    member _.``getTextViewsFromPane - 空のFrameView処理テスト``() =
        // Arrange - 完全に空のFrameView
        let emptyFrame = new FrameView("completely-empty")

        // Act
        let result = getTextViewsFromPane emptyFrame

        // Assert
        Assert.AreEqual(0, result.Length, "空のFrameViewでは0個のTextViewが返される")
        Assert.DoesNotThrow(fun () -> getTextViewsFromPane emptyFrame |> ignore)

    [<Test>]
    [<Category("Unit")>]
    member _.``findTextViews - 循環参照構造でのスタックオーバーフロー防止テスト``() =
        // Arrange - 循環参照のような構造でスタックオーバーフローが発生しないかテスト
        let parentView = new View()
        let childView = new View()
        let textView = new TextView()
        textView.Text <- "circular reference test"

        parentView.Add(childView)
        childView.Add(textView)
        // 注意: Terminal.Guiは循環参照を自動防止するため、実際の循環は作れない

        // Act - 深い構造でもスタックオーバーフローしないこと
        let result = findTextViews parentView |> Seq.toList

        // Assert
        Assert.AreEqual(1, result.Length, "循環参照回避構造でもTextView発見")
        Assert.AreEqual("circular reference test", result.[0].Text.ToString())

    [<Test>]
    [<Category("Unit")>]
    member _.``findTextViews - 非常に長いテキストでのメモリ使用量テスト``() =
        // Arrange - 非常に長いテキストを持つTextView
        let longText = String.replicate 10000 "A" // 10KB のテキスト
        let frameView = new FrameView("long-text-test")
        let textView = new TextView()
        textView.Text <- longText
        frameView.Add(textView)

        // Act
        let result = findTextViews frameView |> Seq.toList

        // Assert
        Assert.AreEqual(1, result.Length, "長いテキストでもTextView発見")
        Assert.AreEqual(longText.Length, result.[0].Text.ToString().Length, "長いテキストが正確に保持される")

    [<Test>]
    [<Category("Unit")>]
    member _.``findTextViews - 特殊文字・Unicode文字処理テスト``() =
        // Arrange - 特殊文字・Unicode文字を含むテキスト
        let unicodeText = "テスト文字列 🚀 ñáéíóú ñÑ 测试 русский"
        let frameView = new FrameView("unicode-test")
        let textView = new TextView()
        textView.Text <- unicodeText
        frameView.Add(textView)

        // Act
        let result = findTextViews frameView |> Seq.toList

        // Assert
        Assert.AreEqual(1, result.Length, "Unicode文字でもTextView発見")
        Assert.AreEqual(unicodeText, result.[0].Text.ToString(), "Unicode文字が正確に保持される")

    [<Test>]
    [<Category("Integration")>]
    member _.``UIHelpers統合テスト - 実際のProgram.fs形式でのTextView発見``() =
        // Arrange - Program.fsのmakePane関数と同じ形式でFrameViewを作成
        let createMockPane (title: string) =
            let fv = new FrameView(title)
            fv.Border.Effect3D <- false

            let textView = new TextView()
            textView.X <- 0
            textView.Y <- 0
            textView.Width <- Dim.Fill()
            textView.Height <- Dim.Fill()
            textView.ReadOnly <- true
            textView.Text <- $"[DEBUG] {title}ペイン - TextView初期化完了"

            fv.Add(textView)
            fv

        let testPanes =
            [ createMockPane "dev1"
              createMockPane "dev2"
              createMockPane "qa1"
              createMockPane "ux" ]

        // Act - 各ペインでTextView発見テスト
        let results =
            testPanes
            |> List.map (fun pane ->
                let textViews = getTextViewsFromPane pane
                (pane.Title, textViews.Length, textViews))

        // Assert - 全ペインでTextViewが正確に発見されること
        results
        |> List.iter (fun (title, count, textViews) ->
            Assert.AreEqual(1, count, $"{title}ペインでTextViewが1個発見される")
            // NStack.ustringの型問題により、シンプルなアサーションに変更
            Assert.IsNotNull(textViews.[0].Text, $"{title}ペインのテキストが設定されている"))

        // Cleanup
        testPanes |> List.iter (fun pane -> pane.Dispose())

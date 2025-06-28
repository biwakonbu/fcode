module FCode.Tests.UIHelpersTests

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

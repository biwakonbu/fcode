module FCode.Tests.MockUI

open System
open System.Collections.Generic
open FCode.Logger

/// CI環境用モックColorScheme
type MockColorScheme() =
    member val Normal = 0
    member val Focus = 1
    member val HotNormal = 2
    member val HotFocus = 3
    member val Disabled = 4

/// CI環境用モックView（基底クラス）
[<AbstractClass>]
type MockView(title: string) =
    let mutable colorScheme = MockColorScheme()
    let mutable x = 0
    let mutable y = 0
    let mutable width = 80
    let mutable height = 24
    let mutable text = ""
    let mutable visible = true
    let subViews = List<MockView>()

    member _.Title = title

    member _.X
        with get () = x
        and set (value) = x <- value

    member _.Y
        with get () = y
        and set (value) = y <- value

    member _.Width
        with get () = width
        and set (value) = width <- value

    member _.Height
        with get () = height
        and set (value) = height <- value

    member _.Text
        with get () = text
        and set (value) = text <- value

    member _.Visible
        with get () = visible
        and set (value) = visible <- value

    member _.ColorScheme
        with get () = colorScheme
        and set (value) = colorScheme <- value

    member _.SubViews = subViews :> IReadOnlyList<MockView>

    member _.Add(view: MockView) =
        subViews.Add(view)
        logDebug "MockUI" $"View追加: {view.Title} -> {title}"

    member _.Remove(view: MockView) =
        let removed = subViews.Remove(view)

        if removed then
            logDebug "MockUI" $"View削除: {view.Title} <- {title}"

        removed

    member _.SetNeedsDisplay() = logDebug "MockUI" $"再描画要求: {title}"

    member _.Focus() = logDebug "MockUI" $"フォーカス設定: {title}"

    abstract member GetContentText: unit -> string
    default _.GetContentText() = text

/// CI環境用モックFrameView
type MockFrameView(title: string) =
    inherit MockView(title)

    member _.Border = "Mock Border"

    override _.GetContentText() =
        $"[Frame: {base.Title}] Content: {base.Text}"

/// CI環境用モックTextView
type MockTextView() =
    inherit MockView("TextView")

    let mutable readOnly = false
    let mutable allowsTab = false
    let mutable allowsReturn = true

    member _.ReadOnly
        with get () = readOnly
        and set (value) = readOnly <- value

    member _.AllowsTab
        with get () = allowsTab
        and set (value) = allowsTab <- value

    member _.AllowsReturn
        with get () = allowsReturn
        and set (value) = allowsReturn <- value

    member _.InsertText(text: string) =
        if not readOnly then
            let newText = base.Text + text
            base.Text <- newText
            logDebug "MockUI" $"テキスト挿入: {text.Length}文字"

    member _.LoadFile(filePath: string) =
        try
            if System.IO.File.Exists(filePath) then
                base.Text <- System.IO.File.ReadAllText(filePath)
                logDebug "MockUI" $"ファイル読み込み成功: {filePath}"
                true
            else
                logWarning "MockUI" $"ファイルが見つかりません: {filePath}"
                false
        with ex ->
            logError "MockUI" $"ファイル読み込み失敗: {ex.Message}"
            false

    member _.SaveFile(filePath: string) =
        try
            System.IO.File.WriteAllText(filePath, base.Text)
            logDebug "MockUI" $"ファイル保存成功: {filePath}"
            true
        with ex ->
            logError "MockUI" $"ファイル保存失敗: {ex.Message}"
            false

    override _.GetContentText() =
        $"[TextView] Lines: {base.Text.Split('\n').Length}, Chars: {base.Text.Length}"

/// CI環境用モックLabel
type MockLabel(text: string) =
    inherit MockView($"Label: {text}")

    do base.Text <- text

    override _.GetContentText() = $"[Label] {base.Text}"

/// CI環境用モックButton
type MockButton(text: string) =
    inherit MockView($"Button: {text}")

    let mutable clickHandler: (unit -> unit) option = None

    do base.Text <- text

    member _.OnClicked
        with set (handler) = clickHandler <- Some handler

    member _.Click() =
        match clickHandler with
        | Some handler ->
            handler ()
            logDebug "MockUI" $"ボタンクリック: {base.Text}"
        | None -> logDebug "MockUI" $"ボタンクリック（ハンドラなし）: {base.Text}"

    override _.GetContentText() = $"[Button] {base.Text}"

/// CI環境用モックApplication
module MockApplication =
    let mutable private isInitialized = false
    let mutable private topLevel: MockView option = None

    let Init () =
        if not isInitialized then
            isInitialized <- true
            logInfo "MockUI" "Mock Application初期化完了"

    let Shutdown () =
        if isInitialized then
            isInitialized <- false
            topLevel <- None
            logInfo "MockUI" "Mock Applicationシャットダウン完了"

    let setTopLevel (view: MockView option) =
        topLevel <- view
        let titleStr = view |> Option.map (fun v -> v.Title) |> Option.defaultValue "None"
        logDebug "MockUI" $"TopLevel設定: {titleStr}"

    let getTopLevel () = topLevel

    let Run (view: MockView) =
        topLevel <- Some view
        logInfo "MockUI" $"Mock Application実行開始: {view.Title}"
        // 実際のUIイベントループの代わりにログのみ
        System.Threading.Thread.Sleep(10) // 最小限の待機
        logInfo "MockUI" $"Mock Application実行完了: {view.Title}"

    let RequestStop () = logInfo "MockUI" "Mock Application停止要求"

    let IsInitialized = isInitialized

/// UI要素ファクトリ（CI環境判定付き）
module UIFactory =

    let createFrameView (title: string) =
        if FCode.Tests.CITestHelper.CIEnvironment.isCI () then
            MockFrameView(title) :> obj
        else
            new Terminal.Gui.FrameView(title) :> obj

    let createTextView () =
        if FCode.Tests.CITestHelper.CIEnvironment.isCI () then
            MockTextView() :> obj
        else
            new Terminal.Gui.TextView() :> obj

    let createLabel (text: string) =
        if FCode.Tests.CITestHelper.CIEnvironment.isCI () then
            MockLabel(text) :> obj
        else
            new Terminal.Gui.Label(text) :> obj

    let createButton (text: string) =
        if FCode.Tests.CITestHelper.CIEnvironment.isCI () then
            MockButton(text) :> obj
        else
            new Terminal.Gui.Button(text) :> obj

/// Terminal.Gui初期化の完全回避（FC-027専用）
module SafeTerminalGuiInitializer =

    /// CI環境でのApplication.Init完全回避
    let safeApplicationInit () =
        if not (FCode.Tests.CITestHelper.CIEnvironment.isCI ()) then
            try
                Terminal.Gui.Application.Init()
                logInfo "MockUI" "Terminal.Gui初期化完了"
            with ex ->
                logWarning "MockUI" $"Terminal.Gui初期化失敗（CI環境として継続）: {ex.Message}"
                FCode.Tests.CITestHelper.CIEnvironment.forceCI true
        else
            MockApplication.Init()
            logInfo "MockUI" "CI環境: Mock Application初期化完了"

    /// CI環境でのApplication.Shutdown完全回避
    let safeApplicationShutdown () =
        if not (FCode.Tests.CITestHelper.CIEnvironment.isCI ()) then
            try
                Terminal.Gui.Application.Shutdown()
                logInfo "MockUI" "Terminal.Guiシャットダウン完了"
            with ex ->
                logWarning "MockUI" $"Terminal.Guiシャットダウン失敗: {ex.Message}"
        else
            MockApplication.Shutdown()
            logInfo "MockUI" "CI環境: Mock Applicationシャットダウン完了"

/// テスト用のモックUI統合設定
module MockUITestSetup =

    /// テスト開始時の安全な設定
    let setupMockUI () =
        if FCode.Tests.CITestHelper.CIEnvironment.isCI () then
            // CI環境ではモックUI使用を強制
            System.Environment.SetEnvironmentVariable("FCODE_MOCK_UI", "true")
            logInfo "MockUI" "CI環境モード有効"
        else
            logInfo "MockUI" "開発環境モード有効"

    /// テスト終了時のクリーンアップ
    let cleanupMockUI () =
        System.Environment.SetEnvironmentVariable("FCODE_MOCK_UI", null)
        logInfo "MockUI" "クリーンアップ完了"

    /// テスト実行時の自動設定
    let withMockUI (action: unit -> 'T) : 'T =
        setupMockUI ()

        try
            action ()
        finally
            cleanupMockUI ()

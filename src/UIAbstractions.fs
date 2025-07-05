module FCode.UIAbstractions

open System
open Terminal.Gui

/// UI操作の抽象化インターフェース
type ITestableView =
    abstract member SetText: string -> unit
    abstract member GetText: unit -> string
    abstract member SetColorScheme: ColorScheme -> unit
    abstract member AddText: string -> unit
    abstract member Clear: unit -> unit
    abstract member Dispose: unit -> unit

/// FrameView操作の抽象化インターフェース
type ITestableFrameView =
    inherit ITestableView
    abstract member SetTitle: string -> unit
    abstract member GetTitle: unit -> string
    abstract member AddSubView: ITestableView -> unit
    abstract member RemoveSubView: ITestableView -> unit

/// CI環境判定ヘルパー
module EnvironmentDetection =
    let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

    let isTesting =
        isCI
        || not (isNull (System.Environment.GetEnvironmentVariable("TESTING")))
        || AppDomain.CurrentDomain.GetAssemblies()
           |> Array.exists (fun a -> a.GetName().Name.Contains("Test"))

/// CI環境対応TextView実装
type TestableTextView(title: string) =
    let mutable text = ""
    let mutable colorScheme: ColorScheme option = None
    let mutable isDisposed = false

    new() = TestableTextView("")

    member val Title = title with get, set

    interface ITestableView with
        member this.SetText(value: string) =
            if not isDisposed then
                text <- value

        member this.GetText() = text

        member this.SetColorScheme(scheme: ColorScheme) =
            if not isDisposed then
                colorScheme <- Some scheme

        member this.AddText(value: string) =
            if not isDisposed then
                text <- text + value

        member this.Clear() =
            if not isDisposed then
                text <- ""

        member this.Dispose() =
            isDisposed <- true
            text <- ""
            colorScheme <- None

/// CI環境対応FrameView実装
type TestableFrameView(title: string) =
    let mutable frameTitle = title
    let mutable subViews = []
    let mutable isDisposed = false
    let textView = new TestableTextView()

    interface ITestableFrameView with
        member this.SetTitle(value: string) =
            if not isDisposed then
                frameTitle <- value

        member this.GetTitle() = frameTitle

        member this.AddSubView(view: ITestableView) =
            if not isDisposed then
                subViews <- view :: subViews

        member this.RemoveSubView(view: ITestableView) =
            if not isDisposed then
                subViews <- subViews |> List.filter ((<>) view)

        member this.SetText(value: string) =
            (textView :> ITestableView).SetText(value)

        member this.GetText() = (textView :> ITestableView).GetText()

        member this.SetColorScheme(scheme: ColorScheme) =
            (textView :> ITestableView).SetColorScheme(scheme)

        member this.AddText(value: string) =
            (textView :> ITestableView).AddText(value)

        member this.Clear() = (textView :> ITestableView).Clear()

        member this.Dispose() =
            if not isDisposed then
                isDisposed <- true
                (textView :> ITestableView).Dispose()
                subViews |> List.iter (fun v -> v.Dispose())
                subViews <- []

/// Terminal.Gui TextView ラッパー
type TerminalGuiTextViewWrapper(textView: TextView) =
    interface ITestableView with
        member this.SetText(value: string) =
            textView.Text <- NStack.ustring.Make(value)

        member this.GetText() = textView.Text.ToString()
        member this.SetColorScheme(scheme: ColorScheme) = textView.ColorScheme <- scheme

        member this.AddText(value: string) =
            let currentText = textView.Text.ToString()
            textView.Text <- NStack.ustring.Make(currentText + value)

        member this.Clear() = textView.Text <- ""
        member this.Dispose() = textView.Dispose()

    interface IDisposable with
        member this.Dispose() = (this :> ITestableView).Dispose()

/// Terminal.Gui FrameView ラッパー
type TerminalGuiFrameViewWrapper(frameView: FrameView) =
    let textViewWrapper =
        frameView.Subviews
        |> Seq.cast<View>
        |> Seq.tryFind (fun v -> v :? TextView)
        |> Option.map (fun tv -> new TerminalGuiTextViewWrapper(tv :?> TextView))

    interface ITestableFrameView with
        member this.SetTitle(value: string) =
            frameView.Title <- NStack.ustring.Make(value)

        member this.GetTitle() = frameView.Title.ToString()

        member this.AddSubView(view: ITestableView) =
            // Terminal.Guiでは直接追加は複雑なため、ログのみ
            FCode.Logger.logInfo "UIAbstractions" "SubView addition not implemented for Terminal.Gui wrapper"

        member this.RemoveSubView(view: ITestableView) =
            FCode.Logger.logInfo "UIAbstractions" "SubView removal not implemented for Terminal.Gui wrapper"

        member this.SetText(value: string) =
            textViewWrapper |> Option.iter (fun tw -> (tw :> ITestableView).SetText(value))

        member this.GetText() =
            textViewWrapper
            |> Option.map (fun tw -> (tw :> ITestableView).GetText())
            |> Option.defaultValue ""

        member this.SetColorScheme(scheme: ColorScheme) =
            frameView.ColorScheme <- scheme

            textViewWrapper
            |> Option.iter (fun tw -> (tw :> ITestableView).SetColorScheme(scheme))

        member this.AddText(value: string) =
            textViewWrapper |> Option.iter (fun tw -> (tw :> ITestableView).AddText(value))

        member this.Clear() =
            textViewWrapper |> Option.iter (fun tw -> (tw :> ITestableView).Clear())

        member this.Dispose() =
            textViewWrapper |> Option.iter (fun tw -> (tw :> ITestableView).Dispose())
            frameView.Dispose()

    interface IDisposable with
        member this.Dispose() = (this :> ITestableFrameView).Dispose()

/// UI要素作成ファクトリー
module UIFactory =

    /// 環境に応じたTextView作成
    let createTextView () : ITestableView =
        if EnvironmentDetection.isTesting then
            new TestableTextView() :> ITestableView
        else
            let textView = new TextView()
            new TerminalGuiTextViewWrapper(textView) :> ITestableView

    /// 環境に応じたFrameView作成
    let createFrameView (title: string) : ITestableFrameView =
        if EnvironmentDetection.isTesting then
            new TestableFrameView(title) :> ITestableFrameView
        else
            let frameView = new FrameView(title)
            new TerminalGuiFrameViewWrapper(frameView) :> ITestableFrameView

    /// 既存TextView のラップ
    let wrapTextView (textView: TextView) : ITestableView =
        if EnvironmentDetection.isTesting then
            new TestableTextView() :> ITestableView
        else
            new TerminalGuiTextViewWrapper(textView) :> ITestableView

    /// 既存FrameView のラップ
    let wrapFrameView (frameView: FrameView) : ITestableFrameView =
        if EnvironmentDetection.isTesting then
            new TestableFrameView(frameView.Title.ToString()) :> ITestableFrameView
        else
            new TerminalGuiFrameViewWrapper(frameView) :> ITestableFrameView

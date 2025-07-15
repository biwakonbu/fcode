module FCode.Tests.TestHelpers

open Terminal.Gui
open FCode.ColorSchemes
open fcode

// MockFrameView: CI環境でのTerminal.Gui完全代替実装
type MockFrameView(title: string) =
    member val ColorScheme = devScheme with get, set
    member val Title = title with get, set
    member val Width = 80 with get, set
    member val Height = 24 with get, set
    member val X = 0 with get, set
    member val Y = 0 with get, set
    member val CanFocus = true with get, set
    member val HasFocus = false with get, set

    // テスト用追加プロパティ
    member val LastColorScheme = devScheme with get, set
    member val InitializationTime = System.DateTime.UtcNow with get

    interface ITestableView with
        member this.ColorScheme
            with get () = this.ColorScheme
            and set (v) =
                this.LastColorScheme <- this.ColorScheme
                this.ColorScheme <- v

        member this.Title = this.Title

// MockTextView: UI依存分離テスト用実装
type MockTextView() =
    member val Text = "" with get, set
    member val DisplayUpdateCount = 0 with get, set

    member this.SetNeedsDisplay() =
        this.DisplayUpdateCount <- this.DisplayUpdateCount + 1

    interface IUpdatableView with
        member this.Text
            with get () = this.Text
            and set (value) = this.Text <- value

        member this.SetNeedsDisplay() = this.SetNeedsDisplay()

// Terminal.GuiのTextView用ラッパー
type TextViewWrapper(textView: TextView) =
    interface IUpdatableView with
        member _.Text
            with get () = textView.Text.ToString()
            and set (value) = textView.Text <- value

        member _.SetNeedsDisplay() = textView.SetNeedsDisplay()

// テスト用のFrameViewラッパー（下位互換性維持）
type TestFrameView(title: string) =
    inherit MockFrameView(title)

    // 下位互換性のためのプロパティアクセス
    member this.GetColorScheme() = this.ColorScheme
    member this.SetColorScheme(scheme) = this.ColorScheme <- scheme

// CI環境判定ヘルパー
let isCI () =
    not (isNull (System.Environment.GetEnvironmentVariable("CI")))

// テスト用FrameView作成
let createTestableFrameView (title: string) : ITestableView =
    if isCI () then
        TestFrameView(title) :> ITestableView
    else
        // 実際のTerminal.GuiのFrameViewをラップ
        let frameView = new FrameView(title)

        { new ITestableView with
            member _.ColorScheme
                with get () = frameView.ColorScheme
                and set (v) = frameView.ColorScheme <- v

            member _.Title = frameView.Title.ToString() }

// Terminal.Gui初期化ヘルパー
let initializeTerminalGui () =
    if not (isCI ()) then
        try
            // 完全にサイレントな初期化
            let originalOut = System.Console.Out
            let originalErr = System.Console.Error

            try
                System.Console.SetOut(System.IO.TextWriter.Null)
                System.Console.SetError(System.IO.TextWriter.Null)
                Application.Init()
            finally
                System.Console.SetOut(originalOut)
                System.Console.SetError(originalErr)
        with _ ->
            () // Already initialized
    else
        // CI環境では一切の出力を抑制
        System.Console.SetOut(System.IO.TextWriter.Null)
        System.Console.SetError(System.IO.TextWriter.Null)

let shutdownTerminalGui () =
    if not (isCI ()) then
        try
            let originalOut = System.Console.Out
            let originalErr = System.Console.Error

            try
                System.Console.SetOut(System.IO.TextWriter.Null)
                System.Console.SetError(System.IO.TextWriter.Null)
                Application.Shutdown()
            finally
                System.Console.SetOut(originalOut)
                System.Console.SetError(originalErr)
        with _ ->
            () // Not initialized or already shutdown
    else
        // CI環境では標準出力を復元
        try
            System.Console.SetOut(new System.IO.StreamWriter(System.Console.OpenStandardOutput()))
            System.Console.SetError(new System.IO.StreamWriter(System.Console.OpenStandardError()))
        with _ ->
            ()

// CI安全なFrameView配列作成（KeyBindingsTests対応）
// CI環境では実際のFrameViewを作成せず、テスト専用実装を返す
type CIMockFrameView(title: string) =
    inherit View()

    let mutable colorScheme = FCode.ColorSchemes.devScheme
    let mutable title = title

    member this.Title
        with get () = title
        and set (value) = title <- value

    override this.ColorScheme
        with get () = colorScheme
        and set (value) = colorScheme <- value

let createMockFrameViews (count: int) =
    if isCI () then
        // CI環境：Terminal.Gui初期化を回避してMockFrameViewを使用
        Array.init count (fun i -> new CIMockFrameView($"mock-pane{i}") :> View)
    else
        // 開発環境：実際のTerminal.Gui FrameViewを使用
        let originalOut = System.Console.Out
        let originalErr = System.Console.Error

        try
            System.Console.SetOut(System.IO.TextWriter.Null)
            System.Console.SetError(System.IO.TextWriter.Null)
            initializeTerminalGui ()
            Array.init count (fun i -> new FrameView($"pane{i}") :> View)
        finally
            System.Console.SetOut(originalOut)
            System.Console.SetError(originalErr)

// MockFrameViewの配列作成（テスト専用）
let createMockFrameViewArray (count: int) (prefix: string) =
    Array.init count (fun i -> new MockFrameView($"{prefix}{i}") :> ITestableView)

// 単一MockFrameView作成
let createMockFrameViewSingle (title: string) =
    new MockFrameView(title) :> ITestableView

// UI依存性検証ヘルパー
let validateUIIndependence (action: unit -> unit) =
    let originalCIValue = System.Environment.GetEnvironmentVariable("CI")

    try
        try
            // CI環境を強制設定
            System.Environment.SetEnvironmentVariable("CI", "true")
            action ()
            true
        with ex ->
            // UI依存性が残存している場合
            printfn "UI依存性エラー: %s" ex.Message
            false
    finally
        // 元の環境変数を復元
        System.Environment.SetEnvironmentVariable("CI", originalCIValue)

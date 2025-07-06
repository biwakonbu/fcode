module FCode.Tests.TestHelpers

open Terminal.Gui
open FCode.ColorSchemes

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
            Application.Init()
        with _ ->
            () // Already initialized

let shutdownTerminalGui () =
    if not (isCI ()) then
        try
            Application.Shutdown()
        with _ ->
            () // Not initialized or already shutdown

// CI安全なFrameView配列作成（KeyBindingsTests対応）
let createMockFrameViews (count: int) =
    if isCI () then
        // CI環境：MockFrameViewを使用（Terminal.Gui初期化不要）
        Array.init count (fun i -> new FrameView($"mock-pane{i}"))
    else
        // 開発環境：実際のTerminal.Gui FrameViewを使用
        initializeTerminalGui ()
        Array.init count (fun i -> new FrameView($"pane{i}"))

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

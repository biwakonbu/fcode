module FCode.Tests.TestHelpers

open Terminal.Gui
open FCode.ColorSchemes

// テスト用のFrameViewラッパー
type TestFrameView(title: string) =
    member val ColorScheme = devScheme with get, set
    member val Title = title

    interface ITestableView with
        member this.ColorScheme
            with get () = this.ColorScheme
            and set (v) = this.ColorScheme <- v

        member this.Title = this.Title

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

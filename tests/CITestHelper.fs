module FCode.Tests.CITestHelper

open System
open System.Threading
open FCode.Logger

/// CI環境統一判定・設定ヘルパー
module CIEnvironment =

    /// CI環境変数の統一判定（強化版）
    let isCI () =
        let ciVars =
            [ "CI" // 汎用CI環境
              "CONTINUOUS_INTEGRATION" // Travis CI等
              "GITHUB_ACTIONS" // GitHub Actions
              "JENKINS_URL" // Jenkins
              "GITLAB_CI" // GitLab CI
              "BUILDKITE" // Buildkite
              "CIRCLECI" // CircleCI
              "APPVEYOR" // AppVeyor
              "FCODE_TEST_CI" ] // fcode専用CI設定

        let hasValidCI =
            ciVars
            |> List.exists (fun var ->
                let value = Environment.GetEnvironmentVariable(var)
                not (String.IsNullOrEmpty(value)) && value <> "false")

        // ヘッドレス環境も考慮（DISPLAY変数なし）
        let isHeadless = String.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))

        hasValidCI || isHeadless

    /// CI環境の強制設定（テスト用）
    let forceCI (enabled: bool) =
        Environment.SetEnvironmentVariable("CI", if enabled then "true" else null)
        Environment.SetEnvironmentVariable("FCODE_TEST_CI", if enabled then "true" else null)

    /// CI環境設定をリストア
    let restoreCI (originalValue: string) =
        Environment.SetEnvironmentVariable("CI", originalValue)
        Environment.SetEnvironmentVariable("FCODE_TEST_CI", null)

    /// テスト実行時のCI環境設定保存・復元
    let withForcedCI (enabled: bool) (action: unit -> 'T) : 'T =
        let originalCI = Environment.GetEnvironmentVariable("CI")
        let originalTestCI = Environment.GetEnvironmentVariable("FCODE_TEST_CI")

        try
            forceCI enabled
            logInfo "CITestHelper" $"CI環境強制設定: {enabled}"
            action ()
        finally
            Environment.SetEnvironmentVariable("CI", originalCI)
            Environment.SetEnvironmentVariable("FCODE_TEST_CI", originalTestCI)

/// Terminal.Gui初期化制御
module TerminalGuiControl =

    /// 初期化状態のトラッキング
    let mutable private isInitialized = false
    let private initLock = new obj ()

    /// CI安全なTerminal.Gui初期化
    let safeInit () =
        if not (CIEnvironment.isCI ()) then
            lock initLock (fun () ->
                if not isInitialized then
                    try
                        // タイムアウト付きでTerminal.Gui初期化を実行
                        let initTask =
                            System.Threading.Tasks.Task.Run(fun () -> Terminal.Gui.Application.Init())

                        if initTask.Wait(TimeSpan.FromSeconds(5.0)) then
                            isInitialized <- true
                            logDebug "CITestHelper" "Terminal.Gui初期化完了"
                        else
                            logWarning "CITestHelper" "Terminal.Gui初期化タイムアウト（CI環境として継続）"
                            CIEnvironment.forceCI true
                    with ex ->
                        logWarning "CITestHelper" $"Terminal.Gui初期化失敗（CI環境として継続）: {ex.Message}"
                        CIEnvironment.forceCI true)
        else
            logDebug "CITestHelper" "CI環境: Terminal.Gui初期化スキップ"

    /// CI安全なTerminal.Guiシャットダウン
    let safeShutdown () =
        if not (CIEnvironment.isCI ()) && isInitialized then
            lock initLock (fun () ->
                try
                    // タイムアウト付きでTerminal.Guiシャットダウンを実行
                    let shutdownTask =
                        System.Threading.Tasks.Task.Run(fun () -> Terminal.Gui.Application.Shutdown())

                    if shutdownTask.Wait(TimeSpan.FromSeconds(3.0)) then
                        isInitialized <- false
                        logDebug "CITestHelper" "Terminal.Guiシャットダウン完了"
                    else
                        logWarning "CITestHelper" "Terminal.Guiシャットダウンタイムアウト（強制終了）"
                        isInitialized <- false
                with ex ->
                    logWarning "CITestHelper" $"Terminal.Guiシャットダウン失敗: {ex.Message}"
                    isInitialized <- false)

/// テストタイムアウト制御
module TestTimeout =

    /// テスト実行タイムアウト（CI環境で短縮）
    let getTestTimeout () =
        if CIEnvironment.isCI () then
            TimeSpan.FromSeconds(30.0) // CI環境: 30秒
        else
            TimeSpan.FromMinutes(2.0) // 開発環境: 2分

    /// タイムアウト付きテスト実行
    let withTimeout (action: unit -> 'T) : 'T =
        let timeout = getTestTimeout ()
        let cts = new CancellationTokenSource(timeout)

        try
            let task = System.Threading.Tasks.Task.Run(action, cts.Token)

            if task.Wait(timeout) then
                task.Result
            else
                raise (TimeoutException($"テストタイムアウト: {timeout.TotalSeconds}秒"))
        finally
            cts.Dispose()

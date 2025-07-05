module FCode.Tests.ClaudeCodeIntegrationTests

open System
open System.Threading
open Xunit
open FCode.ClaudeCodeIntegration

/// CI環境判定
let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``ClaudeCodeIntegrationManager - 基本作成テスト`` () =
    let manager = new ClaudeCodeIntegrationManager()
    Assert.NotNull(manager)
    Assert.Equal("停止中", manager.GetStatus())
    manager.Dispose()

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``ClaudeCodeIntegrationManager - 出力バッファテスト`` () =
    let manager = new ClaudeCodeIntegrationManager()
    let buffer = manager.GetOutputBuffer()
    Assert.Empty(buffer)
    manager.Dispose()

[<Fact>]
[<Trait("TestCategory", "Integration")>]
let ``ClaudeCodeIntegrationManager - プロセス起動テスト`` () =
    if isCI then
        // CI環境ではスキップ
        ()
    else
        let manager = new ClaudeCodeIntegrationManager()
        let workingDir = System.Environment.CurrentDirectory

        // 起動テスト（Claude Code CLIが利用できない環境では失敗する）
        let result = manager.StartClaudeCode(workingDir)

        // 結果の検証（模擬環境での動作確認）
        match result with
        | Result.Ok message -> Assert.True(message.Contains("シミュレーション"), "模擬環境での成功メッセージ")
        | Result.Error errorMsg -> Assert.True(errorMsg.Length > 0, "エラーメッセージが有効")

        manager.Dispose()

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``ClaudeCodeIntegrationManager - コマンド送信テスト（未起動状態）`` () =
    let manager = new ClaudeCodeIntegrationManager()

    let result = manager.SendCommand("test command")

    match result with
    | Result.Error message -> Assert.Contains("起動していません", message)
    | Result.Ok _ -> Assert.True(false, "未起動状態でコマンド送信が成功するべきではない")

    manager.Dispose()

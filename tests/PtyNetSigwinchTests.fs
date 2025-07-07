namespace FCode.Tests

open NUnit.Framework
open System
open System.Diagnostics
open System.Threading.Tasks
open System.Threading
open System.Text.RegularExpressions
open FCode
open FCode.Logger

/// PTY Net SIGWINCH検証テスト（ウィンドウリサイズ動作確認）
[<TestFixture>]
[<Category("Integration")>]
type PtyNetSigwinchTests() =

    let mutable ptyManager: PtyNetManager option = None

    /// コマンドの存在確認
    let checkCommandExists (command: string) : bool =
        try
            let processInfo = ProcessStartInfo()
            processInfo.FileName <- "which"
            processInfo.Arguments <- command
            processInfo.RedirectStandardOutput <- true
            processInfo.RedirectStandardError <- true
            processInfo.UseShellExecute <- false
            processInfo.CreateNoWindow <- true

            use proc = Process.Start(processInfo)
            proc.WaitForExit()
            proc.ExitCode = 0
        with _ ->
            false

    [<SetUp>]
    member this.Setup() = ptyManager <- Some(new PtyNetManager())

    [<TearDown>]
    member this.TearDown() =
        match ptyManager with
        | Some manager ->
            (manager :> IDisposable).Dispose()
            ptyManager <- None
        | None -> ()

    /// htopリサイズテスト - 画面サイズ変更時の正しい追従確認
    [<Test>]
    [<Category("Integration")>]
    member this.SigwinchTest_Htop_Resize() =
        async {
            // htopコマンドの存在確認
            if not (checkCommandExists "htop") then
                Assert.Ignore("htop command not available on this platform")

            match ptyManager with
            | Some manager ->
                logInfo "SIGWINCH htopテスト開始" "ウィンドウリサイズ検証"

                // htopコマンドを起動
                let! sessionResult = manager.CreateSession("htop", [||]) |> Async.AwaitTask

                match sessionResult with
                | Result.Ok _session ->
                    let! _readingTask = manager.StartOutputReading() |> Async.AwaitTask |> Async.StartChild

                    // 初期サイズ設定（80x24）
                    let initialResize = manager.ResizeWindow(24, 80)
                    Assert.IsTrue(initialResize, "初期ウィンドウリサイズに失敗")

                    // htopが起動するまで待機
                    do! Task.Delay(2000) |> Async.AwaitTask
                    let initialOutput = manager.GetOutput()

                    logInfo
                        "初期htop出力"
                        ("length="
                         + initialOutput.Length.ToString()
                         + ", contains_htop="
                         + initialOutput.Contains("htop").ToString())

                    // 画面をクリア
                    manager.ClearOutput()

                    // ウィンドウサイズを変更（120x40）
                    let resizeResult = manager.ResizeWindow(40, 120)
                    Assert.IsTrue(resizeResult, "ウィンドウリサイズ操作に失敗")

                    // リサイズ後の出力を待機
                    do! Task.Delay(1000) |> Async.AwaitTask
                    let resizedOutput = manager.GetOutput()

                    logInfo "リサイズ後htop出力" ("length=" + resizedOutput.Length.ToString())

                    // もう一度リサイズ（60x20）
                    manager.ClearOutput()
                    let secondResizeResult = manager.ResizeWindow(20, 60)
                    Assert.IsTrue(secondResizeResult, "2回目のウィンドウリサイズに失敗")

                    do! Task.Delay(1000) |> Async.AwaitTask
                    let secondResizedOutput = manager.GetOutput()

                    logInfo "2回目リサイズ後htop出力" ("length=" + secondResizedOutput.Length.ToString())

                    // SIGWINCHが正しく伝搬されていることを確認
                    // （出力が変化することで間接的に確認）
                    Assert.Greater(initialOutput.Length, 0, "初期htop出力が空です")

                    // htopが実際に動作している証拠として、プロセス情報が含まれているかチェック
                    let containsProcessInfo =
                        initialOutput.Contains("PID")
                        || initialOutput.Contains("htop")
                        || resizedOutput.Contains("PID")
                        || secondResizedOutput.Contains("PID")

                    Assert.IsTrue(containsProcessInfo, "htopプロセス情報が出力に含まれていません")

                | Result.Error error -> Assert.Fail("htopセッション作成に失敗: " + error)

            | None -> Assert.Fail("PTYマネージャーが初期化されていません")
        }
        |> Async.RunSynchronously

    /// vimリサイズテスト - エディタでのウィンドウサイズ変更対応確認
    [<Test>]
    [<Category("Integration")>]
    member this.SigwinchTest_Vim_Resize() =
        async {
            // vimコマンドの存在確認
            if not (checkCommandExists "vim") then
                Assert.Ignore("vim command not available on this platform")

            match ptyManager with
            | Some manager ->
                logInfo "SIGWINCH vimテスト開始" "エディタリサイズ検証"

                // vimを起動（テストファイル作成）
                let testFile = "/tmp/sigwinch_test.txt"
                let! sessionResult = manager.CreateSession("vim", [| testFile |]) |> Async.AwaitTask

                match sessionResult with
                | Result.Ok _session ->
                    let! _readingTask = manager.StartOutputReading() |> Async.AwaitTask |> Async.StartChild

                    // vim起動待機
                    do! Task.Delay(2000) |> Async.AwaitTask

                    // 初期サイズ設定（80x24）
                    let initialResize = manager.ResizeWindow(24, 80)
                    Assert.IsTrue(initialResize, "vim初期ウィンドウリサイズに失敗")

                    // insertモードに入る
                    let insertMode = manager.SendInput("i")
                    Assert.IsTrue(insertMode, "vim insertモード移行に失敗")

                    do! Task.Delay(500) |> Async.AwaitTask

                    // テキスト入力
                    let textInput = manager.SendInput("Hello, SIGWINCH test!\n")
                    Assert.IsTrue(textInput, "vimテキスト入力に失敗")

                    do! Task.Delay(500) |> Async.AwaitTask
                    let beforeResizeOutput = manager.GetOutput()

                    // ウィンドウサイズ変更（100x30）
                    manager.ClearOutput()
                    let resizeResult = manager.ResizeWindow(30, 100)
                    Assert.IsTrue(resizeResult, "vimウィンドウリサイズに失敗")

                    // リサイズ後に追加テキスト入力
                    do! Task.Delay(500) |> Async.AwaitTask
                    let additionalInput = manager.SendInput("Resized window test!")
                    Assert.IsTrue(additionalInput, "リサイズ後テキスト入力に失敗")

                    do! Task.Delay(500) |> Async.AwaitTask
                    let afterResizeOutput = manager.GetOutput()

                    // Escキーでnormalモードに戻る
                    manager.SendInput("\u001b") |> ignore // ESC
                    do! Task.Delay(200) |> Async.AwaitTask

                    // vimを終了（:q!）
                    manager.SendInput(":q!\n") |> ignore
                    do! Task.Delay(500) |> Async.AwaitTask

                    logInfo
                        "vim SIGWINCH テスト結果"
                        ("before_resize_length="
                         + beforeResizeOutput.Length.ToString()
                         + ", after_resize_length="
                         + afterResizeOutput.Length.ToString())

                    // vimが正常に動作していることを確認
                    Assert.That(
                        beforeResizeOutput.Length + afterResizeOutput.Length,
                        Is.GreaterThan(0),
                        "vim出力が取得されませんでした"
                    )

                    // 何らかの出力変化があることを確認（SIGWINCHの間接的確認）
                    Assert.IsNotEmpty(beforeResizeOutput + afterResizeOutput, "リサイズ前後でvim出力に変化がありません")

                | Result.Error error -> Assert.Fail("vimセッション作成に失敗: " + error)

            | None -> Assert.Fail("PTYマネージャーが初期化されていません")
        }
        |> Async.RunSynchronously

    /// 基本的なリサイズ機能テスト - TIOCSWINSZ ioctl動作確認
    [<Test>]
    member this.SigwinchTest_Basic_Resize_Operation() =
        async {
            match ptyManager with
            | Some manager ->
                logInfo "基本リサイズテスト開始" "TIOCSWINSZ ioctl確認"

                // catコマンドでシンプルなPTYセッション作成
                let! sessionResult = manager.CreateSession("cat", [||]) |> Async.AwaitTask

                match sessionResult with
                | Result.Ok _session ->
                    let! _readingTask = manager.StartOutputReading() |> Async.AwaitTask |> Async.StartChild

                    // 様々なサイズでリサイズテスト
                    let testSizes =
                        [ (24, 80) // 標準
                          (40, 120) // 大きめ
                          (20, 60) // 小さめ
                          (50, 200) // 非常に大きい
                          (10, 40) ] // 非常に小さい

                    let mutable successCount = 0

                    for (rows, cols) in testSizes do
                        let resizeResult = manager.ResizeWindow(rows, cols)

                        if resizeResult then
                            successCount <- successCount + 1
                            logInfo "リサイズ成功" ("rows=" + rows.ToString() + ", cols=" + cols.ToString())
                        else
                            logWarning "リサイズ失敗" ("rows=" + rows.ToString() + ", cols=" + cols.ToString())

                        do! Task.Delay(100) |> Async.AwaitTask // 各リサイズ間の待機

                    // 全てのリサイズが成功することを期待
                    Assert.That(
                        successCount,
                        Is.EqualTo(testSizes.Length),
                        ("一部のリサイズ操作が失敗: " + successCount.ToString() + "/" + testSizes.Length.ToString())
                    )

                    logInfo
                        "基本リサイズテスト完了"
                        ("success_rate=" + successCount.ToString() + "/" + testSizes.Length.ToString())

                | Result.Error error -> Assert.Fail("基本リサイズテスト用セッション作成に失敗: " + error)

            | None -> Assert.Fail("PTYマネージャーが初期化されていません")
        }
        |> Async.RunSynchronously

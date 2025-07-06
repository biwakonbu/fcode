module FCode.Tests.QAEndToEndTests

open NUnit.Framework
open System
open System.Threading
open System.Threading.Tasks
open Terminal.Gui
open FCode.ClaudeCodeProcess
open FCode.QAPromptManager
open FCode.Tests.TestHelpers

/// FC-006 E2Eテスト: QA専用プロンプトの実際の動作確認
[<TestFixture>]
[<Category("Integration")>]
type QAEndToEndTests() =

    let mutable sessionManager: SessionManager option = None
    let mutable mockTextView1: TextView option = None
    let mutable mockTextView2: TextView option = None

    [<SetUp>]
    member _.Setup() =
        initializeTerminalGui ()
        sessionManager <- Some(SessionManager())

        // CI環境ではMockTextViewを使用
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if not isCI then
            mockTextView1 <- Some(new TextView())
            mockTextView2 <- Some(new TextView())

    [<TearDown>]
    member _.TearDown() =
        match sessionManager with
        | Some mgr ->
            try
                mgr.CleanupAllSessions()
            with _ ->
                ()
        | None -> ()

        mockTextView1 |> Option.iter (fun tv -> tv.Dispose())
        mockTextView2 |> Option.iter (fun tv -> tv.Dispose())

        shutdownTerminalGui ()

    [<Test>]
    member _.``QA1ペイン専用プロンプト適用E2Eテスト``() =
        // CI環境ではスキップ
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isCI then
            Assert.Pass("E2E test skipped in CI environment")
        else
            // Arrange
            match sessionManager, mockTextView1 with
            | Some mgr, Some textView ->
                let workingDir = System.Environment.CurrentDirectory

                // Act - QA1ペインでのセッション開始試行
                // 注意: 実際のClaude CLIが必要なため、Mock動作での検証
                let sessionStartAttempted =
                    try
                        // QA1設定が適用されることを検証
                        let qa1Role = getQARoleFromPaneId "qa1"
                        Assert.AreEqual(Some QA1, qa1Role, "qa1ペインがQA1役割に識別されること")

                        let qa1Config = getQAPromptConfig QA1
                        Assert.That(qa1Config.SystemPrompt, Does.Contain("テスト戦略"), "QA1システムプロンプトが正しく設定されること")

                        // 環境変数設定の検証
                        let qa1EnvVars = getQAEnvironmentVariables QA1 |> Map.ofList
                        Assert.AreEqual("test_strategy", qa1EnvVars.["QA_SPECIALIZATION"], "QA1専門化設定")

                        true
                    with ex ->
                        Console.WriteLine($"QA1セッション開始テスト例外: {ex.Message}")
                        false

                // Assert
                Assert.IsTrue(sessionStartAttempted, "QA1ペインでの設定適用が成功すること")

            | _ -> Assert.Fail("SessionManagerまたはTextViewの初期化に失敗")

    [<Test>]
    member _.``QA2ペイン専用プロンプト適用E2Eテスト``() =
        // CI環境ではスキップ
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isCI then
            Assert.Pass("E2E test skipped in CI environment")
        else
            // Arrange
            match sessionManager, mockTextView2 with
            | Some mgr, Some textView ->
                // Act - QA2ペインでの設定検証
                let qa2Role = getQARoleFromPaneId "qa2"
                Assert.AreEqual(Some QA2, qa2Role, "qa2ペインがQA2役割に識別されること")

                let qa2Config = getQAPromptConfig QA2
                Assert.That(qa2Config.SystemPrompt, Does.Contain("品質分析"), "QA2システムプロンプトが正しく設定されること")
                Assert.That(qa2Config.SystemPrompt, Does.Contain("コードレビュー"), "QA2がコードレビュー特化であること")

                // 環境変数設定の検証
                let qa2EnvVars = getQAEnvironmentVariables QA2 |> Map.ofList
                Assert.AreEqual("quality_analysis", qa2EnvVars.["QA_SPECIALIZATION"], "QA2専門化設定")
                Assert.That(qa2EnvVars.["QA_FOCUS_AREA"], Does.Contain("code_review"), "QA2フォーカス領域")

            | _ -> Assert.Fail("SessionManagerまたはTextViewの初期化に失敗")

    [<Test>]
    member _.``QA1とQA2の設定独立性E2Eテスト``() =
        // Arrange & Act
        let qa1Config = getQAPromptConfig QA1
        let qa2Config = getQAPromptConfig QA2
        let qa1EnvVars = getQAEnvironmentVariables QA1 |> Map.ofList
        let qa2EnvVars = getQAEnvironmentVariables QA2 |> Map.ofList

        // Assert - QA1とQA2が完全に独立した設定を持つこと
        Assert.AreEqual(Is.Not.EqualTo(qa2Config.SystemPrompt, qa1Config.SystemPrompt), "QA1とQA2のシステムプロンプトが異なること")

        Assert.AreEqual(
            Is.Not.EqualTo(qa2EnvVars.["QA_SPECIALIZATION"], qa1EnvVars.["QA_SPECIALIZATION"]),
            "専門化設定が異なること"
        )

        Assert.AreEqual(Is.Not.EqualTo(qa2Config.SkillFocus, qa1Config.SkillFocus), "スキル重点が異なること")

        // QA1はテスト戦略、QA2は品質分析に特化していること
        Assert.That(qa1Config.SystemPrompt, Does.Contain("テスト戦略"), "QA1がテスト戦略に特化")
        Assert.That(qa2Config.SystemPrompt, Does.Contain("品質分析"), "QA2が品質分析に特化")

    [<Test>]
    member _.``dev1-3とqa1-2の役割分離E2Eテスト``() =
        // Arrange & Act - dev1-3ペインはQA設定対象外であることを確認
        let devRoles = [ "dev1"; "dev2"; "dev3" ] |> List.map getQARoleFromPaneId
        let qaRoles = [ "qa1"; "qa2" ] |> List.map getQARoleFromPaneId

        // Assert - dev1-3はQA役割に設定されないこと
        devRoles
        |> List.iter (fun role -> Assert.AreEqual(None, role, "dev1-3ペインはQA役割に設定されないこと"))

        // Assert - qa1-2はQA役割に正しく設定されること
        Assert.AreEqual(Some QA1, qaRoles.[0], "qa1がQA1役割に設定されること")
        Assert.AreEqual(Some QA2, qaRoles.[1], "qa2がQA2役割に設定されること")

/// FC-006 統合テスト: 複数ペイン同時動作確認
[<TestFixture>]
[<Category("Integration")>]
type QAMultiPaneIntegrationTests() =

    [<Test>]
    member _.``複数QAペイン同時設定適用テスト``() =
        // Arrange - 複数QAペインの同時設定シミュレーション
        let qa1Role = getQARoleFromPaneId "qa1"
        let qa2Role = getQARoleFromPaneId "qa2"

        // Act - 同時設定適用
        let qa1ConfigResult = qa1Role |> Option.map getQAPromptConfig
        let qa2ConfigResult = qa2Role |> Option.map getQAPromptConfig
        let qa1EnvResult = qa1Role |> Option.map getQAEnvironmentVariables
        let qa2EnvResult = qa2Role |> Option.map getQAEnvironmentVariables

        // Assert - 両方のペインで適切な設定が適用されること
        match qa1ConfigResult, qa2ConfigResult with
        | Some qa1Config, Some qa2Config ->
            Assert.IsTrue(validateQAPromptConfig qa1Config, "QA1設定が有効であること")
            Assert.IsTrue(validateQAPromptConfig qa2Config, "QA2設定が有効であること")

            // 設定の独立性確認
            Assert.AreEqual(Is.Not.EqualTo(qa2Config.TestingApproach, qa1Config.TestingApproach), "テストアプローチが独立していること")
        | _ -> Assert.Fail("QA設定の取得に失敗")

        match qa1EnvResult, qa2EnvResult with
        | Some qa1Env, Some qa2Env ->
            let qa1EnvMap = qa1Env |> Map.ofList
            let qa2EnvMap = qa2Env |> Map.ofList

            // 共通環境変数の確認
            Assert.AreEqual("qa", qa1EnvMap.["CLAUDE_ROLE"], "QA1のCLAUDE_ROLE設定")
            Assert.AreEqual("qa", qa2EnvMap.["CLAUDE_ROLE"], "QA2のCLAUDE_ROLE設定")

            // 専門化設定の独立性確認
            Assert.That(
                qa1EnvMap.["QA_SPECIALIZATION"],
                Is.Not.EqualTo(qa2EnvMap.["QA_SPECIALIZATION"]),
                "専門化設定が独立していること"
            )
        | _ -> Assert.Fail("QA環境変数の取得に失敗")

    [<Test>]
    member _.``QA設定の並行処理安全性テスト``() =
        // Arrange - 並行処理でのQA設定取得テスト
        let qaRoles = [ QA1; QA2 ]

        // Act - 並行処理での設定取得
        let parallelResults =
            qaRoles
            |> List.map (fun role ->
                Task.Run(fun () ->
                    let config = getQAPromptConfig role
                    let envVars = getQAEnvironmentVariables role
                    let displayName = getQARoleDisplayName role
                    (config, envVars, displayName)))
            |> Task.WhenAll
            |> fun task -> task.Result

        // Assert - 並行処理でも正確な設定が取得されること
        Assert.AreEqual(2, parallelResults.Length, "2つのQA設定が取得されること")

        let (qa1Config, qa1Env, qa1Name) = parallelResults.[0]
        let (qa2Config, qa2Env, qa2Name) = parallelResults.[1]

        Assert.IsTrue(validateQAPromptConfig qa1Config, "並行処理でQA1設定が有効であること")
        Assert.IsTrue(validateQAPromptConfig qa2Config, "並行処理でQA2設定が有効であること")
        Assert.AreEqual(Is.Not.EqualTo(qa2Name, qa1Name), "並行処理でも役割名が正しく区別されること")

    [<Test>]
    member _.``QAログ出力の競合状態テスト``() =
        // Arrange - 同時ログ出力でのスレッドセーフティテスト
        let testTasks =
            [ 1..10 ]
            |> List.map (fun i ->
                Task.Run(fun () ->
                    let role = if i % 2 = 0 then QA1 else QA2
                    let paneId = if i % 2 = 0 then "qa1" else "qa2"

                    // 同時ログ出力
                    logQAPromptApplication paneId role

                    // 設定検証も同時実行
                    let config = getQAPromptConfig role
                    validateQAPromptConfig config))

        // Act & Assert - 競合状態でもエラーが発生しないこと
        Assert.DoesNotThrow(fun () ->
            Task.WhenAll(testTasks).Wait()

            testTasks
            |> List.iter (fun task -> Assert.IsTrue(task.Result, "競合状態でも設定検証が成功すること")))

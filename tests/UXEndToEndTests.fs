module FCode.Tests.UXEndToEndTests

open NUnit.Framework
open System
open System.Threading
open System.Threading.Tasks
open Terminal.Gui
open FCode.ClaudeCodeProcess
open FCode.UXPromptManager
open FCode.Tests.TestHelpers

/// FC-007 E2Eテスト: UX専用プロンプトの実際の動作確認
[<TestFixture>]
[<Category("Integration")>]
type UXEndToEndTests() =

    let mutable sessionManager: SessionManager option = None
    let mutable mockTextView: TextView option = None

    [<SetUp>]
    member _.Setup() =
        initializeTerminalGui ()
        sessionManager <- Some(SessionManager())

        // CI環境ではMockTextViewを使用
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if not isCI then
            mockTextView <- Some(new TextView())

    [<TearDown>]
    member _.TearDown() =
        match sessionManager with
        | Some mgr ->
            try
                mgr.CleanupAllSessions()
            with _ ->
                ()
        | None -> ()

        mockTextView |> Option.iter (fun tv -> tv.Dispose())

        shutdownTerminalGui ()

    [<Test>]
    member _.``UXペイン専用プロンプト適用E2Eテスト``() =
        // CI環境ではスキップ
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isCI then
            Assert.Pass("E2E test skipped in CI environment")
        else
            // Arrange
            match sessionManager, mockTextView with
            | Some mgr, Some textView ->
                let workingDir = System.Environment.CurrentDirectory

                // Act - UXペインでのセッション開始試行
                // 注意: 実際のClaude CLIが必要なため、Mock動作での検証
                let sessionStartAttempted =
                    try
                        // UX設定が適用されることを検証
                        let uxRole = getUXRoleFromPaneId "ux"
                        Assert.AreEqual(Some UX, uxRole, "uxペインがUX役割に識別されること")

                        let uxConfig = getUXPromptConfig UX
                        Assert.That(uxConfig.SystemPrompt, Does.Contain("ユーザビリティ"), "UXシステムプロンプトが正しく設定されること")
                        Assert.That(uxConfig.SystemPrompt, Does.Contain("ワイヤーフレーム"), "UXがワイヤーフレーム特化であること")

                        // 環境変数設定の検証
                        let uxEnvVars = getUXEnvironmentVariables UX |> Map.ofList
                        Assert.AreEqual("ui_ux_design", uxEnvVars.["UX_SPECIALIZATION"], "UX専門化設定")

                        true
                    with ex ->
                        Console.WriteLine($"UXセッション開始テスト例外: {ex.Message}")
                        false

                // Assert
                Assert.IsTrue(sessionStartAttempted, "UXペインでの設定適用が成功すること")

            | _ -> Assert.Fail("SessionManagerまたはTextViewの初期化に失敗")

    [<Test>]
    member _.``UX設定の独立性確認テスト``() =
        // Arrange & Act
        let uxConfig = getUXPromptConfig UX
        let uxEnvVars = getUXEnvironmentVariables UX |> Map.ofList

        // Assert - UXが独自の専門性を持つこと
        Assert.That(uxConfig.SystemPrompt, Does.Contain("UX/UIデザイン"), "UXがデザイン専門であること")
        Assert.That(uxConfig.SkillFocus, Does.Contain("ユーザビリティ設計"), "ユーザビリティ設計に特化")
        Assert.That(uxConfig.SkillFocus, Does.Contain("アクセシビリティ"), "アクセシビリティに特化")
        Assert.That(uxConfig.DesignApproach, Does.Contain("ユーザー中心"), "ユーザー中心設計アプローチ")

        // 環境変数の独自性確認
        Assert.AreEqual("ui_ux_design", uxEnvVars.["UX_SPECIALIZATION"], "UX専門化設定")
        Assert.That(uxEnvVars.["UX_FOCUS_AREA"], Does.Contain("usability"), "ユーザビリティフォーカス")
        Assert.That(uxEnvVars.["UX_FOCUS_AREA"], Does.Contain("accessibility"), "アクセシビリティフォーカス")

    [<Test>]
    member _.``dev・qa・uxの役割分離E2Eテスト``() =
        // Arrange & Act - dev1-3, qa1-2ペインはUX設定対象外であることを確認
        let devRoles = [ "dev1"; "dev2"; "dev3" ] |> List.map getUXRoleFromPaneId
        let qaRoles = [ "qa1"; "qa2" ] |> List.map getUXRoleFromPaneId
        let uxRole = getUXRoleFromPaneId "ux"

        // Assert - dev1-3, qa1-2はUX役割に設定されないこと
        devRoles
        |> List.iter (fun role -> Assert.AreEqual(None, role, "dev1-3ペインはUX役割に設定されないこと"))

        qaRoles
        |> List.iter (fun role -> Assert.AreEqual(None, role, "qa1-2ペインはUX役割に設定されないこと"))

        // Assert - uxはUX役割に正しく設定されること
        Assert.AreEqual(Some UX, uxRole, "uxがUX役割に設定されること")

/// FC-007 統合テスト: マルチペイン（dev/qa/ux）同時動作確認
[<TestFixture>]
[<Category("Integration")>]
type UXMultiPaneIntegrationTests() =

    [<Test>]
    member _.``全ペイン役割分離統合テスト``() =
        // Arrange - 全ペインの役割設定確認
        let devRole = "dev1"
        let qaRole = "qa1"
        let uxRole = "ux"

        // Act - 各ペインの専門化設定確認
        let uxRoleResult = getUXRoleFromPaneId uxRole

        // Assert - 各ペインが独立した役割を持つこと
        match uxRoleResult with
        | Some role ->
            let uxConfig = getUXPromptConfig role
            Assert.IsTrue(validateUXPromptConfig uxConfig, "UX設定が有効であること")

            // 他ペインとの差別化確認
            Assert.That(uxConfig.SystemPrompt, Does.Contain("UX/UIデザイン"), "UXが明確にデザイン専門であること")
            Assert.That(uxConfig.SkillFocus, Does.Contain("ユーザビリティ設計"), "UXがユーザビリティに特化")
        | None -> Assert.Fail("UX役割の特定に失敗")

    [<Test>]
    member _.``UX設定の並行処理安全性テスト``() =
        // Arrange - 並行処理でのUX設定取得テスト
        let uxRole = UX

        // Act - 並行処理での設定取得
        let parallelResults =
            [ 1..5 ]
            |> List.map (fun i ->
                Task.Run(fun () ->
                    let config = getUXPromptConfig uxRole
                    let envVars = getUXEnvironmentVariables uxRole
                    let displayName = getUXRoleDisplayName uxRole
                    (config, envVars, displayName)))
            |> Task.WhenAll
            |> fun task -> task.Result

        // Assert - 並行処理でも正確な設定が取得されること
        Assert.AreEqual(5, parallelResults.Length, "5つのUX設定が取得されること")

        parallelResults
        |> Array.iter (fun (config, envVars, displayName) ->
            Assert.IsTrue(validateUXPromptConfig config, "並行処理でUX設定が有効であること")
            Assert.AreEqual("UX (UI/UXデザイン)", displayName, "並行処理でも役割名が正しいこと"))

    [<Test>]
    member _.``UXログ出力の競合状態テスト``() =
        // Arrange - 同時ログ出力でのスレッドセーフティテスト
        let testTasks =
            [ 1..10 ]
            |> List.map (fun i ->
                Task.Run(fun () ->
                    let role = UX
                    let paneId = "ux"

                    // 同時ログ出力
                    logUXPromptApplication paneId role

                    // 設定検証も同時実行
                    let config = getUXPromptConfig role
                    validateUXPromptConfig config))

        // Act & Assert - 競合状態でもエラーが発生しないこと
        Assert.DoesNotThrow(fun () ->
            Task.WhenAll(testTasks).Wait()

            testTasks
            |> List.iter (fun task -> Assert.IsTrue(task.Result, "競合状態でも設定検証が成功すること")))

    [<Test>]
    member _.``6ペイン同時設定適用シミュレーションテスト``() =
        // Arrange - dev1-3, qa1-2, ux の6ペイン同時設定
        let allPanes = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux" ]

        // Act - 各ペインの設定適用をシミュレーション
        let paneResults =
            allPanes
            |> List.map (fun paneId ->
                let uxRole = getUXRoleFromPaneId paneId
                (paneId, uxRole))

        // Assert - 適切な役割分離が行われていること
        let uxPaneResults = paneResults |> List.filter (fun (_, role) -> role.IsSome)
        let nonUxPaneResults = paneResults |> List.filter (fun (_, role) -> role.IsNone)

        Assert.AreEqual(1, uxPaneResults.Length, "UX役割は1ペインのみ")
        Assert.AreEqual(5, nonUxPaneResults.Length, "非UX役割は5ペイン")

        // UXペインの詳細確認
        let (uxPaneId, uxRoleOpt) = uxPaneResults.Head
        Assert.AreEqual("ux", uxPaneId, "UXペインが正しく識別されること")
        Assert.AreEqual(Some UX, uxRoleOpt, "UX役割が正しく設定されること")

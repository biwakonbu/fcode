module FCode.Tests.PMEndToEndTests

open System
open System.Threading.Tasks
open NUnit.Framework
open Terminal.Gui
open FCode.PMPromptManager
open FCode.ClaudeCodeProcess
open FCode.UIHelpers
open FCode.Tests.TestHelpers

[<TestFixture>]
[<Category("Integration")>]
type PMEndToEndTests() =

    let mutable testSessionManager: SessionManager option = None
    let mutable testTextViews: TextView list = []

    [<SetUp>]
    member _.Setup() =
        // CI環境でのTerminal.Gui初期化スキップ
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if not isCI then
            try
                Application.Init()
            with _ ->
                () // Already initialized

        testSessionManager <- Some(new SessionManager())

    [<TearDown>]
    member _.TearDown() =
        // 全てのテストワーカーをクリーンアップ
        match testSessionManager with
        | Some sessionMgr ->
            sessionMgr.CleanupAllSessions()
            testSessionManager <- None
        | None -> ()

        // Cleanup handled by using statements

        // TextViewsをクリーンアップ
        testTextViews |> List.iter (fun tv -> tv.Dispose())
        testTextViews <- []

        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if not isCI then
            try
                Application.Shutdown()
            with _ ->
                ()

    [<Test>]
    member _.``PMペイン完全フロー統合テスト - PM専用設定から動作確認まで``() =
        task {
            // Arrange
            let workingDir = System.IO.Directory.GetCurrentDirectory()
            let testTextView = new TextView()
            testTextViews <- testTextView :: testTextViews

            match testSessionManager with
            | Some sessionMgr ->
                // Act - PMペインの設定検証
                let pmRole = getPMRoleFromPaneId "pm"
                Assert.AreEqual(Some ProjectManager, pmRole, "pmペインがProjectManager役割として認識されること")

                match pmRole with
                | Some role ->
                    let pmConfig = getPMPromptConfig role
                    let pmEnvVars = getPMEnvironmentVariables role

                    // PM設定の完全性確認
                    Assert.IsNotEmpty(pmConfig.SystemPrompt, "PMシステムプロンプトが設定されていること")
                    Assert.That(pmConfig.SystemPrompt, Does.Contain("プロジェクトマネージャー"), "PM役割がプロンプトに含まれること")
                    Assert.That(pmConfig.SystemPrompt, Does.Contain("統合管理"), "統合管理機能がプロンプトに含まれること")
                    Assert.That(pmConfig.SystemPrompt, Does.Contain("dev1-3, qa1-2, ux"), "全ペイン統合監視がプロンプトに含まれること")

                    // PM環境変数の完全性確認
                    let pmEnvMap = pmEnvVars |> Map.ofList
                    Assert.AreEqual("pm", pmEnvMap.["CLAUDE_ROLE"], "CLAUDE_ROLE環境変数が正しく設定されること")
                    Assert.AreEqual("project_management", pmEnvMap.["PM_FOCUS"], "PM_FOCUS環境変数が正しく設定されること")
                    Assert.AreEqual("integration", pmEnvMap.["PM_PERSPECTIVE"], "PM_PERSPECTIVE環境変数が正しく設定されること")
                    Assert.AreEqual("7_panes", pmEnvMap.["PM_TEAM_SIZE"], "PM_TEAM_SIZE環境変数が正しく設定されること")

                    // ログ機能の動作確認
                    Assert.DoesNotThrow(fun () -> logPMPromptApplication "pm" role)

                    // ClaudeCodeProcess統合設定の確認（SessionManager経由）
                    // Note: 実際のClaude起動はテスト環境では行わず、設定の整合性のみ確認
                    let pmDisplayName = getPMRoleDisplayName role
                    Assert.AreEqual("プロジェクトマネージャー", pmDisplayName, "PM表示名が正しく設定されること")

                | None -> Assert.Fail("pmペインからPM役割が取得できない")

            | None -> Assert.Fail("テスト用SessionManagerが初期化されていない")
        }

    [<Test>]
    member _.``PM timelineペイン統合テスト - timeline専用設定検証``() =
        task {
            // Arrange
            let workingDir = System.IO.Directory.GetCurrentDirectory()
            let testTextView = new TextView()
            testTextViews <- testTextView :: testTextViews

            // Act - timelineペインの設定検証
            let timelineRole = getPMRoleFromPaneId "timeline"
            Assert.AreEqual(Some ProjectManager, timelineRole, "timelineペインがProjectManager役割として認識されること")

            let pmTimelineRole = getPMRoleFromPaneId "PM / PdM タイムライン"

            Assert.That(
                pmTimelineRole,
                Is.EqualTo(Some ProjectManager),
                "PM / PdM タイムラインペインがProjectManager役割として認識されること"
            )

            match timelineRole with
            | Some role ->
                let config = getPMPromptConfig role
                let envVars = getPMEnvironmentVariables role

                // timeline設定の完全性確認
                Assert.That(config.SystemPrompt, Does.Contain("統合管理"), "timeline用統合管理機能が設定されていること")
                Assert.That(config.SystemPrompt, Does.Contain("プロジェクト全体のサマリー"), "サマリー機能が設定されていること")
                Assert.That(config.SystemPrompt, Does.Contain("意思決定支援"), "意思決定支援機能が設定されていること")

                let envMap = envVars |> Map.ofList
                Assert.AreEqual("integration", envMap.["PM_PERSPECTIVE"], "timeline用統合視点が設定されていること")

            | None -> Assert.Fail("timelineペインからPM役割が取得できない")
        }

    [<Test>]
    member _.``PM vs ProductManager役割差分テスト``() =
        task {
            // Arrange & Act
            let projectManagerConfig = getPMPromptConfig ProjectManager
            let productManagerConfig = getPMPromptConfig ProductManager

            // Assert - 役割の明確な差分確認
            Assert.That(
                projectManagerConfig.SystemPrompt,
                Does.Contain("プロジェクトマネージャー"),
                "ProjectManagerプロンプトに役割が明記されること"
            )

            Assert.That(
                productManagerConfig.SystemPrompt,
                Does.Contain("プロダクトマネージャー"),
                "ProductManagerプロンプトに役割が明記されること"
            )

            // ProjectManager固有要素
            Assert.That(projectManagerConfig.SystemPrompt, Does.Contain("進捗管理"), "ProjectManagerに進捗管理が含まれること")

            Assert.That(
                projectManagerConfig.SystemPrompt,
                Does.Contain("agile_kanban"),
                "ProjectManagerにアジャイル・カンバン手法が含まれること"
            )

            // ProductManager固有要素
            Assert.That(productManagerConfig.SystemPrompt, Does.Contain("プロダクト戦略"), "ProductManagerにプロダクト戦略が含まれること")
            Assert.That(productManagerConfig.SystemPrompt, Does.Contain("ビジネス価値"), "ProductManagerにビジネス価値が含まれること")

            Assert.That(
                productManagerConfig.SystemPrompt,
                Does.Contain("lean_startup"),
                "ProductManagerにリーンスタートアップ手法が含まれること"
            )

            // 環境変数の差分確認
            let pmEnvMap = projectManagerConfig.EnvironmentVars |> Map.ofList
            let pdmEnvMap = productManagerConfig.EnvironmentVars |> Map.ofList

            Assert.AreEqual("project_management", pmEnvMap.["PM_FOCUS"], "ProjectManagerのフォーカス設定")
            Assert.AreEqual("product_management", pdmEnvMap.["PM_FOCUS"], "ProductManagerのフォーカス設定")
            Assert.AreEqual("integration", pmEnvMap.["PM_PERSPECTIVE"], "ProjectManagerの視点設定")
            Assert.AreEqual("business_value", pdmEnvMap.["PM_PERSPECTIVE"], "ProductManagerの視点設定")
        }

    [<Test>]
    member _.``PM設定と他ロール設定の独立性テスト``() =
        task {
            // Arrange - 各ペインIDでの役割特定テスト
            let testCases =
                [ ("pm", Some ProjectManager, "PM")
                  ("timeline", Some ProjectManager, "timeline")
                  ("PM / PdM タイムライン", Some ProjectManager, "PM / PdM タイムライン")
                  ("dev1", None, "dev1")
                  ("dev2", None, "dev2")
                  ("dev3", None, "dev3")
                  ("qa1", None, "qa1")
                  ("qa2", None, "qa2")
                  ("ux", None, "ux")
                  ("chat", None, "chat")
                  ("会話", None, "会話") ]

            // Act & Assert
            testCases
            |> List.iter (fun (paneId, expectedRole, description) ->
                let actualRole = getPMRoleFromPaneId paneId
                Assert.AreEqual(expectedRole, actualRole, $"{description}ペインの役割特定が正しいこと"))
        }

    [<Test>]
    member _.``PM全機能統合動作テスト``() =
        task {
            // Arrange
            let allPMRoles = [ ProjectManager; ProductManager ]
            let pmPaneIds = [ "pm"; "timeline"; "PM / PdM タイムライン" ]

            // Act & Assert - 全PM機能の統合動作確認
            for role in allPMRoles do
                let config = getPMPromptConfig role
                let envVars = getPMEnvironmentVariables role
                let displayName = getPMRoleDisplayName role

                // 設定完全性テスト
                Assert.Greater(config.SystemPrompt.Length, 500, $"{displayName}のシステムプロンプトが十分詳細であること")
                Assert.AreEqual(5, config.EnvironmentVars.Length, $"{displayName}の環境変数が完全であること")
                Assert.AreEqual(5, envVars.Length, $"{displayName}の環境変数取得が正常であること")

                // 日本語対応テスト
                Assert.That(config.SystemPrompt, Does.Contain("日本語"), $"{displayName}が日本語対応していること")
                Assert.That(config.SystemPrompt, Does.Contain("具体的で実行可能"), $"{displayName}が実用的提案機能を持つこと")

                // ログ機能テスト
                for paneId in pmPaneIds do
                    Assert.DoesNotThrow(fun () -> logPMPromptApplication paneId role)
        }

    [<Test>]
    member _.``PM統合管理機能特化テスト``() =
        task {
            // Arrange
            let pmConfig = getPMPromptConfig ProjectManager

            // Act & Assert - PM統合管理機能の詳細検証
            Assert.That(pmConfig.SystemPrompt, Does.Contain("dev1-3, qa1-2, uxペインの活動統合監視"), "全ペイン統合監視機能")
            Assert.That(pmConfig.SystemPrompt, Does.Contain("プロジェクト全体のサマリー・レポート作成"), "サマリー・レポート作成機能")
            Assert.That(pmConfig.SystemPrompt, Does.Contain("意思決定支援と戦略的提案"), "意思決定支援機能")
            Assert.That(pmConfig.SystemPrompt, Does.Contain("チーム間のコミュニケーション促進"), "コミュニケーション促進機能")

            // 7ペイン環境での動作前提確認
            let pmEnvMap = pmConfig.EnvironmentVars |> Map.ofList
            Assert.AreEqual("7_panes", pmEnvMap.["PM_TEAM_SIZE"], "7ペイン環境設定")
            Assert.AreEqual("integration", pmEnvMap.["PM_PERSPECTIVE"], "統合管理視点設定")
        }

    [<Test>]
    member _.``PM品質保証機能テスト``() =
        task {
            // Arrange
            let pmConfig = getPMPromptConfig ProjectManager
            let pdmConfig = getPMPromptConfig ProductManager

            // Act & Assert - PM品質保証機能の確認
            Assert.That(pmConfig.SystemPrompt, Does.Contain("品質管理"), "ProjectManager品質管理機能")
            Assert.That(pmConfig.SystemPrompt, Does.Contain("成果物の品質基準設定"), "品質基準設定機能")
            Assert.That(pdmConfig.SystemPrompt, Does.Contain("UXとテクニカル品質の統合評価"), "ProductManager品質評価機能")
            Assert.That(pdmConfig.SystemPrompt, Does.Contain("プロダクト改善"), "プロダクト改善機能")

            // 継続的改善プロセス確認
            Assert.That(pmConfig.SystemPrompt, Does.Contain("継続的改善プロセス"), "ProjectManager継続的改善")
            Assert.That(pdmConfig.SystemPrompt, Does.Contain("継続的価値提供"), "ProductManager継続的価値提供")
        }

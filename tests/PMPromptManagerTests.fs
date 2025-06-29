module FCode.Tests.PMPromptManagerTests

open NUnit.Framework
open FCode.PMPromptManager
open FCode.Tests.TestHelpers

[<TestFixture>]
[<Category("Unit")>]
type PMPromptManagerTests() =

    [<SetUp>]
    member _.Setup() = initializeTerminalGui ()

    [<TearDown>]
    member _.TearDown() = shutdownTerminalGui ()

    [<Test>]
    member _.``PM役割定義テスト``() =
        // Arrange & Act
        let pmConfig = getPMPromptConfig ProjectManager
        let pdmConfig = getPMPromptConfig ProductManager

        // Assert
        Assert.That(pmConfig.DisplayName, Is.EqualTo("プロジェクトマネージャー"), "ProjectManager設定の表示名が正しいこと")
        Assert.That(pdmConfig.DisplayName, Is.EqualTo("プロダクトマネージャー"), "ProductManager設定の表示名が正しいこと")

        Assert.That(pmConfig.SystemPrompt, Is.Not.Empty, "ProjectManagerシステムプロンプトが設定されていること")
        Assert.That(pdmConfig.SystemPrompt, Is.Not.Empty, "ProductManagerシステムプロンプトが設定されていること")

    [<Test>]
    member _.``ProjectManager専用設定内容テスト``() =
        // Arrange & Act
        let pmConfig = getPMPromptConfig ProjectManager

        // Assert - ProjectManagerは進捗・リスク・品質・統合管理専門
        Assert.That(pmConfig.SystemPrompt, Does.Contain("進捗管理"), "進捗管理が含まれること")
        Assert.That(pmConfig.SystemPrompt, Does.Contain("リスク管理"), "リスク管理が含まれること")
        Assert.That(pmConfig.SystemPrompt, Does.Contain("品質管理"), "品質管理が含まれること")
        Assert.That(pmConfig.SystemPrompt, Does.Contain("統合管理"), "統合管理が含まれること")
        Assert.That(pmConfig.SystemPrompt, Does.Contain("dev1-3, qa1-2, ux"), "全ペイン統合監視が含まれること")

        // 環境変数確認
        let pmEnvMap = pmConfig.EnvironmentVars |> Map.ofList
        Assert.That(pmEnvMap.["PM_FOCUS"], Is.EqualTo("project_management"), "PM_FOCUS設定")
        Assert.That(pmEnvMap.["PM_PERSPECTIVE"], Is.EqualTo("integration"), "PM_PERSPECTIVE設定")
        Assert.That(pmEnvMap.["PM_METHODOLOGY"], Is.EqualTo("agile_kanban"), "PM_METHODOLOGY設定")

    [<Test>]
    member _.``ProductManager専用設定内容テスト``() =
        // Arrange & Act
        let pdmConfig = getPMPromptConfig ProductManager

        // Assert - ProductManagerはプロダクト戦略・ステークホルダー管理専門
        Assert.That(pdmConfig.SystemPrompt, Does.Contain("プロダクト戦略"), "プロダクト戦略が含まれること")
        Assert.That(pdmConfig.SystemPrompt, Does.Contain("ステークホルダー管理"), "ステークホルダー管理が含まれること")
        Assert.That(pdmConfig.SystemPrompt, Does.Contain("プロダクト分析"), "プロダクト分析が含まれること")
        Assert.That(pdmConfig.SystemPrompt, Does.Contain("チーム協調"), "チーム協調が含まれること")
        Assert.That(pdmConfig.SystemPrompt, Does.Contain("ビジネス価値"), "ビジネス価値が含まれること")

        // 環境変数確認
        let pdmEnvMap = pdmConfig.EnvironmentVars |> Map.ofList
        Assert.That(pdmEnvMap.["PM_FOCUS"], Is.EqualTo("product_management"), "PM_FOCUS設定")
        Assert.That(pdmEnvMap.["PM_PERSPECTIVE"], Is.EqualTo("business_value"), "PM_PERSPECTIVE設定")
        Assert.That(pdmEnvMap.["PM_METHODOLOGY"], Is.EqualTo("lean_startup"), "PM_METHODOLOGY設定")

    [<Test>]
    member _.``ペインIDからPM役割特定テスト``() =
        // Arrange & Act & Assert
        Assert.That(getPMRoleFromPaneId "pm", Is.EqualTo(Some ProjectManager), "pmペインがProjectManager役割に特定されること")
        Assert.That(getPMRoleFromPaneId "PM", Is.EqualTo(Some ProjectManager), "大文字PMも認識されること")
        Assert.That(getPMRoleFromPaneId "timeline", Is.EqualTo(Some ProjectManager), "timelineペインも認識されること")
        Assert.That(getPMRoleFromPaneId "PM / PdM タイムライン", Is.EqualTo(Some ProjectManager), "PM含有文字列も認識されること")

        // 非PMペインはNoneを返すこと
        Assert.That(getPMRoleFromPaneId "dev1", Is.EqualTo(None), "dev1ペインはPM役割でないこと")
        Assert.That(getPMRoleFromPaneId "qa1", Is.EqualTo(None), "qa1ペインはPM役割でないこと")
        Assert.That(getPMRoleFromPaneId "ux", Is.EqualTo(None), "uxペインはPM役割でないこと")

    [<Test>]
    member _.``PM専用環境変数設定テスト``() =
        // Arrange & Act
        let pmEnvVars = getPMEnvironmentVariables ProjectManager
        let pdmEnvVars = getPMEnvironmentVariables ProductManager

        // Assert - 共通環境変数
        let pmEnvMap = pmEnvVars |> Map.ofList
        let pdmEnvMap = pdmEnvVars |> Map.ofList

        Assert.That(pmEnvMap.["CLAUDE_ROLE"], Is.EqualTo("pm"), "ProjectManagerのCLAUDE_ROLEが設定されること")
        Assert.That(pdmEnvMap.["CLAUDE_ROLE"], Is.EqualTo("pm"), "ProductManagerのCLAUDE_ROLEが設定されること")
        Assert.That(pmEnvMap.["PM_TEAM_SIZE"], Is.EqualTo("7_panes"), "ProjectManagerのPM_TEAM_SIZEが設定されること")
        Assert.That(pdmEnvMap.["PM_TEAM_SIZE"], Is.EqualTo("7_panes"), "ProductManagerのPM_TEAM_SIZEが設定されること")

        // Assert - ProjectManager固有環境変数
        Assert.That(pmEnvMap.["PM_FOCUS"], Is.EqualTo("project_management"), "ProjectManager専門化設定")
        Assert.That(pmEnvMap.["PM_PERSPECTIVE"], Is.EqualTo("integration"), "ProjectManager視点設定")
        Assert.That(pmEnvMap.["PM_METHODOLOGY"], Is.EqualTo("agile_kanban"), "ProjectManager手法設定")

        // Assert - ProductManager固有環境変数
        Assert.That(pdmEnvMap.["PM_FOCUS"], Is.EqualTo("product_management"), "ProductManager専門化設定")
        Assert.That(pdmEnvMap.["PM_PERSPECTIVE"], Is.EqualTo("business_value"), "ProductManager視点設定")
        Assert.That(pdmEnvMap.["PM_METHODOLOGY"], Is.EqualTo("lean_startup"), "ProductManager手法設定")

    [<Test>]
    member _.``PM役割表示名テスト``() =
        // Arrange & Act & Assert
        Assert.That(getPMRoleDisplayName ProjectManager, Is.EqualTo("プロジェクトマネージャー"), "ProjectManager表示名が正しいこと")
        Assert.That(getPMRoleDisplayName ProductManager, Is.EqualTo("プロダクトマネージャー"), "ProductManager表示名が正しいこと")

    [<Test>]
    member _.``PM設定の一意性テスト``() =
        // Arrange & Act
        let pmConfig = getPMPromptConfig ProjectManager
        let pdmConfig = getPMPromptConfig ProductManager

        // Assert - ProjectManagerとProductManagerの設定が異なることを確認
        Assert.That(
            pmConfig.SystemPrompt,
            Is.Not.EqualTo(pdmConfig.SystemPrompt),
            "ProjectManagerとProductManagerのシステムプロンプトが異なること"
        )

        Assert.That(
            pmConfig.DisplayName,
            Is.Not.EqualTo(pdmConfig.DisplayName),
            "ProjectManagerとProductManagerの表示名が異なること"
        )

        let pmEnvMap = pmConfig.EnvironmentVars |> Map.ofList
        let pdmEnvMap = pdmConfig.EnvironmentVars |> Map.ofList
        Assert.That(pmEnvMap.["PM_FOCUS"], Is.Not.EqualTo(pdmEnvMap.["PM_FOCUS"]), "PM_FOCUSが異なること")
        Assert.That(pmEnvMap.["PM_PERSPECTIVE"], Is.Not.EqualTo(pdmEnvMap.["PM_PERSPECTIVE"]), "PM_PERSPECTIVEが異なること")
        Assert.That(pmEnvMap.["PM_METHODOLOGY"], Is.Not.EqualTo(pdmEnvMap.["PM_METHODOLOGY"]), "PM_METHODOLOGYが異なること")

    [<Test>]
    member _.``PMプロンプト内容品質テスト``() =
        // Arrange & Act
        let pmConfig = getPMPromptConfig ProjectManager
        let pdmConfig = getPMPromptConfig ProductManager

        // Assert - システムプロンプトの長さと内容の品質チェック
        Assert.That(pmConfig.SystemPrompt.Length, Is.GreaterThan(300), "ProjectManagerシステムプロンプトが十分な長さであること")
        Assert.That(pdmConfig.SystemPrompt.Length, Is.GreaterThan(300), "ProductManagerシステムプロンプトが十分な長さであること")

        // 専門用語の存在確認
        Assert.That(pmConfig.SystemPrompt, Does.Contain("プロジェクト"), "ProjectManagerが適切な専門用語を含むこと")
        Assert.That(pdmConfig.SystemPrompt, Does.Contain("プロダクト"), "ProductManagerが適切な専門用語を含むこと")

        // 日本語応答要求の確認
        Assert.That(pmConfig.SystemPrompt, Does.Contain("日本語"), "ProjectManagerが日本語応答要求を含むこと")
        Assert.That(pdmConfig.SystemPrompt, Does.Contain("日本語"), "ProductManagerが日本語応答要求を含むこと")

        // 環境変数数の確認
        Assert.That(pmConfig.EnvironmentVars.Length, Is.GreaterThan(4), "ProjectManagerが十分な環境変数を持つこと")
        Assert.That(pdmConfig.EnvironmentVars.Length, Is.GreaterThan(4), "ProductManagerが十分な環境変数を持つこと")

    [<Test>]
    member _.``PM環境変数統合テスト``() =
        // Arrange
        let pmEnvVars = getPMEnvironmentVariables ProjectManager
        let pdmEnvVars = getPMEnvironmentVariables ProductManager

        // Act - 環境変数数と必須項目の確認
        let pmEnvCount = pmEnvVars.Length
        let pdmEnvCount = pdmEnvVars.Length

        // Assert
        Assert.That(pmEnvCount, Is.EqualTo(5), "ProjectManagerが正確な環境変数数を持つこと")
        Assert.That(pdmEnvCount, Is.EqualTo(5), "ProductManagerが正確な環境変数数を持つこと")

        // 必須環境変数の存在確認
        let pmKeys = pmEnvVars |> List.map fst |> Set.ofList
        let pdmKeys = pdmEnvVars |> List.map fst |> Set.ofList

        let requiredKeys =
            Set.ofList
                [ "CLAUDE_ROLE"
                  "PM_FOCUS"
                  "PM_PERSPECTIVE"
                  "PM_TEAM_SIZE"
                  "PM_METHODOLOGY" ]

        Assert.That(Set.isSubset requiredKeys pmKeys, Is.True, "ProjectManagerが必須環境変数をすべて含むこと")
        Assert.That(Set.isSubset requiredKeys pdmKeys, Is.True, "ProductManagerが必須環境変数をすべて含むこと")

    [<Test>]
    member _.``PMログ機能テスト``() =
        // Arrange
        let pmRole = ProjectManager
        let pdmRole = ProductManager

        // Act & Assert - ログ出力機能がクラッシュしないことを確認
        Assert.DoesNotThrow(fun () -> logPMPromptApplication "pm" pmRole)
        Assert.DoesNotThrow(fun () -> logPMPromptApplication "timeline" pdmRole)

[<TestFixture>]
[<Category("Integration")>]
type PMPromptIntegrationTests() =

    [<Test>]
    member _.``PM設定とClaudeCodeProcess統合テスト``() =
        // Arrange - PM役割の設定取得をシミュレート
        let pmRole = getPMRoleFromPaneId "pm"
        let timelineRole = getPMRoleFromPaneId "timeline"

        // Act & Assert
        match pmRole with
        | Some role ->
            let config = getPMPromptConfig role
            let envVars = getPMEnvironmentVariables role

            // 統合テスト: 設定が完全かつ一貫していること
            Assert.That(config.SystemPrompt, Is.Not.Empty, "PM統合設定のシステムプロンプトが有効であること")
            Assert.That(envVars.Length, Is.GreaterThan(0), "PM環境変数が設定されていること")
            Assert.That(config.DisplayName, Is.Not.Empty, "PM表示名が設定されていること")
        | None -> Assert.Fail("pmペインからPM役割が特定できない")

        match timelineRole with
        | Some role ->
            let config = getPMPromptConfig role
            let envVars = getPMEnvironmentVariables role

            Assert.That(config.SystemPrompt, Is.Not.Empty, "timeline統合設定のシステムプロンプトが有効であること")
            Assert.That(envVars.Length, Is.GreaterThan(0), "timeline環境変数が設定されていること")
        | None -> Assert.Fail("timelineペインからPM役割が特定できない")

    [<Test>]
    member _.``PM設定ログ出力統合テスト``() =
        // Arrange
        let pmRole = ProjectManager
        let pdmRole = ProductManager

        // Act & Assert - ログ出力機能がクラッシュしないことを確認
        Assert.DoesNotThrow(fun () -> logPMPromptApplication "pm" pmRole)
        Assert.DoesNotThrow(fun () -> logPMPromptApplication "PM / PdM タイムライン" pdmRole)

    [<Test>]
    member _.``全PM設定の整合性テスト``() =
        // Arrange
        let allPMRoles = [ ProjectManager; ProductManager ]

        // Act & Assert - 全PM設定が整合性を持つことを確認
        allPMRoles
        |> List.iter (fun role ->
            let config = getPMPromptConfig role
            let envVars = getPMEnvironmentVariables role
            let displayName = getPMRoleDisplayName role

            // 設定の整合性確認
            Assert.That(config.SystemPrompt, Is.Not.Empty, $"{displayName}システムプロンプトが設定されていること")
            Assert.That(envVars |> List.map fst, Does.Contain("CLAUDE_ROLE"), $"{displayName}がCLAUDE_ROLE環境変数を含むこと")
            Assert.That(displayName, Is.Not.Empty, $"{displayName}表示名が設定されていること")
            Assert.That(config.DisplayName, Is.EqualTo(displayName), $"{displayName}設定と表示名が一致すること"))

    [<Test>]
    member _.``PM役割とQA/UX役割の独立性テスト``() =
        // Arrange - 他役割との重複がないことを確認
        let pmPaneIds = [ "pm"; "timeline"; "PM"; "PM / PdM タイムライン" ]
        let nonPmPaneIds = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "chat"; "会話" ]

        // Act & Assert
        pmPaneIds
        |> List.iter (fun paneId ->
            Assert.That(getPMRoleFromPaneId paneId, Is.Not.EqualTo(None), $"{paneId}がPM役割として認識されること"))

        nonPmPaneIds
        |> List.iter (fun paneId ->
            Assert.That(getPMRoleFromPaneId paneId, Is.EqualTo(None), $"{paneId}がPM役割として認識されないこと"))

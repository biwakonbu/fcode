module FCode.Tests.UXPromptManagerTests

open NUnit.Framework
open FCode.UXPromptManager
open FCode.Tests.TestHelpers

[<TestFixture>]
[<Category("Unit")>]
type UXPromptManagerTests() =

    [<SetUp>]
    member _.Setup() = initializeTerminalGui ()

    [<TearDown>]
    member _.TearDown() = shutdownTerminalGui ()

    [<Test>]
    member _.``UX役割定義テスト``() =
        // Arrange & Act
        let uxConfig = getUXPromptConfig UX

        // Assert
        Assert.AreEqual(UX, uxConfig.Role, "UX設定の役割が正しいこと")
        Assert.IsNotEmpty(uxConfig.SystemPrompt, "UXシステムプロンプトが設定されていること")

    [<Test>]
    member _.``UX専用設定内容テスト``() =
        // Arrange & Act
        let uxConfig = getUXPromptConfig UX

        // Assert - UXはユーザビリティ・UI/UXデザイン専門
        Assert.That(uxConfig.SkillFocus, Does.Contain("ユーザビリティ設計"), "ユーザビリティ設計スキルが含まれること")
        Assert.That(uxConfig.SkillFocus, Does.Contain("ワイヤーフレーム作成"), "ワイヤーフレーム作成スキルが含まれること")
        Assert.That(uxConfig.SkillFocus, Does.Contain("アクセシビリティ"), "アクセシビリティスキルが含まれること")
        Assert.That(uxConfig.OutputFormat, Does.Contain("ワイヤーフレーム"), "ワイヤーフレーム形式が指定されていること")
        Assert.AreEqual("ユーザー中心・反復改善・データ駆動", uxConfig.DesignApproach, "デザインアプローチが正しいこと")

        // システムプロンプト内容確認
        Assert.That(uxConfig.SystemPrompt, Does.Contain("ユーザビリティ"), "システムプロンプトにユーザビリティが含まれること")
        Assert.That(uxConfig.SystemPrompt, Does.Contain("アクセシビリティ"), "アクセシビリティが含まれること")
        Assert.That(uxConfig.SystemPrompt, Does.Contain("ワイヤーフレーム"), "ワイヤーフレームが含まれること")

    [<Test>]
    member _.``ペインIDからUX役割特定テスト``() =
        // Arrange & Act & Assert
        Assert.AreEqual(Some UX, getUXRoleFromPaneId "ux", "uxペインがUX役割に特定されること")
        Assert.AreEqual(Some UX, getUXRoleFromPaneId "UX", "大文字UXも認識されること")

        // 非UXペインはNoneを返すこと
        Assert.AreEqual(None, getUXRoleFromPaneId "dev1", "dev1ペインはUX役割でないこと")
        Assert.AreEqual(None, getUXRoleFromPaneId "qa1", "qa1ペインはUX役割でないこと")
        Assert.AreEqual(None, getUXRoleFromPaneId "pm", "pmペインはUX役割でないこと")

    [<Test>]
    member _.``UX専用環境変数設定テスト``() =
        // Arrange & Act
        let uxEnvVars = getUXEnvironmentVariables UX

        // Assert - 共通環境変数
        let uxEnvMap = uxEnvVars |> Map.ofList

        Assert.AreEqual("ux", uxEnvMap.["CLAUDE_ROLE"], "UXのCLAUDE_ROLEが設定されること")
        Assert.AreEqual("enabled", uxEnvMap.["UX_MODE"], "UXのUX_MODEが設定されること")
        Assert.AreEqual("user_experience", uxEnvMap.["DESIGN_FOCUS"], "DESIGN_FOCUSが設定されること")

        // Assert - UX固有環境変数
        Assert.AreEqual("ui_ux_design", uxEnvMap.["UX_SPECIALIZATION"], "UX専門化設定")
        Assert.That(uxEnvMap.["UX_FOCUS_AREA"], Does.Contain("usability"), "UXフォーカス領域")
        Assert.That(uxEnvMap.["UX_FOCUS_AREA"], Does.Contain("accessibility"), "UXアクセシビリティ領域")
        Assert.That(uxEnvMap.["UX_OUTPUT_FORMAT"], Does.Contain("wireframe"), "UX出力形式")

    [<Test>]
    member _.``UX役割表示名テスト``() =
        // Arrange & Act & Assert
        Assert.AreEqual("UX (UI/UXデザイン)", getUXRoleDisplayName UX, "UX表示名が正しいこと")

    [<Test>]
    member _.``UX設定検証機能テスト``() =
        // Arrange
        let validUXConfig = getUXPromptConfig UX

        let invalidConfig =
            { Role = UX
              SystemPrompt = "" // 無効な空文字列
              PersonalityTraits = [] // 無効な空リスト
              SkillFocus = [] // 無効な空リスト
              OutputFormat = "" // 無効な空文字列
              DesignApproach = "test" }

        // Act & Assert
        Assert.IsTrue(validateUXPromptConfig validUXConfig, "有効なUX設定は検証に通ること")
        Assert.IsFalse(validateUXPromptConfig invalidConfig, "無効な設定は検証に失敗すること")

    [<Test>]
    member _.``UXプロンプト内容品質テスト``() =
        // Arrange & Act
        let uxConfig = getUXPromptConfig UX

        // Assert - システムプロンプトの長さと内容の品質チェック
        Assert.Greater(uxConfig.SystemPrompt.Length, 200, "UXシステムプロンプトが十分な長さであること")

        // 専門用語の存在確認
        Assert.That(uxConfig.SystemPrompt, Does.Contain("ユーザビリティ"), "UXが適切な専門用語を含むこと")
        Assert.That(uxConfig.SystemPrompt, Does.Contain("ワイヤーフレーム"), "UXがワイヤーフレーム用語を含むこと")
        Assert.That(uxConfig.SystemPrompt, Does.Contain("アクセシビリティ"), "UXがアクセシビリティ用語を含むこと")

        // スキル重点項目数の確認
        Assert.Greater(uxConfig.SkillFocus.Length, 3, "UXが十分なスキル重点項目を持つこと")

    [<Test>]
    member _.``UX環境変数統合テスト``() =
        // Arrange
        let uxEnvVars = getUXEnvironmentVariables UX

        // Act - 環境変数数と必須項目の確認
        let uxEnvCount = uxEnvVars.Length

        // Assert
        Assert.Greater(uxEnvCount, 5, "UXが十分な環境変数を持つこと")

        // 必須環境変数の存在確認
        let uxKeys = uxEnvVars |> List.map fst |> Set.ofList

        let requiredKeys =
            Set.ofList
                [ "CLAUDE_ROLE"
                  "UX_MODE"
                  "UX_SPECIALIZATION"
                  "UX_FOCUS_AREA"
                  "UX_OUTPUT_FORMAT" ]

        Assert.IsTrue(Set.isSubset requiredKeys uxKeys, "UXが必須環境変数をすべて含むこと")

[<TestFixture>]
[<Category("Integration")>]
type UXPromptIntegrationTests() =

    [<Test>]
    member _.``UX設定とClaudeCodeProcess統合テスト``() =
        // Arrange - UX役割の設定取得をシミュレート
        let uxRole = getUXRoleFromPaneId "ux"

        // Act & Assert
        match uxRole with
        | Some role ->
            let config = getUXPromptConfig role
            let envVars = getUXEnvironmentVariables role

            // 統合テスト: 設定が完全かつ一貫していること
            Assert.IsTrue(validateUXPromptConfig config, "UX統合設定が有効であること")
            Assert.Greater(envVars.Length, 0, "UX環境変数が設定されていること")
        | None -> Assert.Fail("uxペインからUX役割が特定できない")

    [<Test>]
    member _.``UX設定ログ出力統合テスト``() =
        // Arrange
        let uxRole = UX

        // Act & Assert - ログ出力機能がクラッシュしないことを確認
        Assert.DoesNotThrow(fun () -> logUXPromptApplication "ux" uxRole)

    [<Test>]
    member _.``UX設定の整合性テスト``() =
        // Arrange
        let uxRole = UX

        // Act & Assert - UX設定が整合性を持つことを確認
        let config = getUXPromptConfig uxRole
        let envVars = getUXEnvironmentVariables uxRole
        let displayName = getUXRoleDisplayName uxRole

        // 設定の整合性確認
        Assert.IsTrue(validateUXPromptConfig config, $"{displayName}設定が有効であること")
        Assert.That(envVars |> List.map fst, Does.Contain("CLAUDE_ROLE"), $"{displayName}がCLAUDE_ROLE環境変数を含むこと")
        Assert.IsNotEmpty(displayName, $"{displayName}表示名が設定されていること")

    [<Test>]
    member _.``dev・qa・uxペインの役割分離統合テスト``() =
        // Arrange & Act - dev1-3/qa1-2ペインはUX設定対象外であることを確認
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

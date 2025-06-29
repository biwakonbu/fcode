module FCode.Tests.QAPromptManagerTests

open NUnit.Framework
open FCode.QAPromptManager
open FCode.Tests.TestHelpers

[<TestFixture>]
[<Category("Unit")>]
type QAPromptManagerTests() =

    [<SetUp>]
    member _.Setup() = initializeTerminalGui ()

    [<TearDown>]
    member _.TearDown() = shutdownTerminalGui ()

    [<Test>]
    member _.``QA役割定義テスト``() =
        // Arrange & Act
        let qa1Config = getQAPromptConfig QA1
        let qa2Config = getQAPromptConfig QA2

        // Assert
        Assert.That(qa1Config.Role, Is.EqualTo(QA1), "QA1設定の役割が正しいこと")
        Assert.That(qa2Config.Role, Is.EqualTo(QA2), "QA2設定の役割が正しいこと")

        Assert.That(qa1Config.SystemPrompt, Is.Not.Empty, "QA1システムプロンプトが設定されていること")
        Assert.That(qa2Config.SystemPrompt, Is.Not.Empty, "QA2システムプロンプトが設定されていること")

    [<Test>]
    member _.``QA1専用設定内容テスト``() =
        // Arrange & Act
        let qa1Config = getQAPromptConfig QA1

        // Assert - QA1はテスト戦略専門
        Assert.That(qa1Config.SkillFocus, Does.Contain("テスト戦略設計"), "テスト戦略設計スキルが含まれること")
        Assert.That(qa1Config.SkillFocus, Does.Contain("自動化計画"), "自動化計画スキルが含まれること")
        Assert.That(qa1Config.OutputFormat, Does.Contain("Given-When-Then"), "Given-When-Then形式が指定されていること")
        Assert.That(qa1Config.TestingApproach, Is.EqualTo("戦略的・計画的・包括的"), "テストアプローチが正しいこと")

        // システムプロンプト内容確認
        Assert.That(qa1Config.SystemPrompt, Does.Contain("テスト戦略"), "システムプロンプトにテスト戦略が含まれること")
        Assert.That(qa1Config.SystemPrompt, Does.Contain("境界値分析"), "境界値分析が含まれること")

    [<Test>]
    member _.``QA2専用設定内容テスト``() =
        // Arrange & Act
        let qa2Config = getQAPromptConfig QA2

        // Assert - QA2は品質分析・バグ検出専門
        Assert.That(qa2Config.SkillFocus, Does.Contain("コードレビュー"), "コードレビュースキルが含まれること")
        Assert.That(qa2Config.SkillFocus, Does.Contain("バグ検出"), "バグ検出スキルが含まれること")
        Assert.That(qa2Config.SkillFocus, Does.Contain("セキュリティテスト"), "セキュリティテストスキルが含まれること")
        Assert.That(qa2Config.OutputFormat, Does.Contain("バグレポート"), "バグレポート形式が指定されていること")
        Assert.That(qa2Config.TestingApproach, Is.EqualTo("探索的・分析的・詳細検証"), "テストアプローチが正しいこと")

        // システムプロンプト内容確認
        Assert.That(qa2Config.SystemPrompt, Does.Contain("品質分析"), "システムプロンプトに品質分析が含まれること")
        Assert.That(qa2Config.SystemPrompt, Does.Contain("セキュリティ"), "セキュリティが含まれること")

    [<Test>]
    member _.``ペインIDからQA役割特定テスト``() =
        // Arrange & Act & Assert
        Assert.That(getQARoleFromPaneId "qa1", Is.EqualTo(Some QA1), "qa1ペインがQA1役割に特定されること")
        Assert.That(getQARoleFromPaneId "qa2", Is.EqualTo(Some QA2), "qa2ペインがQA2役割に特定されること")
        Assert.That(getQARoleFromPaneId "QA1", Is.EqualTo(Some QA1), "大文字QA1も認識されること")
        Assert.That(getQARoleFromPaneId "QA2", Is.EqualTo(Some QA2), "大文字QA2も認識されること")

        // 非QAペインはNoneを返すこと
        Assert.That(getQARoleFromPaneId "dev1", Is.EqualTo(None), "dev1ペインはQA役割でないこと")
        Assert.That(getQARoleFromPaneId "ux", Is.EqualTo(None), "uxペインはQA役割でないこと")
        Assert.That(getQARoleFromPaneId "pm", Is.EqualTo(None), "pmペインはQA役割でないこと")

    [<Test>]
    member _.``QA専用環境変数設定テスト``() =
        // Arrange & Act
        let qa1EnvVars = getQAEnvironmentVariables QA1
        let qa2EnvVars = getQAEnvironmentVariables QA2

        // Assert - 共通環境変数
        let qa1EnvMap = qa1EnvVars |> Map.ofList
        let qa2EnvMap = qa2EnvVars |> Map.ofList

        Assert.That(qa1EnvMap.["CLAUDE_ROLE"], Is.EqualTo("qa"), "QA1のCLAUDE_ROLEが設定されること")
        Assert.That(qa2EnvMap.["CLAUDE_ROLE"], Is.EqualTo("qa"), "QA2のCLAUDE_ROLEが設定されること")
        Assert.That(qa1EnvMap.["QA_MODE"], Is.EqualTo("enabled"), "QA1のQA_MODEが設定されること")
        Assert.That(qa2EnvMap.["QA_MODE"], Is.EqualTo("enabled"), "QA2のQA_MODEが設定されること")

        // Assert - QA1固有環境変数
        Assert.That(qa1EnvMap.["QA_SPECIALIZATION"], Is.EqualTo("test_strategy"), "QA1専門化設定")
        Assert.That(qa1EnvMap.["QA_FOCUS_AREA"], Does.Contain("test_planning"), "QA1フォーカス領域")
        Assert.That(qa1EnvMap.["QA_OUTPUT_FORMAT"], Is.EqualTo("given_when_then"), "QA1出力形式")

        // Assert - QA2固有環境変数
        Assert.That(qa2EnvMap.["QA_SPECIALIZATION"], Is.EqualTo("quality_analysis"), "QA2専門化設定")
        Assert.That(qa2EnvMap.["QA_FOCUS_AREA"], Does.Contain("code_review"), "QA2フォーカス領域")
        Assert.That(qa2EnvMap.["QA_OUTPUT_FORMAT"], Does.Contain("bug_report"), "QA2出力形式")

    [<Test>]
    member _.``QA役割表示名テスト``() =
        // Arrange & Act & Assert
        Assert.That(getQARoleDisplayName QA1, Is.EqualTo("QA1 (テスト戦略)"), "QA1表示名が正しいこと")
        Assert.That(getQARoleDisplayName QA2, Is.EqualTo("QA2 (品質分析)"), "QA2表示名が正しいこと")

    [<Test>]
    member _.``QA設定検証機能テスト``() =
        // Arrange
        let validQA1Config = getQAPromptConfig QA1
        let validQA2Config = getQAPromptConfig QA2

        let invalidConfig =
            { Role = QA1
              SystemPrompt = "" // 無効な空文字列
              PersonalityTraits = [] // 無効な空リスト
              SkillFocus = [] // 無効な空リスト
              OutputFormat = "" // 無効な空文字列
              TestingApproach = "test" }

        // Act & Assert
        Assert.That(validateQAPromptConfig validQA1Config, Is.True, "有効なQA1設定は検証に通ること")
        Assert.That(validateQAPromptConfig validQA2Config, Is.True, "有効なQA2設定は検証に通ること")
        Assert.That(validateQAPromptConfig invalidConfig, Is.False, "無効な設定は検証に失敗すること")

    [<Test>]
    member _.``QA設定の一意性テスト``() =
        // Arrange & Act
        let qa1Config = getQAPromptConfig QA1
        let qa2Config = getQAPromptConfig QA2

        // Assert - QA1とQA2の設定が異なることを確認
        Assert.That(qa1Config.SystemPrompt, Is.Not.EqualTo(qa2Config.SystemPrompt), "QA1とQA2のシステムプロンプトが異なること")
        Assert.That(qa1Config.SkillFocus, Is.Not.EqualTo(qa2Config.SkillFocus), "QA1とQA2のスキル重点が異なること")
        Assert.That(qa1Config.OutputFormat, Is.Not.EqualTo(qa2Config.OutputFormat), "QA1とQA2の出力形式が異なること")
        Assert.That(qa1Config.TestingApproach, Is.Not.EqualTo(qa2Config.TestingApproach), "QA1とQA2のアプローチが異なること")

    [<Test>]
    member _.``QAプロンプト内容品質テスト``() =
        // Arrange & Act
        let qa1Config = getQAPromptConfig QA1
        let qa2Config = getQAPromptConfig QA2

        // Assert - システムプロンプトの長さと内容の品質チェック
        Assert.That(qa1Config.SystemPrompt.Length, Is.GreaterThan(200), "QA1システムプロンプトが十分な長さであること")
        Assert.That(qa2Config.SystemPrompt.Length, Is.GreaterThan(200), "QA2システムプロンプトが十分な長さであること")

        // 専門用語の存在確認
        Assert.That(qa1Config.SystemPrompt, Does.Contain("テストケース"), "QA1が適切な専門用語を含むこと")
        Assert.That(qa2Config.SystemPrompt, Does.Contain("コードレビュー"), "QA2が適切な専門用語を含むこと")

        // スキル重点項目数の確認
        Assert.That(qa1Config.SkillFocus.Length, Is.GreaterThan(3), "QA1が十分なスキル重点項目を持つこと")
        Assert.That(qa2Config.SkillFocus.Length, Is.GreaterThan(3), "QA2が十分なスキル重点項目を持つこと")

    [<Test>]
    member _.``QA環境変数統合テスト``() =
        // Arrange
        let qa1EnvVars = getQAEnvironmentVariables QA1
        let qa2EnvVars = getQAEnvironmentVariables QA2

        // Act - 環境変数数と必須項目の確認
        let qa1EnvCount = qa1EnvVars.Length
        let qa2EnvCount = qa2EnvVars.Length

        // Assert
        Assert.That(qa1EnvCount, Is.GreaterThan(5), "QA1が十分な環境変数を持つこと")
        Assert.That(qa2EnvCount, Is.GreaterThan(5), "QA2が十分な環境変数を持つこと")

        // 必須環境変数の存在確認
        let qa1Keys = qa1EnvVars |> List.map fst |> Set.ofList
        let qa2Keys = qa2EnvVars |> List.map fst |> Set.ofList

        let requiredKeys =
            Set.ofList
                [ "CLAUDE_ROLE"
                  "QA_MODE"
                  "QA_SPECIALIZATION"
                  "QA_FOCUS_AREA"
                  "QA_OUTPUT_FORMAT" ]

        Assert.That(Set.isSubset requiredKeys qa1Keys, Is.True, "QA1が必須環境変数をすべて含むこと")
        Assert.That(Set.isSubset requiredKeys qa2Keys, Is.True, "QA2が必須環境変数をすべて含むこと")

[<TestFixture>]
[<Category("Integration")>]
type QAPromptIntegrationTests() =

    [<Test>]
    member _.``QA設定とClaudeCodeProcess統合テスト``() =
        // Arrange - QA役割の設定取得をシミュレート
        let qa1Role = getQARoleFromPaneId "qa1"
        let qa2Role = getQARoleFromPaneId "qa2"

        // Act & Assert
        match qa1Role with
        | Some role ->
            let config = getQAPromptConfig role
            let envVars = getQAEnvironmentVariables role

            // 統合テスト: 設定が完全かつ一貫していること
            Assert.That(validateQAPromptConfig config, Is.True, "QA1統合設定が有効であること")
            Assert.That(envVars.Length, Is.GreaterThan(0), "QA1環境変数が設定されていること")
        | None -> Assert.Fail("qa1ペインからQA役割が特定できない")

        match qa2Role with
        | Some role ->
            let config = getQAPromptConfig role
            let envVars = getQAEnvironmentVariables role

            Assert.That(validateQAPromptConfig config, Is.True, "QA2統合設定が有効であること")
            Assert.That(envVars.Length, Is.GreaterThan(0), "QA2環境変数が設定されていること")
        | None -> Assert.Fail("qa2ペインからQA役割が特定できない")

    [<Test>]
    member _.``QA設定ログ出力統合テスト``() =
        // Arrange
        let qa1Role = QA1
        let qa2Role = QA2

        // Act & Assert - ログ出力機能がクラッシュしないことを確認
        Assert.DoesNotThrow(fun () -> logQAPromptApplication "qa1" qa1Role)
        Assert.DoesNotThrow(fun () -> logQAPromptApplication "qa2" qa2Role)

    [<Test>]
    member _.``全QA設定の整合性テスト``() =
        // Arrange
        let allQARoles = [ QA1; QA2 ]

        // Act & Assert - 全QA設定が整合性を持つことを確認
        allQARoles
        |> List.iter (fun role ->
            let config = getQAPromptConfig role
            let envVars = getQAEnvironmentVariables role
            let displayName = getQARoleDisplayName role

            // 設定の整合性確認
            Assert.That(validateQAPromptConfig config, Is.True, $"{displayName}設定が有効であること")
            Assert.That(envVars |> List.map fst, Does.Contain("CLAUDE_ROLE"), $"{displayName}がCLAUDE_ROLE環境変数を含むこと")
            Assert.That(displayName, Is.Not.Empty, $"{displayName}表示名が設定されていること"))

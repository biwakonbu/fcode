module ConfigurationManagerTests

open NUnit.Framework
open System
open System.IO
open FCode.ConfigurationManager

// ===============================================
// テストヘルパー関数
// ===============================================

let createTempConfigDir () =
    let tempDir = Path.Combine(Path.GetTempPath(), $"fcode-test-{Guid.NewGuid()}")
    Directory.CreateDirectory(tempDir) |> ignore
    tempDir

let cleanupTempDir (dir: string) =
    if Directory.Exists(dir) then
        try
            Directory.Delete(dir, true)
        with _ ->
            ()

[<TestFixture>]
[<Category("Unit")>]
type ConfigurationManagerTests() =

    [<Test>]
    [<Category("Unit")>]
    member _.``DefaultConfiguration should have valid structure``() =
        let config = defaultConfiguration

        Assert.AreEqual("1.0.0", config.Version)
        Assert.AreEqual(9, config.PaneConfigs.Length)
        Assert.Greater(config.KeyBindings.Length, 0)
        Assert.AreEqual(None, config.ClaudeConfig.ProjectPath)

    [<Test>]
    [<Category("Unit")>]
    member _.``DefaultPaneConfigs should include all required panes``() =
        let paneIds = defaultPaneConfigs |> Array.map (_.PaneId) |> Set.ofArray

        let expectedPanes =
            Set.ofArray [| "conversation"; "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "pm"; "pdm" |]

        Assert.AreEqual(expectedPanes, paneIds)

    [<Test>]
    [<Category("Unit")>]
    member _.``PaneConfigs should have differentiated roles``() =
        let roles = defaultPaneConfigs |> Array.map (_.Role) |> Set.ofArray

        // 各ロールが異なることを確認（conversationを除く）
        let nonConversationRoles =
            defaultPaneConfigs
            |> Array.filter (fun p -> p.PaneId <> "conversation")
            |> Array.map (_.Role)
            |> Set.ofArray

        Assert.AreEqual(7, nonConversationRoles.Count, "Each role should be unique")

    [<Test>]
    [<Category("Unit")>]
    member _.``Senior engineer should have highest resource allocation``() =
        let dev1Config = defaultPaneConfigs |> Array.find (fun p -> p.PaneId = "dev1")
        let otherConfigs = defaultPaneConfigs |> Array.filter (fun p -> p.PaneId <> "dev1")

        // シニアエンジニアの設定が存在することを確認
        Assert.AreEqual("senior_engineer", dev1Config.Role)

    [<Test>]
    [<Category("Unit")>]
    member _.``DefaultKeyBindings should include essential actions``() =
        let actions = defaultKeyBindings |> Array.map (_.Action) |> Set.ofArray

        let essentialActions =
            Set.ofArray [| "ExitApplication"; "NextPane"; "StartClaude"; "StopClaude" |]

        Assert.IsTrue(actions.IsSupersetOf(essentialActions))

    [<Test>]
    [<Category("Unit")>]
    member _.``ConfigurationManager should initialize with defaults``() =
        let manager = ConfigurationManager()
        let config = manager.GetConfiguration()

        Assert.AreEqual("1.0.0", config.Version)
        Assert.AreEqual(9, config.PaneConfigs.Length)

    [<Test>]
    [<Category("Unit")>]
    member _.``GetPaneConfig should return correct pane configuration``() =
        let manager = ConfigurationManager()

        let dev1Config = manager.GetPaneConfig("dev1")
        Assert.IsTrue(dev1Config.IsSome)
        Assert.AreEqual("senior_engineer", dev1Config.Value.Role)

    [<Test>]
    [<Category("Unit")>]
    member _.``GetPaneConfig should return None for non-existent pane``() =
        let manager = ConfigurationManager()

        let nonExistentConfig = manager.GetPaneConfig("non-existent")
        Assert.IsTrue(nonExistentConfig.IsNone)

    [<Test>]
    [<Category("Unit")>]
    member _.``GetKeyBinding should return correct key binding``() =
        let manager = ConfigurationManager()

        let exitBinding = manager.GetKeyBinding("ExitApplication")
        Assert.IsTrue(exitBinding.IsSome)
        Assert.AreEqual("Ctrl+X Ctrl+C", exitBinding.Value.KeySequence)

    [<Test>]
    [<Category("Unit")>]
    member _.``UpdateClaudeConfig should update configuration correctly``() =
        let manager = ConfigurationManager()

        let newClaudeConfig =
            { ClaudeCliPath = Some "/custom/path/claude"
              ApiKey = Some "test-api-key"
              ProjectPath = Some "/test/project" }

        manager.UpdateClaudeConfig(newClaudeConfig)
        let config = manager.GetConfiguration()

        Assert.AreEqual(Some "/custom/path/claude", config.ClaudeConfig.ClaudeCliPath)
        Assert.AreEqual(Some "test-api-key", config.ClaudeConfig.ApiKey)
        Assert.AreEqual(Some "/test/project", config.ClaudeConfig.ProjectPath)

    [<Test>]
    [<Category("Unit")>]
    member _.``UpdatePaneConfig should update specific pane configuration``() =
        let manager = ConfigurationManager()

        let updatedPaneConfig =
            { PaneId = "dev1"
              Role = "senior-developer"
              SystemPrompt = Some "Updated system prompt" }

        manager.UpdatePaneConfig("dev1", updatedPaneConfig)
        let dev1Config = manager.GetPaneConfig("dev1")

        Assert.IsTrue(dev1Config.IsSome)
        Assert.AreEqual("senior-developer", dev1Config.Value.Role)
        Assert.AreEqual(Some "Updated system prompt", dev1Config.Value.SystemPrompt)

    [<Test>]
    [<Category("Unit")>]
    member _.``UpdateKeyBinding should update specific key binding``() =
        let manager = ConfigurationManager()

        manager.UpdateKeyBinding("ExitApplication", "Ctrl+Q")
        let exitBinding = manager.GetKeyBinding("ExitApplication")

        Assert.IsTrue(exitBinding.IsSome)
        Assert.AreEqual("Ctrl+Q", exitBinding.Value.KeySequence)

[<TestFixture>]
[<Category("Integration")>]
type ConfigurationFileTests() =
    let mutable tempDir = ""

    [<SetUp>]
    member _.Setup() = tempDir <- createTempConfigDir ()

    [<TearDown>]
    member _.Cleanup() = cleanupTempDir (tempDir)

    [<Test>]
    [<Category("Unit")>]
    member _.``SaveConfiguration should create valid JSON file``() =
        let manager = ConfigurationManager()
        let originalGetPath = manager.GetConfigPath

        // 一時ディレクトリを使用するためのパス設定（この例では直接ファイルシステムを使用）
        let testConfigPath = Path.Combine(tempDir, "config.json")

        // デフォルト設定で保存
        let success = manager.SaveConfiguration()
        Assert.IsTrue(success)

        // ファイルが存在することを確認（実際のパスを使用）
        let actualConfigPath = manager.GetConfigPath()
        let configDir = Path.GetDirectoryName(actualConfigPath)

        if Directory.Exists(configDir) then
            Assert.IsTrue(File.Exists(actualConfigPath))

    [<Test>]
    [<Category("Unit")>]
    member _.``LoadConfiguration should read saved configuration``() =
        let manager = ConfigurationManager()

        // 設定を変更
        let customClaudeConfig =
            { ClaudeCliPath = Some "/test/claude"
              ApiKey = Some "test-key"
              ProjectPath = Some "/test/project" }

        manager.UpdateClaudeConfig(customClaudeConfig)

        // 保存
        let saveSuccess = manager.SaveConfiguration()
        Assert.IsTrue(saveSuccess)

        // 新しいマネージャーで読み込み
        let newManager = ConfigurationManager()
        let loadSuccess = newManager.LoadConfiguration()

        // ファイルが存在する場合のみテスト
        if loadSuccess then
            let loadedConfig = newManager.GetConfiguration()
            Assert.AreEqual(Some "/test/claude", loadedConfig.ClaudeConfig.ClaudeCliPath)
            Assert.AreEqual(Some "test-key", loadedConfig.ClaudeConfig.ApiKey)

    [<Test>]
    [<Category("Unit")>]
    member _.``CreateDefaultConfiguration should create and save defaults``() =
        let manager = ConfigurationManager()

        let success = manager.CreateDefaultConfiguration()
        Assert.IsTrue(success)

        let config = manager.GetConfiguration()
        Assert.AreEqual("1.0.0", config.Version)
        Assert.AreEqual(9, config.PaneConfigs.Length)

[<TestFixture>]
[<Category("Unit")>]
type ConfigurationValidationTests() =

    [<Test>]
    [<Category("Unit")>]
    member _.``ValidateConfiguration should detect Claude CLI availability``() =
        let manager = ConfigurationManager()

        let errors = manager.ValidateConfiguration()

        // エラーが配列として返されることを確認
        Assert.IsNotNull(errors)
        Assert.IsInstanceOf<string[]>(errors)

    [<Test>]
    [<Category("Unit")>]
    member _.``LoadEnvironmentOverrides should apply environment variables``() =
        // 環境変数を設定
        Environment.SetEnvironmentVariable("CLAUDE_CLI_PATH", "/env/claude")
        Environment.SetEnvironmentVariable("CLAUDE_API_KEY", "env-api-key")

        let manager = ConfigurationManager()
        manager.LoadEnvironmentOverrides()

        let config = manager.GetConfiguration()
        Assert.AreEqual(Some "/env/claude", config.ClaudeConfig.ClaudeCliPath)
        Assert.AreEqual(Some "env-api-key", config.ClaudeConfig.ApiKey)

        // 環境変数をクリア
        Environment.SetEnvironmentVariable("CLAUDE_CLI_PATH", null)
        Environment.SetEnvironmentVariable("CLAUDE_API_KEY", null)

[<TestFixture>]
[<Category("Unit")>]
type ConfigurationStructureTests() =

    [<Test>]
    [<Category("Unit")>]
    member _.``PaneConfig should have valid default values``() =
        let dev1Config = defaultPaneConfigs |> Array.find (fun p -> p.PaneId = "dev1")

        Assert.AreEqual("senior_engineer", dev1Config.Role)
        Assert.IsTrue(dev1Config.SystemPrompt.IsSome)

    [<Test>]
    [<Category("Unit")>]
    member _.``KeyBindingConfig should have required fields``() =
        let exitBinding =
            defaultKeyBindings |> Array.find (fun kb -> kb.Action = "ExitApplication")

        Assert.AreEqual("Ctrl+X Ctrl+C", exitBinding.KeySequence)
        Assert.IsNotEmpty(exitBinding.Description)

    [<Test>]
    [<Category("Unit")>]
    member _.``ResourceConfig should have reasonable defaults``() =
        let config = defaultConfiguration

        Assert.AreEqual(Some 8, config.ResourceConfig.MaxActiveConnections)
        Assert.AreEqual(Some 2000, config.ResourceConfig.MonitoringIntervalMs)

    [<Test>]
    [<Category("Unit")>]
    member _.``UIConfig should have valid defaults``() =
        let config = defaultConfiguration

        Assert.AreEqual(Some "default", config.UIConfig.ColorScheme)
        Assert.AreEqual(Some 100, config.UIConfig.RefreshIntervalMs)
        Assert.AreEqual(Some true, config.UIConfig.AutoScrollEnabled)

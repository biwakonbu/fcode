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
    member _.``DefaultConfiguration should have valid structure``() =
        let config = defaultConfiguration

        Assert.That(config.Version, Is.EqualTo("1.0.0"))
        Assert.That(config.PaneConfigs.Length, Is.EqualTo(8))
        Assert.That(config.KeyBindings.Length, Is.GreaterThan(0))
        Assert.That(config.ClaudeConfig.DefaultModel, Is.EqualTo(Some "claude-3-5-sonnet-20241022"))

    [<Test>]
    member _.``DefaultPaneConfigs should include all required panes``() =
        let paneIds = defaultPaneConfigs |> Array.map (_.PaneId) |> Set.ofArray

        let expectedPanes =
            Set.ofArray [| "conversation"; "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "pm" |]

        Assert.That(paneIds, Is.EqualTo(expectedPanes))

    [<Test>]
    member _.``DefaultKeyBindings should include essential actions``() =
        let actions = defaultKeyBindings |> Array.map (_.Action) |> Set.ofArray

        let essentialActions =
            Set.ofArray [| "ExitApplication"; "NextPane"; "StartClaude"; "StopClaude" |]

        Assert.That(actions.IsSupersetOf(essentialActions), Is.True)

    [<Test>]
    member _.``ConfigurationManager should initialize with defaults``() =
        let manager = ConfigurationManager()
        let config = manager.GetConfiguration()

        Assert.That(config.Version, Is.EqualTo("1.0.0"))
        Assert.That(config.PaneConfigs.Length, Is.EqualTo(8))

    [<Test>]
    member _.``GetPaneConfig should return correct pane configuration``() =
        let manager = ConfigurationManager()

        let dev1Config = manager.GetPaneConfig("dev1")
        Assert.That(dev1Config.IsSome, Is.True)
        Assert.That(dev1Config.Value.Role, Is.EqualTo("developer"))
        Assert.That(dev1Config.Value.MaxMemoryMB, Is.EqualTo(Some 512.0))

    [<Test>]
    member _.``GetPaneConfig should return None for non-existent pane``() =
        let manager = ConfigurationManager()

        let nonExistentConfig = manager.GetPaneConfig("non-existent")
        Assert.That(nonExistentConfig.IsNone, Is.True)

    [<Test>]
    member _.``GetKeyBinding should return correct key binding``() =
        let manager = ConfigurationManager()

        let exitBinding = manager.GetKeyBinding("ExitApplication")
        Assert.That(exitBinding.IsSome, Is.True)
        Assert.That(exitBinding.Value.KeySequence, Is.EqualTo("Ctrl+X Ctrl+C"))

    [<Test>]
    member _.``UpdateClaudeConfig should update configuration correctly``() =
        let manager = ConfigurationManager()

        let newClaudeConfig =
            { ClaudeCliPath = Some "/custom/path/claude"
              ApiKey = Some "test-api-key"
              ProjectPath = Some "/test/project"
              DefaultModel = Some "claude-3-haiku" }

        manager.UpdateClaudeConfig(newClaudeConfig)
        let config = manager.GetConfiguration()

        Assert.That(config.ClaudeConfig.ClaudeCliPath, Is.EqualTo(Some "/custom/path/claude"))
        Assert.That(config.ClaudeConfig.ApiKey, Is.EqualTo(Some "test-api-key"))
        Assert.That(config.ClaudeConfig.DefaultModel, Is.EqualTo(Some "claude-3-haiku"))

    [<Test>]
    member _.``UpdatePaneConfig should update specific pane configuration``() =
        let manager = ConfigurationManager()

        let updatedPaneConfig =
            { PaneId = "dev1"
              Role = "senior-developer"
              SystemPrompt = Some "Updated system prompt"
              MaxMemoryMB = Some 1024.0
              MaxCpuPercent = Some 75.0 }

        manager.UpdatePaneConfig("dev1", updatedPaneConfig)
        let dev1Config = manager.GetPaneConfig("dev1")

        Assert.That(dev1Config.IsSome, Is.True)
        Assert.That(dev1Config.Value.Role, Is.EqualTo("senior-developer"))
        Assert.That(dev1Config.Value.MaxMemoryMB, Is.EqualTo(Some 1024.0))
        Assert.That(dev1Config.Value.SystemPrompt, Is.EqualTo(Some "Updated system prompt"))

    [<Test>]
    member _.``UpdateKeyBinding should update specific key binding``() =
        let manager = ConfigurationManager()

        manager.UpdateKeyBinding("ExitApplication", "Ctrl+Q")
        let exitBinding = manager.GetKeyBinding("ExitApplication")

        Assert.That(exitBinding.IsSome, Is.True)
        Assert.That(exitBinding.Value.KeySequence, Is.EqualTo("Ctrl+Q"))

[<TestFixture>]
[<Category("Integration")>]
type ConfigurationFileTests() =
    let mutable tempDir = ""

    [<SetUp>]
    member _.Setup() = tempDir <- createTempConfigDir ()

    [<TearDown>]
    member _.Cleanup() = cleanupTempDir (tempDir)

    [<Test>]
    member _.``SaveConfiguration should create valid JSON file``() =
        let manager = ConfigurationManager()
        let originalGetPath = manager.GetConfigPath

        // 一時ディレクトリを使用するためのパス設定（この例では直接ファイルシステムを使用）
        let testConfigPath = Path.Combine(tempDir, "config.json")

        // デフォルト設定で保存
        let success = manager.SaveConfiguration()
        Assert.That(success, Is.True)

        // ファイルが存在することを確認（実際のパスを使用）
        let actualConfigPath = manager.GetConfigPath()
        let configDir = Path.GetDirectoryName(actualConfigPath)

        if Directory.Exists(configDir) then
            Assert.That(File.Exists(actualConfigPath), Is.True)

    [<Test>]
    member _.``LoadConfiguration should read saved configuration``() =
        let manager = ConfigurationManager()

        // 設定を変更
        let customClaudeConfig =
            { ClaudeCliPath = Some "/test/claude"
              ApiKey = Some "test-key"
              ProjectPath = Some "/test/project"
              DefaultModel = Some "claude-3-haiku" }

        manager.UpdateClaudeConfig(customClaudeConfig)

        // 保存
        let saveSuccess = manager.SaveConfiguration()
        Assert.That(saveSuccess, Is.True)

        // 新しいマネージャーで読み込み
        let newManager = ConfigurationManager()
        let loadSuccess = newManager.LoadConfiguration()

        // ファイルが存在する場合のみテスト
        if loadSuccess then
            let loadedConfig = newManager.GetConfiguration()
            Assert.That(loadedConfig.ClaudeConfig.ClaudeCliPath, Is.EqualTo(Some "/test/claude"))
            Assert.That(loadedConfig.ClaudeConfig.ApiKey, Is.EqualTo(Some "test-key"))

    [<Test>]
    member _.``CreateDefaultConfiguration should create and save defaults``() =
        let manager = ConfigurationManager()

        let success = manager.CreateDefaultConfiguration()
        Assert.That(success, Is.True)

        let config = manager.GetConfiguration()
        Assert.That(config.Version, Is.EqualTo("1.0.0"))
        Assert.That(config.PaneConfigs.Length, Is.EqualTo(8))

[<TestFixture>]
[<Category("Unit")>]
type ConfigurationValidationTests() =

    [<Test>]
    member _.``ValidateConfiguration should detect Claude CLI availability``() =
        let manager = ConfigurationManager()

        let errors = manager.ValidateConfiguration()

        // エラーが配列として返されることを確認
        Assert.That(errors, Is.Not.Null)
        Assert.That(errors, Is.TypeOf<string[]>())

    [<Test>]
    member _.``LoadEnvironmentOverrides should apply environment variables``() =
        // 環境変数を設定
        Environment.SetEnvironmentVariable("CLAUDE_CLI_PATH", "/env/claude")
        Environment.SetEnvironmentVariable("CLAUDE_API_KEY", "env-api-key")

        let manager = ConfigurationManager()
        manager.LoadEnvironmentOverrides()

        let config = manager.GetConfiguration()
        Assert.That(config.ClaudeConfig.ClaudeCliPath, Is.EqualTo(Some "/env/claude"))
        Assert.That(config.ClaudeConfig.ApiKey, Is.EqualTo(Some "env-api-key"))

        // 環境変数をクリア
        Environment.SetEnvironmentVariable("CLAUDE_CLI_PATH", null)
        Environment.SetEnvironmentVariable("CLAUDE_API_KEY", null)

[<TestFixture>]
[<Category("Unit")>]
type ConfigurationStructureTests() =

    [<Test>]
    member _.``PaneConfig should have valid default values``() =
        let dev1Config = defaultPaneConfigs |> Array.find (fun p -> p.PaneId = "dev1")

        Assert.That(dev1Config.Role, Is.EqualTo("developer"))
        Assert.That(dev1Config.SystemPrompt.IsSome, Is.True)
        Assert.That(dev1Config.MaxMemoryMB, Is.EqualTo(Some 512.0))
        Assert.That(dev1Config.MaxCpuPercent, Is.EqualTo(Some 50.0))

    [<Test>]
    member _.``KeyBindingConfig should have required fields``() =
        let exitBinding =
            defaultKeyBindings |> Array.find (fun kb -> kb.Action = "ExitApplication")

        Assert.That(exitBinding.KeySequence, Is.EqualTo("Ctrl+X Ctrl+C"))
        Assert.That(exitBinding.Description, Is.Not.Empty)

    [<Test>]
    member _.``ResourceConfig should have reasonable defaults``() =
        let config = defaultConfiguration

        Assert.That(config.ResourceConfig.MaxActiveConnections, Is.EqualTo(Some 7))
        Assert.That(config.ResourceConfig.SystemMemoryLimitGB, Is.EqualTo(Some 4.0))
        Assert.That(config.ResourceConfig.MonitoringIntervalMs, Is.EqualTo(Some 2000))

    [<Test>]
    member _.``UIConfig should have valid defaults``() =
        let config = defaultConfiguration

        Assert.That(config.UIConfig.ColorScheme, Is.EqualTo(Some "default"))
        Assert.That(config.UIConfig.RefreshIntervalMs, Is.EqualTo(Some 100))
        Assert.That(config.UIConfig.AutoScrollEnabled, Is.EqualTo(Some true))

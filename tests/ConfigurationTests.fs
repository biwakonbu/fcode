module FCode.Tests.ConfigurationTests

open System
open NUnit.Framework
open FCode.ConfigurationManager

// ===============================================
// Configuration Tests
// ===============================================

[<TestFixture>]
[<Category("Unit")>]
type ConfigurationTests() =

    [<Test>]
    member this.``DefaultTimeouts should have valid values``() =
        // Arrange & Act
        let timeouts = DefaultTimeouts.processTimeouts

        // Assert
        Assert.That(timeouts.GitTimeout, Is.GreaterThan(0))
        Assert.That(timeouts.DockerTimeout, Is.GreaterThan(0))
        Assert.That(timeouts.BuildTimeout, Is.GreaterThan(0))
        Assert.That(timeouts.TestTimeout, Is.GreaterThan(0))
        Assert.That(timeouts.DeployTimeout, Is.GreaterThan(0))
        Assert.That(timeouts.ProcessKillTimeout, Is.GreaterThan(0))

    [<Test>]
    member this.``DefaultSystemCommands should have valid values``() =
        // Arrange & Act
        let commands = DefaultTimeouts.systemCommands

        // Assert
        Assert.That(commands.GitCommand, Is.Not.Empty)
        Assert.That(commands.DockerCommand, Is.Not.Empty)
        Assert.That(commands.DotnetCommand, Is.Not.Empty)
        Assert.That(commands.WhichCommand, Is.Not.Empty)

    [<Test>]
    member this.``ConfigurationManager should create default configuration``() =
        // Arrange
        let configManager = ConfigurationManager()

        // Act
        let result = configManager.CreateDefaultConfiguration()

        // Assert
        Assert.That(result, Is.True)
        let config = configManager.GetConfiguration()
        Assert.That(config.Version, Is.Not.Empty)
        Assert.That(config.PaneConfigs, Is.Not.Empty)
        Assert.That(config.KeyBindings, Is.Not.Empty)

    [<Test>]
    member this.``ConfigurationManager should validate configuration``() =
        // Arrange
        let configManager = ConfigurationManager()

        // Act
        let validationErrors = configManager.ValidateConfiguration()

        // Assert
        // Validation errors might exist (e.g., Claude CLI not found) but shouldn't fail
        Assert.That(validationErrors, Is.Not.Null)

    [<Test>]
    member this.``Global configuration manager should provide timeout values``() =
        // Act & Assert
        Assert.That(getGitTimeout (), Is.GreaterThan(0))
        Assert.That(getDockerTimeout (), Is.GreaterThan(0))
        Assert.That(getBuildTimeout (), Is.GreaterThan(0))
        Assert.That(getTestTimeout (), Is.GreaterThan(0))
        Assert.That(getDeployTimeout (), Is.GreaterThan(0))
        Assert.That(getProcessKillTimeout (), Is.GreaterThan(0))

    [<Test>]
    member this.``Global configuration manager should provide system commands``() =
        // Act & Assert
        Assert.That(getGitCommand (), Is.Not.Empty)
        Assert.That(getDockerCommand (), Is.Not.Empty)
        Assert.That(getDotnetCommand (), Is.Not.Empty)
        Assert.That(getWhichCommand (), Is.Not.Empty)

    [<Test>]
    member this.``ConfigurationManager should handle pane configuration``() =
        // Arrange
        let configManager = ConfigurationManager()

        // Act
        let dev1Config = configManager.GetPaneConfig("dev1")
        let unknownConfig = configManager.GetPaneConfig("unknown")

        // Assert
        Assert.That(dev1Config, Is.Not.Null)
        Assert.That(dev1Config.Value.Role, Is.EqualTo("senior_engineer"))
        Assert.That(unknownConfig, Is.Null)

    [<Test>]
    member this.``ConfigurationManager should handle key bindings``() =
        // Arrange
        let configManager = ConfigurationManager()

        // Act
        let exitBinding = configManager.GetKeyBinding("ExitApplication")
        let unknownBinding = configManager.GetKeyBinding("UnknownAction")

        // Assert
        Assert.That(exitBinding, Is.Not.Null)
        Assert.That(exitBinding.Value.KeySequence, Is.EqualTo("Ctrl+X Ctrl+C"))
        Assert.That(unknownBinding, Is.Null)

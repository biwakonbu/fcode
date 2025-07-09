module FCode.Tests.ConfigurationTests

open System
open NUnit.Framework
open FCode.Configuration

// ===============================================
// Configuration Tests
// ===============================================

[<TestFixture>]
[<Category("Unit")>]
type ConfigurationTests() =

    [<Test>]
    member this.``DefaultConfig should have valid values``() =
        // Arrange & Act
        let config = DefaultConfig.applicationConfig

        // Assert
        Assert.That(config.ProcessTimeouts.GitTimeout, Is.GreaterThan(0))
        Assert.That(config.ProcessTimeouts.DockerTimeout, Is.GreaterThan(0))
        Assert.That(config.ProcessTimeouts.BuildTimeout, Is.GreaterThan(0))
        Assert.That(config.ProcessTimeouts.TestTimeout, Is.GreaterThan(0))
        Assert.That(config.ProcessTimeouts.DeployTimeout, Is.GreaterThan(0))
        Assert.That(config.ProcessTimeouts.ProcessKillTimeout, Is.GreaterThan(0))

        Assert.That(config.PortConfig.ApplicationPort, Is.InRange(1024, 65535))
        Assert.That(config.PortConfig.DatabasePort, Is.InRange(1024, 65535))

        Assert.That(config.SecurityConfig.MaxBranchNameLength, Is.GreaterThan(0))
        Assert.That(config.SecurityConfig.AllowedFileExtensions, Is.Not.Empty)
        Assert.That(config.SecurityConfig.DangerousPathPatterns, Is.Not.Empty)
        Assert.That(config.SecurityConfig.MaxPathLength, Is.GreaterThan(0))

        Assert.That(config.AIModelConfig.MaxTokens, Is.GreaterThan(0))
        Assert.That(config.AIModelConfig.CostThreshold, Is.GreaterThan(0.0))
        Assert.That(config.AIModelConfig.TimeoutThreshold, Is.GreaterThan(TimeSpan.Zero))

    [<Test>]
    member this.``ConfigurationManager should update config correctly``() =
        // Arrange
        let originalConfig = ConfigurationManager.Current

        let newTimeouts =
            { GitTimeout = 2000
              DockerTimeout = 3000
              BuildTimeout = 4000
              TestTimeout = 5000
              DeployTimeout = 6000
              ProcessKillTimeout = 500 }

        let newConfig =
            { originalConfig with
                ProcessTimeouts = newTimeouts }

        // Act
        ConfigurationManager.UpdateConfig(newConfig)

        // Assert
        Assert.That(ConfigurationManager.Current.ProcessTimeouts.GitTimeout, Is.EqualTo(2000))
        Assert.That(ConfigurationManager.Current.ProcessTimeouts.DockerTimeout, Is.EqualTo(3000))
        Assert.That(ConfigurationManager.Current.ProcessTimeouts.BuildTimeout, Is.EqualTo(4000))
        Assert.That(ConfigurationManager.Current.ProcessTimeouts.TestTimeout, Is.EqualTo(5000))
        Assert.That(ConfigurationManager.Current.ProcessTimeouts.DeployTimeout, Is.EqualTo(6000))
        Assert.That(ConfigurationManager.Current.ProcessTimeouts.ProcessKillTimeout, Is.EqualTo(500))

        // Cleanup
        ConfigurationManager.UpdateConfig(originalConfig)

    [<Test>]
    member this.``Config helpers should return current values``() =
        // Arrange
        let currentConfig = ConfigurationManager.Current

        // Act & Assert
        Assert.That(Config.getGitTimeout (), Is.EqualTo(currentConfig.ProcessTimeouts.GitTimeout))
        Assert.That(Config.getDockerTimeout (), Is.EqualTo(currentConfig.ProcessTimeouts.DockerTimeout))
        Assert.That(Config.getBuildTimeout (), Is.EqualTo(currentConfig.ProcessTimeouts.BuildTimeout))
        Assert.That(Config.getTestTimeout (), Is.EqualTo(currentConfig.ProcessTimeouts.TestTimeout))
        Assert.That(Config.getDeployTimeout (), Is.EqualTo(currentConfig.ProcessTimeouts.DeployTimeout))
        Assert.That(Config.getProcessKillTimeout (), Is.EqualTo(currentConfig.ProcessTimeouts.ProcessKillTimeout))

        Assert.That(Config.getApplicationPort (), Is.EqualTo(currentConfig.PortConfig.ApplicationPort))
        Assert.That(Config.getDatabasePort (), Is.EqualTo(currentConfig.PortConfig.DatabasePort))
        Assert.That(Config.getMonitoringPort (), Is.EqualTo(currentConfig.PortConfig.MonitoringPort))

        Assert.That(Config.getMaxBranchNameLength (), Is.EqualTo(currentConfig.SecurityConfig.MaxBranchNameLength))
        Assert.That(Config.getAllowedFileExtensions (), Is.EqualTo(currentConfig.SecurityConfig.AllowedFileExtensions))
        Assert.That(Config.getDangerousPathPatterns (), Is.EqualTo(currentConfig.SecurityConfig.DangerousPathPatterns))
        Assert.That(Config.getMaxPathLength (), Is.EqualTo(currentConfig.SecurityConfig.MaxPathLength))

        Assert.That(Config.getDefaultModel (), Is.EqualTo(currentConfig.AIModelConfig.DefaultModel))
        Assert.That(Config.getMaxTokens (), Is.EqualTo(currentConfig.AIModelConfig.MaxTokens))
        Assert.That(Config.getCostThreshold (), Is.EqualTo(currentConfig.AIModelConfig.CostThreshold))
        Assert.That(Config.getTimeoutThreshold (), Is.EqualTo(currentConfig.AIModelConfig.TimeoutThreshold))

    [<Test>]
    member this.``LoadFromEnvironment should handle missing environment variables gracefully``() =
        // Arrange
        let originalConfig = ConfigurationManager.Current

        // Act
        ConfigurationManager.LoadFromEnvironment()

        // Assert - Should not throw and should maintain reasonable values
        let config = ConfigurationManager.Current
        Assert.That(config.ProcessTimeouts.GitTimeout, Is.GreaterThan(0))
        Assert.That(config.ProcessTimeouts.DockerTimeout, Is.GreaterThan(0))
        Assert.That(config.PortConfig.ApplicationPort, Is.InRange(1024, 65535))
        Assert.That(config.PortConfig.DatabasePort, Is.InRange(1024, 65535))

        // Cleanup
        ConfigurationManager.UpdateConfig(originalConfig)

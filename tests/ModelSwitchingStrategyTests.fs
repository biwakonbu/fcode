namespace FCode.Tests

open System
open NUnit.Framework
open FCode.ModelSwitchingStrategy
open FCode.AIModelProvider

[<TestFixture>]
[<Category("Unit")>]
type ModelSwitchingStrategyTests() =

    [<Test>]
    [<Category("Unit")>]
    member this.``ModelSwitchingEngine should recommend model``() =
        async {
            // Arrange
            let config = ModelSwitchingUtils.createDefaultConfiguration ()
            let engine = ModelSwitchingEngine(config)

            // Act
            let! result = engine.RecommendModelSwitch("test task description")

            // Assert
            match result with
            | Result.Ok model -> Assert.AreEqual(Claude3Sonnet, model)
            | Result.Error _ -> Assert.Fail("RecommendModelSwitch should succeed")
        }

    [<Test>]
    [<Category("Unit")>]
    member this.``CurrentModel should return initial model``() =
        // Arrange
        let config = ModelSwitchingUtils.createDefaultConfiguration ()
        let engine = ModelSwitchingEngine(config)

        // Act
        let currentModel = engine.CurrentModel

        // Assert
        Assert.AreEqual(Claude3Sonnet, currentModel)

    [<Test>]
    [<Category("Unit")>]
    member this.``UpdateConfiguration should succeed``() =
        // Arrange
        let config = ModelSwitchingUtils.createDefaultConfiguration ()
        let engine = ModelSwitchingEngine(config)

        let newConfig =
            { config with
                EnableAutoSwitching = false }

        // Act
        let result = engine.UpdateConfiguration(newConfig)

        // Assert
        match result with
        | Result.Ok _ -> Assert.Pass()
        | Result.Error _ -> Assert.Fail("UpdateConfiguration should succeed")

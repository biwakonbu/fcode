module FCode.Tests.MinimalWorkflowTests

open System
open Xunit
open FCode.FullWorkflowCoordinator

/// CI環境判定
let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``MinimalWorkflowCoordinator - 基本作成テスト`` () =
    use coordinator = new FullWorkflowCoordinator()
    Assert.NotNull(coordinator)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``MinimalWorkflowCoordinator - ワークフロー開始テスト`` () =
    async {
        use coordinator = new FullWorkflowCoordinator()
        let instructions = [ "テスト指示1"; "テスト指示2" ]

        let! result = coordinator.StartWorkflow(instructions)

        match result with
        | Result.Ok message -> Assert.Equal("ワークフロー正常完了", message)
        | Result.Error error -> Assert.True(false, sprintf "予期しないエラー: %s" error)
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``MinimalWorkflowCoordinator - 状態取得テスト`` () =
    use coordinator = new FullWorkflowCoordinator()
    let state = coordinator.GetCurrentWorkflowState()
    Assert.Equal(None, state)

[<Fact>]
[<Trait("TestCategory", "Unit")>]
let ``MinimalWorkflowCoordinator - 緊急停止テスト`` () =
    async {
        use coordinator = new FullWorkflowCoordinator()

        let! result = coordinator.EmergencyStop("テスト停止")

        match result with
        | Result.Ok() -> Assert.True(true) // 成功
        | Result.Error error -> Assert.True(false, sprintf "予期しないエラー: %s" error)
    }
    |> Async.RunSynchronously

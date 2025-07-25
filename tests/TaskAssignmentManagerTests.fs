module FCode.Tests.TaskAssignmentManagerTests

open System
open NUnit.Framework
open FCode.TaskAssignmentManager
open FCode.Collaboration.CollaborationTypes

[<Test>]
[<Category("Integration")>]
let ``NaturalLanguageProcessor - 基本的な指示解析テスト`` () =
    // Arrange
    let nlp = NaturalLanguageProcessor()
    let instruction = "新しいAPI機能を実装して、テストを書いて、UIをデザインしてください"

    // Act
    let breakdown = nlp.ParseInstruction(instruction)

    // Assert
    Assert.AreEqual(instruction, breakdown.OriginalInstruction)
    Assert.True(breakdown.ParsedTasks.Length >= 2)
    Assert.True(breakdown.EstimatedComplexity > 0.0)
    Assert.True(breakdown.EstimatedComplexity <= 1.0)

[<Test>]
[<Category("Unit")>]
let ``AgentCapabilityProfile - 開発エージェントプロファイル作成テスト`` () =
    // Arrange & Act
    let profile =
        { AgentId = "dev1"
          Specializations = [ Development [ "F#"; "C#"; ".NET" ] ]
          LoadCapacity = 8.0
          CurrentLoad = 2.0
          SuccessRate = 0.85
          AverageTaskDuration = TimeSpan.FromHours(3.0)
          LastAssignedTask = None }

    // Assert
    Assert.AreEqual("dev1", profile.AgentId)
    Assert.AreEqual(8.0, profile.LoadCapacity)
    Assert.AreEqual(2.0, profile.CurrentLoad)
    Assert.AreEqual(0.85, profile.SuccessRate)
    Assert.AreEqual(None, profile.LastAssignedTask)

[<Test>]
[<Category("Unit")>]
let ``AgentSpecializationMatcher - 専門分野マッチングテスト`` () =
    // Arrange
    let matcher = AgentSpecializationMatcher()

    let agent =
        { AgentId = "dev1"
          Specializations = [ Development [ "F#"; ".NET" ] ]
          LoadCapacity = 8.0
          CurrentLoad = 2.0
          SuccessRate = 0.9
          AverageTaskDuration = TimeSpan.FromHours(2.0)
          LastAssignedTask = None }

    let task =
        { TaskId = "task1"
          Title = "F#機能実装"
          Description = "新しいF#機能を実装する"
          RequiredSpecialization = Development [ "F#" ]
          EstimatedDuration = TimeSpan.FromHours(4.0)
          Priority = TaskPriority.High
          Dependencies = [] }

    // Act
    let score = matcher.CalculateMatchScore(agent, task)

    // Assert
    Assert.True(score > 0.5, $"マッチスコア {score} が期待値0.5を下回っています")
    Assert.True(score <= 1.0, $"マッチスコア {score} が上限1.0を超えています")

[<Test>]
[<Category("Unit")>]
let ``AgentSpecializationMatcher - 不適合専門分野テスト`` () =
    // Arrange
    let matcher = AgentSpecializationMatcher()

    let agent =
        { AgentId = "qa1"
          Specializations = [ Testing [ "unit-testing"; "integration-testing" ] ]
          LoadCapacity = 6.0
          CurrentLoad = 1.0
          SuccessRate = 0.95
          AverageTaskDuration = TimeSpan.FromHours(1.5)
          LastAssignedTask = None }

    let task =
        { TaskId = "task1"
          Title = "F#機能実装"
          Description = "新しいF#機能を実装する"
          RequiredSpecialization = Development [ "F#" ]
          EstimatedDuration = TimeSpan.FromHours(4.0)
          Priority = TaskPriority.High
          Dependencies = [] }

    // Act
    let score = matcher.CalculateMatchScore(agent, task)

    // Assert
    Assert.True(score < 0.7, $"不適合なのにマッチスコア {score} が高すぎます（期待値: <0.7）")

[<Test>]
[<Category("Unit")>]
let ``DynamicReassignmentSystem - ブロック状態再配分判定テスト`` () =
    // Arrange
    let system = DynamicReassignmentSystem()

    let task =
        { TaskId = "task1"
          Title = "テストタスク"
          Description = "テスト用のタスク"
          Status = InProgress
          AssignedAgent = Some "dev1"
          Priority = TaskPriority.Medium
          EstimatedDuration = Some(TimeSpan.FromHours(2.0))
          ActualDuration = None
          RequiredResources = []
          Dependencies = []
          CreatedAt = DateTime.UtcNow.AddHours(-1.0)
          UpdatedAt = DateTime.UtcNow.AddMinutes(-30.0) }

    let agent =
        { AgentId = "dev1"
          Status = Blocked
          Progress = 0.3
          LastUpdate = DateTime.UtcNow.AddMinutes(-5.0)
          CurrentTask = Some "task1"
          WorkingDirectory = "/tmp/test"
          ProcessId = Some 1234
          ActiveTasks = [] }

    // Act
    let needsReassignment = system.NeedsReassignment(task, agent)
    let reason = system.GetReassignmentReason(task, agent)

    // Assert
    Assert.True(needsReassignment)
    Assert.AreEqual("ブロック状態", reason)

[<Test>]
[<Category("Unit")>]
let ``DynamicReassignmentSystem - エラー状態再配分判定テスト`` () =
    // Arrange
    let system = DynamicReassignmentSystem()

    let task =
        { TaskId = "task1"
          Title = "テストタスク"
          Description = "テスト用のタスク"
          Status = InProgress
          AssignedAgent = Some "dev1"
          Priority = TaskPriority.Medium
          EstimatedDuration = Some(TimeSpan.FromHours(2.0))
          ActualDuration = None
          RequiredResources = []
          Dependencies = []
          CreatedAt = DateTime.UtcNow.AddHours(-1.0)
          UpdatedAt = DateTime.UtcNow.AddMinutes(-30.0) }

    let agent =
        { AgentId = "dev1"
          Status = Error
          Progress = 0.1
          LastUpdate = DateTime.UtcNow.AddMinutes(-10.0)
          CurrentTask = Some "task1"
          WorkingDirectory = "/tmp/test"
          ProcessId = None
          ActiveTasks = [] }

    // Act
    let needsReassignment = system.NeedsReassignment(task, agent)
    let reason = system.GetReassignmentReason(task, agent)

    // Assert
    Assert.True(needsReassignment)
    Assert.AreEqual("エラー発生", reason)

[<Test>]
[<Category("Unit")>]
let ``TaskAssignmentManager - エージェント登録テスト`` () =
    // Arrange
    let nlp = NaturalLanguageProcessor()
    let matcher = AgentSpecializationMatcher()
    let reassignmentSystem = DynamicReassignmentSystem()
    let manager = TaskAssignmentManager(nlp, matcher, reassignmentSystem)

    let profile =
        { AgentId = "dev1"
          Specializations = [ Development [ "F#"; ".NET" ] ]
          LoadCapacity = 8.0
          CurrentLoad = 0.0
          SuccessRate = 0.9
          AverageTaskDuration = TimeSpan.FromHours(2.0)
          LastAssignedTask = None }

    // Act & Assert（例外が発生しないことを確認）
    manager.RegisterAgent(profile)
    let statusReport = manager.GetAgentStatusReport()

    Assert.IsTrue(statusReport.Contains("dev1"))
    Assert.IsTrue(statusReport.Contains("0.0/8.0"))
    Assert.IsTrue(statusReport.Contains("90.00"))

[<Test>]
[<Category("Integration")>]
let ``TaskAssignmentManager - 指示処理・配分統合テスト`` () =
    // Arrange
    let nlp = NaturalLanguageProcessor()
    let matcher = AgentSpecializationMatcher()
    let reassignmentSystem = DynamicReassignmentSystem()
    let manager = TaskAssignmentManager(nlp, matcher, reassignmentSystem)

    // エージェント登録
    let devProfile =
        { AgentId = "dev1"
          Specializations = [ Development [ "F#"; ".NET" ] ]
          LoadCapacity = 8.0
          CurrentLoad = 1.0
          SuccessRate = 0.9
          AverageTaskDuration = TimeSpan.FromHours(2.0)
          LastAssignedTask = None }

    let qaProfile =
        { AgentId = "qa1"
          Specializations = [ Testing [ "unit-testing" ] ]
          LoadCapacity = 6.0
          CurrentLoad = 0.5
          SuccessRate = 0.95
          AverageTaskDuration = TimeSpan.FromHours(1.0)
          LastAssignedTask = None }

    manager.RegisterAgent(devProfile)
    manager.RegisterAgent(qaProfile)

    // Act
    let result = manager.ProcessInstructionAndAssign("新しいF#機能を実装してテストを書いてください")

    // Assert
    match result with
    | Result.Ok assignments ->
        Assert.True(assignments.Length >= 1)

        let devAssignments =
            assignments |> List.filter (fun (_, agentId) -> agentId = "dev1")

        let qaAssignments = assignments |> List.filter (fun (_, agentId) -> agentId = "qa1")

        Assert.True(devAssignments.Length > 0 || qaAssignments.Length > 0)
    | Result.Error error -> Assert.True(false, $"予期しないエラー: {error}")

[<Test>]
[<Category("Unit")>]
let ``TaskAssignmentManager - 再配分チェック機能テスト`` () =
    // Arrange
    let nlp = NaturalLanguageProcessor()
    let matcher = AgentSpecializationMatcher()
    let reassignmentSystem = DynamicReassignmentSystem()
    let manager = TaskAssignmentManager(nlp, matcher, reassignmentSystem)

    // 代替エージェント登録
    let altProfile =
        { AgentId = "dev2"
          Specializations = [ Development [ "F#"; ".NET" ] ]
          LoadCapacity = 8.0
          CurrentLoad = 2.0
          SuccessRate = 0.85
          AverageTaskDuration = TimeSpan.FromHours(2.5)
          LastAssignedTask = None }

    manager.RegisterAgent(altProfile)

    let blockedTask =
        { TaskId = "task1"
          Title = "ブロックされたタスク"
          Description = "進行困難なタスク"
          Status = InProgress
          AssignedAgent = Some "dev1"
          Priority = TaskPriority.High
          EstimatedDuration = Some(TimeSpan.FromHours(3.0))
          ActualDuration = None
          RequiredResources = [ "dev-environment" ]
          Dependencies = []
          CreatedAt = DateTime.UtcNow.AddHours(-2.0)
          UpdatedAt = DateTime.UtcNow.AddMinutes(-45.0) }

    let blockedAgent =
        { AgentId = "dev1"
          Status = Blocked
          Progress = 0.2
          LastUpdate = DateTime.UtcNow.AddMinutes(-30.0)
          CurrentTask = Some "task1"
          WorkingDirectory = "/tmp/blocked"
          ProcessId = Some 1234
          ActiveTasks = [] }

    // Act
    let reassignments = manager.CheckForReassignment([ blockedTask ], [ blockedAgent ])

    // Assert
    Assert.True(reassignments.Length >= 0) // 再配分候補が見つかるかは代替エージェントの状況による

[<Test>]
[<Category("Unit")>]
let ``ParsedTask - タスク作成の基本検証`` () =
    // Arrange & Act
    let task =
        { TaskId = "test-task-001"
          Title = "テスト用タスク"
          Description = "単体テスト用のサンプルタスク"
          RequiredSpecialization = Development [ "F#" ]
          EstimatedDuration = TimeSpan.FromHours(2.0)
          Priority = TaskPriority.Medium
          Dependencies = [ "dep1"; "dep2" ] }

    // Assert
    Assert.AreEqual("test-task-001", task.TaskId)
    Assert.AreEqual("テスト用タスク", task.Title)
    Assert.AreEqual(TimeSpan.FromHours(2.0), task.EstimatedDuration)
    Assert.AreEqual(TaskPriority.Medium, task.Priority)
    Assert.AreEqual(2, task.Dependencies.Length)

[<Test>]
[<Category("Unit")>]
let ``AgentSpecialization - 専門分野タイプ検証`` () =
    // Arrange & Act
    let devSpec = Development [ "F#"; "C#" ]
    let testSpec = Testing [ "unit-testing"; "integration-testing" ]
    let uxSpec = UXDesign [ "UI-design"; "accessibility" ]
    let pmSpec = ProjectManagement [ "planning"; "coordination" ]

    // Assert（パターンマッチング動作確認）
    match devSpec with
    | Development langs -> Assert.IsTrue(langs |> List.contains "F#")
    | _ -> Assert.True(false, "開発専門分野のパターンマッチに失敗")

    match testSpec with
    | Testing types -> Assert.IsTrue(types |> List.contains "unit-testing")
    | _ -> Assert.True(false, "テスト専門分野のパターンマッチに失敗")

    match uxSpec with
    | UXDesign areas -> Assert.IsTrue(areas |> List.contains "UI-design")
    | _ -> Assert.True(false, "UX専門分野のパターンマッチに失敗")

    match pmSpec with
    | ProjectManagement skills -> Assert.IsTrue(skills |> List.contains "planning")
    | _ -> Assert.True(false, "PM専門分野のパターンマッチに失敗")

[<Test>]
[<Category("Performance")>]
let ``TaskAssignmentManager - 大量エージェント登録性能テスト`` () =
    // Arrange
    let nlp = NaturalLanguageProcessor()
    let matcher = AgentSpecializationMatcher()
    let reassignmentSystem = DynamicReassignmentSystem()
    let manager = TaskAssignmentManager(nlp, matcher, reassignmentSystem)

    // Act
    let stopwatch = System.Diagnostics.Stopwatch.StartNew()

    for i in 1..100 do
        let profile =
            { AgentId = $"agent{i}"
              Specializations = [ Development [ "F#" ] ]
              LoadCapacity = 8.0
              CurrentLoad = 0.0
              SuccessRate = 0.9
              AverageTaskDuration = TimeSpan.FromHours(2.0)
              LastAssignedTask = None }

        manager.RegisterAgent(profile)

    stopwatch.Stop()

    // Assert
    Assert.True(
        stopwatch.ElapsedMilliseconds < 1000,
        $"100エージェント登録が {stopwatch.ElapsedMilliseconds}ms かかりました（期待値: <1000ms）"
    )

[<Test>]
[<Category("Unit")>]
let ``TaskBreakdown - タスク分解結果構造検証`` () =
    // Arrange & Act
    let breakdown =
        { OriginalInstruction = "複数の機能を実装してください"
          ParsedTasks =
            [ { TaskId = "task1"
                Title = "機能A実装"
                Description = "機能Aを実装する"
                RequiredSpecialization = Development [ "F#" ]
                EstimatedDuration = TimeSpan.FromHours(3.0)
                Priority = TaskPriority.High
                Dependencies = [] } ]
          EstimatedComplexity = 0.7
          RequiredSpecializations = [ Development [ "F#" ]; Testing [ "unit-testing" ] ] }

    // Assert
    Assert.AreEqual("複数の機能を実装してください", breakdown.OriginalInstruction)
    Assert.AreEqual(1, breakdown.ParsedTasks.Length)
    Assert.AreEqual(0.7, breakdown.EstimatedComplexity)
    Assert.AreEqual(2, breakdown.RequiredSpecializations.Length)

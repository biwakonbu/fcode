module FCode.Tests.SC1IntegrationTests

open NUnit.Framework
open System
open System.Threading.Tasks
open FCode.Logger
open FCode.TaskAssignmentManager
open FCode.QualityGateManager
open FCode.Collaboration.CollaborationTypes
open FCode.SC1IntegrationTest

// 安全な型キャストヘルパー関数
let safeCastInt (key: string) (value: obj) : int =
    match value with
    | :? int as i -> i
    | :? string as s ->
        match Int32.TryParse(s) with
        | (true, i) -> i
        | (false, _) ->
            Assert.Fail(sprintf "キー '%s' の値 '%s' をintに変換できませんでした" key s)
            0
    | _ ->
        Assert.Fail(sprintf "キー '%s' の値の型が不正です。期待型: int, 実際型: %s" key (value.GetType().Name))
        0

let safeCastFloat (key: string) (value: obj) : float =
    match value with
    | :? double as d -> d
    | :? float32 as f -> float f
    | :? int as i -> float i
    | :? string as s ->
        match Double.TryParse(s) with
        | (true, f) -> f
        | (false, _) ->
            Assert.Fail(sprintf "キー '%s' の値 '%s' をfloatに変換できませんでした" key s)
            0.0
    | _ ->
        Assert.Fail(sprintf "キー '%s' の値の型が不正です。期待型: float, 実際型: %s" key (value.GetType().Name))
        0.0

let safeCastBool (key: string) (value: obj) : bool =
    match value with
    | :? bool as b -> b
    | :? string as s ->
        match Boolean.TryParse(s) with
        | (true, b) -> b
        | (false, _) ->
            Assert.Fail(sprintf "キー '%s' の値 '%s' をboolに変換できませんでした" key s)
            false
    | _ ->
        Assert.Fail(sprintf "キー '%s' の値の型が不正です。期待型: bool, 実際型: %s" key (value.GetType().Name))
        false

/// SC-1統合テストクラス
[<TestFixture>]
[<Category("Integration")>]
type SC1IntegrationTestFixture() =

    [<Test>]
    [<Category("Integration")>]
    member _.``SC-1-1 PO指示入力機能テスト``() =
        // PO指示入力機能のテスト
        match testPOInstructionInput () with
        | Result.Ok details ->
            Assert.That(details.ContainsKey("assignmentCount"), Is.True, "タスク配分数が記録されていること")
            let assignmentCount = safeCastInt "assignmentCount" details.["assignmentCount"]
            Assert.That(assignmentCount, Is.GreaterThan(0), "1つ以上のタスクが配分されること")
        | Result.Error errorMsg -> Assert.Fail(sprintf "PO指示入力テスト失敗: %s" errorMsg)

    [<Test>]
    [<Category("Integration")>]
    member _.``SC-1-2 エージェント作業表示テスト``() =
        // エージェント作業表示機能のテスト
        match testAgentWorkDisplay () with
        | Result.Ok details ->
            Assert.That(details.ContainsKey("agentId"), Is.True, "エージェントIDが記録されていること")
            Assert.That(details.ContainsKey("taskTitle"), Is.True, "タスクタイトルが記録されていること")
            let statusLength = safeCastInt "statusLength" details.["statusLength"]
            Assert.That(statusLength, Is.GreaterThan(0), "作業状況が表示されること")
        | Result.Error errorMsg -> Assert.Fail(sprintf "エージェント作業表示テスト失敗: %s" errorMsg)

    [<Test>]
    [<Category("Integration")>]
    member _.``SC-1-3 18分スプリント連携テスト``() =
        // 18分スプリント連携機能のテスト
        match testSprintIntegration () with
        | Result.Ok details ->
            Assert.That(details.ContainsKey("sprintId"), Is.True, "スプリントIDが記録されていること")
            Assert.That(details.ContainsKey("managerInitialized"), Is.True, "スプリントマネージャーが初期化されていること")
            let initialized = safeCastBool "managerInitialized" details.["managerInitialized"]
            Assert.That(initialized, Is.True, "スプリントマネージャーが正常に初期化されること")
        | Result.Error errorMsg -> Assert.Fail(sprintf "スプリント連携テスト失敗: %s" errorMsg)

    [<Test>]
    [<Category("Integration")>]
    member _.``SC-1-4 品質ゲート連携テスト``() =
        // 品質ゲート連携機能のテスト
        match testQualityGateIntegration () with
        | Result.Ok details ->
            Assert.That(details.ContainsKey("taskId"), Is.True, "タスクIDが記録されていること")
            Assert.That(details.ContainsKey("evaluationStatus"), Is.True, "評価状況が記録されていること")
        | Result.Error errorMsg -> Assert.Fail(sprintf "品質ゲート連携テスト失敗: %s" errorMsg)

    [<Test>]
    [<Category("Integration")>]
    [<Timeout(30000)>] // 30秒タイムアウト
    member _.``エンドツーエンド統合テスト``() =
        // エンドツーエンドワークフローのテスト
        match testEndToEndWorkflow () with
        | Result.Ok details ->
            Assert.That(details.ContainsKey("assignmentCount"), Is.True, "タスク配分数が記録されていること")
            Assert.That(details.ContainsKey("registeredAgents"), Is.True, "登録エージェント数が記録されていること")

            let assignmentCount = safeCastInt "assignmentCount" details.["assignmentCount"]
            let registeredAgents = safeCastInt "registeredAgents" details.["registeredAgents"]
            let workflowDuration = safeCastFloat "workflowDuration" details.["workflowDuration"]

            Assert.That(assignmentCount, Is.GreaterThan(0), "タスクが配分されること")
            Assert.That(registeredAgents, Is.GreaterThan(0), "エージェントが登録されること")
            Assert.That(workflowDuration, Is.LessThan(10.0), "ワークフローが10秒以内で完了すること")
        | Result.Error errorMsg -> Assert.Fail(sprintf "エンドツーエンドテスト失敗: %s" errorMsg)

    [<Test>]
    [<Category("Performance")>]
    [<Timeout(15000)>] // 15秒タイムアウト
    member _.``パフォーマンス・安定性テスト``() =
        // パフォーマンスと安定性のテスト
        match testPerformanceAndStability () with
        | Result.Ok details ->
            let successRate = safeCastFloat "successRate" details.["successRate"]
            let executionTime = safeCastFloat "executionTime" details.["executionTime"]
            let avgOperationTime = safeCastFloat "avgOperationTime" details.["avgOperationTime"]

            Assert.That(successRate, Is.GreaterThanOrEqualTo(90.0), "90%以上の成功率を維持すること")
            Assert.That(executionTime, Is.LessThan(10.0), "実行時間が10秒以内であること")
            Assert.That(avgOperationTime, Is.LessThan(200.0), "平均操作時間が200ms以内であること")
        | Result.Error errorMsg -> Assert.Fail(sprintf "パフォーマンステスト失敗: %s" errorMsg)

    [<Test>]
    [<Category("Integration")>]
    [<Timeout(60000)>] // 60秒タイムアウト
    member _.``SC-1統合テストスイート全体実行``() =
        // SC-1統合テストスイート全体の実行
        match executeSC1IntegrationTest () with
        | Result.Ok report ->
            // レポートが生成されることを確認
            Assert.That(String.IsNullOrEmpty(report), Is.False, "レポートが生成されること")
            Assert.That(report.Contains("SC-1統合テスト・動作確認レポート"), Is.True, "レポートヘッダーが含まれること")
            Assert.That(report.Contains("成功率"), Is.True, "成功率が記録されること")

            printfn "=== SC-1統合テストレポート ===\n%s" report
        | Result.Error errorMsg -> Assert.Fail(sprintf "SC-1統合テストスイート失敗: %s" errorMsg)

/// SC-1統合テスト結果の詳細検証
[<TestFixture>]
[<Category("Integration")>]
type SC1DetailedValidationTests() =

    [<Test>]
    [<Category("Integration")>]
    member _.``TaskAssignmentManager基本動作確認``() =
        // TaskAssignmentManagerの基本動作確認
        let nlp = NaturalLanguageProcessor()
        let matcher = AgentSpecializationMatcher()
        let reassignmentSystem = DynamicReassignmentSystem()
        let taskManager = TaskAssignmentManager(nlp, matcher, reassignmentSystem)

        // エージェント登録テスト
        let testProfile =
            { AgentId = "validation-agent"
              Specializations = [ Development [ "validation"; "testing" ] ]
              LoadCapacity = 2.0
              CurrentLoad = 0.0
              SuccessRate = 0.95
              AverageTaskDuration = TimeSpan.FromHours(1.0)
              LastAssignedTask = None }

        taskManager.RegisterAgent(testProfile)

        // 指示処理テスト
        let testInstruction = "バリデーション機能のテスト実装"

        match taskManager.ProcessInstructionAndAssign(testInstruction) with
        | Result.Ok assignments ->
            Assert.That(assignments, Is.Not.Empty, "タスクが配分されること")
            let (task, agentId) = assignments.Head
            Assert.That(agentId, Is.EqualTo("validation-agent"), "正しいエージェントに配分されること")
            Assert.That(task.Title, Is.Not.Empty, "タスクタイトルが設定されること")
        | Result.Error errorMsg -> Assert.Fail(sprintf "TaskAssignmentManager基本動作確認失敗: %s" errorMsg)

    [<Test>]
    [<Category("Integration")>]
    member _.``基本動作確認テスト``() =
        // 基本的なSC-1機能の動作確認
        try
            // TaskAssignmentManagerの基本確認
            let nlp = NaturalLanguageProcessor()
            let matcher = AgentSpecializationMatcher()
            let reassignmentSystem = DynamicReassignmentSystem()
            let taskManager = TaskAssignmentManager(nlp, matcher, reassignmentSystem)

            Assert.That(taskManager, Is.Not.Null, "TaskAssignmentManagerが初期化されること")

            // 基本的なエージェント登録テスト
            let testProfile =
                { AgentId = "basic-validation-agent"
                  Specializations = [ Development [ "validation"; "testing" ] ]
                  LoadCapacity = 2.0
                  CurrentLoad = 0.0
                  SuccessRate = 0.95
                  AverageTaskDuration = TimeSpan.FromHours(1.0)
                  LastAssignedTask = None }

            taskManager.RegisterAgent(testProfile)
            Assert.Pass("基本動作確認が成功しました")

        with ex ->
            Assert.Fail(sprintf "基本動作確認でエラーが発生: %s" ex.Message)

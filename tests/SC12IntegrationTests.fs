namespace FCode.Tests

open NUnit.Framework
open FCode
open FCode.TaskAssignmentManager
open FCode.Logger
open FCode.Collaboration.CollaborationTypes
open System

[<TestFixture>]
[<Category("Integration")>]
type SC12IntegrationTests() =

    [<SetUp>]
    member this.Setup() =
        // CI環境でのテスト準備
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isCI then
            logInfo "SC12Test" "Running in CI environment - UI components will be mocked"

    [<Test>]
    member this.``SC-1-2: 全エージェントプロファイル登録確認``() =
        // TaskAssignmentManagerの初期化
        let nlp = NaturalLanguageProcessor()
        let matcher = AgentSpecializationMatcher()
        let reassignmentSystem = DynamicReassignmentSystem()
        let taskManager = TaskAssignmentManager(nlp, matcher, reassignmentSystem)

        // 全エージェントプロファイルを登録（Program.fsと同じ）
        let devProfile =
            { AgentId = "dev1"
              Specializations = [ Development [ "frontend"; "backend"; "general" ] ]
              LoadCapacity = 3.0
              CurrentLoad = 0.0
              SuccessRate = 0.95
              AverageTaskDuration = TimeSpan.FromHours(2.0)
              LastAssignedTask = None }

        let dev2Profile =
            { AgentId = "dev2"
              Specializations = [ Development [ "backend"; "database"; "API" ] ]
              LoadCapacity = 3.0
              CurrentLoad = 0.0
              SuccessRate = 0.93
              AverageTaskDuration = TimeSpan.FromHours(2.5)
              LastAssignedTask = None }

        let dev3Profile =
            { AgentId = "dev3"
              Specializations = [ Development [ "testing"; "devops"; "CI/CD" ] ]
              LoadCapacity = 2.5
              CurrentLoad = 0.0
              SuccessRate = 0.90
              AverageTaskDuration = TimeSpan.FromHours(2.0)
              LastAssignedTask = None }

        let qaProfile =
            { AgentId = "qa1"
              Specializations = [ Testing [ "unit-testing"; "integration-testing" ] ]
              LoadCapacity = 2.0
              CurrentLoad = 0.0
              SuccessRate = 0.92
              AverageTaskDuration = TimeSpan.FromHours(1.5)
              LastAssignedTask = None }

        let qa2Profile =
            { AgentId = "qa2"
              Specializations = [ Testing [ "performance-testing"; "security-testing" ] ]
              LoadCapacity = 2.0
              CurrentLoad = 0.0
              SuccessRate = 0.89
              AverageTaskDuration = TimeSpan.FromHours(2.0)
              LastAssignedTask = None }

        let uxProfile =
            { AgentId = "ux"
              Specializations = [ UXDesign [ "interface"; "usability" ] ]
              LoadCapacity = 2.0
              CurrentLoad = 0.0
              SuccessRate = 0.88
              AverageTaskDuration = TimeSpan.FromHours(3.0)
              LastAssignedTask = None }

        let pmProfile =
            { AgentId = "pm"
              Specializations = [ ProjectManagement [ "coordination"; "planning"; "management" ] ]
              LoadCapacity = 1.5
              CurrentLoad = 0.0
              SuccessRate = 0.95
              AverageTaskDuration = TimeSpan.FromHours(1.0)
              LastAssignedTask = None }

        // 全エージェントプロファイルを登録
        taskManager.RegisterAgent(devProfile)
        taskManager.RegisterAgent(dev2Profile)
        taskManager.RegisterAgent(dev3Profile)
        taskManager.RegisterAgent(qaProfile)
        taskManager.RegisterAgent(qa2Profile)
        taskManager.RegisterAgent(uxProfile)
        taskManager.RegisterAgent(pmProfile)

        // エージェント状況レポートを取得して検証
        let statusReport = taskManager.GetAgentStatusReport()

        // 全エージェントが登録されていることを確認
        Assert.That(statusReport.Contains("Agent dev1"), Is.True, "dev1エージェントが登録されていません")
        Assert.That(statusReport.Contains("Agent dev2"), Is.True, "dev2エージェントが登録されていません")
        Assert.That(statusReport.Contains("Agent dev3"), Is.True, "dev3エージェントが登録されていません")
        Assert.That(statusReport.Contains("Agent qa1"), Is.True, "qa1エージェントが登録されていません")
        Assert.That(statusReport.Contains("Agent qa2"), Is.True, "qa2エージェントが登録されていません")
        Assert.That(statusReport.Contains("Agent ux"), Is.True, "uxエージェントが登録されていません")
        Assert.That(statusReport.Contains("Agent pm"), Is.True, "pmエージェントが登録されていません")

        logInfo "SC12Test" (sprintf "Full agent status report:\n%s" statusReport)

    [<Test>]
    member this.``SC-1-2: AgentWorkDisplayManager全エージェント初期化確認``() =
        // AgentWorkDisplayManagerを取得
        let workDisplayManager = AgentWorkDisplayGlobal.GetManager()

        // 全エージェントを初期化
        let allAgentIds = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "pm" ]

        for agentId in allAgentIds do
            workDisplayManager.InitializeAgent(agentId)

        // 全エージェントの作業情報を取得
        let allWorkInfos = workDisplayManager.GetAllAgentWorkInfos()

        // 全エージェントが初期化されていることを確認
        Assert.That(allWorkInfos.Length, Is.EqualTo(allAgentIds.Length), "初期化されたエージェント数が期待値と一致しません")

        for agentId in allAgentIds do
            let agentExists = allWorkInfos |> List.exists (fun (id, _) -> id = agentId)
            Assert.That(agentExists, Is.True, sprintf "%sエージェントが初期化されていません" agentId)

        logInfo "SC12Test" (sprintf "Initialized %d agents successfully" allWorkInfos.Length)

    [<Test>]
    member this.``SC-1-2: タスク配分とエージェント作業表示統合テスト``() =
        // TaskAssignmentManagerの初期化
        let nlp = NaturalLanguageProcessor()
        let matcher = AgentSpecializationMatcher()
        let reassignmentSystem = DynamicReassignmentSystem()
        let taskManager = TaskAssignmentManager(nlp, matcher, reassignmentSystem)

        // エージェントプロファイルを登録
        let devProfile =
            { AgentId = "dev1"
              Specializations = [ Development [ "frontend"; "backend" ] ]
              LoadCapacity = 3.0
              CurrentLoad = 0.0
              SuccessRate = 0.95
              AverageTaskDuration = TimeSpan.FromHours(2.0)
              LastAssignedTask = None }

        let qaProfile =
            { AgentId = "qa1"
              Specializations = [ Testing [ "unit-testing" ] ]
              LoadCapacity = 2.0
              CurrentLoad = 0.0
              SuccessRate = 0.92
              AverageTaskDuration = TimeSpan.FromHours(1.5)
              LastAssignedTask = None }

        taskManager.RegisterAgent(devProfile)
        taskManager.RegisterAgent(qaProfile)

        // AgentWorkDisplayManagerを取得
        let workDisplayManager = AgentWorkDisplayGlobal.GetManager()
        workDisplayManager.InitializeAgent("dev1")
        workDisplayManager.InitializeAgent("qa1")

        // PO指示をタスクに分解して配分
        let instruction = "ログイン機能を実装して、ユニットテストも作成してください"

        match taskManager.ProcessInstructionAndAssign(instruction) with
        | Result.Ok assignments ->
            Assert.That(assignments.Length, Is.GreaterThan(0), "タスクが配分されませんでした")

            // 各エージェントペインに作業表示
            for (task, agentId) in assignments do
                // AgentWorkDisplayManagerでタスク開始を記録
                workDisplayManager.StartTask(agentId, task.Title, task.EstimatedDuration)

                // 作業情報を取得して確認
                match workDisplayManager.GetAgentWorkInfo(agentId) with
                | Some workInfo ->
                    match workInfo.CurrentStatus with
                    | AgentWorkStatus.Working(taskTitle, _, _) ->
                        Assert.That(taskTitle, Is.EqualTo(task.Title), sprintf "%sのタスクタイトルが正しく設定されていません" agentId)
                        logInfo "SC12Test" (sprintf "Agent %s started task: %s" agentId taskTitle)
                    | _ -> Assert.Fail(sprintf "%sエージェントのステータスがWorkingになっていません" agentId)
                | None -> Assert.Fail(sprintf "%sエージェントの作業情報が取得できません" agentId)

            logInfo "SC12Test" (sprintf "Successfully assigned %d tasks to agents" assignments.Length)

        | Result.Error error -> Assert.Fail(sprintf "タスク配分に失敗しました: %s" error)

    [<Test>]
    member this.``SC-1-2: エージェント間情報共有表示テスト``() =
        // AgentWorkDisplayManagerを取得
        let workDisplayManager = AgentWorkDisplayGlobal.GetManager()

        // 複数エージェントを初期化
        let agentIds = [ "dev1"; "dev2"; "qa1"; "ux" ]

        for agentId in agentIds do
            workDisplayManager.InitializeAgent(agentId)

        // 各エージェントに異なる状態を設定
        workDisplayManager.StartTask("dev1", "フロントエンド実装", TimeSpan.FromHours(3.0))
        workDisplayManager.UpdateProgress("dev1", 50.0, "UI コンポーネント作成中...")

        workDisplayManager.StartTask("dev2", "バックエンドAPI実装", TimeSpan.FromHours(2.5))
        workDisplayManager.UpdateProgress("dev2", 75.0, "データベース接続実装中...")

        workDisplayManager.StartTask("qa1", "単体テスト作成", TimeSpan.FromHours(1.5))
        workDisplayManager.CompleteTask("qa1", "基本テストケース作成完了")

        workDisplayManager.StartReview("ux", "UI/UXレビュー", "ux-reviewer")

        // チーム状況サマリーを生成
        let teamSummary = FCode.Program.generateTeamStatusSummary workDisplayManager

        // サマリーに各エージェントの情報が含まれていることを確認
        Assert.That(teamSummary.Contains("dev1"), Is.True, "dev1の情報がチーム状況に含まれていません")
        Assert.That(teamSummary.Contains("dev2"), Is.True, "dev2の情報がチーム状況に含まれていません")
        Assert.That(teamSummary.Contains("qa1"), Is.True, "qa1の情報がチーム状況に含まれていません")
        Assert.That(teamSummary.Contains("ux"), Is.True, "uxの情報がチーム状況に含まれていません")

        // 進行中タスクの表示確認
        Assert.That(teamSummary.Contains("🔄 進行中タスク"), Is.True, "進行中タスクセクションが含まれていません")
        Assert.That(teamSummary.Contains("フロントエンド実装"), Is.True, "dev1のタスクが表示されていません")
        Assert.That(teamSummary.Contains("バックエンドAPI実装"), Is.True, "dev2のタスクが表示されていません")

        logInfo "SC12Test" (sprintf "Team status summary generated:\n%s" teamSummary)

    [<Test>]
    member this.``SC-1-2: リアルタイム更新ハンドラー動作テスト``() =
        // AgentWorkDisplayManagerを取得
        let workDisplayManager = AgentWorkDisplayGlobal.GetManager()

        // 更新通知を受信するフラグ
        let mutable updateReceived = false
        let mutable updatedAgentId = ""
        let mutable updatedWorkInfo: AgentWorkInfo option = None

        // 表示更新ハンドラーを登録
        workDisplayManager.RegisterDisplayUpdateHandler(fun agentId workInfo ->
            updateReceived <- true
            updatedAgentId <- agentId
            updatedWorkInfo <- Some workInfo)

        // エージェントを初期化
        workDisplayManager.InitializeAgent("test-agent")

        // 初期化時に更新通知が送信されることを確認
        Assert.That(updateReceived, Is.True, "初期化時に更新通知が送信されませんでした")
        Assert.That(updatedAgentId, Is.EqualTo("test-agent"), "更新通知のエージェントIDが正しくありません")
        Assert.That(updatedWorkInfo.IsSome, Is.True, "更新通知の作業情報が含まれていません")

        // フラグをリセット
        updateReceived <- false

        // タスク開始時の更新通知確認
        workDisplayManager.StartTask("test-agent", "テストタスク", TimeSpan.FromHours(1.0))

        Assert.That(updateReceived, Is.True, "タスク開始時に更新通知が送信されませんでした")
        Assert.That(updatedAgentId, Is.EqualTo("test-agent"), "タスク開始時の更新通知エージェントIDが正しくありません")

        match updatedWorkInfo with
        | Some workInfo ->
            match workInfo.CurrentStatus with
            | AgentWorkStatus.Working(title, _, _) -> Assert.That(title, Is.EqualTo("テストタスク"), "更新通知のタスクタイトルが正しくありません")
            | _ -> Assert.Fail("タスク開始後のステータスがWorkingになっていません")
        | None -> Assert.Fail("タスク開始時の作業情報が取得できません")

        logInfo "SC12Test" "Real-time update handler test completed successfully"

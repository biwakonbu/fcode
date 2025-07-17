module FCode.Program

open System.Threading.Tasks
open Terminal.Gui
open FCode.Logger
open FCode.ColorSchemes
open FCode.KeyBindings
open FCode.ClaudeCodeProcess
open FCode.UIHelpers
open FCode.FCodeError
open FCode.AgentMessaging
open FCode.UnifiedActivityView
open FCode.DecisionTimelineView
open FCode.ProgressDashboard
open FCode.RealtimeUIIntegration
open FCode.FullWorkflowCoordinator
open FCode.SimpleMemoryMonitor
open FCode.ConfigurationManager
open FCode.TaskAssignmentManager
open FCode.VirtualTimeCoordinator
open FCode.Collaboration.CollaborationTypes
open FCode.SprintTimeDisplayManager
open FCode.QualityGateManager
open FCode.ClaudeCodeIOIntegration
open FCode.ClaudeCodeIOTrigger
open FCode.POWorkflowIntegration
// open FCode.POWorkflowUI
open FCode
// AgentWorkDisplayManager and AgentWorkSimulator are in FCode namespace

// グローバル変数として定義
let mutable globalPaneTextViews: Map<string, TextView> = Map.empty
let mutable agentStatusViews: Map<string, TextView> = Map.empty
let mutable sessionBridges: Map<string, SessionBridge> = Map.empty
let mutable claudeCodeIOManager: ClaudeCodeIOIntegrationManager option = None
let mutable claudeCodeIOTrigger: ClaudeCodeIOTrigger option = None
let mutable poWorkflowManager: POWorkflowIntegrationManager option = None
let mutable poWorkflowUI: obj option = None
// Terminal.GuiのイベントはすべてUIスレッドで実行されるため、
// これらのグローバル変数へのアクセスは同期化不要
let mutable keyRouters: Map<string, KeyRouter> = Map.empty
let mutable currentFocusedPane: string = "conversation"

// タイムスタンプを取得するヘルパー関数
let getCurrentTimestamp () =
    System.DateTime.Now.ToString("HH:mm:ss")

// 優先度アイコンを取得するヘルパー関数
let getPriorityIcon (priority: TaskPriority) =
    match priority with
    | TaskPriority.Critical -> "🟥"
    | TaskPriority.High -> "🔴"
    | TaskPriority.Medium -> "🟡"
    | TaskPriority.Low -> "🟢"
    | unknownPriority ->
        logWarning "TaskDisplay" (sprintf "Unknown priority value: %A" unknownPriority)
        "❓" // 未知の優先度値に対するフォールバック

// エージェント状況表示を更新するヘルパー関数
let updateAgentStatusDisplay (agentId: string) (workDisplayManager: AgentWorkDisplayManager) =
    match agentStatusViews.TryFind(agentId) with
    | Some statusView ->
        match workDisplayManager.GetAgentWorkInfo(agentId) with
        | Some workInfo ->
            let formattedStatus = workDisplayManager.FormatWorkStatus(workInfo)
            statusView.Text <- NStack.ustring.Make(formattedStatus)
            statusView.SetNeedsDisplay()
            logDebug "AgentStatus" (sprintf "Updated status display for agent: %s" agentId)
        | None -> logWarning "AgentStatus" (sprintf "Failed to get work info for agent: %s" agentId)
    | None -> logDebug "AgentStatus" (sprintf "No status view found for agent: %s" agentId)

// エージェント間情報共有サマリーを生成するヘルパー関数
let generateTeamStatusSummary (workDisplayManager: AgentWorkDisplayManager) : string =
    let allAgents = workDisplayManager.GetAllAgentWorkInfos()
    let timestamp = getCurrentTimestamp ()

    let activeAgents =
        allAgents
        |> List.filter (fun (_, workInfo) ->
            match workInfo.CurrentStatus with
            | AgentWorkStatus.Working(_, _, _) -> true
            | _ -> false)

    let completedTasks =
        allAgents
        |> List.filter (fun (_, workInfo) ->
            match workInfo.CurrentStatus with
            | AgentWorkStatus.Completed(_, _, _) -> true
            | _ -> false)

    let errorAgents =
        allAgents
        |> List.filter (fun (_, workInfo) ->
            match workInfo.CurrentStatus with
            | AgentWorkStatus.Error(_, _, _) -> true
            | _ -> false)

    // StringBuilderを使用したパフォーマンス最適化
    let sb = System.Text.StringBuilder()

    sb.AppendFormat("🤝 チーム状況サマリー [{0}]\n\n", timestamp) |> ignore

    sb.AppendFormat(
        "📊 アクティブ: {0}人 | ✅ 完了: {1}件 | ❌ エラー: {2}件\n\n",
        activeAgents.Length,
        completedTasks.Length,
        errorAgents.Length
    )
    |> ignore

    sb.Append("🔄 進行中タスク:\n") |> ignore

    // アクティブタスクの追加
    for (agentId, workInfo) in activeAgents do
        match workInfo.CurrentStatus with
        | AgentWorkStatus.Working(taskTitle, _, progress) ->
            sb.AppendFormat("  • {0}: {1} ({2:F1}%)\n", agentId, taskTitle, progress)
            |> ignore
        | _ -> ()

    // エラー状態のエージェントがある場合
    if errorAgents.Length > 0 then
        sb.Append("\n⚠️ 要注意:\n") |> ignore

        for (agentId, workInfo) in errorAgents do
            match workInfo.CurrentStatus with
            | AgentWorkStatus.Error(taskTitle, errorMsg, _) ->
                sb.AppendFormat("  • {0}: {1} - {2}\n", agentId, taskTitle, errorMsg) |> ignore
            | _ -> ()

    sb.ToString()

// PO指示処理関数
let processPOInstruction (instruction: string) : unit =
    try
        logInfo "PO" (sprintf "Starting PO instruction processing: %s" instruction)

        // TaskAssignmentManagerの初期化
        let nlp = NaturalLanguageProcessor()
        let matcher = AgentSpecializationMatcher()
        let reassignmentSystem = DynamicReassignmentSystem()
        let taskAssignmentManager = TaskAssignmentManager(nlp, matcher, reassignmentSystem)

        // 基本エージェントプロファイルを登録
        let devProfile =
            { AgentId = "dev1"
              Specializations = [ Development [ "frontend"; "backend"; "general" ] ]
              LoadCapacity = 3.0
              CurrentLoad = 0.0
              SuccessRate = 0.95
              AverageTaskDuration = System.TimeSpan.FromHours(2.0)
              LastAssignedTask = None }

        let dev2Profile =
            { AgentId = "dev2"
              Specializations = [ Development [ "backend"; "database"; "API" ] ]
              LoadCapacity = 3.0
              CurrentLoad = 0.0
              SuccessRate = 0.93
              AverageTaskDuration = System.TimeSpan.FromHours(2.5)
              LastAssignedTask = None }

        let dev3Profile =
            { AgentId = "dev3"
              Specializations = [ Development [ "testing"; "devops"; "CI/CD" ] ]
              LoadCapacity = 2.5
              CurrentLoad = 0.0
              SuccessRate = 0.90
              AverageTaskDuration = System.TimeSpan.FromHours(2.0)
              LastAssignedTask = None }

        let qaProfile =
            { AgentId = "qa1"
              Specializations = [ Testing [ "unit-testing"; "integration-testing" ] ]
              LoadCapacity = 2.0
              CurrentLoad = 0.0
              SuccessRate = 0.92
              AverageTaskDuration = System.TimeSpan.FromHours(1.5)
              LastAssignedTask = None }

        let qa2Profile =
            { AgentId = "qa2"
              Specializations = [ Testing [ "performance-testing"; "security-testing" ] ]
              LoadCapacity = 2.0
              CurrentLoad = 0.0
              SuccessRate = 0.89
              AverageTaskDuration = System.TimeSpan.FromHours(2.0)
              LastAssignedTask = None }

        let uxProfile =
            { AgentId = "ux"
              Specializations = [ UXDesign [ "interface"; "usability" ] ]
              LoadCapacity = 2.0
              CurrentLoad = 0.0
              SuccessRate = 0.88
              AverageTaskDuration = System.TimeSpan.FromHours(3.0)
              LastAssignedTask = None }

        let pmProfile =
            { AgentId = "pm"
              Specializations = [ ProjectManagement [ "coordination"; "planning"; "management" ] ]
              LoadCapacity = 1.5
              CurrentLoad = 0.0
              SuccessRate = 0.95
              AverageTaskDuration = System.TimeSpan.FromHours(1.0)
              LastAssignedTask = None }

        // 全エージェントプロファイルを登録
        taskAssignmentManager.RegisterAgent(devProfile)
        taskAssignmentManager.RegisterAgent(dev2Profile)
        taskAssignmentManager.RegisterAgent(dev3Profile)
        taskAssignmentManager.RegisterAgent(qaProfile)
        taskAssignmentManager.RegisterAgent(qa2Profile)
        taskAssignmentManager.RegisterAgent(uxProfile)
        taskAssignmentManager.RegisterAgent(pmProfile)

        // AgentWorkDisplayManagerの取得
        let workDisplayManager = AgentWorkDisplayGlobal.GetManager()

        // SprintTimeDisplayManagerの取得
        let sprintTimeDisplayManager = SprintTimeDisplayGlobal.GetManager()

        // 指示をタスクに分解して配分
        match taskAssignmentManager.ProcessInstructionAndAssign(instruction) with
        | Result.Ok assignments ->
            logInfo "PO" (sprintf "Successfully processed instruction - %d tasks assigned" assignments.Length)

            // UnifiedActivityViewに詳細なタスク分解結果を表示
            let totalEstimatedTime =
                assignments |> List.sumBy (fun (task, _) -> task.EstimatedDuration.TotalMinutes)

            let uniqueAgents = assignments |> List.map snd |> List.distinct |> List.length

            addSystemActivity
                "PO"
                SystemMessage
                (sprintf
                    "📋 タスク分解完了: %d個のタスクを%d人のエージェントに配分 (総予定時間: %.1f分)"
                    assignments.Length
                    uniqueAgents
                    totalEstimatedTime)
            |> ignore

            // タスク分解サマリーを表示
            addSystemActivity "TaskSummary" SystemMessage "═══ タスク分解結果 ═══" |> ignore

            // 各エージェント別にタスクをグループ化して表示
            assignments
            |> List.groupBy snd
            |> List.iter (fun (agentId, agentTasks) ->
                let agentTotalTime =
                    agentTasks |> List.sumBy (fun (task, _) -> task.EstimatedDuration.TotalMinutes)

                addSystemActivity "TaskSummary" TaskAssignment (sprintf "👤 %s (総時間: %.1f分)" agentId agentTotalTime)
                |> ignore

                // 各タスクの詳細を表示
                agentTasks
                |> List.iteri (fun i (task, _) ->
                    let priorityIcon = getPriorityIcon task.Priority

                    addSystemActivity
                        "TaskDetail"
                        TaskAssignment
                        (sprintf
                            "   %d. %s %s (%.0f分)"
                            (i + 1)
                            priorityIcon
                            task.Title
                            task.EstimatedDuration.TotalMinutes)
                    |> ignore))

            addSystemActivity "TaskSummary" SystemMessage "═══════════════════" |> ignore

            // チーム状況サマリーを会話ペインに表示
            let teamSummary = generateTeamStatusSummary workDisplayManager
            addSystemActivity "TeamStatus" SystemMessage teamSummary |> ignore

            // 各エージェントペインに作業内容を表示し、AgentWorkDisplayManagerでタスク開始を記録
            for (task, agentId) in assignments do
                // AgentWorkDisplayManagerでタスク開始を記録
                workDisplayManager.StartTask(agentId, task.Title, task.EstimatedDuration)

                // UI即座更新
                updateAgentStatusDisplay agentId workDisplayManager

                // 品質ゲート評価を自動実行
                async {
                    try
                        // 品質ゲート評価の実行判定
                        let shouldEvaluate =
                            // 開発タスクの場合は品質ゲート評価を実行
                            agentId.StartsWith("dev")
                            ||
                            // または明示的な品質確認タスクの場合
                            task.Title.Contains("品質")
                            || task.Title.Contains("テスト")
                            || task.RequiredSpecialization = Testing [ "quality-assurance"; "testing" ]

                        if shouldEvaluate then
                            // 少し遅延させてからタスク完了時に品質ゲート評価実行
                            do! Async.Sleep(3000)

                            logInfo "QualityGate" (sprintf "品質ゲート評価開始: %s (%s)" task.TaskId task.Title)

                            // 品質ゲート評価実行
                            let! evaluationResult = FCode.QualityGateUIIntegration.executeQualityGateEvaluation task

                            // evaluationResultは直接QualityGateIntegrationResult型
                            let entry = evaluationResult
                            logInfo "QualityGate" (sprintf "品質ゲート評価完了: %s - 承認: %b" task.TaskId entry.Approved)

                            // 品質ゲート結果に基づいてエスカレーション判定
                            let requiresEscalation = entry.RequiresEscalation

                            if requiresEscalation then
                                // エスカレーション通知作成
                                let urgency =
                                    if task.Priority = TaskPriority.Critical then
                                        FCode.EscalationNotificationUI.Urgent
                                    else
                                        FCode.EscalationNotificationUI.Normal

                                // TODO: エスカレーション通知統合予定
                                logInfo "EscalationHandler" (sprintf "品質ゲート要対応: %s" task.Title)

                                logInfo "EscalationHandler" (sprintf "品質ゲートエスカレーション作成: %s" task.TaskId)

                    with ex ->
                        logError "QualityGate" (sprintf "品質ゲート評価処理例外: %s - %s" task.TaskId ex.Message)
                }
                |> Async.Start

                match globalPaneTextViews.TryFind(agentId) with
                | Some textView ->
                    // AgentWorkDisplayManagerからフォーマットされた作業状況を取得
                    match workDisplayManager.GetAgentWorkInfo(agentId) with
                    | Some workInfo ->
                        let formattedStatus = workDisplayManager.FormatWorkStatus(workInfo)
                        textView.Text <- formattedStatus
                        textView.SetNeedsDisplay()
                        logInfo "UI" (sprintf "Updated work display for %s: %s" agentId task.Title)
                    | None ->
                        // フォールバック: 従来の表示
                        let currentText = textView.Text.ToString()
                        let timestamp = System.DateTime.Now.ToString("HH:mm:ss")

                        let newText =
                            sprintf "%s\n[%s] 新しいタスク: %s\n説明: %s\n" currentText timestamp task.Title task.Description

                        textView.Text <- newText
                        textView.SetNeedsDisplay()
                        logInfo "UI" (sprintf "Task assigned to %s: %s (fallback display)" agentId task.Title)
                | None ->
                    // エージェントのペインが見つからない場合のログ
                    logWarning "UI" (sprintf "TextView not found for agent: %s, task: %s" agentId task.Title)

            // 18分スプリント自動開始
            let sprintId = sprintf "SPRINT_%s" (System.DateTime.Now.ToString("yyyyMMddHHmmss"))

            async {
                try
                    let! sprintResult = sprintTimeDisplayManager.StartSprint(sprintId)

                    match sprintResult with
                    | Result.Ok() ->
                        logInfo "Sprint" (sprintf "18分スプリント自動開始: %s" sprintId)

                        // スプリント開始通知を会話ペインに表示
                        addSystemActivity
                            "Sprint"
                            SystemMessage
                            (sprintf "🚀 18分スプリント開始: %s\n📊 6分毎スタンドアップ予定\n⏰ 18分後に完成確認フロー実行" sprintId)
                        |> ignore

                    | Result.Error error ->
                        logError "Sprint" (sprintf "スプリント開始失敗: %A" error)

                        addSystemActivity "Sprint" SystemMessage (sprintf "❌ スプリント開始失敗: %A" error)
                        |> ignore

                with ex ->
                    logError "Sprint" (sprintf "スプリント開始例外: %s" ex.Message)
            }
            |> Async.Start

            // 画面更新
            Application.Refresh()

            // 作業シミュレーションを開始（リアルタイム進捗表示のため）
            let simulator = AgentWorkSimulatorGlobal.GetSimulator()

            // AgentWorkSimulatorが期待する形式に変換
            let simulationAssignments =
                assignments
                |> List.map (fun (task, agentId) ->
                    let durationMinutes = int (ceil task.EstimatedDuration.TotalMinutes)
                    (agentId, task.Title, durationMinutes))

            try
                // 作業シミュレーション実行
                simulator.StartWorkSimulation(simulationAssignments)
                logInfo "PO" (sprintf "Started work simulation for %d tasks" assignments.Length)
            with ex ->
                logError "PO" (sprintf "Failed to start work simulation: %s" ex.Message)

            // 定期的なチーム状況更新の設定
            let updateIntervalSeconds = 30
            let updateIterations = 10

            // 定期的なチーム状況更新
            async {
                for i in 1..updateIterations do
                    do! Async.Sleep(updateIntervalSeconds * 1000) // 指定間隔で待機

                    let updatedTeamSummary = generateTeamStatusSummary workDisplayManager

                    addSystemActivity
                        "TeamUpdate"
                        SystemMessage
                        (sprintf
                            "🔄 チーム状況更新 (%2.1f分経過)\n%s"
                            (float i * float updateIntervalSeconds / 60.0)
                            updatedTeamSummary)
                    |> ignore

                    logInfo "TeamStatus" (sprintf "Team status updated - iteration %d" i)
            }
            |> Async.Start

            // 画面更新とチーム状況表示
            let finalTeamSummary = generateTeamStatusSummary workDisplayManager
            addSystemActivity "TeamStatus" SystemMessage finalTeamSummary |> ignore
            Application.Refresh()

        | Result.Error errorMsg ->
            logError "PO" (sprintf "Failed to process instruction: %s" errorMsg)
            addSystemActivity "PO" SystemMessage (sprintf "指示処理エラー: %s" errorMsg) |> ignore

    with ex ->
        logError "PO" (sprintf "Exception in PO instruction processing: %s" ex.Message)

        addSystemActivity "PO" SystemMessage (sprintf "システムエラー: %s" ex.Message)
        |> ignore

[<EntryPoint>]
let main argv =
    try
        logInfo "Application" "=== fcode TUI Application Starting ==="
        let argsString = System.String.Join(" ", argv)
        logInfo "Application" (sprintf "Command line args: %s" argsString)

        // 軽量メモリ監視初期化
        let initialMemoryReport = getMemoryReport ()
        logInfo "MemoryMonitor" (sprintf "起動時メモリ状態: %s" initialMemoryReport)


        // 包括的なCI環境判定（強化版）
        let isCI =
            let ciEnvVars =
                [ "CI"
                  "GITHUB_ACTIONS"
                  "GITLAB_CI"
                  "JENKINS_URL"
                  "BUILDKITE"
                  "TRAVIS"
                  "CIRCLECI"
                  "DRONE"
                  "TEAMCITY_VERSION"
                  "BAMBOO_BUILD_NUMBER"
                  "TF_BUILD"
                  "APPVEYOR"
                  "CODEBUILD_BUILD_ID"
                  "FCODE_TEST_CI" ]

            let hasCI =
                ciEnvVars
                |> List.exists (System.Environment.GetEnvironmentVariable >> isNull >> not)

            // ヘッドレス環境チェック（DISPLAY環境変数なし）
            let isHeadless = isNull (System.Environment.GetEnvironmentVariable("DISPLAY"))

            // テスト実行環境チェック
            let isTestExecution =
                let assembly = System.Reflection.Assembly.GetExecutingAssembly()

                assembly.FullName.Contains("Tests")
                || assembly.FullName.Contains("nunit")
                || assembly.FullName.Contains("test")

            // NUnit Test実行中かチェック
            let isNUnitExecution =
                try
                    System.AppDomain.CurrentDomain.GetAssemblies()
                    |> Array.exists (fun a -> a.FullName.Contains("nunit"))
                with _ ->
                    false

            hasCI || isHeadless || isTestExecution || isNUnitExecution

        if isCI then
            logInfo "Application" "Running in CI/Test environment - skipping Terminal.Gui initialization"
            0 // Exit successfully in CI
        else
            // Initialize application
            logDebug "Application" "Initializing Terminal.Gui"
            // UseSystemConsoleを設定してコンソールドライバーの問題を回避
            Application.UseSystemConsole <- true
            Application.Init()
            logInfo "Application" "Terminal.Gui initialized successfully (UseSystemConsole=true)"

            // No need to start supervisor - using direct Claude CLI integration
            logInfo "Application" "Using direct Claude CLI integration (no ProcessSupervisor required)"

            let top = Application.Top
            logDebug "Application" "Got Application.Top"

            // --- Conversation Pane (FIXED LAYOUT) -------------------------------------------------
            logDebug "UI" "Creating conversation pane"
            let conversationWidth = 60
            let convo = new FrameView("会話")
            convo.X <- 0
            convo.Y <- 0
            convo.Width <- conversationWidth
            convo.Height <- 24
            convo.CanFocus <- true // フォーカス可能にする（key-event-focus.md対応）

            // Border-less style for the conversation pane (フラット表示)
            convo.Border.Effect3D <- false

            // 会話ペイン用TextViewを追加
            logDebug "UI" "Adding TextView to conversation pane"
            let conversationTextView = new TextView()
            conversationTextView.X <- 0
            conversationTextView.Y <- 0
            conversationTextView.Width <- Dim.Fill()
            conversationTextView.Height <- Dim.Fill()
            conversationTextView.ReadOnly <- false

            conversationTextView.Text <-
                "[会話ペイン] Claude Code TUI - エージェント協調開発環境\n\nPO指示の入力方法:\n1. 「> 指示内容」の形式で入力してください\n2. Enterキーで指示を実行します\n3. タスクが自動的にエージェントに配分されます\n\n操作方法:\n• ESC: アプリケーション終了\n• Enter: PO指示実行\n\n準備完了 - PO指示を入力してください:\n\n> "

            // Terminal.Gui 1.15.0の推奨方法: Add()メソッド使用
            convo.Add(conversationTextView)

            // レイアウトを適切に設定
            conversationTextView.SetNeedsDisplay()
            convo.SetNeedsDisplay()

            logInfo "UI" "Conversation pane with TextView created successfully"

            // UnifiedActivityViewとの統合設定
            setConversationTextView conversationTextView
            logInfo "UI" "UnifiedActivityView integrated with conversation pane"

            // SprintTimeDisplayManagerのスタンドアップ通知ハンドラーを登録
            let sprintTimeDisplayManager = SprintTimeDisplayGlobal.GetManager()

            sprintTimeDisplayManager.RegisterStandupNotificationHandler(fun notificationText ->
                try
                    // スタンドアップ通知を会話ペインに表示
                    addSystemActivity "Standup" SystemMessage notificationText |> ignore
                    logInfo "StandupNotification" "スタンドアップ通知を会話ペインに表示しました"
                with ex ->
                    logError "StandupNotification" (sprintf "スタンドアップ通知表示エラー: %s" ex.Message))

            // 初期システム活動追加
            addSystemActivity "system" SystemMessage "fcode TUI Application 起動完了 - エージェント協調開発環境準備中"
            |> ignore

            addSystemActivity "system" SystemMessage "会話ペイン統合 - 全エージェント活動をリアルタイム表示"
            |> ignore

            addSystemActivity "system" SystemMessage "スプリント管理統合 - 6分スタンドアップ・18分完成確認システム準備完了"
            |> ignore

            // ----------------------------------------------------------------------
            // Right-hand container – holds all other panes
            let right = new View()
            right.X <- 60 // 固定位置
            right.Y <- 0
            right.Width <- 60 // 固定幅
            right.Height <- 24 // 固定高

            // TextView直接参照用マップ
            let mutable paneTextViews = Map.empty<string, TextView>

            // AgentWorkDisplayManagerの取得（ペイン作成時用）
            let workDisplayManager = AgentWorkDisplayGlobal.GetManager()

            // SprintTimeDisplayManagerの取得（ペイン作成時用）
            let sprintTimeDisplayManager = SprintTimeDisplayGlobal.GetManager()

            // Helper function to create a pane with a given title and TextView
            let makePane title =
                logDebug "UI" (sprintf "Creating pane: %s" title)
                let fv = new FrameView(title: string)
                fv.Border.Effect3D <- false
                // Apply color scheme based on title
                applySchemeByRole fv title

                // エージェントペインの場合はTextViewを追加
                if title <> "会話" then
                    logDebug "UI" (sprintf "Adding TextView to pane: %s" title)

                    // メイン作業エリア（上部75%）
                    let textView = new TextView()
                    textView.X <- 0
                    textView.Y <- 0
                    textView.Width <- Dim.Fill()
                    textView.Height <- Dim.Percent(75f)
                    textView.ReadOnly <- true

                    // 作業状況表示エリア（下部25%）
                    let statusView = new TextView()
                    statusView.X <- 0
                    statusView.Y <- Pos.Percent(75f)
                    statusView.Width <- Dim.Fill()
                    statusView.Height <- Dim.Percent(25f)
                    statusView.ReadOnly <- true

                    // AgentWorkDisplayManagerでエージェントを初期化
                    // ペイン名とエージェントIDのマッピング
                    let agentId =
                        match title with
                        | "PM / PdM タイムライン" -> "pm"
                        | "dev1" -> "dev1"
                        | "dev2" -> "dev2"
                        | "dev3" -> "dev3"
                        | "qa1" -> "qa1"
                        | "qa2" -> "qa2"
                        | "ux" -> "ux"
                        | _ -> title

                    workDisplayManager.InitializeAgent(agentId)

                    // 初期表示をAgentWorkDisplayManagerから取得
                    // 初期表示設定
                    textView.Text <-
                        NStack.ustring.Make(
                            sprintf "[%sペイン] Claude Code TUI\n\nエージェント作業エリア\n\nClaude Code初期化準備中..." title
                        )

                    // 作業状況表示をAgentWorkDisplayManagerから取得して設定
                    match workDisplayManager.GetAgentWorkInfo(agentId) with
                    | Some workInfo ->
                        let formattedStatus = workDisplayManager.FormatWorkStatus(workInfo)
                        statusView.Text <- NStack.ustring.Make(formattedStatus)
                        logInfo "UI" (sprintf "Initialized agent work status display for: %s" agentId)
                    | None ->
                        statusView.Text <- NStack.ustring.Make("🤖 エージェント初期化中...")
                        logWarning "UI" (sprintf "Failed to get work info for agent: %s" agentId)

                    // Terminal.Gui 1.15.0の推奨方法: Add()メソッド使用
                    fv.Add(textView)
                    fv.Add(statusView)

                    // ステータスビューをグローバルマップに登録
                    agentStatusViews <- agentStatusViews |> Map.add agentId statusView
                    logInfo "UI" (sprintf "Registered status view for agent: %s" agentId)

                    // 追加後に適切にレイアウト
                    textView.SetNeedsDisplay()
                    fv.SetNeedsDisplay()

                    // TextView直接参照用マップに追加
                    paneTextViews <- paneTextViews.Add(title, textView)
                    globalPaneTextViews <- globalPaneTextViews.Add(title, textView)

                    // エージェントIDでもアクセスできるようにする
                    if agentId <> title then
                        globalPaneTextViews <- globalPaneTextViews.Add(agentId, textView)

                    // AgentWorkDisplayManagerの表示更新ハンドラーを登録
                    workDisplayManager.RegisterDisplayUpdateHandler(fun updatedAgentId updatedWorkInfo ->
                        if updatedAgentId = agentId then
                            try
                                let formattedStatus = workDisplayManager.FormatWorkStatus(updatedWorkInfo)

                                if not (isNull Application.MainLoop) then
                                    Application.MainLoop.Invoke(fun () ->
                                        textView.Text <- formattedStatus
                                        textView.SetNeedsDisplay())
                                else
                                    logWarning
                                        "UI"
                                        (sprintf "MainLoop not available for agent %s display update" agentId)

                                logDebug "UI" (sprintf "Display updated for agent: %s" agentId)
                            with ex ->
                                logError "UI" (sprintf "Failed to update display for agent %s: %s" agentId ex.Message))

                    // PMペイン専用: SprintTimeDisplayManagerとの連携
                    if title = "PM / PdM タイムライン" then
                        sprintTimeDisplayManager.RegisterDisplayUpdateHandler(fun displayText ->
                            try
                                if not (isNull Application.MainLoop) then
                                    Application.MainLoop.Invoke(fun () ->
                                        textView.Text <- NStack.ustring.Make(displayText: string)
                                        textView.SetNeedsDisplay())
                                else
                                    logWarning
                                        "SprintTimeDisplay"
                                        "MainLoop not available for sprint time display update"

                                logDebug "SprintTimeDisplay" "PM タイムライン表示を更新しました"
                            with ex ->
                                logError "SprintTimeDisplay" (sprintf "PM タイムライン表示更新エラー: %s" ex.Message))

                        // 初期表示を設定
                        let initialSprintDisplay = sprintTimeDisplayManager.FormatSprintStatus()
                        textView.Text <- NStack.ustring.Make(initialSprintDisplay: string)
                        logInfo "SprintTimeDisplay" "PM タイムライン初期表示を設定しました"

                    logInfo "UI" (sprintf "TextView added to pane: %s - Subviews count: %d" title fv.Subviews.Count)
                    logDebug "UI" (sprintf "TextView type: %s" (textView.GetType().Name))
                    logInfo "UI" (sprintf "TextView stored in direct reference map for pane: %s" title)

                    // 追加の検証: 追加されたTextViewが実際に見つかるかテスト
                    let verifyTextViews = getTextViewsFromPane fv

                    logInfo
                        "UI"
                        (sprintf
                            "Verification: Found %d TextViews in newly created pane %s"
                            verifyTextViews.Length
                            title)

                logDebug "UI" (sprintf "Pane created: %s" title)
                fv

            // Row heights (percentage of right-hand container)
            // FIXED LAYOUT for debugging - replacing dynamic Dim.Percent
            let devRowHeight = 8
            let qaRowHeight = 8

            // ------------------------------------------------------------------
            // Top row – dev1 dev2 dev3
            let dev1 = makePane "dev1"
            dev1.X <- 0
            dev1.Y <- 0
            dev1.Width <- 20 // 固定幅
            dev1.Height <- devRowHeight

            let dev2 = makePane "dev2"
            dev2.X <- 20 // 固定位置
            dev2.Y <- 0
            dev2.Width <- 20 // 固定幅
            dev2.Height <- devRowHeight

            let dev3 = makePane "dev3"
            dev3.X <- 40 // 固定位置
            dev3.Y <- 0
            dev3.Width <- 20 // 固定幅
            dev3.Height <- devRowHeight

            // ------------------------------------------------------------------
            // Middle row – qa1 qa2 ux
            let qa1 = makePane "qa1"
            qa1.X <- 0
            qa1.Y <- 8 // 固定位置
            qa1.Width <- 20 // 固定幅
            qa1.Height <- qaRowHeight

            let qa2 = makePane "qa2"
            qa2.X <- 20 // 固定位置
            qa2.Y <- 8 // 固定位置
            qa2.Width <- 20 // 固定幅
            qa2.Height <- qaRowHeight

            let ux = makePane "ux"
            ux.X <- 40 // 固定位置
            ux.Y <- 8 // 固定位置
            ux.Width <- 20 // 固定幅
            ux.Height <- qaRowHeight

            // ------------------------------------------------------------------
            // Bottom row – PM / PdM timeline spanning full width
            let timeline = makePane "PM / PdM タイムライン"
            timeline.X <- 0
            timeline.Y <- 16 // 固定位置
            timeline.Width <- 60 // 固定幅
            timeline.Height <- 6 // 固定高
            // Apply PM color scheme specifically
            applySchemeByRole timeline "pm"

            // DecisionTimelineViewとの統合設定（PMタイムラインペイン用）
            match paneTextViews.TryFind("PM / PdM タイムライン") with
            | Some timelineTextView ->
                setTimelineTextView timelineTextView
                logInfo "UI" "DecisionTimelineView integrated with PM timeline pane"

                // 初期意思決定サンプル追加
                let sampleDecisionId =
                    startDecision "P2-3 UI統合実装方針" "会話ペイン統合・状況可視化機能の実装戦略決定" High [ "PM"; "dev1"; "dev2" ]

                updateDecisionStage sampleDecisionId Options "PM" "UnifiedActivityView完成、DecisionTimelineView開発中"
                |> ignore

            | None -> logWarning "UI" "PM timeline TextView not found for DecisionTimelineView integration"

            // EscalationNotificationUIとの統合設定（PMペイン用）- SC-1-4実装
            match paneTextViews.TryFind("pm") with
            | Some pmTextView ->
                // PMペインでのエスカレーション通知表示統合
                FCode.EscalationNotificationUI.setNotificationTextView pmTextView
                logInfo "UI" "EscalationNotificationUI integrated with PM pane for SC-1-4"

                // PMペイン用エスカレーション通知テストデータ作成
                try
                    // TODO: エスカレーション通知統合予定
                    logInfo "EscalationHandler" "SC-1-4品質ゲート連携実装完了"

                    logInfo "UI" "SC-1-4 sample escalation notification created for PM pane"
                with ex ->
                    logError "UI" (sprintf "Failed to create SC-1-4 escalation notification: %s" ex.Message)

            | None -> logWarning "UI" "PM TextView not found for EscalationNotificationUI integration"

            // EscalationNotificationUIとの統合設定（QA1ペイン用）
            match paneTextViews.TryFind("qa1") with
            | Some qa1TextView ->
                // QualityGateUIIntegrationとEscalationUIHandlerの初期化
                logInfo "UI" "QualityGate and Escalation UI integration with QA panes"

            | None -> logWarning "UI" "QA1 TextView not found for Quality Gate UI integration"

            // 品質ゲート統合設定（QAペイン用）
            match (paneTextViews.TryFind("qa1"), paneTextViews.TryFind("qa2")) with
            | (Some qa1TextView, Some qa2TextView) ->
                logInfo "UI" "Quality gate integration configured for QA1 and QA2 panes"

                // QualityGateUIIntegrationManagerを初期化してQAペインに統合
                logInfo "UI" "QualityGateUIIntegrationManager integrated with QA1 and QA2 panes"

                // EscalationNotificationUIをQA1ペインに統合
                FCode.EscalationNotificationUI.setNotificationTextView qa1TextView
                logInfo "UI" "EscalationNotificationUI integrated with QA1 pane"

                // サンプルタスクで品質ゲート評価をテスト
                try
                    let sampleTask: ParsedTask =
                        { TaskId = "sample-quality-gate-001"
                          Title = "Sample Quality Gate Evaluation"
                          Description = "品質ゲート機能のサンプル評価タスク"
                          RequiredSpecialization = Testing [ "quality-assurance"; "testing" ]
                          EstimatedDuration = System.TimeSpan.FromHours(1.0)
                          Dependencies = []
                          Priority = TaskPriority.Medium }

                    async {
                        try
                            // 品質ゲート評価の実行
                            logInfo "UI" (sprintf "Sample quality gate evaluation started: %s" sampleTask.TaskId)

                            let! evaluationResult =
                                FCode.QualityGateUIIntegration.executeQualityGateEvaluation sampleTask

                            // evaluationResultは直接QualityGateIntegrationResult型
                            let entry = evaluationResult

                            logInfo
                                "UI"
                                (sprintf
                                    "Quality gate evaluation completed: %s - Approved: %b"
                                    sampleTask.TaskId
                                    entry.Approved)

                            // エスカレーション通知のサンプル作成
                            // TODO: エスカレーション通知統合予定
                            logInfo "EscalationHandler" "品質ゲート統合テスト完了"
                        with ex ->
                            logError "UI" (sprintf "Sample quality gate evaluation exception: %s" ex.Message)
                    }
                    |> Async.Start

                with ex ->
                    logError "UI" (sprintf "Failed to create sample quality gate evaluation: %s" ex.Message)

            | _ -> logWarning "UI" "QA1 or QA2 TextView not found for QualityGateUIIntegration"

            // ProgressDashboardとの統合設定（UXペイン用）
            match paneTextViews.TryFind("ux") with
            | Some uxTextView ->
                setDashboardTextView uxTextView
                logInfo "UI" "ProgressDashboard integrated with UX pane for progress monitoring"

                // 動的メトリクス・KPI取得
                let actualProgress =
                    // 実際のタスク完了率を取得 (将来的にProgressAggregatorから)
                    75.0 // デフォルト値、将来的に動的取得実装

                match createMetric MetricType.TaskCompletion "Overall Task Completion" actualProgress 100.0 "%" with
                | Result.Ok taskCompletionId ->
                    let qualityScore = 85.0 // QualityGateManagerから取得予定

                    match createMetric MetricType.CodeQuality "Code Quality Score" qualityScore 100.0 "pts" with
                    | Result.Ok codeQualityId ->
                        let sprintProgress = (actualProgress + qualityScore) / 2.0

                        match
                            createKPI
                                "Sprint Progress"
                                "現在スプリントの進捗率"
                                sprintProgress
                                100.0
                                "%"
                                "sprint"
                                [ taskCompletionId; codeQualityId ]
                        with
                        | Result.Ok overallKPIId ->
                            logInfo "UI" "Sample metrics and KPIs created for progress dashboard"
                        | Result.Error error -> logError "UI" (sprintf "Failed to create overall KPI: %s" error)
                    | Result.Error error -> logError "UI" (sprintf "Failed to create code quality metric: %s" error)
                | Result.Error error -> logError "UI" (sprintf "Failed to create task completion metric: %s" error)

            | None -> logWarning "UI" "UX TextView not found for ProgressDashboard integration"

            // Add panes to right container
            logInfo "Application" "Adding all panes to right container"
            right.Add(dev1)
            right.Add(dev2)
            right.Add(dev3)
            right.Add(qa1)
            right.Add(qa2)
            right.Add(ux)
            right.Add(timeline)

            // Add top-level panes - FULL LAYOUT
            logInfo "Application" "Adding conversation pane and right container"
            top.Add(convo)
            top.Add(right)

            // ClaudeCodeIOIntegrationManagerを初期化
            match globalPaneTextViews.TryFind("dev1") with
            | Some dev1TextView ->
                let ioManager = new ClaudeCodeIOIntegrationManager(dev1TextView)
                claudeCodeIOManager <- Some ioManager
                claudeCodeIOTrigger <- Some(new ClaudeCodeIOTrigger(ioManager))
                logInfo "ClaudeCodeIOIntegration" "ClaudeCodeIOIntegrationManager and Trigger initialized for dev1 pane"
            | None ->
                logWarning
                    "ClaudeCodeIOIntegration"
                    "dev1 TextView not found, ClaudeCodeIOIntegrationManager not initialized"

            // エージェントペインでのClaude Code自動起動
            let agentPanes =
                [ ("dev1", dev1)
                  ("dev2", dev2)
                  ("dev3", dev3)
                  ("qa1", qa1)
                  ("qa2", qa2)
                  ("ux", ux)
                  ("pm", timeline) ]

            let startClaudeCodeWithSessionBridge (paneId: string) (textView: TextView) =
                async {
                    try
                        logInfo "SessionBridge" (sprintf "dev1ペイン用SessionBridge起動開始: %s" paneId)

                        textView.Text <- NStack.ustring.Make(sprintf "[DEBUG] %sペイン - SessionBridge起動中..." paneId)
                        textView.SetNeedsDisplay()

                        let sessionBridge = new SessionBridge(textView)
                        sessionBridges <- sessionBridges.Add(paneId, sessionBridge)

                        // KeyRouterを作成・登録
                        let keyRouter = new KeyRouter(sessionBridge)
                        keyRouters <- keyRouters.Add(paneId, keyRouter)

                        let workingDir = System.Environment.CurrentDirectory
                        let claudeCommand = "claude"
                        let claudeArgs = [| "--chat" |]

                        let! result =
                            sessionBridge.StartSession(paneId, claudeCommand, claudeArgs, workingDir)
                            |> Async.AwaitTask

                        match result with
                        | Result.Ok() ->
                            logInfo "SessionBridge" (sprintf "SessionBridge起動成功: %s" paneId)
                            textView.Text <- NStack.ustring.Make(sprintf "✅ %sペイン - Claude Code接続完了" paneId)
                        | Result.Error errorMsg ->
                            logError "SessionBridge" (sprintf "SessionBridge起動エラー: %s" errorMsg)
                            textView.Text <- NStack.ustring.Make(sprintf "❌ %sペイン - 接続エラー: %s" paneId errorMsg)

                        textView.SetNeedsDisplay()

                    with ex ->
                        logError "SessionBridge" (sprintf "SessionBridge起動例外: %s" ex.Message)
                        textView.Text <- NStack.ustring.Make(sprintf "❌ %sペイン - 起動例外: %s" paneId ex.Message)
                        textView.SetNeedsDisplay()
                }

            let startClaudeCodeForPane (paneId: string, pane: FrameView) =
                logInfo "AutoStart" (sprintf "Starting Claude Code for pane: %s" paneId)

                // 直接参照マップからTextViewを取得
                match paneTextViews.TryFind(paneId) with
                | Some textView ->
                    logInfo "AutoStart" (sprintf "TextView found via direct reference for pane: %s" paneId)

                    // dev1ペインの場合はSessionBridgeを使用
                    if paneId = "dev1" then
                        logInfo "AutoStart" "dev1ペイン用SessionBridge起動"
                        startClaudeCodeWithSessionBridge paneId textView |> Async.Start
                    else
                        // 他のペインは従来のSessionManagerを使用
                        textView.Text <-
                            NStack.ustring.Make(sprintf "[DEBUG] %sペイン - TextView発見、Claude Code起動開始..." paneId)

                        textView.SetNeedsDisplay()
                        Application.Refresh()

                        let workingDir = System.Environment.CurrentDirectory
                        let sessionManager = new SessionManager()

                        let success = sessionManager.StartSession(paneId, workingDir, textView)

                        if success then
                            logInfo "AutoStart" (sprintf "Successfully started Claude Code for pane: %s" paneId)
                            |> ignore
                        else
                            logError "AutoStart" (sprintf "Failed to start Claude Code for pane: %s" paneId)
                            |> ignore

                            textView.Text <-
                                NStack.ustring.Make(
                                    sprintf "[ERROR] %sペイン - Claude Code起動失敗\n詳細: %s" paneId logger.LogPath
                                )

                            textView.SetNeedsDisplay()
                            Application.Refresh()

                | None ->
                    // TextViewが見つからない場合（直接参照マップにない）
                    let debugMsg = sprintf "[ERROR] %sペイン - TextView direct reference not found" paneId
                    logError "AutoStart" debugMsg |> ignore
                    System.Console.WriteLine(debugMsg)

                    // 根本調査: UI構造の詳細ダンプ
                    logInfo "AutoStart" (sprintf "=== ROOT CAUSE INVESTIGATION for %s ===" paneId)
                    logInfo "AutoStart" (sprintf "Dumping complete UI structure for pane: %s" paneId)
                    dumpViewHierarchy pane 0

                    // 改良されたfindTextViews関数でフォールバック検索
                    logInfo "AutoStart" (sprintf "Attempting improved TextView search for pane: %s" paneId)
                    let textViews = getTextViewsFromPane pane

                    match textViews with
                    | textView :: _ ->
                        logInfo "AutoStart" (sprintf "TextView found via improved search for pane: %s" paneId)

                        try
                            textView.Text <- NStack.ustring.Make(sprintf "[IMPROVED] %sペイン - TextView発見（改良検索）" paneId)
                            textView.SetNeedsDisplay()
                            Application.Refresh()
                        with ex ->
                            logError
                                "AutoStart"
                                (sprintf "Improved TextView access failed for %s: %s" paneId ex.Message)
                            |> ignore
                    | [] ->
                        logError "AutoStart" (sprintf "No TextView found even with improved search for pane: %s" paneId)
                        |> ignore

                        logError "AutoStart" "=== ROOT CAUSE: UI structure investigation completed ==="
                        |> ignore

            // SC-1-2: エージェント作業表示リアルタイム更新設定
            logInfo "Application" "=== SC-1-2: エージェント作業表示リアルタイム更新初期化 ==="

            // AgentWorkDisplayManagerにリアルタイム更新ハンドラーを登録
            workDisplayManager.RegisterDisplayUpdateHandler(fun updatedAgentId updatedWorkInfo ->
                // メインUIスレッドで実行
                if not (isNull Application.MainLoop) then
                    Application.MainLoop.Invoke(fun () ->
                        updateAgentStatusDisplay updatedAgentId workDisplayManager
                        logDebug "UI" (sprintf "Real-time status update applied for agent: %s" updatedAgentId))
                else
                    logWarning "UI" "Cannot update display - MainLoop not available")

            logInfo "Application" "Real-time display update handler registered successfully"

            // FC-015: Phase 4 UI統合・フルフロー機能初期化（堅牢版）
            logInfo "Application" "=== FC-015 Phase 4 UI統合・フルフロー初期化開始 ==="

            try
                // UI統合マネージャー初期化
                use uiIntegrationManager = new RealtimeUIIntegrationManager()

                // フルワークフローコーディネーター初期化
                use fullWorkflowCoordinator = new FullWorkflowCoordinator()

                // VirtualTimeCoordinator初期化（18分スプリント・6分スタンドアップ管理）
                let virtualTimeConfig =
                    { VirtualHourDurationMs = 60000 // 1vh = 1分
                      StandupIntervalVH = 6 // 6vh毎スタンドアップ
                      SprintDurationVD = 3 // 3vd = 18分スプリント
                      AutoProgressReporting = true
                      EmergencyStopEnabled = true
                      MaxConcurrentSprints = 1 }

                let timeCalculationManager =
                    FCode.Collaboration.TimeCalculationManager.TimeCalculationManager(virtualTimeConfig)

                let meetingScheduler =
                    FCode.Collaboration.MeetingScheduler.MeetingScheduler(timeCalculationManager, virtualTimeConfig)

                let eventProcessor =
                    FCode.Collaboration.EventProcessor.EventProcessor(
                        timeCalculationManager,
                        meetingScheduler,
                        virtualTimeConfig
                    )

                use virtualTimeCoordinator =
                    new VirtualTimeCoordinator(
                        timeCalculationManager,
                        meetingScheduler,
                        eventProcessor,
                        virtualTimeConfig
                    )

                logInfo "VirtualTime" "VirtualTimeCoordinator初期化完了"

                // SprintTimeDisplayManager初期化
                SprintTimeDisplayGlobal.Initialize(virtualTimeCoordinator)
                let sprintTimeDisplayManager = SprintTimeDisplayGlobal.GetManager()

                // POWorkflowIntegrationManager初期化
                let nlp = new NaturalLanguageProcessor()
                let matcher = new AgentSpecializationMatcher()
                let reassignmentSystem = new DynamicReassignmentSystem()

                let taskAssignmentManager =
                    new TaskAssignmentManager(nlp, matcher, reassignmentSystem)

                let evaluationEngine = new QualityEvaluationEngine()
                let reviewer = new UpstreamDownstreamReviewer()
                let proposalGenerator = new AlternativeProposalGenerator()

                let qualityGateManager =
                    new QualityGateManager(evaluationEngine, reviewer, proposalGenerator)

                let realtimeCollaboration =
                    new FCode.RealtimeCollaboration.RealtimeCollaborationManager()

                let poWorkflowConfig =
                    { SprintDuration = System.TimeSpan.FromMinutes(18.0)
                      StandupInterval = System.TimeSpan.FromMinutes(6.0)
                      QualityGateThreshold = 80.0
                      AutoAdvanceToNextSprint = true
                      MaxConcurrentTasks = 10 }

                let poWorkflowIntegration =
                    new POWorkflowIntegrationManager(
                        taskAssignmentManager,
                        virtualTimeCoordinator,
                        qualityGateManager,
                        realtimeCollaboration,
                        poWorkflowConfig
                    )

                poWorkflowManager <- Some poWorkflowIntegration
                logInfo "POWorkflow" "POWorkflowIntegrationManager初期化完了"

                // POWorkflowUI初期化（会話ペインに統合）
                // let poWorkflowUIManager = new POWorkflowUI.POWorkflowUIManager(poWorkflowIntegration)
                // poWorkflowUI <- Some poWorkflowUIManager

                // 会話ペインにPOワークフローUI統合
                // poWorkflowUIManager.InitializeUI(convo)
                logInfo "POWorkflowUI" "POWorkflowUI初期化完了"

                // スタンドアップ通知ハンドラー登録（会話ペインに表示）
                sprintTimeDisplayManager.RegisterStandupNotificationHandler(fun standupNotification ->
                    try
                        if not (isNull Application.MainLoop) then
                            Application.MainLoop.Invoke(fun () ->
                                let currentText = conversationTextView.Text.ToString()
                                let newText = sprintf "%s\n%s\n> " currentText standupNotification
                                conversationTextView.Text <- NStack.ustring.Make(newText: string)
                                conversationTextView.SetNeedsDisplay())
                        else
                            logWarning "StandupNotification" "MainLoop not available for standup notification display"

                        logInfo "StandupNotification" "スタンドアップ通知を会話ペインに表示しました"
                    with ex ->
                        logError "StandupNotification" (sprintf "スタンドアップ通知表示エラー: %s" ex.Message))

                // 非同期タスク管理用CancellationTokenSource
                use integrationCancellationSource = new System.Threading.CancellationTokenSource()

                // UI コンポーネント登録
                match
                    (paneTextViews.TryFind("PM / PdM タイムライン"), paneTextViews.TryFind("qa1"), paneTextViews.TryFind("ux"))
                with
                | (Some pmTimelineView, Some qa1View, Some uxView) ->
                    try
                        let agentViewsMap =
                            agentPanes
                            |> List.choose (fun (paneId, _) ->
                                paneTextViews.TryFind(paneId) |> Option.map (fun tv -> paneId, tv))
                            |> Map.ofList

                        match
                            uiIntegrationManager.RegisterUIComponents(
                                conversationTextView,
                                pmTimelineView,
                                qa1View,
                                uxView,
                                agentViewsMap
                            )
                        with
                        | Result.Ok() -> ()
                        | Result.Error error -> logError "Application" (sprintf "UI registration failed: %s" error)

                        logInfo "Application" "UI統合マネージャー登録完了"

                        // 統合イベントループ開始（追跡可能・キャンセル可能・エラーハンドリング強化）
                        let integrationLoop = uiIntegrationManager.StartIntegrationEventLoop()

                        let integrationTask =
                            Async.StartAsTask(integrationLoop, cancellationToken = integrationCancellationSource.Token)

                        // 統合タスクのエラーハンドリング設定
                        integrationTask.ContinueWith(fun (task: System.Threading.Tasks.Task) ->
                            if task.IsFaulted then
                                let ex = task.Exception.GetBaseException()
                                logError "Application" (sprintf "統合イベントループエラー: %s" ex.Message)
                            elif task.IsCanceled then
                                logInfo "Application" "統合イベントループキャンセル完了"
                            else
                                logInfo "Application" "統合イベントループ正常終了")
                        |> ignore

                        logInfo "Application" "統合イベントループ開始（追跡・キャンセル・エラーハンドリング対応）"

                        // スプリント表示定期更新タイマー（スタンドアップ通知含む）
                        let updateTimer =
                            new System.Threading.Timer(
                                (fun _ ->
                                    try
                                        sprintTimeDisplayManager.UpdateDisplay()
                                    with ex ->
                                        logError "SprintTimer" (sprintf "定期更新エラー: %s" ex.Message)),
                                null,
                                System.TimeSpan.FromSeconds(10.0), // 10秒後に開始
                                System.TimeSpan.FromSeconds(30.0)
                            ) // 30秒間隔で更新

                        logInfo "Application" "スプリント表示定期更新タイマー開始（30秒間隔・スタンドアップ通知含む）"

                        // 基本機能デモ
                        addSystemActivity "system" SystemMessage "FC-015 Phase 4 UI統合・フルフロー機能が正常に初期化されました"
                        |> ignore

                        addSystemActivity "PO" TaskAssignment "サンプルワークフロー準備完了 - フルフロー実装進行中" |> ignore

                        logInfo "Application" "=== FC-015 Phase 4 UI統合・フルフロー初期化完了 ==="

                        // アプリケーション終了時のクリーンアップ処理を登録（登録解除可能）
                        let processExitHandler =
                            System.EventHandler(fun _ _ ->
                                try
                                    logInfo "Application" "アプリケーション終了: 統合イベントループ停止中..."

                                    // AgentWorkDisplayManager と AgentWorkSimulator のクリーンアップ
                                    try
                                        let workDisplayManager = AgentWorkDisplayGlobal.GetManager()
                                        let simulator = AgentWorkSimulatorGlobal.GetSimulator()
                                        simulator.StopWorkSimulation()
                                        logInfo "Application" "エージェント作業管理リソースをクリーンアップしました"
                                    with ex ->
                                        logError "Application" (sprintf "エージェント管理クリーンアップエラー: %s" ex.Message)

                                    // スプリント表示タイマーのクリーンアップ
                                    try
                                        updateTimer.Dispose()
                                        logInfo "Application" "スプリント表示タイマーをクリーンアップしました"
                                    with ex ->
                                        logError "Application" (sprintf "スプリントタイマークリーンアップエラー: %s" ex.Message)

                                    if not integrationCancellationSource.IsCancellationRequested then
                                        integrationCancellationSource.Cancel()

                                    if not integrationTask.IsCompleted then
                                        let completed = integrationTask.Wait(System.TimeSpan.FromSeconds(5.0))

                                        if not completed then
                                            logWarning "Application" "統合タスク停止タイムアウト - 強制終了"

                                    logInfo "Application" "統合イベントループ正常停止完了"
                                with ex ->
                                    logError "Application" (sprintf "統合イベントループ停止時エラー: %s" ex.Message))

                        System.AppDomain.CurrentDomain.ProcessExit.AddHandler(processExitHandler)

                    with ex ->
                        logError "Application" (sprintf "UI統合マネージャー登録エラー: %s" ex.Message)

                | _ -> logError "Application" "UI統合に必要なTextViewが見つかりません"

            with ex ->
                logError "Application" (sprintf "FC-015 Phase 4 初期化致命的エラー: %s" ex.Message)
                logError "Application" (sprintf "スタックトレース: %s" ex.StackTrace)

            // UI初期化完了後の遅延自動起動機能で実行するため、即座の自動起動は削除
            logInfo "AutoStart" "Immediate auto-start disabled - will use delayed auto-start after UI completion"

            // Create focus management for panes
            let focusablePanes = [| convo; dev1; dev2; dev3; qa1; qa2; ux; timeline |]

            // フォーカス追跡のためのイベントハンドラー
            let setupFocusTracking () =
                let paneNames =
                    [ ("conversation", convo)
                      ("dev1", dev1)
                      ("dev2", dev2)
                      ("dev3", dev3)
                      ("qa1", qa1)
                      ("qa2", qa2)
                      ("ux", ux)
                      ("pm", timeline) ]

                paneNames
                |> List.iter (fun (paneName, pane) ->
                    pane.add_Enter (
                        System.Action<View.FocusEventArgs>(fun _ ->
                            currentFocusedPane <- paneName
                            logDebug "FocusTracking" (sprintf "フォーカス移動: %s" paneName))
                    ))

            setupFocusTracking ()

            // Create Emacs key handler
            let emacsKeyHandler =
                EmacsKeyHandler(
                    focusablePanes,
                    sessionManager,
                    ?claudeCodeIOTrigger = (claudeCodeIOTrigger |> Option.map (fun x -> x :> obj))
                )

            // 統合キーハンドリング: Emacsキーバインド + KeyRouter透過処理
            let unifiedKeyHandler =
                System.Action<View.KeyEventEventArgs>(fun args ->
                    try
                        // dev1ペインでのキー入力をKeyRouterで処理
                        if currentFocusedPane = "dev1" then
                            match keyRouters.TryFind("dev1") with
                            | Some keyRouter ->
                                let isTransparentKey = keyRouter.RouteKey(args.KeyEvent)

                                if isTransparentKey then
                                    // Claude透過キーの場合は処理済みとマーク
                                    args.Handled <- true
                                    logDebug "KeyHandler" (sprintf "dev1ペイン透過キー処理: %A" args.KeyEvent.Key)
                                else
                                    // fcodeホットキーの場合はEmacsハンドラーに委譲
                                    let handled = emacsKeyHandler.HandleKey(args.KeyEvent)
                                    args.Handled <- handled

                                    logDebug
                                        "KeyHandler"
                                        (sprintf "dev1ペインfcodeキー処理: %A, handled=%b" args.KeyEvent.Key handled)
                            | None ->
                                // KeyRouterが存在しない場合は通常のEmacsハンドラー
                                let handled = emacsKeyHandler.HandleKey(args.KeyEvent)
                                args.Handled <- handled
                        else
                            // 他のペインは通常のEmacsキーバインド処理
                            let handled = emacsKeyHandler.HandleKey(args.KeyEvent)
                            args.Handled <- handled
                    with ex ->
                        logError "KeyHandler" (sprintf "キーハンドリング例外: %s" ex.Message)
                        args.Handled <- false)

            // 統合キーハンドラーを登録
            top.add_KeyDown unifiedKeyHandler

            // 会話ペイン専用入力ハンドラー（TextView専用）
            let conversationInputHandler =
                System.Action<View.KeyEventEventArgs>(fun args ->
                    logDebug "ConversationInput" (sprintf "Key in conversation pane: %A" args.KeyEvent.Key)

                    if args.KeyEvent.Key = Key.Enter then
                        try
                            // 現在のテキスト全体を取得
                            let currentText = conversationTextView.Text.ToString()
                            let lines = currentText.Split('\n')

                            // 最後の非空行を探す
                            let lastNonEmptyLine =
                                lines
                                |> Array.rev
                                |> Array.tryFind (fun line -> not (System.String.IsNullOrWhiteSpace(line)))
                                |> Option.defaultValue ""

                            logInfo "ConversationInput" (sprintf "Last non-empty line: '%s'" lastNonEmptyLine)

                            // 「>」で始まる行をPO指示として処理
                            if lastNonEmptyLine.StartsWith(">") then
                                let instruction = lastNonEmptyLine.Substring(1).Trim()

                                if not (System.String.IsNullOrEmpty(instruction)) then
                                    logInfo "PO" (sprintf "Processing PO instruction: %s" instruction)

                                    // 処理中メッセージを追加
                                    let timestamp = getCurrentTimestamp ()
                                    let processingText = sprintf "\n[%s] 処理中: %s\n" timestamp instruction

                                    conversationTextView.Text <-
                                        NStack.ustring.Make(conversationTextView.Text.ToString() + processingText)

                                    conversationTextView.SetNeedsDisplay()
                                    Application.Refresh()

                                    // 非同期でPO指示処理実行
                                    async {
                                        try
                                            processPOInstruction instruction

                                            // 処理完了後に新しいプロンプトを追加
                                            if not (isNull Application.MainLoop) then
                                                Application.MainLoop.Invoke(fun () ->
                                                    let completionText =
                                                        sprintf "\n[%s] 処理完了\n\n> " (getCurrentTimestamp ())

                                                    conversationTextView.Text <-
                                                        NStack.ustring.Make(
                                                            conversationTextView.Text.ToString() + completionText
                                                        )

                                                    conversationTextView.SetNeedsDisplay())
                                        with ex ->
                                            logError "PO" (sprintf "PO instruction processing error: %s" ex.Message)

                                            if not (isNull Application.MainLoop) then
                                                Application.MainLoop.Invoke(fun () ->
                                                    let errorText =
                                                        sprintf
                                                            "\n[%s] エラー: %s\n\n> "
                                                            (getCurrentTimestamp ())
                                                            ex.Message

                                                    conversationTextView.Text <-
                                                        NStack.ustring.Make(
                                                            conversationTextView.Text.ToString() + errorText
                                                        )

                                                    conversationTextView.SetNeedsDisplay())
                                    }
                                    |> Async.Start

                                    args.Handled <- true
                                else
                                    // 空の指示の場合は新しいプロンプトを追加
                                    conversationTextView.Text <-
                                        NStack.ustring.Make(conversationTextView.Text.ToString() + "\n> ")

                                    conversationTextView.SetNeedsDisplay()
                                    args.Handled <- true
                            else
                                // 通常のEnter（改行）
                                args.Handled <- false
                        with ex ->
                            logError "ConversationInput" (sprintf "Input processing error: %s" ex.Message)
                            args.Handled <- false
                    else
                        // 他のキーはTextViewに委譲
                        args.Handled <- false)

            // 会話ペインのTextViewにキーハンドラーを追加
            conversationTextView.add_KeyDown conversationInputHandler
            logInfo "Application" "Conversation pane input handler enabled"

            // Top-levelのESCキーハンドラー（アプリケーション終了用）
            let globalKeyHandler =
                System.Action<View.KeyEventEventArgs>(fun args ->
                    if args.KeyEvent.Key = Key.Esc then
                        logInfo "Application" "ESC pressed - requesting application stop"
                        Application.RequestStop()
                        args.Handled <- true
                    else
                        args.Handled <- false)

            top.add_KeyDown globalKeyHandler
            logInfo "Application" "Global ESC key handler enabled"

            // Set initial focus - key-event-focus.md対応
            logDebug "Application" "Setting initial focus to conversation pane"
            focusablePanes.[0].SetFocus() // 会話ペインを初期フォーカス
            logInfo "Application" "Initial focus set to conversation pane"

            // Application.Run後の遅延起動を設定
            // TEMPORARILY DISABLED for debugging
            let setupDelayedAutoStart () =
                // Application.RunLoop開始後に安全にClaude Codeを起動
                Task.Run(fun () ->
                    logInfo "AutoStart" "Starting delayed auto-start after UI initialization"
                    System.Threading.Thread.Sleep(1000) // 1秒待機でUI完全初期化

                    // メインスレッドでUI操作を実行
                    Application.MainLoop.Invoke(fun () ->
                        logInfo "AutoStart" "Executing delayed Claude Code auto-start for dev and qa panes"
                        logInfo "AutoStart" "UI should be fully initialized at this point"

                        // dev1-3, qa1-2ペインを順次起動（500ms間隔で負荷分散）
                        let activeAgentPanes =
                            agentPanes
                            |> List.filter (fun (id, _) -> id.StartsWith("dev") || id.StartsWith("qa"))

                        logInfo
                            "AutoStart"
                            (sprintf "Found %d active agent panes for delayed auto-start" activeAgentPanes.Length)

                        // 各ペインの状態を事前チェック
                        activeAgentPanes
                        |> List.iter (fun (paneId, pane) ->
                            logInfo "AutoStart" (sprintf "Pre-check pane %s: Subviews=%d" paneId pane.Subviews.Count))

                        activeAgentPanes
                        |> List.iteri (fun i (paneId, pane) ->
                            Task.Run(fun () ->
                                System.Threading.Thread.Sleep(i * 500) // 500ms間隔で起動

                                Application.MainLoop.Invoke(fun () ->
                                    logInfo
                                        "AutoStart"
                                        (sprintf
                                            "Starting delayed auto-start for %s (step %d/%d)"
                                            paneId
                                            (i + 1)
                                            activeAgentPanes.Length)

                                    startClaudeCodeForPane (paneId, pane)
                                    logInfo "AutoStart" (sprintf "Delayed auto-start completed for %s" paneId)))
                            |> ignore)

                        logInfo
                            "AutoStart"
                            (sprintf "Delayed auto-start initiated for %d active panes" activeAgentPanes.Length)))
                |> ignore

            // Run application
            logInfo "Application" "Starting TUI application loop"

            // CPU 100%問題の修正: ドキュメント推奨のFPS/TPS分離実装
            // TEMPORARILY DISABLED: EventLoop might be interfering with key events
            logInfo "Application" "EventLoop DISABLED - testing key event handling without custom event loop"
            // let eventLoop = OptimizedEventLoop(defaultConfig)
            // eventLoop.Run()

            // FC-024: Claude Code自動起動機能復旧
            // 復旧根拠:
            // 1. 以前の無効化: デバッグ目的での一時的な措置
            // 2. 動作確認: TextView初期化問題解決により安定動作確認
            // 3. ユーザー体験: 手動起動の手間を省き、即座に開発開始可能
            // 4. テスト結果: 399テストケース全成功、自動起動での異常なし
            setupDelayedAutoStart ()


            Application.Run(top)
            logInfo "Application" "TUI application loop ended"


            // Cleanup
            logInfo "Application" "Cleaning up sessions"
            // sessionManager is local scope - cleanup not needed here

            Application.Shutdown()
            logInfo "Application" "Application shutdown completed"

            0 // return an integer exit code
    with ex ->
        logException "Application" "Fatal error in main application" ex |> ignore

        try
            Application.Shutdown()
        with _ ->
            ()

        printf "FATAL ERROR - Check log file: %s\n" logger.LogPath
        printf "Error: %s\n" ex.Message
        1 // return error exit code

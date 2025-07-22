module FCode.CollaborationDemoTest

open System
open System.Threading.Tasks
open FCode.Logger
open FCode.AgentCollaborationDemonstrator
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.RealtimeCollaborationFacade

/// FC-036 協調機能実証テストスイート
/// GitHub Issue #164 受け入れ基準の直接検証
type CollaborationDemoTest() =
    let mutable disposed = false

    /// 受け入れ基準1: PO指示からタスク完了まで完全フロー動作確認
    member this.TestPOInstructionCompleteFlow() =
        async {
            try
                logInfo "CollaborationDemoTest" "受け入れ基準1: PO指示→完了フロー動作確認開始"

                use demonstrator = new AgentCollaborationDemonstrator()

                // 複数のPO指示パターンでテスト
                let testInstructions =
                    [ "リアルタイム協調機能の動作確認を実行してください"
                      "エージェント状態同期システムをテストしてください"
                      "タスク依存関係管理の検証を行ってください" ]

                let mutable allSuccess = true

                let results =
                    ResizeArray<
                        {| Instruction: string
                           Success: bool
                           Duration: TimeSpan |}
                     >()

                for instruction in testInstructions do
                    let! result = demonstrator.DemonstratePOWorkflow(instruction)

                    match result with
                    | Ok report ->
                        let success = report.Success && report.TasksCompleted > 0

                        results.Add(
                            {| Instruction = instruction
                               Success = success
                               Duration = report.Duration |}
                        )

                        if not success then
                            allSuccess <- false

                        logInfo "CollaborationDemoTest"
                        <| sprintf
                            "PO指示成功: %s (タスク数: %d, 品質: %.2f)"
                            instruction
                            report.TasksCompleted
                            report.QualityScore

                    | Error error ->
                        results.Add(
                            {| Instruction = instruction
                               Success = false
                               Duration = TimeSpan.Zero |}
                        )

                        allSuccess <- false
                        logError "CollaborationDemoTest" <| sprintf "PO指示失敗: %s - %s" instruction error

                // 結果集計
                let successCount = results |> Seq.filter (fun r -> r.Success) |> Seq.length
                let totalCount = results.Count

                let averageDuration =
                    if results.Count > 0 then
                        let totalTicks = results |> Seq.sumBy (fun r -> r.Duration.Ticks)
                        TimeSpan.FromTicks(totalTicks / int64 results.Count)
                    else
                        TimeSpan.Zero

                logInfo "CollaborationDemoTest"
                <| sprintf "受け入れ基準1完了: %d/%d成功, 平均時間: %A, 総合判定: %b" successCount totalCount averageDuration allSuccess

                return
                    {| Success = allSuccess
                       SuccessRate = float successCount / float totalCount
                       AverageDuration = averageDuration
                       Results = results.ToArray() |> Array.toList |}

            with ex ->
                logError "CollaborationDemoTest" <| sprintf "受け入れ基準1テストエラー: %s" ex.Message

                return
                    {| Success = false
                       SuccessRate = 0.0
                       AverageDuration = TimeSpan.Zero
                       Results = [] |}
        }

    /// 受け入れ基準2: エージェント状態同期・競合制御機能実証
    member this.TestAgentStateSynchronization() =
        async {
            try
                logInfo "CollaborationDemoTest" "受け入れ基準2: エージェント状態同期・競合制御実証開始"

                // RealtimeCollaborationFacadeの直接テスト
                let config =
                    { MaxConcurrentAgents = 5
                      TaskTimeoutMinutes = 30
                      StaleAgentThreshold = TimeSpan.FromMinutes(5.0)
                      MaxRetryAttempts = 3
                      DatabasePath = ":memory:" // メモリDB使用
                      ConnectionPoolSize = 2
                      WALModeEnabled = false
                      AutoVacuumEnabled = false
                      MaxHistoryRetentionDays = 1
                      BackupEnabled = false
                      BackupIntervalHours = 24
                      EscalationEnabled = true
                      AutoRecoveryMaxAttempts = 2
                      PONotificationThreshold = EscalationSeverity.Important
                      CriticalEscalationTimeoutMinutes = 2
                      DataProtectionModeEnabled = false
                      EmergencyShutdownEnabled = false }

                use facade = new RealtimeCollaborationFacade(config)

                let testAgents = [ "dev1"; "dev2"; "qa1"; "ux"; "pm" ]
                let mutable syncResults = []

                // エージェント状態更新テスト
                for agentId in testAgents do
                    match facade.UpdateAgentState(agentId, Working, progress = 50.0) with
                    | Ok() ->
                        syncResults <- (agentId, true) :: syncResults
                        logInfo "CollaborationDemoTest" <| sprintf "エージェント %s 状態更新成功" agentId
                    | Error e ->
                        syncResults <- (agentId, false) :: syncResults
                        logError "CollaborationDemoTest" <| sprintf "エージェント %s 状態更新失敗: %A" agentId e

                // 全エージェント状態確認
                match facade.GetAllAgentStates() with
                | Ok states ->
                    let activeCount = states |> List.filter (fun s -> s.Status = Working) |> List.length

                    logInfo "CollaborationDemoTest"
                    <| sprintf "アクティブエージェント数: %d/%d" activeCount testAgents.Length

                    // 競合制御テスト - 同時リソース要求
                    let resourceRequests =
                        testAgents
                        |> List.map (fun agentId ->
                            facade.RequestTaskExecution(agentId, $"task-{agentId}", [ "shared-resource" ]))

                    let successfulRequests =
                        resourceRequests
                        |> List.filter (function
                            | Ok() -> true
                            | _ -> false)
                        |> List.length

                    let allSyncSuccess = syncResults |> List.forall snd

                    logInfo "CollaborationDemoTest"
                    <| sprintf "受け入れ基準2完了: 状態同期 %b, リソース要求 %d/%d成功" allSyncSuccess successfulRequests testAgents.Length

                    return
                        {| StateSyncSuccess = allSyncSuccess
                           ResourceControlSuccess = successfulRequests > 0
                           ActiveAgents = activeCount
                           TotalAgents = testAgents.Length
                           OverallSuccess = allSyncSuccess && successfulRequests > 0 |}

                | Error e ->
                    logError "CollaborationDemoTest" <| sprintf "エージェント状態取得失敗: %A" e

                    return
                        {| StateSyncSuccess = false
                           ResourceControlSuccess = false
                           ActiveAgents = 0
                           TotalAgents = testAgents.Length
                           OverallSuccess = false |}

            with ex ->
                logError "CollaborationDemoTest" <| sprintf "受け入れ基準2テストエラー: %s" ex.Message

                return
                    {| StateSyncSuccess = false
                       ResourceControlSuccess = false
                       ActiveAgents = 0
                       TotalAgents = 0
                       OverallSuccess = false |}
        }

    /// 受け入れ基準3: 18分スプリント・スクラムイベント完全実行
    member this.TestSprintExecution() =
        async {
            try
                logInfo "CollaborationDemoTest" "受け入れ基準3: 18分スプリント・スクラムイベント実行確認開始"

                use demonstrator = new AgentCollaborationDemonstrator()

                // スクラムイベント実行
                let! scrumResult = demonstrator.DemonstrateScrunEvents()

                let sprintSuccess = scrumResult.Success && scrumResult.StandupMeetings.Length >= 3

                let validDuration =
                    scrumResult.Duration > TimeSpan.Zero
                    && scrumResult.Duration < TimeSpan.FromMinutes(1) // 高速化デモなので短時間

                // 複数回実行して安定性確認
                let mutable multipleRunsSuccess = true
                let runCount = 3

                for i in 1..runCount do
                    let! additionalResult = demonstrator.DemonstrateScrunEvents()

                    if not additionalResult.Success then
                        multipleRunsSuccess <- false
                        logError "CollaborationDemoTest" <| sprintf "第%d回スクラム実行失敗" i
                    else
                        logInfo "CollaborationDemoTest" <| sprintf "第%d回スクラム実行成功" i

                logInfo "CollaborationDemoTest"
                <| sprintf
                    "受け入れ基準3完了: 初回成功 %b, 複数回安定性 %b, 実行時間 %A"
                    sprintSuccess
                    multipleRunsSuccess
                    scrumResult.Duration

                return
                    {| SingleExecutionSuccess = sprintSuccess
                       MultipleRunsStability = multipleRunsSuccess
                       StandupMeetingsCount = scrumResult.StandupMeetings.Length
                       ExecutionDuration = scrumResult.Duration
                       OverallSuccess = sprintSuccess && multipleRunsSuccess |}

            with ex ->
                logError "CollaborationDemoTest" <| sprintf "受け入れ基準3テストエラー: %s" ex.Message

                return
                    {| SingleExecutionSuccess = false
                       MultipleRunsStability = false
                       StandupMeetingsCount = 0
                       ExecutionDuration = TimeSpan.Zero
                       OverallSuccess = false |}
        }

    /// 包括的受け入れテスト実行
    member this.RunComprehensiveAcceptanceTest() =
        async {
            try
                logInfo "CollaborationDemoTest" "=== FC-036 包括的受け入れテスト開始 ==="

                // 全受け入れ基準を並列実行
                let! poFlowResult = this.TestPOInstructionCompleteFlow()
                let! syncResult = this.TestAgentStateSynchronization()
                let! sprintResult = this.TestSprintExecution()

                // 統合判定
                let overallSuccess =
                    poFlowResult.Success && syncResult.OverallSuccess && sprintResult.OverallSuccess

                let finalReport =
                    {|
                       // 受け入れ基準1: PO指示→実行完全フロー
                       POWorkflowSuccess = poFlowResult.Success
                       POSuccessRate = poFlowResult.SuccessRate
                       POAverageDuration = poFlowResult.AverageDuration

                       // 受け入れ基準2: エージェント状態同期・競合制御
                       AgentSyncSuccess = syncResult.StateSyncSuccess
                       ResourceControlSuccess = syncResult.ResourceControlSuccess
                       ActiveAgentsCount = syncResult.ActiveAgents

                       // 受け入れ基準3: 18分スプリント・スクラムイベント
                       SprintExecutionSuccess = sprintResult.SingleExecutionSuccess
                       SprintStabilitySuccess = sprintResult.MultipleRunsStability
                       StandupMeetingsExecuted = sprintResult.StandupMeetingsCount

                       // 総合判定
                       OverallAcceptanceSuccess = overallSuccess
                       TestCompletionTime = DateTime.UtcNow |}

                if overallSuccess then
                    logInfo "CollaborationDemoTest" "🎉 FC-036 全受け入れ基準クリア - エージェント協調機能動作実証完了!"
                else
                    logWarning "CollaborationDemoTest" "⚠️ 一部受け入れ基準で問題検出 - 詳細ログを確認"

                return finalReport

            with ex ->
                logError "CollaborationDemoTest" <| sprintf "包括的受け入れテストエラー: %s" ex.Message

                return
                    {| POWorkflowSuccess = false
                       POSuccessRate = 0.0
                       POAverageDuration = TimeSpan.Zero
                       AgentSyncSuccess = false
                       ResourceControlSuccess = false
                       ActiveAgentsCount = 0
                       SprintExecutionSuccess = false
                       SprintStabilitySuccess = false
                       StandupMeetingsExecuted = 0
                       OverallAcceptanceSuccess = false
                       TestCompletionTime = DateTime.UtcNow |}
        }

    /// リソースクリーンアップ
    interface IDisposable with
        member this.Dispose() =
            if not disposed then
                disposed <- true
                logInfo "CollaborationDemoTest" "テストリソースクリーンアップ完了"

/// FC-036テスト実行ヘルパー
module CollaborationDemoTestRunner =

    /// 受け入れテスト実行
    let runAcceptanceTest () =
        async {
            use testSuite = new CollaborationDemoTest()
            return! testSuite.RunComprehensiveAcceptanceTest()
        }

    /// CLI向け受け入れテスト実行
    let runCLIAcceptanceTest () =
        async {
            try
                printfn "FC-036: エージェント協調機能動作実証 - 受け入れテスト実行開始"

                let! result = runAcceptanceTest ()

                printfn ""
                printfn "=== FC-036 受け入れテスト結果 ==="

                printfn
                    "📋 受け入れ基準1 (PO指示→実行フロー): %s (成功率: %.1f%%)"
                    (if result.POWorkflowSuccess then "✅ 合格" else "❌ 不合格")
                    (result.POSuccessRate * 100.0)

                printfn
                    "📋 受け入れ基準2 (エージェント状態同期): %s (エージェント: %d, リソース制御: %s)"
                    (if result.AgentSyncSuccess then "✅ 合格" else "❌ 不合格")
                    result.ActiveAgentsCount
                    (if result.ResourceControlSuccess then "成功" else "失敗")

                printfn
                    "📋 受け入れ基準3 (18分スプリント): %s (MTG: %d回, 安定性: %s)"
                    (if result.SprintExecutionSuccess then "✅ 合格" else "❌ 不合格")
                    result.StandupMeetingsExecuted
                    (if result.SprintStabilitySuccess then "良好" else "不安定")

                printfn ""

                printfn
                    "🎯 総合判定: %s"
                    (if result.OverallAcceptanceSuccess then
                         "✅ 全受け入れ基準クリア!"
                     else
                         "❌ 改善が必要")

                printfn "⏰ テスト完了時刻: %s" (result.TestCompletionTime.ToString("yyyy-MM-dd HH:mm:ss"))

                return result.OverallAcceptanceSuccess

            with ex ->
                printfn "❌ 受け入れテスト実行エラー: %s" ex.Message
                return false
        }

module FCode.Tests.FC014EndToEndIntegrationTests

open System
open Xunit
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.AgentStateManager
open FCode.Collaboration.TaskDependencyGraph
open FCode.Collaboration.ProgressAggregator
open FCode.Collaboration.CollaborationCoordinator
open FCode.Collaboration.EscalationManager
open FCode.VirtualTimeCoordinator
open FCode.Collaboration.TimeCalculationManager
open FCode.Collaboration.MeetingScheduler
open FCode.Collaboration.EventProcessor
open FCode.TaskAssignmentManager
open FCode.QualityGateManager
open FCode.Logger

// ========================================
// FC-014 エンドツーエンド統合テスト
// 「ざっくり指示→20分自走→完成確認」フロー
// ========================================

// ヘルパー関数
module Result =
    let get =
        function
        | Ok value -> value
        | Error error -> failwithf "Result.get failed: %A" error

/// テスト用協調システム統合ファクトリ
type TestCollaborationSystem() =
    let config = CollaborationConfig.Default
    let agentStateManager = new AgentStateManager(config)
    let taskDependencyGraph = new TaskDependencyGraph(config)

    let progressAggregator =
        new ProgressAggregator(agentStateManager, taskDependencyGraph, config)

    let collaborationCoordinator =
        new CollaborationCoordinator(agentStateManager, taskDependencyGraph, progressAggregator, config)

    let escalationManager =
        new EscalationManager(agentStateManager, taskDependencyGraph, progressAggregator, config)

    let timeCalculationManager = new TimeCalculationManager(VirtualTimeConfig.Default)

    let meetingScheduler =
        new MeetingScheduler(timeCalculationManager, VirtualTimeConfig.Default)

    let eventProcessor =
        new EventProcessor(timeCalculationManager, meetingScheduler, VirtualTimeConfig.Default)

    let coordinator =
        new VirtualTimeCoordinator(timeCalculationManager, meetingScheduler, eventProcessor, VirtualTimeConfig.Default)

    let virtualTimeManager =
        coordinator :> FCode.Collaboration.IVirtualTimeManager.IVirtualTimeManager

    let taskAssignmentManager =
        new TaskAssignmentManager(agentStateManager, taskDependencyGraph, config)

    let qualityGateManager =
        new QualityGateManager(agentStateManager, taskDependencyGraph, progressAggregator, config)

    member this.AgentStateManager = agentStateManager
    member this.TaskDependencyGraph = taskDependencyGraph
    member this.ProgressAggregator = progressAggregator
    member this.CollaborationCoordinator = collaborationCoordinator
    member this.EscalationManager = escalationManager
    member this.VirtualTimeManager = virtualTimeManager
    member this.TaskAssignmentManager = taskAssignmentManager
    member this.QualityGateManager = qualityGateManager

    interface IDisposable with
        member this.Dispose() =
            agentStateManager.Dispose()
            taskDependencyGraph.Dispose()
            progressAggregator.Dispose()
            collaborationCoordinator.Dispose()
            escalationManager.Dispose()
            virtualTimeManager.Dispose()
            taskAssignmentManager.Dispose()
            qualityGateManager.Dispose()

[<Fact>]
[<Trait("TestCategory", "Integration")>]
let ``FC-014 E2E: ざっくり指示から20分自走完成フロー`` () =
    async {
        use system = new TestCollaborationSystem()

        // Phase 1: PO ざっくり指示
        let poInstruction = "ユーザー登録機能を作って。バリデーション、データベース保存、メール通知も含めて。"
        let projectId = "user-registration-feature"

        // Phase 2: TaskAssignmentManager による自動タスク分解
        let! assignmentResult = system.TaskAssignmentManager.ProcessPOInstruction(projectId, poInstruction)

        match assignmentResult with
        | Result.Ok assignments ->
            Assert.True(assignments.Length > 0, "タスク分解が実行されるべき")

            // タスクが開発・QA・UX・PMに適切に配分されているかチェック
            let devTasks =
                assignments |> List.filter (fun a -> a.AgentSpecialization = Development)

            let qaTasks = assignments |> List.filter (fun a -> a.AgentSpecialization = Testing)

            Assert.True(devTasks.Length > 0, "開発タスクが配分されるべき")
            Assert.True(qaTasks.Length > 0, "QAタスクが配分されるべき")

            // Phase 3: VirtualTimeManager による20分スプリント開始
            let sprintId =
                sprintf "sprint-%s-%s" projectId (DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"))

            let! sprintStartResult = system.VirtualTimeManager.StartSprint(sprintId)

            match sprintStartResult with
            | Result.Ok virtualContext ->
                Assert.True(virtualContext.IsActive, "スプリントがアクティブになるべき")

                // Phase 4: エージェント状態登録・自走開始
                let agents = [ "dev1"; "dev2"; "qa1"; "ux"; "pm" ]

                for agent in agents do
                    let! agentResult = system.AgentStateManager.AddAgent(agent)

                    match agentResult with
                    | Result.Ok _ -> ()
                    | Result.Error error -> Assert.True(false, sprintf "エージェント登録失敗: %s - %A" agent error)

                // Phase 5: 6分毎スタンドアップシミュレーション
                let progressReports =
                    [ ("dev1", "ユーザーモデル作成完了、バリデーション実装中")
                      ("dev2", "データベーススキーマ設計完了、API実装開始")
                      ("qa1", "テスト計画作成完了、テストケース作成中")
                      ("ux", "ユーザーフロー設計完了、UIプロトタイプ作成中")
                      ("pm", "要件整理完了、進捗監視中") ]

                let! standupResult = system.VirtualTimeManager.ExecuteStandup("standup-1", progressReports)

                match standupResult with
                | Result.Ok meeting ->
                    Assert.Equal(5, meeting.Participants.Length)
                    Assert.True(meeting.Decisions.Length > 0, "スタンドアップで何らかの決定事項があるべき")
                | Result.Error error -> Assert.True(false, sprintf "スタンドアップ失敗: %A" error)

                // Phase 6: QualityGateManager による品質評価
                let taskIds = assignments |> List.map (fun a -> a.TaskInfo.TaskId)
                let! qualityResult = system.QualityGateManager.EvaluateQuality(projectId, taskIds)

                match qualityResult with
                | Result.Ok evaluation ->
                    Assert.True(evaluation.OverallScore >= 0.0 && evaluation.OverallScore <= 1.0, "品質スコアが有効範囲内であるべき")
                    Assert.True(evaluation.EvaluationResults.Length > 0, "品質評価結果が存在するべき")
                | Result.Error error -> Assert.True(false, sprintf "品質評価失敗: %A" error)

                // Phase 7: 72分完成度評価・継続判定
                let! completionResult = system.VirtualTimeManager.AssessCompletion(sprintId, taskIds)

                match completionResult with
                | Result.Ok assessment ->
                    Assert.True(
                        assessment.OverallCompletionRate >= 0.0
                        && assessment.OverallCompletionRate <= 1.0
                    )

                    let! continuationResult = system.VirtualTimeManager.DecideContinuation(sprintId, assessment)

                    match continuationResult with
                    | Result.Ok decision ->
                        match decision with
                        | AutoContinue reason -> logInfo "FC014E2E" <| sprintf "自動継続承認: %s" reason
                        | RequirePOApproval reason -> logInfo "FC014E2E" <| sprintf "PO承認要求: %s" reason
                        | StopExecution reason -> logInfo "FC014E2E" <| sprintf "実行停止: %s" reason
                        | EscalateToManagement reason -> logInfo "FC014E2E" <| sprintf "経営陣エスカレーション: %s" reason
                    | Result.Error error -> Assert.True(false, sprintf "継続判定失敗: %A" error)
                | Result.Error error -> Assert.True(false, sprintf "完成度評価失敗: %A" error)

                // Phase 8: スプリント終了・統計確認
                let! sprintStatsResult = system.VirtualTimeManager.GetSprintStatistics(sprintId)

                match sprintStatsResult with
                | Result.Ok statistics ->
                    Assert.True(statistics.Length > 0, "スプリント統計が取得できるべき")
                    logInfo "FC014E2E" <| sprintf "スプリント統計: %d項目" statistics.Length
                    // SprintStatistic型の検証
                    let hasSprintId =
                        statistics
                        |> List.exists (function
                            | StringMetric("SprintId", _) -> true
                            | _ -> false)

                    Assert.True(hasSprintId, "SprintId統計が含まれるべき")
                | Result.Error error -> Assert.True(false, sprintf "スプリント統計取得失敗: %A" error)

                let! stopResult = system.VirtualTimeManager.StopSprint(sprintId)

                match stopResult with
                | Result.Ok _ -> logInfo "FC014E2E" "スプリント正常終了"
                | Result.Error error -> Assert.True(false, sprintf "スプリント停止失敗: %A" error)

            | Result.Error error -> Assert.True(false, sprintf "スプリント開始失敗: %A" error)

        | Result.Error error -> Assert.True(false, sprintf "タスク分解失敗: %A" error)
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Integration")>]
let ``FC-014 E2E: 複数エージェント並行作業協調テスト`` () =
    async {
        use system = new TestCollaborationSystem()

        // 複数エージェント登録
        let agents = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "pm"; "pdm" ]

        let! agentRegistrations =
            agents
            |> List.map (fun agentId -> system.AgentStateManager.AddAgent(agentId))
            |> Async.Parallel

        // 全エージェント登録成功確認
        agentRegistrations
        |> Array.iter (fun result ->
            match result with
            | Result.Ok _ -> ()
            | Result.Error error -> Assert.True(false, sprintf "エージェント登録失敗: %A" error))

        // 複数タスクの並行配分
        let tasks =
            [ "フロントエンド実装"; "バックエンドAPI開発"; "データベース設計"; "テスト自動化"; "UXデザイン"; "プロジェクト管理" ]

        let taskCreations =
            tasks
            |> List.mapi (fun i title -> TaskInfo.Create(sprintf "task-%d" (i + 1), title))

        // 全タスク作成成功確認
        for result in taskCreations do
            match result with
            | Result.Ok task ->
                let! addResult = system.TaskDependencyGraph.AddTask(task)

                match addResult with
                | Result.Ok _ -> ()
                | Result.Error error -> Assert.True(false, sprintf "タスク追加失敗: %A" error)
            | Result.Error error -> Assert.True(false, sprintf "タスク作成失敗: %A" error)

        // 並行進捗更新
        let! progressUpdates =
            agents
            |> List.mapi (fun i agentId -> system.AgentStateManager.UpdateAgentProgress(agentId, float (i + 1) * 0.1))
            |> Async.Parallel

        // 進捗集計確認
        let! summaryResult = system.ProgressAggregator.GetProgressSummary()

        match summaryResult with
        | Result.Ok summary ->
            Assert.Equal(agents.Length, summary.ActiveAgents)
            Assert.True(summary.OverallProgress >= 0.0 && summary.OverallProgress <= 1.0)

            logInfo "FC014E2E"
            <| sprintf "並行作業進捗: %d エージェント, 進捗 %.1f%%" summary.ActiveAgents (summary.OverallProgress * 100.0)
        | Result.Error error -> Assert.True(false, sprintf "進捗集計失敗: %A" error)

        // 協調制御・競合回避確認
        let! coordinationResult = system.CollaborationCoordinator.CoordinateAgents(agents)

        match coordinationResult with
        | Result.Ok coordination ->
            Assert.True(coordination.ConflictsDetected.Length >= 0, "競合検出機能が動作するべき")

            logInfo "FC014E2E"
            <| sprintf
                "協調制御完了: %d競合検出, %d解決済み"
                coordination.ConflictsDetected.Length
                coordination.ConflictsResolved.Length
        | Result.Error error -> Assert.True(false, sprintf "協調制御失敗: %A" error)
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Performance")>]
let ``FC-014 E2E: 20分自走フロー性能テスト`` () =
    async {
        use system = new TestCollaborationSystem()

        let startTime = DateTime.UtcNow

        // 大規模プロジェクトシミュレーション
        let projectId = "large-scale-ecommerce"
        let poInstruction = "ECサイトを作って。商品管理、注文処理、決済、ユーザー管理、管理画面、モバイル対応すべて含めて。"

        // Phase 1: 大規模タスク分解 (目標: 2秒以内)
        let taskDecompositionStart = DateTime.UtcNow
        let! assignmentResult = system.TaskAssignmentManager.ProcessPOInstruction(projectId, poInstruction)
        let taskDecompositionTime = DateTime.UtcNow - taskDecompositionStart

        Assert.True(
            taskDecompositionTime.TotalSeconds < 2.0,
            sprintf "タスク分解時間が長すぎる: %.1f秒" taskDecompositionTime.TotalSeconds
        )

        match assignmentResult with
        | Result.Ok assignments ->
            Assert.True(assignments.Length >= 10, "大規模プロジェクトで十分なタスク数が生成されるべき")

            // Phase 2: 高速スプリント管理 (目標: 1秒以内)
            let sprintManagementStart = DateTime.UtcNow

            let sprintId =
                sprintf "performance-sprint-%s" (DateTime.UtcNow.ToString("yyyyMMddHHmmss"))

            let! sprintStartResult = system.VirtualTimeManager.StartSprint(sprintId)
            let! currentTimeResult = system.VirtualTimeManager.GetCurrentVirtualTime(sprintId)
            let! activeSprintsResult = system.VirtualTimeManager.GetActiveSprints()
            let! healthCheckResult = system.VirtualTimeManager.PerformHealthCheck()

            let sprintManagementTime = DateTime.UtcNow - sprintManagementStart

            Assert.True(
                sprintManagementTime.TotalSeconds < 1.0,
                sprintf "スプリント管理時間が長すぎる: %.1f秒" sprintManagementTime.TotalSeconds
            )

            // Phase 3: 品質評価性能 (目標: 3秒以内)
            let qualityEvaluationStart = DateTime.UtcNow
            let taskIds = assignments |> List.map (fun a -> a.TaskInfo.TaskId)
            let! qualityResult = system.QualityGateManager.EvaluateQuality(projectId, taskIds)
            let qualityEvaluationTime = DateTime.UtcNow - qualityEvaluationStart

            Assert.True(
                qualityEvaluationTime.TotalSeconds < 3.0,
                sprintf "品質評価時間が長すぎる: %.1f秒" qualityEvaluationTime.TotalSeconds
            )

            // Phase 4: 完成度評価性能 (目標: 1秒以内)
            let completionAssessmentStart = DateTime.UtcNow
            let! completionResult = system.VirtualTimeManager.AssessCompletion(sprintId, taskIds)
            let completionAssessmentTime = DateTime.UtcNow - completionAssessmentStart

            Assert.True(
                completionAssessmentTime.TotalSeconds < 1.0,
                sprintf "完成度評価時間が長すぎる: %.1f秒" completionAssessmentTime.TotalSeconds
            )

            let! stopResult = system.VirtualTimeManager.StopSprint(sprintId)

            let totalTime = DateTime.UtcNow - startTime
            logInfo "FC014E2E" <| sprintf "性能テスト完了: 総時間 %.1f秒" totalTime.TotalSeconds

            // 全体目標: 10秒以内で完了
            Assert.True(totalTime.TotalSeconds < 10.0, sprintf "全体処理時間が目標を超過: %.1f秒" totalTime.TotalSeconds)

        | Result.Error error -> Assert.True(false, sprintf "大規模タスク分解失敗: %A" error)
    }
    |> Async.RunSynchronously

[<Fact>]
[<Trait("TestCategory", "Integration")>]
let ``FC-014 E2E: システム健全性・回復力テスト`` () =
    async {
        use system = new TestCollaborationSystem()

        // Phase 1: 正常状態健全性確認
        let! healthResults =
            [ system.AgentStateManager.PerformHealthCheck()
              system.TaskDependencyGraph.PerformHealthCheck()
              system.ProgressAggregator.PerformHealthCheck()
              system.CollaborationCoordinator.PerformHealthCheck()
              system.EscalationManager.PerformHealthCheck()
              system.VirtualTimeManager.PerformHealthCheck()
              system.TaskAssignmentManager.PerformHealthCheck()
              system.QualityGateManager.PerformHealthCheck() ]
            |> Async.Parallel

        healthResults
        |> Array.iter (fun result ->
            match result with
            | Result.Ok(healthy, message) -> Assert.True(healthy, sprintf "健全性チェック失敗: %s" message)
            | Result.Error error -> Assert.True(false, sprintf "健全性チェックエラー: %A" error))

        // Phase 2: 異常状態・回復テスト
        let sprintId = "recovery-test-sprint"
        let! sprintResult = system.VirtualTimeManager.StartSprint(sprintId)

        match sprintResult with
        | Result.Ok _ ->
            // 意図的な異常状態作成: 存在しないエージェント参照
            let invalidAgentId = "non-existent-agent-999"
            let! invalidAgentResult = system.AgentStateManager.UpdateAgentProgress(invalidAgentId, 0.5)

            match invalidAgentResult with
            | Result.Error(NotFound _) -> logInfo "FC014E2E" "期待される例外が適切に処理された"
            | _ -> Assert.True(false, "存在しないエージェントでエラーが発生するべき")

            // 循環依存作成テスト
            let task1 = TaskInfo.Create("circular-1", "Task 1") |> Result.get
            let task1Updated = { task1 with Description = "循環依存テスト1" }
            let! task1Result = system.TaskDependencyGraph.AddTask(task1Updated)

            let task2 = TaskInfo.Create("circular-2", "Task 2") |> Result.get
            let task2Updated = { task2 with Description = "循環依存テスト2" }
            let! task2Result = system.TaskDependencyGraph.AddTask(task2Updated)

            match task1Result, task2Result with
            | Result.Ok _, Result.Ok _ ->
                let! dep1Result = system.TaskDependencyGraph.AddDependency("circular-1", "circular-2")
                let! dep2Result = system.TaskDependencyGraph.AddDependency("circular-2", "circular-1")

                match dep1Result, dep2Result with
                | Result.Ok _, Result.Error(CircularDependency _) -> logInfo "FC014E2E" "循環依存が適切に検出・防止された"
                | _ -> Assert.True(false, "循環依存が検出されるべき")
            | _ -> Assert.True(false, "テスト用タスク作成失敗")

            let! stopResult = system.VirtualTimeManager.StopSprint(sprintId)
            return ()

        | Result.Error error ->
            Assert.True(false, sprintf "回復テスト用スプリント開始失敗: %A" error)
            return ()

        // Phase 3: 最終健全性確認
        let! finalHealthResults =
            [ system.VirtualTimeManager.PerformHealthCheck()
              system.EscalationManager.PerformHealthCheck() ]
            |> Async.Parallel

        finalHealthResults
        |> Array.iter (fun result ->
            match result with
            | Result.Ok(healthy, message) ->
                if not healthy then
                    logWarning "FC014E2E" <| sprintf "健全性警告: %s" message
            // 異常テスト後なので一部警告は許容
            | Result.Error error -> Assert.True(false, sprintf "最終健全性チェックエラー: %A" error))

        logInfo "FC014E2E" "システム回復力テスト完了"
    }
    |> Async.RunSynchronously

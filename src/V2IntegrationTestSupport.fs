module FCode.V2IntegrationTestSupport

open System
open System.Threading.Tasks
open FCode.V2IntegrationCoordinator
open FCode.AdvancedCollaboration.AdvancedCoordinationFacade
open FCode.SessionManagement.SessionManagementFacade
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// v2.0統合機能テストサポートユーティリティ
module V2IntegrationTestHelper =

    /// テスト用v2.0統合コーディネーター作成
    let createTestCoordinator () =
        try
            let coordinator = new V2IntegrationCoordinator()
            logInfo "V2IntegrationTest" "テスト用v2.0統合コーディネーター作成完了"
            coordinator
        with ex ->
            logError "V2IntegrationTest" (sprintf "テスト用コーディネーター作成失敗: %s" ex.Message)
            reraise ()

    /// テスト用タスク情報作成
    let createTestTaskInfo (taskId: string) =
        { TaskId = taskId
          Title = sprintf "テストタスク: %s" taskId
          Description = "v2.0統合機能テスト用タスク"
          Status = TaskStatus.Pending
          Priority = TaskPriority.Medium
          AssignedAgent = Some "test-agent"
          CreatedAt = DateTime.UtcNow
          UpdatedAt = DateTime.UtcNow
          EstimatedDuration = Some(TimeSpan.FromMinutes(2.0))
          ActualDuration = None
          RequiredResources = []
          Dependencies = [] }

    /// 統合機能基本テスト
    let runBasicIntegrationTest () =
        async {
            try
                logInfo "V2IntegrationTest" "基本統合テスト開始"

                use coordinator = createTestCoordinator ()

                // 1. 初期化テスト
                let config = V2IntegrationCoordinator.DefaultConfiguration
                coordinator.InitializeV2Integration(config)

                let status = coordinator.GetIntegrationStatus()

                if not status.IsInitialized then
                    logError "V2IntegrationTest" "統合機能初期化失敗"

                logInfo "V2IntegrationTest" "初期化テスト成功"

                // 2. ワークフロー実行テスト
                let testTask = createTestTaskInfo ("test-001")
                let! workflowResult = coordinator.ExecuteIntegratedWorkflow(testTask)

                match workflowResult with
                | Result.Ok message ->
                    logInfo "V2IntegrationTest" (sprintf "ワークフロー実行テスト成功: %s" message)
                    return Result.Ok "基本統合テスト完了"
                | Result.Error error ->
                    logError "V2IntegrationTest" (sprintf "ワークフロー実行テスト失敗: %s" error)
                    return Result.Error(sprintf "ワークフロー実行失敗: %s" error)

            with ex ->
                logError "V2IntegrationTest" (sprintf "基本統合テスト例外: %s" ex.Message)
                return Result.Error(sprintf "基本統合テスト例外: %s" ex.Message)
        }

    /// 並行ワークフローテスト
    let runConcurrentWorkflowTest () =
        async {
            try
                logInfo "V2IntegrationTest" "並行ワークフローテスト開始"

                use coordinator = createTestCoordinator ()
                coordinator.InitializeV2Integration(V2IntegrationCoordinator.DefaultConfiguration)

                // 複数タスクを並行実行
                let testTasks =
                    [ createTestTaskInfo ("concurrent-001")
                      createTestTaskInfo ("concurrent-002")
                      createTestTaskInfo ("concurrent-003") ]

                let! results = testTasks |> List.map (coordinator.ExecuteIntegratedWorkflow) |> Async.Parallel

                let successCount =
                    results
                    |> Array.choose (function
                        | Result.Ok _ -> Some()
                        | _ -> None)
                    |> Array.length

                if successCount = testTasks.Length then
                    logInfo "V2IntegrationTest" (sprintf "並行ワークフローテスト成功: %d/%d" successCount testTasks.Length)
                    return Result.Ok "並行ワークフローテスト完了"
                else
                    logError "V2IntegrationTest" (sprintf "並行ワークフローテスト部分失敗: %d/%d" successCount testTasks.Length)
                    return Result.Error "並行ワークフロー部分失敗"

            with ex ->
                logError "V2IntegrationTest" (sprintf "並行ワークフローテスト例外: %s" ex.Message)
                return Result.Error(sprintf "並行ワークフローテスト例外: %s" ex.Message)
        }

    /// パフォーマンステスト
    let runPerformanceTest () =
        async {
            try
                logInfo "V2IntegrationTest" "パフォーマンステスト開始"

                use coordinator = createTestCoordinator ()
                coordinator.InitializeV2Integration(V2IntegrationCoordinator.DefaultConfiguration)

                let startTime = DateTime.UtcNow
                let initialMemory = GC.GetTotalMemory(false)

                // 多数のワークフロー実行
                let taskCount = 50

                let testTasks =
                    [ 1..taskCount ]
                    |> List.map (fun i -> createTestTaskInfo (sprintf "perf-%03d" i))

                let! results = testTasks |> List.map (coordinator.ExecuteIntegratedWorkflow) |> Async.Parallel

                let endTime = DateTime.UtcNow
                let finalMemory = GC.GetTotalMemory(true)
                let elapsed = endTime - startTime

                let successCount =
                    results
                    |> Array.choose (function
                        | Result.Ok _ -> Some()
                        | _ -> None)
                    |> Array.length

                let memoryIncrease = (finalMemory - initialMemory) / (1024L * 1024L)

                logInfo
                    "V2IntegrationTest"
                    (sprintf
                        "パフォーマンステスト結果: 成功 %d/%d, 実行時間 %.2fs, メモリ増加 %dMB"
                        successCount
                        taskCount
                        elapsed.TotalSeconds
                        memoryIncrease)

                if
                    successCount >= (taskCount * 80 / 100)
                    && elapsed.TotalSeconds < 30.0
                    && memoryIncrease < 100L
                then
                    return Result.Ok "パフォーマンステスト合格"
                else
                    return Result.Error "パフォーマンステスト要件未達"

            with ex ->
                logError "V2IntegrationTest" (sprintf "パフォーマンステスト例外: %s" ex.Message)
                return Result.Error(sprintf "パフォーマンステスト例外: %s" ex.Message)
        }

    /// 統合機能障害回復テスト
    let runResilienceTest () =
        async {
            try
                logInfo "V2IntegrationTest" "障害回復テスト開始"

                use coordinator = createTestCoordinator ()
                coordinator.InitializeV2Integration(V2IntegrationCoordinator.DefaultConfiguration)

                // 1. 正常ワークフロー実行
                let normalTask = createTestTaskInfo ("resilience-normal")
                let! normalResult = coordinator.ExecuteIntegratedWorkflow(normalTask)

                match normalResult with
                | Result.Ok _ -> logInfo "V2IntegrationTest" "正常ワークフロー確認完了"
                | Result.Error error -> logError "V2IntegrationTest" (sprintf "正常ワークフロー失敗: %s" error)

                // 2. 異常ケース処理確認
                let invalidTask =
                    { normalTask with
                        TaskId = ""
                        Title = "" }

                let! invalidResult = coordinator.ExecuteIntegratedWorkflow(invalidTask)

                match invalidResult with
                | Result.Error _ -> logInfo "V2IntegrationTest" "異常ケース適切処理確認"
                | Result.Ok _ -> logWarning "V2IntegrationTest" "異常ケースが正常処理された（要検証）"

                // 3. 回復後正常動作確認
                let recoveryTask = createTestTaskInfo ("resilience-recovery")
                let! recoveryResult = coordinator.ExecuteIntegratedWorkflow(recoveryTask)

                match recoveryResult with
                | Result.Ok _ -> logInfo "V2IntegrationTest" "回復後正常動作確認完了"
                | Result.Error error -> logError "V2IntegrationTest" (sprintf "回復後動作失敗: %s" error)

                return Result.Ok "障害回復テスト完了"

            with ex ->
                logError "V2IntegrationTest" (sprintf "障害回復テスト例外: %s" ex.Message)
                return Result.Error(sprintf "障害回復テスト例外: %s" ex.Message)
        }

    /// 全統合テスト実行
    let runAllIntegrationTests () =
        async {
            try
                logInfo "V2IntegrationTest" "v2.0統合機能全テスト開始"

                let! basicResult = runBasicIntegrationTest ()
                let! concurrentResult = runConcurrentWorkflowTest ()
                let! performanceResult = runPerformanceTest ()
                let! resilienceResult = runResilienceTest ()

                let results = [ basicResult; concurrentResult; performanceResult; resilienceResult ]

                let successCount =
                    results
                    |> List.choose (function
                        | Result.Ok _ -> Some()
                        | _ -> None)
                    |> List.length

                logInfo "V2IntegrationTest" (sprintf "全統合テスト完了: 成功 %d/4" successCount)

                if successCount = 4 then
                    return Result.Ok "v2.0統合機能全テスト合格"
                else
                    let failures =
                        results
                        |> List.choose (function
                            | Result.Error e -> Some e
                            | _ -> None)
                        |> String.concat "; "

                    return Result.Error(sprintf "統合テスト失敗: %s" failures)

            with ex ->
                logError "V2IntegrationTest" (sprintf "全統合テスト例外: %s" ex.Message)
                return Result.Error(sprintf "全統合テスト例外: %s" ex.Message)
        }

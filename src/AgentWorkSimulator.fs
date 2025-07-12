namespace FCode

open System
open System.Threading
open System.Threading.Tasks
open FCode.Logger
// AgentWorkDisplayManager types will be referenced through FCode namespace

/// エージェント作業シミュレーター（デモ・テスト用）
type AgentWorkSimulator() =

    let mutable isRunning = false
    let mutable cancellationTokenSource = new CancellationTokenSource()

    /// シミュレーション実行状態
    member this.IsRunning = isRunning

    /// 単一エージェントの作業進捗をシミュレート
    member private this.SimulateAgentWork
        (agentId: string, taskTitle: string, durationMinutes: int, workDisplayManager: AgentWorkDisplayManager)
        =
        async {
            try
                logInfo "WorkSimulator" (sprintf "Starting work simulation for %s: %s" agentId taskTitle)

                let totalSteps = 10
                let stepDuration = (durationMinutes * 60 * 1000) / totalSteps // ミリ秒

                for step in 1..totalSteps do
                    if not cancellationTokenSource.Token.IsCancellationRequested then
                        let progress = float step / float totalSteps * 100.0

                        let statusNote =
                            match step with
                            | 1 -> "タスク分析中..."
                            | 2 -> "設計検討中..."
                            | 3 -> "実装開始..."
                            | 4 -> "コード記述中..."
                            | 5 -> "中間テスト実行..."
                            | 6 -> "バグ修正中..."
                            | 7 -> "機能追加中..."
                            | 8 -> "最終テスト..."
                            | 9 -> "品質チェック..."
                            | 10 -> "完了処理中..."
                            | _ -> sprintf "進捗 %.1f%%" progress

                        workDisplayManager.UpdateProgress(agentId, progress, statusNote)

                        do! Async.Sleep(stepDuration)

                // タスク完了
                if not cancellationTokenSource.Token.IsCancellationRequested then
                    let result = sprintf "%s 実装完了（品質チェック済み）" taskTitle
                    workDisplayManager.CompleteTask(agentId, result)
                    logInfo "WorkSimulator" (sprintf "Completed work simulation for %s: %s" agentId taskTitle)

            with ex ->
                logError "WorkSimulator" (sprintf "Work simulation error for %s: %s" agentId ex.Message)
                workDisplayManager.ReportError(agentId, sprintf "シミュレーションエラー: %s" ex.Message)
        }

    /// 複数エージェントの並行作業をシミュレート
    member this.StartWorkSimulation(assignments: (string * string * int) list) =
        if isRunning then
            logWarning "WorkSimulator" "Work simulation is already running"
        else
            isRunning <- true
            cancellationTokenSource <- new CancellationTokenSource()
            let workDisplayManager = AgentWorkDisplayGlobal.GetManager()

            logInfo "WorkSimulator" (sprintf "Starting work simulation for %d agents" assignments.Length)

            // 各エージェントのシミュレーションを並行実行
            let simulationTasks =
                assignments
                |> List.map (fun (agentId, taskTitle, durationMinutes) ->
                    this.SimulateAgentWork(agentId, taskTitle, durationMinutes, workDisplayManager))

            // 全シミュレーション完了を監視
            let masterTask =
                async {
                    try
                        do! Async.Parallel simulationTasks |> Async.Ignore
                        logInfo "WorkSimulator" "All work simulations completed"
                    finally
                        isRunning <- false
                }

            Async.Start(masterTask, cancellationTokenSource.Token)

    /// シミュレーションを停止
    member this.StopWorkSimulation() =
        if isRunning then
            cancellationTokenSource.Cancel()
            isRunning <- false
            logInfo "WorkSimulator" "Work simulation stopped"
        else
            logInfo "WorkSimulator" "No work simulation running"

    /// デモ用のサンプル作業シミュレーション開始
    member this.StartDemoSimulation() =
        let demoAssignments =
            [ ("dev1", "ユーザー認証機能実装", 8)
              ("dev2", "データベース設計・実装", 12)
              ("dev3", "フロントエンド画面作成", 10)
              ("qa1", "単体テスト設計・実行", 6)
              ("qa2", "統合テスト・性能テスト", 8)
              ("ux", "UI/UXデザイン改善", 15) ]

        logInfo "WorkSimulator" "Starting demo work simulation"
        this.StartWorkSimulation(demoAssignments)

    /// レビューシミュレーション
    member this.StartReviewSimulation(agentId: string, reviewTarget: string, reviewer: string, durationMinutes: int) =
        let workDisplayManager = AgentWorkDisplayGlobal.GetManager()
        workDisplayManager.StartReview(agentId, reviewTarget, reviewer)

        let reviewTask =
            async {
                try
                    let totalSteps = 5
                    let stepDuration = (durationMinutes * 60 * 1000) / totalSteps

                    let reviewSteps =
                        [ "コード品質チェック中..."; "機能仕様確認中..."; "テストケース検証中..."; "セキュリティ確認中..."; "最終承認判定中..." ]

                    for i, stepNote in List.indexed reviewSteps do
                        if not cancellationTokenSource.Token.IsCancellationRequested then
                            let progress = float (i + 1) / float totalSteps * 100.0
                            workDisplayManager.UpdateProgress(agentId, progress, stepNote)
                            do! Async.Sleep(stepDuration)

                    if not cancellationTokenSource.Token.IsCancellationRequested then
                        workDisplayManager.CompleteTask(agentId, sprintf "%s レビュー完了（承認）" reviewTarget)

                with ex ->
                    workDisplayManager.ReportError(agentId, sprintf "レビューエラー: %s" ex.Message)
            }

        Async.Start(reviewTask, cancellationTokenSource.Token)
        logInfo "WorkSimulator" (sprintf "Started review simulation: %s reviewing %s" agentId reviewTarget)

/// グローバルなAgentWorkSimulatorインスタンス
module AgentWorkSimulatorGlobal =
    let private globalSimulator = lazy (new AgentWorkSimulator())

    /// グローバルシミュレーターインスタンスを取得
    let GetSimulator () = globalSimulator.Value

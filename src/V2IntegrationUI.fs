module FCode.V2IntegrationUI

open System
open System.Text
open Terminal.Gui
open FCode.V2IntegrationCoordinator
open FCode.Collaboration.CollaborationTypes
open FCode.Logger
open FCode.ColorSchemes

/// v2.0統合機能UI管理
type V2IntegrationUIManager(coordinator: V2IntegrationCoordinator) =

    let mutable integrationStatusView: TextView option = None
    let mutable workflowProgressView: TextView option = None
    let mutable performanceMetricsView: TextView option = None
    let mutable isUIInitialized = false

    /// v2.0統合UI初期化
    member this.InitializeIntegrationUI() =
        if not isUIInitialized then
            try
                logInfo "V2IntegrationUI" "v2.0統合UI初期化開始"
                isUIInitialized <- true
                logInfo "V2IntegrationUI" "v2.0統合UI初期化完了"
            with ex ->
                logError "V2IntegrationUI" (sprintf "v2.0統合UI初期化失敗: %s" ex.Message)

    /// 統合状態表示ビュー設定
    member this.SetIntegrationStatusView(view: TextView) =
        integrationStatusView <- Some view
        this.UpdateIntegrationStatusDisplay()

    /// ワークフロー進捗表示ビュー設定
    member this.SetWorkflowProgressView(view: TextView) =
        workflowProgressView <- Some view
        this.UpdateWorkflowProgressDisplay()

    /// パフォーマンス指標表示ビュー設定
    member this.SetPerformanceMetricsView(view: TextView) =
        performanceMetricsView <- Some view
        this.UpdatePerformanceMetricsDisplay()

    /// 統合状態表示更新
    member this.UpdateIntegrationStatusDisplay() =
        match integrationStatusView with
        | Some view ->
            try
                let status = coordinator.GetIntegrationStatus()
                let displayText = this.BuildIntegrationStatusText(status)

                Application.MainLoop.Invoke(fun () ->
                    view.Text <- NStack.ustring.Make(displayText: string)
                    view.SetNeedsDisplay())

            with ex ->
                logError "V2IntegrationUI" (sprintf "統合状態表示更新失敗: %s" ex.Message)
        | None -> ()

    /// ワークフロー進捗表示更新
    member this.UpdateWorkflowProgressDisplay() =
        match workflowProgressView with
        | Some view ->
            try
                let status = coordinator.GetIntegrationStatus()
                let displayText = this.BuildWorkflowProgressText(status)

                Application.MainLoop.Invoke(fun () ->
                    view.Text <- NStack.ustring.Make(displayText: string)
                    view.SetNeedsDisplay())

            with ex ->
                logError "V2IntegrationUI" (sprintf "ワークフロー進捗表示更新失敗: %s" ex.Message)
        | None -> ()

    /// パフォーマンス指標表示更新
    member this.UpdatePerformanceMetricsDisplay() =
        match performanceMetricsView with
        | Some view ->
            try
                let status = coordinator.GetIntegrationStatus()
                let displayText = this.BuildPerformanceMetricsText(status)

                Application.MainLoop.Invoke(fun () ->
                    view.Text <- NStack.ustring.Make(displayText: string)
                    view.SetNeedsDisplay())

            with ex ->
                logError "V2IntegrationUI" (sprintf "パフォーマンス指標表示更新失敗: %s" ex.Message)
        | None -> ()

    /// 統合状態テキスト構築
    member private _.BuildIntegrationStatusText(status) =
        let sb = StringBuilder()

        sb.AppendLine("🚀 v2.0高度機能統合状況") |> ignore
        sb.AppendLine("=" + String.replicate 40 "=") |> ignore
        sb.AppendLine() |> ignore

        // 初期化状態
        let initIcon = if status.IsInitialized then "✅" else "❌"

        sb.AppendFormat("{0} 統合機能初期化: {1}\n", initIcon, if status.IsInitialized then "完了" else "未初期化")
        |> ignore

        sb.AppendLine() |> ignore

        // 各機能状態
        sb.AppendLine("🔧 各機能状態:") |> ignore

        let advancedIcon =
            if status.WorkflowState.AdvancedCollaborationActive then
                "🟢"
            else
                "🔴"

        sb.AppendFormat(
            "  {0} 高度AI協調: {1}\n",
            advancedIcon,
            if status.WorkflowState.AdvancedCollaborationActive then
                "アクティブ"
            else
                "非アクティブ"
        )
        |> ignore

        let sessionIcon =
            if status.WorkflowState.SessionPersistenceActive then
                "🟢"
            else
                "🔴"

        sb.AppendFormat(
            "  {0} セッション永続化: {1}\n",
            sessionIcon,
            if status.WorkflowState.SessionPersistenceActive then
                "アクティブ"
            else
                "非アクティブ"
        )
        |> ignore

        let externalIcon =
            if status.WorkflowState.ExternalIntegrationActive then
                "🟢"
            else
                "🟡"

        sb.AppendFormat(
            "  {0} 外部ツール統合: {1}\n",
            externalIcon,
            if status.WorkflowState.ExternalIntegrationActive then
                "アクティブ"
            else
                "段階的実装中"
        )
        |> ignore

        sb.AppendLine() |> ignore

        // メモリ使用量
        let memoryIcon = if status.MemoryUsageMB <= 500L then "🟢" else "🟡"

        sb.AppendFormat("{0} メモリ使用量: {1} MB\n", memoryIcon, status.MemoryUsageMB)
        |> ignore

        // アクティブワークフロー数
        sb.AppendFormat("🔄 アクティブワークフロー: {0} 件\n", status.ActiveWorkflowCount) |> ignore

        sb.ToString()

    /// ワークフロー進捗テキスト構築
    member private _.BuildWorkflowProgressText(status) =
        let sb = StringBuilder()

        sb.AppendLine("📊 統合ワークフロー進捗") |> ignore
        sb.AppendLine("=" + String.replicate 40 "=") |> ignore
        sb.AppendLine() |> ignore

        if status.ActiveWorkflowCount > 0 then
            sb.AppendFormat("🔄 実行中: {0} ワークフロー\n", status.ActiveWorkflowCount) |> ignore
            sb.AppendLine() |> ignore

            sb.AppendLine("進行状況:") |> ignore
            sb.AppendLine("  1. 高度AI協調分散 🤖") |> ignore
            sb.AppendLine("  2. セッション状態永続化 💾") |> ignore
            sb.AppendLine("  3. 最適化検証 ⚡") |> ignore
            sb.AppendLine("  4. 統合結果確認 ✅") |> ignore
        else
            sb.AppendLine("💤 待機中") |> ignore
            sb.AppendLine() |> ignore
            sb.AppendLine("統合ワークフローの実行を待機しています。") |> ignore
            sb.AppendLine("タスクが開始されると自動的に") |> ignore
            sb.AppendLine("v2.0高度機能による最適化処理が") |> ignore
            sb.AppendLine("実行されます。") |> ignore

        sb.AppendLine() |> ignore
        sb.AppendFormat("⏰ 最終更新: {0:HH:mm:ss}\n", DateTime.Now) |> ignore

        sb.ToString()

    /// パフォーマンス指標テキスト構築
    member private _.BuildPerformanceMetricsText(status) =
        let sb = StringBuilder()

        sb.AppendLine("⚡ パフォーマンス指標") |> ignore
        sb.AppendLine("=" + String.replicate 40 "=") |> ignore
        sb.AppendLine() |> ignore

        // メモリ使用量評価
        let memoryStatus, memoryColor =
            if status.MemoryUsageMB <= 250L then ("優秀", "🟢")
            elif status.MemoryUsageMB <= 500L then ("良好", "🟡")
            else ("要改善", "🔴")

        sb.AppendFormat("{0} メモリ使用量: {1} MB ({2})\n", memoryColor, status.MemoryUsageMB, memoryStatus)
        |> ignore

        // ワークフロー効率性
        let workflowEfficiency =
            if status.ActiveWorkflowCount = 0 then "待機中"
            elif status.ActiveWorkflowCount <= 5 then "効率的"
            else "高負荷"

        sb.AppendFormat("🔄 ワークフロー効率: {0}\n", workflowEfficiency) |> ignore

        // v2.0機能利用状況
        sb.AppendLine() |> ignore
        sb.AppendLine("🚀 v2.0機能利用状況:") |> ignore

        let activeFeatures =
            [ ("高度AI協調", status.WorkflowState.AdvancedCollaborationActive)
              ("セッション永続化", status.WorkflowState.SessionPersistenceActive)
              ("外部ツール統合", status.WorkflowState.ExternalIntegrationActive) ]

        for (featureName, isActive) in activeFeatures do
            let icon = if isActive then "✅" else "⭕"
            sb.AppendFormat("  {0} {1}\n", icon, featureName) |> ignore

        // 統合効果
        sb.AppendLine() |> ignore
        sb.AppendLine("📈 統合効果:") |> ignore

        let integrationScore =
            let activeCount = activeFeatures |> List.filter snd |> List.length

            match activeCount with
            | 3 -> "最高 (全機能統合)"
            | 2 -> "高 (主要機能統合)"
            | 1 -> "中 (基本統合)"
            | _ -> "低 (統合準備中)"

        sb.AppendFormat("  🎯 統合レベル: {0}\n", integrationScore) |> ignore

        sb.ToString()

    /// 全表示更新
    member this.UpdateAllDisplays() =
        this.UpdateIntegrationStatusDisplay()
        this.UpdateWorkflowProgressDisplay()
        this.UpdatePerformanceMetricsDisplay()

    /// 統合ワークフロー実行UI（デモ用）
    member this.ExecuteIntegratedWorkflowDemo() =
        async {
            try
                logInfo "V2IntegrationUI" "統合ワークフローデモ実行開始"

                // ダミータスク作成
                let demoTask =
                    { TaskId = sprintf "demo-%s" (Guid.NewGuid().ToString("N")[..7])
                      Title = "v2.0統合機能デモタスク"
                      Description = "高度機能統合のデモンストレーション"
                      Status = TaskStatus.Pending
                      Priority = TaskPriority.Medium
                      AssignedAgent = Some "demo-agent"
                      CreatedAt = DateTime.UtcNow
                      UpdatedAt = DateTime.UtcNow
                      EstimatedDuration = Some(TimeSpan.FromMinutes(5.0))
                      ActualDuration = None
                      RequiredResources = []
                      Dependencies = [] }

                // 統合ワークフロー実行
                let! result = coordinator.ExecuteIntegratedWorkflow(demoTask)

                match result with
                | Result.Ok message ->
                    logInfo "V2IntegrationUI" (sprintf "統合ワークフローデモ成功: %s" message)
                    this.UpdateAllDisplays()
                | Result.Error error -> logError "V2IntegrationUI" (sprintf "統合ワークフローデモ失敗: %s" error)

            with ex ->
                logError "V2IntegrationUI" (sprintf "統合ワークフローデモ例外: %s" ex.Message)
        }

    /// UIリソース解放
    member this.Dispose() =
        integrationStatusView <- None
        workflowProgressView <- None
        performanceMetricsView <- None
        isUIInitialized <- false
        logInfo "V2IntegrationUI" "v2.0統合UI リソース解放完了"

    interface IDisposable with
        member this.Dispose() = this.Dispose()

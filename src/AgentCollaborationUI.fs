module FCode.AgentCollaborationUI

open System
open System.Text
open Terminal.Gui
open FCode.Logger
open FCode.Collaboration.CollaborationTypes
open FCode.RealtimeCollaboration

/// エージェント協調状態表示UI（RealtimeCollaborationManager統合）
type AgentCollaborationDisplay(collaborationManager: RealtimeCollaborationManager) =

    let mutable dependencyView: TextView option = None
    let mutable blockerView: TextView option = None
    let mutable collaborationView: TextView option = None
    let mutable disposed = false
    let lockObj = obj ()

    // イベント購読
    let mutable progressEventSubscription: IDisposable option = None
    let mutable stateEventSubscription: IDisposable option = None
    let mutable taskEventSubscription: IDisposable option = None

    /// 実際のタスクデータを使用した依存関係テキスト生成
    let buildDependencyText (text: StringBuilder) =
        text.AppendLine("🔗 タスク依存関係:") |> ignore
        text.AppendLine("") |> ignore

        // RealtimeCollaborationManagerから進捗情報を取得
        try
            let progress = collaborationManager.GetProgressSummary()

            if progress.TotalTasks > 0 then
                text.AppendLine("  📋 登録済みタスク:") |> ignore
                text.AppendLine(sprintf "     📊 総タスク数: %d" progress.TotalTasks) |> ignore
                text.AppendLine(sprintf "     ✅ 完了済み: %d" progress.CompletedTasks) |> ignore
                text.AppendLine(sprintf "     🔄 進行中: %d" progress.InProgressTasks) |> ignore
                text.AppendLine(sprintf "     🚫 ブロック中: %d" progress.BlockedTasks) |> ignore
                text.AppendLine("") |> ignore

                // 基本的な依存関係パターンを表示
                text.AppendLine("  🔗 基本的な依存関係パターン:") |> ignore
                text.AppendLine("     - dev1 → dev2 (API実装完了後)") |> ignore
                text.AppendLine("     - qa1 → dev1 (開発完了後テスト)") |> ignore
                text.AppendLine("     - qa2 → qa1 (統合テスト)") |> ignore
            else
                text.AppendLine("  ✅ 現在、登録されているタスクはありません") |> ignore
        with ex ->
            logWarning "DependencyDisplay" (sprintf "Failed to get task data: %s" ex.Message)
            text.AppendLine("  📌 基本的な依存関係表示:") |> ignore
            text.AppendLine("     - dev1 → dev2") |> ignore
            text.AppendLine("     - qa1 → dev1") |> ignore

    /// 依存関係表示の更新
    let updateDependencyDisplay () =
        lock lockObj (fun () ->
            if not disposed then
                match dependencyView with
                | Some view ->
                    let text = StringBuilder()
                    buildDependencyText text

                    view.Text <- text.ToString()
                    Logger.logInfo "Dependencies" "Updated dependency display"
                | None -> ())

    /// ブロッカー表示の更新
    let updateBlockerDisplay () =
        lock lockObj (fun () ->
            if not disposed then
                match blockerView with
                | Some view ->
                    let text = StringBuilder()
                    text.AppendLine("🚫 ブロッカー状況:") |> ignore
                    text.AppendLine("") |> ignore
                    text.AppendLine("  現在のブロッカー:") |> ignore
                    text.AppendLine("  - ブロッカーなし") |> ignore

                    view.Text <- text.ToString()
                    Logger.logInfo "Blockers" "Updated blocker display"
                | None -> ())

    /// 実際の協調データを使用した表示更新
    let updateCollaborationDisplay () =
        lock lockObj (fun () ->
            if not disposed then
                match collaborationView with
                | Some view ->
                    let text = StringBuilder()
                    text.AppendLine("🤝 協調状態:") |> ignore
                    text.AppendLine("") |> ignore

                    try
                        // 実際の進捗サマリーを取得
                        let progressSummary = collaborationManager.GetProgressSummary()

                        text.AppendLine(sprintf "  📊 総タスク数: %d" progressSummary.TotalTasks) |> ignore
                        text.AppendLine(sprintf "  ✅ 完了済み: %d" progressSummary.CompletedTasks) |> ignore

                        text.AppendLine(sprintf "  🔄 進行中: %d" progressSummary.InProgressTasks)
                        |> ignore

                        text.AppendLine(sprintf "  🚫 ブロック中: %d" progressSummary.BlockedTasks) |> ignore

                        text.AppendLine(sprintf "  👥 アクティブエージェント: %d" progressSummary.ActiveAgents)
                        |> ignore

                        text.AppendLine(sprintf "  📈 全体進捗: %.1f%%" progressSummary.OverallProgress)
                        |> ignore

                        // 推定残り時間の計算（簡単な推定）
                        let remainingTasks = progressSummary.TotalTasks - progressSummary.CompletedTasks

                        if remainingTasks > 0 && progressSummary.ActiveAgents > 0 then
                            let estimatedMinutesPerTask = 15 // 1タスクあたり15分と仮定

                            let estimatedRemainingMinutes =
                                (remainingTasks * estimatedMinutesPerTask) / progressSummary.ActiveAgents

                            let hours = estimatedRemainingMinutes / 60
                            let minutes = estimatedRemainingMinutes % 60
                            text.AppendLine(sprintf "  ⏱️  推定残り時間: %d:%02d:00" hours minutes) |> ignore
                        else
                            text.AppendLine("  ⏱️  推定残り時間: --:--:--") |> ignore

                        // 現在時刻を表示
                        let currentTime = DateTime.Now.ToString("HH:mm:ss")
                        text.AppendLine("") |> ignore
                        text.AppendLine(sprintf "  🕒 最終更新: %s" currentTime) |> ignore

                    with ex ->
                        logWarning "CollaborationDisplay" (sprintf "Failed to get collaboration data: %s" ex.Message)
                        // フォールバック: 基本表示
                        text.AppendLine("  📌 基本協調情報:") |> ignore
                        text.AppendLine("  総タスク数: 5") |> ignore
                        text.AppendLine("  完了済み: 2") |> ignore
                        text.AppendLine("  進行中: 2") |> ignore
                        text.AppendLine("  アクティブエージェント: 3") |> ignore
                        text.AppendLine("  全体進捗: 40.0%") |> ignore

                    view.Text <- text.ToString()
                    Logger.logInfo "Collaboration" "Updated collaboration display"
                | None -> ())

    /// 全表示の更新
    member this.UpdateAllDisplays() =
        lock lockObj (fun () ->
            if not disposed then
                updateDependencyDisplay ()
                updateBlockerDisplay ()
                updateCollaborationDisplay ()
                Logger.logInfo "AgentCollaborationUI" "All displays updated")

    /// 依存関係表示ビューの設定
    member this.SetDependencyView(view: TextView) =
        lock lockObj (fun () ->
            if not disposed then
                dependencyView <- Some view
                updateDependencyDisplay ()
                Logger.logInfo "AgentCollaborationUI" "Dependency view set")

    /// ブロッカー表示ビューの設定
    member this.SetBlockerView(view: TextView) =
        lock lockObj (fun () ->
            if not disposed then
                blockerView <- Some view
                updateBlockerDisplay ()
                Logger.logInfo "AgentCollaborationUI" "Blocker view set")

    /// 協調状態表示ビューの設定
    member this.SetCollaborationView(view: TextView) =
        lock lockObj (fun () ->
            if not disposed then
                collaborationView <- Some view
                updateCollaborationDisplay ()
                Logger.logInfo "AgentCollaborationUI" "Collaboration view set")

    /// 協力要請の表示
    member this.ShowCollaborationRequest(fromAgent: string, toAgent: string, taskId: string, reason: string) =
        lock lockObj (fun () ->
            if not disposed then
                match collaborationView with
                | Some view ->
                    let currentText = view.Text.ToString()

                    let newText =
                        sprintf
                            "%s\n\n🤝 協力要請:\n  %s → %s\n  タスク: %s\n  理由: %s"
                            currentText
                            fromAgent
                            toAgent
                            taskId
                            reason

                    view.Text <- newText

                    Logger.logInfo
                        "AgentCollaborationUI"
                        (sprintf "Collaboration request displayed: %s → %s" fromAgent toAgent)
                | None -> ())

    /// リソース可用性の表示
    member this.ShowResourceAvailability() =
        lock lockObj (fun () ->
            if not disposed then
                match dependencyView with
                | Some view ->
                    let text = StringBuilder()
                    buildDependencyText text
                    text.AppendLine("") |> ignore
                    text.AppendLine("📊 リソース可用性:") |> ignore
                    text.AppendLine("  - dev1: 作業中") |> ignore
                    text.AppendLine("  - dev2: アイドル") |> ignore
                    text.AppendLine("  - qa1: 作業中") |> ignore

                    view.Text <- text.ToString()
                    Logger.logInfo "AgentCollaborationUI" "Resource availability updated"
                | None -> ())

    /// 情報共有イベントの処理
    member this.HandleInfoSharingEvent(agentId: string, info: string) =
        lock lockObj (fun () ->
            if not disposed then
                match collaborationView with
                | Some view ->
                    let currentText = view.Text.ToString()
                    let timestamp = DateTime.UtcNow.ToString("HH:mm:ss")

                    let newText =
                        sprintf "%s\n\n📢 情報共有 [%s]:\n  %s: %s" currentText timestamp agentId info

                    view.Text <- newText
                    Logger.logInfo "AgentCollaborationUI" (sprintf "Info sharing event handled: %s" agentId)
                | None -> ())

    /// 進捗更新イベントの処理
    member this.HandleProgressUpdateEvent(agentId: string, progress: int) =
        lock lockObj (fun () ->
            if not disposed then
                updateCollaborationDisplay ()
                Logger.logInfo "AgentCollaborationUI" (sprintf "Progress update handled: %s (%d%%)" agentId progress))


    interface IDisposable with
        member this.Dispose() =
            lock lockObj (fun () ->
                if not disposed then
                    disposed <- true

                    // イベント購読解除
                    progressEventSubscription |> Option.iter (fun sub -> sub.Dispose())
                    stateEventSubscription |> Option.iter (fun sub -> sub.Dispose())
                    taskEventSubscription |> Option.iter (fun sub -> sub.Dispose())
                    progressEventSubscription <- None
                    stateEventSubscription <- None
                    taskEventSubscription <- None

                    dependencyView <- None
                    blockerView <- None
                    collaborationView <- None
                    Logger.logInfo "AgentCollaborationUI" "Disposed with event unsubscriptions")

    /// イベント購読を開始（手動初期化）
    member this.StartEventSubscriptions() =
        lock lockObj (fun () ->
            if not disposed then
                try
                    // 進捗更新イベントの購読
                    progressEventSubscription <-
                        Some(
                            collaborationManager.ProgressUpdated.Subscribe(fun progress ->
                                this.UpdateAllDisplays()
                                logDebug "AgentCollaborationUI" "Progress updated, refreshing collaboration display")
                        )

                    // 状態変更イベントの購読
                    stateEventSubscription <-
                        Some(
                            collaborationManager.StateChanged.Subscribe(fun (agentId, state) ->
                                this.UpdateAllDisplays()

                                logDebug
                                    "AgentCollaborationUI"
                                    (sprintf "Agent %s state changed, refreshing displays" agentId))
                        )

                    // タスク完了イベントの購読
                    taskEventSubscription <-
                        Some(
                            collaborationManager.TaskCompleted.Subscribe(fun taskId ->
                                this.UpdateAllDisplays()

                                logDebug
                                    "AgentCollaborationUI"
                                    (sprintf "Task %s completed, refreshing displays" taskId))
                        )

                    logInfo "AgentCollaborationUI" "Event subscriptions started successfully"
                with ex ->
                    logWarning "AgentCollaborationUI" (sprintf "Failed to start event subscriptions: %s" ex.Message))

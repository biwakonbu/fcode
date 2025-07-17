module FCode.AgentCollaborationUI

open System
open System.Text
open Terminal.Gui
open FCode.Logger
open FCode.Collaboration.CollaborationTypes
open FCode.RealtimeCollaboration

/// エージェント協調状態表示UI
type AgentCollaborationDisplay(collaborationManager: RealtimeCollaborationManager) =
    // TODO: 実際のcollaborationManagerとの統合は将来の実装で行う

    let mutable dependencyView: TextView option = None
    let mutable blockerView: TextView option = None
    let mutable collaborationView: TextView option = None
    let mutable disposed = false
    let lockObj = obj ()

    /// 依存関係テキストの生成
    let buildDependencyText (text: StringBuilder) =
        text.AppendLine("🔗 タスク依存関係:") |> ignore
        text.AppendLine("") |> ignore
        text.AppendLine("  基本的な依存関係表示") |> ignore
        text.AppendLine("  - dev1 → dev2") |> ignore
        text.AppendLine("  - qa1 → dev1") |> ignore

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

    /// 協調状態表示の更新
    let updateCollaborationDisplay () =
        lock lockObj (fun () ->
            if not disposed then
                match collaborationView with
                | Some view ->
                    let text = StringBuilder()
                    text.AppendLine("🤝 協調状態:") |> ignore
                    text.AppendLine("") |> ignore
                    text.AppendLine("  総タスク数: 5") |> ignore
                    text.AppendLine("  完了済み: 2") |> ignore
                    text.AppendLine("  進行中: 2") |> ignore
                    text.AppendLine("  アクティブエージェント: 3") |> ignore
                    text.AppendLine("  全体進捗: 40.0%") |> ignore
                    text.AppendLine("  推定残り時間: 01:30:00") |> ignore

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
                    dependencyView <- None
                    blockerView <- None
                    collaborationView <- None
                    Logger.logInfo "AgentCollaborationUI" "Disposed")

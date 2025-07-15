module FCode.ProgressDashboardUI

open System
open System.Collections.Generic
open System.Text
open Terminal.Gui
open FCode.TaskAssignmentManager
open FCode.AgentMessaging
open FCode.QualityGateManager
open FCode.EscalationNotificationUI
open FCode.Collaboration.CollaborationTypes
open FCode.Logger
open FCode.ColorSchemes

/// 進捗ダッシュボード表示モード
type ProgressDashboardMode =
    | Overview // 全体概要
    | AgentDetail // エージェント詳細
    | TaskDetail // タスク詳細
    | QualityFocus // 品質フォーカス
    | TeamCollaboration // チーム協調

/// エージェント進捗情報
type AgentProgressInfo =
    { AgentId: string
      AgentRole: string
      CurrentTask: string option
      TaskProgress: float
      CompletedTasks: int
      TotalTasks: int
      QualityScore: float
      CollaborationScore: float
      BlockedTasks: string list
      LastActivity: DateTime }

/// タスク進捗情報
type TaskProgressInfo =
    { TaskId: string
      TaskTitle: string
      AssignedAgent: string
      Priority: TaskPriority
      Status: string
      Progress: float
      EstimatedTimeRemaining: TimeSpan
      QualityGateStatus: string option
      Dependencies: string list
      Blockers: string list
      LastUpdated: DateTime }

/// チーム進捗サマリー
type TeamProgressSummary =
    { TotalTasks: int
      CompletedTasks: int
      InProgressTasks: int
      BlockedTasks: int
      OverallProgress: float
      TeamVelocity: float
      QualityScore: float
      CollaborationEfficiency: float
      LastUpdated: DateTime }

/// 進捗ダッシュボードUI管理クラス
type ProgressDashboardUIManager() =

    let mutable currentMode: ProgressDashboardMode = Overview
    let mutable agentProgressMap: Map<string, AgentProgressInfo> = Map.empty
    let mutable taskProgressMap: Map<string, TaskProgressInfo> = Map.empty
    let mutable teamSummary: TeamProgressSummary option = None
    let mutable lastUpdateTime: DateTime = DateTime.MinValue

    /// エージェント進捗情報を更新
    member this.UpdateAgentProgress
        (
            agentId: string,
            currentTask: string option,
            taskProgress: float,
            completedTasks: int,
            totalTasks: int,
            qualityScore: float,
            collaborationScore: float,
            blockedTasks: string list
        ) : unit =
        let agentInfo =
            { AgentId = agentId
              AgentRole = this.GetAgentRole(agentId)
              CurrentTask = currentTask
              TaskProgress = taskProgress
              CompletedTasks = completedTasks
              TotalTasks = totalTasks
              QualityScore = qualityScore
              CollaborationScore = collaborationScore
              BlockedTasks = blockedTasks
              LastActivity = DateTime.UtcNow }

        agentProgressMap <- agentProgressMap |> Map.add agentId agentInfo
        lastUpdateTime <- DateTime.UtcNow

        logDebug "ProgressDashboardUI" (sprintf "エージェント進捗更新: %s (進捗: %.1f%%)" agentId (taskProgress * 100.0))

    /// タスク進捗情報を更新
    member this.UpdateTaskProgress
        (
            taskId: string,
            title: string,
            assignedAgent: string,
            priority: TaskPriority,
            status: string,
            progress: float,
            estimatedTimeRemaining: TimeSpan,
            qualityGateStatus: string option,
            dependencies: string list,
            blockers: string list
        ) : unit =
        let taskInfo =
            { TaskId = taskId
              TaskTitle = title
              AssignedAgent = assignedAgent
              Priority = priority
              Status = status
              Progress = progress
              EstimatedTimeRemaining = estimatedTimeRemaining
              QualityGateStatus = qualityGateStatus
              Dependencies = dependencies
              Blockers = blockers
              LastUpdated = DateTime.UtcNow }

        taskProgressMap <- taskProgressMap |> Map.add taskId taskInfo
        lastUpdateTime <- DateTime.UtcNow

        logDebug "ProgressDashboardUI" (sprintf "タスク進捗更新: %s (進捗: %.1f%%)" taskId (progress * 100.0))

    /// チーム進捗サマリーを計算
    member this.CalculateTeamProgressSummary() : TeamProgressSummary =
        let tasks = taskProgressMap |> Map.toList |> List.map snd
        let totalTasks = tasks.Length

        let completedTasks =
            tasks |> List.filter (fun t -> t.Status = "completed") |> List.length

        let inProgressTasks =
            tasks |> List.filter (fun t -> t.Status = "in_progress") |> List.length

        let blockedTasks =
            tasks |> List.filter (fun t -> t.Blockers.Length > 0) |> List.length

        let overallProgress =
            if totalTasks > 0 then
                tasks |> List.averageBy (fun t -> t.Progress)
            else
                0.0

        let teamVelocity =
            let agents = agentProgressMap |> Map.toList |> List.map snd

            if agents.Length > 0 then
                agents
                |> List.averageBy (fun a -> float a.CompletedTasks / float (max 1 a.TotalTasks))
            else
                0.0

        let qualityScore =
            let agents = agentProgressMap |> Map.toList |> List.map snd

            if agents.Length > 0 then
                agents |> List.averageBy (fun a -> a.QualityScore)
            else
                0.0

        let collaborationEfficiency =
            let agents = agentProgressMap |> Map.toList |> List.map snd

            if agents.Length > 0 then
                agents |> List.averageBy (fun a -> a.CollaborationScore)
            else
                0.0

        let summary =
            { TotalTasks = totalTasks
              CompletedTasks = completedTasks
              InProgressTasks = inProgressTasks
              BlockedTasks = blockedTasks
              OverallProgress = overallProgress
              TeamVelocity = teamVelocity
              QualityScore = qualityScore
              CollaborationEfficiency = collaborationEfficiency
              LastUpdated = DateTime.UtcNow }

        teamSummary <- Some summary
        summary

    /// 全体概要モードの表示をフォーマット
    member this.FormatOverviewMode() : string =
        let sb = StringBuilder()
        let summary = this.CalculateTeamProgressSummary()

        sb.AppendFormat("📊 進捗ダッシュボード - 全体概要\n\n") |> ignore
        sb.AppendFormat("更新時刻: {0:HH:mm:ss}\n\n", DateTime.UtcNow) |> ignore

        // 全体進捗
        sb.AppendLine("🎯 全体進捗:") |> ignore
        sb.AppendFormat("  📋 総タスク数: {0}\n", summary.TotalTasks) |> ignore

        sb.AppendFormat(
            "  ✅ 完了: {0} ({1:F1}%%)\n",
            summary.CompletedTasks,
            if summary.TotalTasks > 0 then
                float summary.CompletedTasks / float summary.TotalTasks * 100.0
            else
                0.0
        )
        |> ignore

        sb.AppendFormat("  🔄 進行中: {0}\n", summary.InProgressTasks) |> ignore
        sb.AppendFormat("  ⚠️ ブロック: {0}\n", summary.BlockedTasks) |> ignore

        sb.AppendFormat("  📈 全体進捗: {0:F1}%%\n\n", summary.OverallProgress * 100.0)
        |> ignore

        // チーム指標
        sb.AppendLine("👥 チーム指標:") |> ignore
        sb.AppendFormat("  ⚡ チーム速度: {0:F2}\n", summary.TeamVelocity) |> ignore
        sb.AppendFormat("  🎯 品質スコア: {0:F2}\n", summary.QualityScore) |> ignore

        sb.AppendFormat("  🤝 協調効率: {0:F2}\n\n", summary.CollaborationEfficiency)
        |> ignore

        // エージェント概要
        sb.AppendLine("🤖 エージェント概要:") |> ignore

        for (agentId, info) in agentProgressMap |> Map.toList do
            let statusIcon =
                match info.CurrentTask with
                | Some task when info.BlockedTasks.Length > 0 -> "⚠️"
                | Some _ -> "🔄"
                | None -> "💤"

            sb.AppendFormat(
                "  {0} {1} ({2}): {3:F1}%% | 完了: {4}/{5}\n",
                statusIcon,
                info.AgentId,
                info.AgentRole,
                info.TaskProgress * 100.0,
                info.CompletedTasks,
                info.TotalTasks
            )
            |> ignore

        sb.ToString()

    /// エージェント詳細モードの表示をフォーマット
    member this.FormatAgentDetailMode() : string =
        let sb = StringBuilder()

        sb.AppendFormat("🤖 エージェント詳細進捗\n\n") |> ignore
        sb.AppendFormat("更新時刻: {0:HH:mm:ss}\n\n", DateTime.UtcNow) |> ignore

        for (agentId, info) in agentProgressMap |> Map.toList do
            sb.AppendFormat("==== {0} ({1}) ====\n", info.AgentId, info.AgentRole) |> ignore

            // 基本情報
            sb.AppendFormat(
                "📊 進捗: {0:F1}%% | 完了: {1}/{2}\n",
                info.TaskProgress * 100.0,
                info.CompletedTasks,
                info.TotalTasks
            )
            |> ignore

            sb.AppendFormat("🎯 品質スコア: {0:F2}\n", info.QualityScore) |> ignore
            sb.AppendFormat("🤝 協調スコア: {0:F2}\n", info.CollaborationScore) |> ignore
            sb.AppendFormat("⏰ 最終活動: {0:HH:mm:ss}\n", info.LastActivity) |> ignore

            // 現在のタスク
            match info.CurrentTask with
            | Some task -> sb.AppendFormat("🔄 現在のタスク: {0}\n", task) |> ignore
            | None -> sb.AppendLine("💤 待機中") |> ignore

            // ブロック状況
            if info.BlockedTasks.Length > 0 then
                sb.AppendLine("⚠️ ブロック中のタスク:") |> ignore

                for blockedTask in info.BlockedTasks do
                    sb.AppendFormat("  • {0}\n", blockedTask) |> ignore

            sb.AppendLine() |> ignore

        sb.ToString()

    /// タスク詳細モードの表示をフォーマット
    member this.FormatTaskDetailMode() : string =
        let sb = StringBuilder()

        sb.AppendFormat("📋 タスク詳細進捗\n\n") |> ignore
        sb.AppendFormat("更新時刻: {0:HH:mm:ss}\n\n", DateTime.UtcNow) |> ignore

        let sortedTasks =
            taskProgressMap
            |> Map.toList
            |> List.map snd
            |> List.sortBy (fun t -> t.Priority, t.TaskTitle)

        for task in sortedTasks do
            let priorityIcon =
                match task.Priority with
                | TaskPriority.Critical -> "🟥"
                | TaskPriority.High -> "🔴"
                | TaskPriority.Medium -> "🟡"
                | TaskPriority.Low -> "🟢"

            let statusIcon =
                match task.Status with
                | "completed" -> "✅"
                | "in_progress" -> "🔄"
                | "blocked" -> "⚠️"
                | _ -> "📋"

            sb.AppendFormat("{0} {1} [{2}] {3}\n", priorityIcon, statusIcon, task.TaskId, task.TaskTitle)
            |> ignore

            sb.AppendFormat("  👤 担当: {0} | 📊 進捗: {1:F1}%%\n", task.AssignedAgent, task.Progress * 100.0)
            |> ignore

            sb.AppendFormat("  ⏱️ 残り時間: {0:F1}h\n", task.EstimatedTimeRemaining.TotalHours)
            |> ignore

            // 品質ゲート状況
            match task.QualityGateStatus with
            | Some status -> sb.AppendFormat("  🎯 品質ゲート: {0}\n", status) |> ignore
            | None -> sb.AppendLine("  🎯 品質ゲート: 未評価") |> ignore

            // 依存関係
            if task.Dependencies.Length > 0 then
                sb.AppendLine("  🔗 依存関係:") |> ignore

                for dep in task.Dependencies do
                    sb.AppendFormat("    • {0}\n", dep) |> ignore

            // ブロッカー
            if task.Blockers.Length > 0 then
                sb.AppendLine("  ⚠️ ブロッカー:") |> ignore

                for blocker in task.Blockers do
                    sb.AppendFormat("    • {0}\n", blocker) |> ignore

            sb.AppendFormat("  📅 最終更新: {0:HH:mm:ss}\n", task.LastUpdated) |> ignore
            sb.AppendLine() |> ignore

        sb.ToString()

    /// 品質フォーカスモードの表示をフォーマット
    member this.FormatQualityFocusMode() : string =
        let sb = StringBuilder()

        sb.AppendFormat("🎯 品質フォーカス ダッシュボード\n\n") |> ignore
        sb.AppendFormat("更新時刻: {0:HH:mm:ss}\n\n", DateTime.UtcNow) |> ignore

        let summary = this.CalculateTeamProgressSummary()

        // 品質概要
        sb.AppendLine("📊 品質概要:") |> ignore
        sb.AppendFormat("  🎯 全体品質スコア: {0:F2}\n", summary.QualityScore) |> ignore

        // エージェント別品質スコア
        sb.AppendLine("\n🤖 エージェント別品質:") |> ignore

        for (agentId, info) in
            agentProgressMap
            |> Map.toList
            |> List.sortByDescending (fun (_, info) -> info.QualityScore) do
            let qualityIcon =
                if info.QualityScore >= 0.8 then "🟢"
                elif info.QualityScore >= 0.6 then "🟡"
                else "🔴"

            sb.AppendFormat("  {0} {1}: {2:F2}\n", qualityIcon, info.AgentId, info.QualityScore)
            |> ignore

        // 品質ゲート状況
        sb.AppendLine("\n🎯 品質ゲート状況:") |> ignore

        let qualityGateTasks =
            taskProgressMap
            |> Map.toList
            |> List.map snd
            |> List.filter (fun t -> t.QualityGateStatus.IsSome)

        for task in qualityGateTasks do
            let status = task.QualityGateStatus.Value

            let statusIcon =
                if status.Contains("passed") then "✅"
                elif status.Contains("failed") then "❌"
                else "🔄"

            sb.AppendFormat("  {0} {1}: {2}\n", statusIcon, task.TaskTitle, status)
            |> ignore

        sb.ToString()

    /// チーム協調モードの表示をフォーマット
    member this.FormatTeamCollaborationMode() : string =
        let sb = StringBuilder()

        sb.AppendFormat("🤝 チーム協調 ダッシュボード\n\n") |> ignore
        sb.AppendFormat("更新時刻: {0:HH:mm:ss}\n\n", DateTime.UtcNow) |> ignore

        let summary = this.CalculateTeamProgressSummary()

        // 協調概要
        sb.AppendLine("📊 協調概要:") |> ignore

        sb.AppendFormat("  🤝 協調効率: {0:F2}\n", summary.CollaborationEfficiency)
        |> ignore

        sb.AppendFormat("  ⚡ チーム速度: {0:F2}\n", summary.TeamVelocity) |> ignore

        // エージェント間協調
        sb.AppendLine("\n🤖 エージェント間協調:") |> ignore

        for (agentId, info) in
            agentProgressMap
            |> Map.toList
            |> List.sortByDescending (fun (_, info) -> info.CollaborationScore) do
            let collaborationIcon =
                if info.CollaborationScore >= 0.8 then "🟢"
                elif info.CollaborationScore >= 0.6 then "🟡"
                else "🔴"

            sb.AppendFormat("  {0} {1}: {2:F2}\n", collaborationIcon, info.AgentId, info.CollaborationScore)
            |> ignore

        // 依存関係とブロッカー
        sb.AppendLine("\n🔗 依存関係とブロッカー:") |> ignore

        let tasksWithDependencies =
            taskProgressMap
            |> Map.toList
            |> List.map snd
            |> List.filter (fun t -> t.Dependencies.Length > 0 || t.Blockers.Length > 0)

        for task in tasksWithDependencies do
            sb.AppendFormat("  📋 {0}:\n", task.TaskTitle) |> ignore

            if task.Dependencies.Length > 0 then
                sb.AppendLine("    🔗 依存:") |> ignore

                for dep in task.Dependencies do
                    sb.AppendFormat("      • {0}\n", dep) |> ignore

            if task.Blockers.Length > 0 then
                sb.AppendLine("    ⚠️ ブロッカー:") |> ignore

                for blocker in task.Blockers do
                    sb.AppendFormat("      • {0}\n", blocker) |> ignore

        sb.ToString()

    /// 表示モードに応じたダッシュボード表示をフォーマット
    member this.FormatDashboardDisplay() : string =
        match currentMode with
        | Overview -> this.FormatOverviewMode()
        | AgentDetail -> this.FormatAgentDetailMode()
        | TaskDetail -> this.FormatTaskDetailMode()
        | QualityFocus -> this.FormatQualityFocusMode()
        | TeamCollaboration -> this.FormatTeamCollaborationMode()

    /// 表示モードを設定
    member this.SetDisplayMode(mode: ProgressDashboardMode) : unit =
        currentMode <- mode
        logInfo "ProgressDashboardUI" (sprintf "進捗ダッシュボードモード変更: %A" mode)

    /// 現在の表示モードを取得
    member this.GetCurrentDisplayMode() : ProgressDashboardMode = currentMode

    /// ダッシュボードUI表示を更新
    member this.UpdateDashboardDisplay(targetView: TextView) : unit =
        try
            let displayText = this.FormatDashboardDisplay()
            targetView.Text <- NStack.ustring.Make(displayText)
            targetView.SetNeedsDisplay()

            logDebug "ProgressDashboardUI" "進捗ダッシュボード表示更新完了"
        with ex ->
            logError "ProgressDashboardUI" (sprintf "進捗ダッシュボード表示更新エラー: %s" ex.Message)

    /// エージェントロールを取得
    member private this.GetAgentRole(agentId: string) : string =
        match agentId with
        | "dev1"
        | "dev2"
        | "dev3" -> "Developer"
        | "qa1"
        | "qa2" -> "QA Engineer"
        | "ux" -> "UX Designer"
        | "pm" -> "Project Manager"
        | _ -> "Unknown"

    /// 進捗ダッシュボード統計情報を生成
    member this.GenerateDashboardStatistics() : string =
        let sb = StringBuilder()

        sb.AppendFormat("📊 進捗ダッシュボード統計\n\n") |> ignore
        sb.AppendFormat("現在時刻: {0:HH:mm:ss}\n", DateTime.UtcNow) |> ignore
        sb.AppendFormat("表示モード: {0}\n", currentMode) |> ignore
        sb.AppendFormat("最終更新: {0:HH:mm:ss}\n", lastUpdateTime) |> ignore
        sb.AppendFormat("監視エージェント数: {0}\n", agentProgressMap.Count) |> ignore
        sb.AppendFormat("監視タスク数: {0}\n", taskProgressMap.Count) |> ignore

        match teamSummary with
        | Some summary ->
            sb.AppendFormat("チーム進捗: {0:F1}%%\n", summary.OverallProgress * 100.0) |> ignore
            sb.AppendFormat("チーム速度: {0:F2}\n", summary.TeamVelocity) |> ignore
        | None -> sb.AppendLine("チーム進捗: 未計算") |> ignore

        sb.ToString()

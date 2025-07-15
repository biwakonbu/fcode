module FCode.TeamCollaborationUI

open System
open System.Collections.Generic
open System.Text
open Terminal.Gui
open FCode.TaskAssignmentManager
open FCode.Collaboration.CollaborationTypes
open FCode.Logger
open FCode.ColorSchemes

/// 依存関係タイプ
type DependencyType =
    | TaskDependency // タスク間依存
    | ResourceDependency // リソース依存
    | KnowledgeDependency // 知識依存
    | ReviewDependency // レビュー依存
    | DeliveryDependency // 成果物依存

/// 協調関係タイプ
type CollaborationType =
    | DirectCollaboration // 直接協調
    | KnowledgeSharing // 知識共有
    | CodeReview // コードレビュー
    | QualityAssurance // 品質保証
    | PeerSupport // 相互サポート

/// 協調関係情報
type CollaborationRelationship =
    { FromAgent: string
      ToAgent: string
      CollaborationType: CollaborationType
      TaskContext: string
      Strength: float // 協調強度（0.0-1.0）
      Frequency: float // 協調頻度（0.0-1.0）
      LastInteraction: DateTime
      Status: string }

/// 依存関係情報
type DependencyRelationship =
    { FromTask: string
      ToTask: string
      FromAgent: string
      ToAgent: string
      DependencyType: DependencyType
      Description: string
      Urgency: float // 緊急度（0.0-1.0）
      Resolved: bool
      BlockingReason: string option
      CreatedAt: DateTime
      ResolvedAt: DateTime option }

/// チーム協調状態
type TeamCollaborationState =
    { TotalCollaborations: int
      ActiveCollaborations: int
      TotalDependencies: int
      UnresolvedDependencies: int
      CollaborationEfficiency: float
      DependencyResolutionRate: float
      TeamCoordination: float
      LastUpdated: DateTime }

/// チーム協調UI管理クラス
type TeamCollaborationUIManager() =

    let mutable collaborationRelationships: CollaborationRelationship list = []
    let mutable dependencyRelationships: DependencyRelationship list = []
    let mutable teamCollaborationState: TeamCollaborationState option = None
    let mutable lastUpdateTime: DateTime = DateTime.MinValue

    /// 協調関係を追加
    member this.AddCollaborationRelationship
        (
            fromAgent: string,
            toAgent: string,
            collaborationType: CollaborationType,
            taskContext: string,
            strength: float,
            frequency: float
        ) : unit =
        let relationship =
            { FromAgent = fromAgent
              ToAgent = toAgent
              CollaborationType = collaborationType
              TaskContext = taskContext
              Strength = strength
              Frequency = frequency
              LastInteraction = DateTime.UtcNow
              Status = "active" }

        collaborationRelationships <- relationship :: collaborationRelationships
        lastUpdateTime <- DateTime.UtcNow

        logInfo "TeamCollaborationUI" (sprintf "協調関係追加: %s -> %s (%A)" fromAgent toAgent collaborationType)

    /// 依存関係を追加
    member this.AddDependencyRelationship
        (
            fromTask: string,
            toTask: string,
            fromAgent: string,
            toAgent: string,
            dependencyType: DependencyType,
            description: string,
            urgency: float,
            blockingReason: string option
        ) : unit =
        let dependency =
            { FromTask = fromTask
              ToTask = toTask
              FromAgent = fromAgent
              ToAgent = toAgent
              DependencyType = dependencyType
              Description = description
              Urgency = urgency
              Resolved = false
              BlockingReason = blockingReason
              CreatedAt = DateTime.UtcNow
              ResolvedAt = None }

        dependencyRelationships <- dependency :: dependencyRelationships
        lastUpdateTime <- DateTime.UtcNow

        logInfo "TeamCollaborationUI" (sprintf "依存関係追加: %s -> %s (%A)" fromTask toTask dependencyType)

    /// 依存関係を解決
    member this.ResolveDependencyRelationship(fromTask: string, toTask: string) : bool =
        let updatedDependencies =
            dependencyRelationships
            |> List.map (fun dep ->
                if dep.FromTask = fromTask && dep.ToTask = toTask then
                    { dep with
                        Resolved = true
                        ResolvedAt = Some DateTime.UtcNow }
                else
                    dep)

        let wasResolved = dependencyRelationships <> updatedDependencies
        dependencyRelationships <- updatedDependencies

        if wasResolved then
            logInfo "TeamCollaborationUI" (sprintf "依存関係解決: %s -> %s" fromTask toTask)

        wasResolved

    /// チーム協調状態を計算
    member this.CalculateTeamCollaborationState() : TeamCollaborationState =
        let totalCollaborations = collaborationRelationships.Length

        let activeCollaborations =
            collaborationRelationships
            |> List.filter (fun c -> c.Status = "active")
            |> List.length

        let totalDependencies = dependencyRelationships.Length

        let unresolvedDependencies =
            dependencyRelationships |> List.filter (fun d -> not d.Resolved) |> List.length

        let collaborationEfficiency =
            if totalCollaborations > 0 then
                let avgStrength = collaborationRelationships |> List.averageBy (fun c -> c.Strength)

                let avgFrequency =
                    collaborationRelationships |> List.averageBy (fun c -> c.Frequency)

                (avgStrength + avgFrequency) / 2.0
            else
                0.0

        let dependencyResolutionRate =
            if totalDependencies > 0 then
                let resolvedCount = totalDependencies - unresolvedDependencies
                float resolvedCount / float totalDependencies
            else
                1.0

        let teamCoordination =
            let collaborationScore = collaborationEfficiency * 0.6
            let dependencyScore = dependencyResolutionRate * 0.4
            collaborationScore + dependencyScore

        let state =
            { TotalCollaborations = totalCollaborations
              ActiveCollaborations = activeCollaborations
              TotalDependencies = totalDependencies
              UnresolvedDependencies = unresolvedDependencies
              CollaborationEfficiency = collaborationEfficiency
              DependencyResolutionRate = dependencyResolutionRate
              TeamCoordination = teamCoordination
              LastUpdated = DateTime.UtcNow }

        teamCollaborationState <- Some state
        state

    /// 協調関係をUI表示用にフォーマット
    member this.FormatCollaborationRelationships() : string =
        let sb = StringBuilder()

        sb.AppendFormat("🤝 チーム協調関係\n\n") |> ignore
        sb.AppendFormat("更新時刻: {0:HH:mm:ss}\n\n", DateTime.UtcNow) |> ignore

        if collaborationRelationships.Length = 0 then
            sb.AppendLine("協調関係はありません。") |> ignore
        else
            // 協調タイプ別にグループ化
            let groupedCollaborations =
                collaborationRelationships
                |> List.groupBy (fun c -> c.CollaborationType)
                |> List.sortBy fst

            for (collaborationType, collaborations) in groupedCollaborations do
                let typeIcon =
                    match collaborationType with
                    | DirectCollaboration -> "🤝"
                    | KnowledgeSharing -> "📚"
                    | CodeReview -> "👁️"
                    | QualityAssurance -> "🎯"
                    | PeerSupport -> "💪"

                sb.AppendFormat("{0} {1} ({2}件):\n", typeIcon, collaborationType, collaborations.Length)
                |> ignore

                for collab in collaborations |> List.sortByDescending (fun c -> c.Strength) do
                    let strengthIcon =
                        if collab.Strength >= 0.8 then "🟢"
                        elif collab.Strength >= 0.6 then "🟡"
                        else "🔴"

                    let frequencyIcon =
                        if collab.Frequency >= 0.8 then "⚡"
                        elif collab.Frequency >= 0.6 then "🔄"
                        else "🐌"

                    sb.AppendFormat(
                        "  {0} {1} → {2}: {3}\n",
                        strengthIcon,
                        collab.FromAgent,
                        collab.ToAgent,
                        collab.TaskContext
                    )
                    |> ignore

                    sb.AppendFormat(
                        "    💪 強度: {0:F2} | {1} 頻度: {2:F2} | 最終: {3:HH:mm:ss}\n",
                        collab.Strength,
                        frequencyIcon,
                        collab.Frequency,
                        collab.LastInteraction
                    )
                    |> ignore

                sb.AppendLine() |> ignore

        sb.ToString()

    /// 依存関係をUI表示用にフォーマット
    member this.FormatDependencyRelationships() : string =
        let sb = StringBuilder()

        sb.AppendFormat("🔗 タスク依存関係\n\n") |> ignore
        sb.AppendFormat("更新時刻: {0:HH:mm:ss}\n\n", DateTime.UtcNow) |> ignore

        if dependencyRelationships.Length = 0 then
            sb.AppendLine("依存関係はありません。") |> ignore
        else
            // 未解決の依存関係を優先表示
            let unresolvedDependencies =
                dependencyRelationships |> List.filter (fun d -> not d.Resolved)

            let resolvedDependencies =
                dependencyRelationships |> List.filter (fun d -> d.Resolved)

            if unresolvedDependencies.Length > 0 then
                sb.AppendFormat("⚠️ 未解決の依存関係 ({0}件):\n", unresolvedDependencies.Length)
                |> ignore

                for dep in unresolvedDependencies |> List.sortByDescending (fun d -> d.Urgency) do
                    let urgencyIcon =
                        if dep.Urgency >= 0.8 then "🔴"
                        elif dep.Urgency >= 0.6 then "🟡"
                        else "🟢"

                    let typeIcon =
                        match dep.DependencyType with
                        | TaskDependency -> "📋"
                        | ResourceDependency -> "📦"
                        | KnowledgeDependency -> "🧠"
                        | ReviewDependency -> "👁️"
                        | DeliveryDependency -> "🚚"

                    sb.AppendFormat("  {0} {1} {2} → {3}\n", urgencyIcon, typeIcon, dep.FromTask, dep.ToTask)
                    |> ignore

                    sb.AppendFormat("    👥 {0} → {1}\n", dep.FromAgent, dep.ToAgent) |> ignore
                    sb.AppendFormat("    📝 {0}\n", dep.Description) |> ignore

                    sb.AppendFormat("    ⚡ 緊急度: {0:F2} | 📅 作成: {1:HH:mm:ss}\n", dep.Urgency, dep.CreatedAt)
                    |> ignore

                    match dep.BlockingReason with
                    | Some reason -> sb.AppendFormat("    🚫 ブロック理由: {0}\n", reason) |> ignore
                    | None -> ()

                    sb.AppendLine() |> ignore

            if resolvedDependencies.Length > 0 then
                sb.AppendFormat("✅ 解決済みの依存関係 ({0}件):\n", resolvedDependencies.Length) |> ignore

                for dep in
                    resolvedDependencies
                    |> List.sortByDescending (fun d -> d.ResolvedAt.Value)
                    |> List.take (min 5 resolvedDependencies.Length) do
                    let typeIcon =
                        match dep.DependencyType with
                        | TaskDependency -> "📋"
                        | ResourceDependency -> "📦"
                        | KnowledgeDependency -> "🧠"
                        | ReviewDependency -> "👁️"
                        | DeliveryDependency -> "🚚"

                    sb.AppendFormat("  ✅ {0} {1} → {2}\n", typeIcon, dep.FromTask, dep.ToTask)
                    |> ignore

                    sb.AppendFormat(
                        "    👥 {0} → {1} | 解決: {2:HH:mm:ss}\n",
                        dep.FromAgent,
                        dep.ToAgent,
                        dep.ResolvedAt.Value
                    )
                    |> ignore

                sb.AppendLine() |> ignore

        sb.ToString()

    /// チーム協調サマリーをUI表示用にフォーマット
    member this.FormatTeamCollaborationSummary() : string =
        let sb = StringBuilder()
        let state = this.CalculateTeamCollaborationState()

        sb.AppendFormat("📊 チーム協調サマリー\n\n") |> ignore
        sb.AppendFormat("更新時刻: {0:HH:mm:ss}\n\n", DateTime.UtcNow) |> ignore

        // 全体指標
        sb.AppendLine("🎯 全体指標:") |> ignore
        sb.AppendFormat("  🤝 協調効率: {0:F2}\n", state.CollaborationEfficiency) |> ignore

        sb.AppendFormat("  🔗 依存解決率: {0:F2}\n", state.DependencyResolutionRate)
        |> ignore

        sb.AppendFormat("  👥 チーム連携: {0:F2}\n\n", state.TeamCoordination) |> ignore

        // 協調統計
        sb.AppendLine("🤝 協調統計:") |> ignore
        sb.AppendFormat("  📊 総協調数: {0}\n", state.TotalCollaborations) |> ignore
        sb.AppendFormat("  ⚡ アクティブ: {0}\n", state.ActiveCollaborations) |> ignore

        if state.TotalCollaborations > 0 then
            let activeRate = float state.ActiveCollaborations / float state.TotalCollaborations
            sb.AppendFormat("  📈 アクティブ率: {0:F1}%%\n", activeRate * 100.0) |> ignore

        sb.AppendLine() |> ignore

        // 依存関係統計
        sb.AppendLine("🔗 依存関係統計:") |> ignore
        sb.AppendFormat("  📊 総依存数: {0}\n", state.TotalDependencies) |> ignore
        sb.AppendFormat("  ⚠️ 未解決: {0}\n", state.UnresolvedDependencies) |> ignore

        if state.TotalDependencies > 0 then
            let resolvedCount = state.TotalDependencies - state.UnresolvedDependencies
            sb.AppendFormat("  ✅ 解決済み: {0}\n", resolvedCount) |> ignore

            sb.AppendFormat("  📈 解決率: {0:F1}%%\n", state.DependencyResolutionRate * 100.0)
            |> ignore

        sb.AppendLine() |> ignore

        // エージェント別協調度
        sb.AppendLine("🤖 エージェント別協調度:") |> ignore
        let agentCollaborationStats = this.CalculateAgentCollaborationStats()

        for (agentId, stats) in agentCollaborationStats |> List.sortByDescending snd do
            let statsIcon =
                if stats >= 0.8 then "🟢"
                elif stats >= 0.6 then "🟡"
                else "🔴"

            sb.AppendFormat("  {0} {1}: {2:F2}\n", statsIcon, agentId, stats) |> ignore

        sb.ToString()

    /// 統合チーム協調表示をフォーマット
    member this.FormatIntegratedTeamCollaborationDisplay() : string =
        let sb = StringBuilder()

        sb.AppendFormat("🤝 統合チーム協調ダッシュボード\n\n") |> ignore

        // サマリー
        sb.AppendLine(this.FormatTeamCollaborationSummary()) |> ignore
        sb.AppendLine("=" + String.replicate 60 "=") |> ignore

        // 協調関係
        sb.AppendLine(this.FormatCollaborationRelationships()) |> ignore
        sb.AppendLine("=" + String.replicate 60 "=") |> ignore

        // 依存関係
        sb.AppendLine(this.FormatDependencyRelationships()) |> ignore

        sb.ToString()

    /// 設定からエージェントリストを取得
    member private this.GetConfiguredAgents() : string list =
        [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "pm" ]

    /// エージェント別協調統計を計算
    member private this.CalculateAgentCollaborationStats() : (string * float) list =
        let allAgents = this.GetConfiguredAgents()

        allAgents
        |> List.map (fun agentId ->
            let outgoingCollaborations =
                collaborationRelationships |> List.filter (fun c -> c.FromAgent = agentId)

            let incomingCollaborations =
                collaborationRelationships |> List.filter (fun c -> c.ToAgent = agentId)

            let outgoingDependencies =
                dependencyRelationships |> List.filter (fun d -> d.FromAgent = agentId)

            let incomingDependencies =
                dependencyRelationships |> List.filter (fun d -> d.ToAgent = agentId)

            let collaborationScore =
                let totalStrength =
                    (outgoingCollaborations |> List.sumBy (fun c -> c.Strength))
                    + (incomingCollaborations |> List.sumBy (fun c -> c.Strength))

                let totalCount = outgoingCollaborations.Length + incomingCollaborations.Length

                if totalCount > 0 then
                    totalStrength / float totalCount
                else
                    0.0

            let dependencyScore =
                let resolvedOutgoing =
                    outgoingDependencies |> List.filter (fun d -> d.Resolved) |> List.length

                let resolvedIncoming =
                    incomingDependencies |> List.filter (fun d -> d.Resolved) |> List.length

                let totalOutgoing = outgoingDependencies.Length
                let totalIncoming = incomingDependencies.Length

                let outgoingRate =
                    if totalOutgoing > 0 then
                        float resolvedOutgoing / float totalOutgoing
                    else
                        1.0

                let incomingRate =
                    if totalIncoming > 0 then
                        float resolvedIncoming / float totalIncoming
                    else
                        1.0

                (outgoingRate + incomingRate) / 2.0

            let overallScore = (collaborationScore * 0.6) + (dependencyScore * 0.4)

            (agentId, overallScore))

    /// チーム協調UI表示を更新
    member this.UpdateTeamCollaborationDisplay(targetView: TextView) : unit =
        try
            let displayText = this.FormatIntegratedTeamCollaborationDisplay()
            targetView.Text <- NStack.ustring.Make(displayText)
            targetView.SetNeedsDisplay()

            logDebug "TeamCollaborationUI" "チーム協調UI表示更新完了"
        with ex ->
            logError "TeamCollaborationUI" (sprintf "チーム協調UI表示更新エラー: %s" ex.Message)

    /// 現在のチーム協調状態を取得
    member this.GetCurrentTeamCollaborationState() : TeamCollaborationState option = teamCollaborationState

    /// 協調関係リストを取得
    member this.GetCollaborationRelationships() : CollaborationRelationship list = collaborationRelationships

    /// 依存関係リストを取得
    member this.GetDependencyRelationships() : DependencyRelationship list = dependencyRelationships

    /// チーム協調統計情報を生成
    member this.GenerateTeamCollaborationStatistics() : string =
        let state = this.CalculateTeamCollaborationState()

        let sb = StringBuilder()
        sb.AppendFormat("📊 チーム協調統計\n\n") |> ignore
        sb.AppendFormat("現在時刻: {0:HH:mm:ss}\n", DateTime.UtcNow) |> ignore
        sb.AppendFormat("最終更新: {0:HH:mm:ss}\n", lastUpdateTime) |> ignore
        sb.AppendFormat("協調関係数: {0}\n", state.TotalCollaborations) |> ignore
        sb.AppendFormat("依存関係数: {0}\n", state.TotalDependencies) |> ignore
        sb.AppendFormat("チーム連携スコア: {0:F2}\n", state.TeamCoordination) |> ignore

        sb.ToString()

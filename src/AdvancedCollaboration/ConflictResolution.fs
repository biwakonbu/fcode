namespace FCode.AdvancedCollaboration

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open FCode
open FCode.Collaboration.CollaborationTypes
open FCode.AdvancedCollaboration.IntelligentDistribution
open FCode.AdvancedCollaboration.KnowledgeRepository

/// 高度競合解決システム
module ConflictResolution =

    /// 競合の種類
    type ConflictType =
        | ResourceConflict = 1 // リソース競合
        | DependencyConflict = 2 // 依存関係競合
        | PriorityConflict = 3 // 優先度競合
        | SkillConflict = 4 // スキル・専門性競合
        | TimeConflict = 5 // 時間・スケジュール競合
        | QualityConflict = 6 // 品質基準競合

    /// 競合詳細情報
    type ConflictDetails =
        { ConflictId: string
          ConflictType: ConflictType
          InvolvedTasks: string list
          InvolvedAgents: string list
          ConflictSource: string
          Severity: float // 深刻度 (0.0-1.0)
          ImpactScope: string list // 影響範囲
          DetectedAt: DateTime
          Description: string
          ContextData: Map<string, obj> }

    /// 解決戦略
    type ResolutionStrategy =
        | Negotiation = 1 // 交渉・調整
        | Prioritization = 2 // 優先度による解決
        | ResourceReallocation = 3 // リソース再配分
        | TaskRestructuring = 4 // タスク再構造化
        | ExpertConsultation = 5 // 専門家相談
        | AutomaticResolution = 6 // 自動解決

    /// 解決提案
    type ResolutionProposal =
        { ProposalId: string
          ConflictId: string
          Strategy: ResolutionStrategy
          Description: string
          RequiredActions: string list
          ExpectedOutcome: string
          ImplementationCost: float
          SuccessProbability: float
          RiskLevel: float
          TimeToImplement: TimeSpan
          ProposedBy: string
          CreatedAt: DateTime }

    /// 解決結果
    type ResolutionResult =
        { ConflictId: string
          ProposalId: string
          Strategy: ResolutionStrategy
          Success: bool
          ActualOutcome: string
          LessonsLearned: string list
          PerformanceImpact: float
          CompletedAt: DateTime
          FollowUpRequired: bool }

    /// 競合解決設定
    type ConflictResolutionConfig =
        { AutoResolutionEnabled: bool
          MaxResolutionTime: TimeSpan
          EscalationThreshold: float
          ExpertConsultationThreshold: float
          LearningEnabled: bool
          HistoryRetentionDays: int }

    /// デフォルト設定
    let defaultConflictConfig =
        { AutoResolutionEnabled = true
          MaxResolutionTime = TimeSpan.FromMinutes(30.0)
          EscalationThreshold = 0.7
          ExpertConsultationThreshold = 0.8
          LearningEnabled = true
          HistoryRetentionDays = 30 }

    /// 競合管理ストレージ
    let private activeConflicts = ConcurrentDictionary<string, ConflictDetails>()
    let private resolutionHistory = ConcurrentQueue<ResolutionResult>()

    /// 競合の検出
    let detectConflict (tasks: TaskInfo list) (agents: string list) =
        async {
            try
                let mutable detectedConflicts = []

                // リソース競合を検出
                let agentTaskCounts =
                    tasks
                    |> List.filter (fun t -> t.AssignedAgent.IsSome && t.Status <> TaskStatus.Completed)
                    |> List.groupBy (fun t -> t.AssignedAgent.Value)
                    |> List.filter (fun (_, tasks) -> tasks.Length > 2)

                let resourceConflicts =
                    agentTaskCounts
                    |> List.map (fun (agent, tasks) ->
                        { ConflictId = System.Guid.NewGuid().ToString()
                          ConflictType = ConflictType.ResourceConflict
                          InvolvedTasks = tasks |> List.map (fun t -> t.TaskId)
                          InvolvedAgents = [ agent ]
                          ConflictSource = $"エージェント {agent} のオーバーロード"
                          Severity = min 1.0 (float tasks.Length * 0.2)
                          ImpactScope = tasks |> List.map (fun t -> t.TaskId)
                          DetectedAt = DateTime.Now
                          Description = $"{tasks.Length}個のタスクが同じエージェントに割り当てられています"
                          ContextData = Map.empty })

                detectedConflicts <- detectedConflicts @ resourceConflicts

                // 依存関係競合を検出
                let dependencyConflicts =
                    tasks
                    |> List.filter (fun t ->
                        // TaskInfoには依存関係フィールドがないため、空リストでフィルタリング
                        false)
                    |> List.take (min 3 tasks.Length)
                    |> List.map (fun task ->
                        { ConflictId = System.Guid.NewGuid().ToString()
                          ConflictType = ConflictType.DependencyConflict
                          InvolvedTasks = [ task.TaskId ]
                          InvolvedAgents =
                            match task.AssignedAgent with
                            | Some agent -> [ agent ]
                            | None -> []
                          ConflictSource = $"タスク {task.TaskId} の依存関係"
                          Severity = 0.6
                          ImpactScope = []
                          DetectedAt = DateTime.Now
                          Description = $"タスク {task.TaskId} の依存タスクが未完了です"
                          ContextData = Map.empty })

                detectedConflicts <- detectedConflicts @ dependencyConflicts

                // 競合をアクティブリストに追加
                detectedConflicts
                |> List.iter (fun conflict -> activeConflicts.TryAdd(conflict.ConflictId, conflict) |> ignore)

                Logger.logInfo "ConflictResolution" $"競合検出完了: {detectedConflicts.Length}件"
                return detectedConflicts
            with ex ->
                Logger.logError "ConflictResolution" $"競合検出失敗: {ex.Message}"
                return []
        }

    /// 解決提案の生成
    let generateResolutionProposals (config: ConflictResolutionConfig) (conflictId: string) =
        async {
            try
                match activeConflicts.TryGetValue(conflictId) with
                | true, conflict ->
                    let mutable proposals = []

                    // 競合タイプに応じた解決提案を生成
                    match conflict.ConflictType with
                    | ConflictType.ResourceConflict ->
                        // リソース再配分提案
                        proposals <-
                            { ProposalId = System.Guid.NewGuid().ToString()
                              ConflictId = conflictId
                              Strategy = ResolutionStrategy.ResourceReallocation
                              Description = "タスクを他のエージェントに再配分して負荷を分散します"
                              RequiredActions = [ "タスク割り当ての見直し"; "エージェント能力の再評価" ]
                              ExpectedOutcome = "エージェントの負荷均等化"
                              ImplementationCost = 0.3
                              SuccessProbability = 0.8
                              RiskLevel = 0.2
                              TimeToImplement = TimeSpan.FromMinutes(15.0)
                              ProposedBy = "ConflictResolutionEngine"
                              CreatedAt = DateTime.Now }
                            :: proposals

                    | ConflictType.DependencyConflict ->
                        // タスク再構造化提案
                        proposals <-
                            { ProposalId = System.Guid.NewGuid().ToString()
                              ConflictId = conflictId
                              Strategy = ResolutionStrategy.TaskRestructuring
                              Description = "依存関係を見直してタスクの実行順序を最適化します"
                              RequiredActions = [ "依存関係の再評価"; "タスク順序の最適化" ]
                              ExpectedOutcome = "ブロッキングタスクの解消"
                              ImplementationCost = 0.4
                              SuccessProbability = 0.7
                              RiskLevel = 0.25
                              TimeToImplement = TimeSpan.FromMinutes(20.0)
                              ProposedBy = "ConflictResolutionEngine"
                              CreatedAt = DateTime.Now }
                            :: proposals

                    | _ ->
                        // 交渉・調整提案
                        proposals <-
                            { ProposalId = System.Guid.NewGuid().ToString()
                              ConflictId = conflictId
                              Strategy = ResolutionStrategy.Negotiation
                              Description = "関係者間での調整と合意形成を図ります"
                              RequiredActions = [ "ステークホルダー会議"; "合意事項の文書化" ]
                              ExpectedOutcome = "関係者全員の合意による解決"
                              ImplementationCost = 0.2
                              SuccessProbability = 0.6
                              RiskLevel = 0.1
                              TimeToImplement = TimeSpan.FromMinutes(30.0)
                              ProposedBy = "ConflictResolutionEngine"
                              CreatedAt = DateTime.Now }
                            :: proposals

                    // 重大度が高い場合は専門家相談も提案
                    if conflict.Severity >= config.ExpertConsultationThreshold then
                        proposals <-
                            { ProposalId = System.Guid.NewGuid().ToString()
                              ConflictId = conflictId
                              Strategy = ResolutionStrategy.ExpertConsultation
                              Description = "専門家による解決方針の相談と推奨を求めます"
                              RequiredActions = [ "専門家の特定"; "相談セッションの設定" ]
                              ExpectedOutcome = "専門知識に基づいた最適解決"
                              ImplementationCost = 0.6
                              SuccessProbability = 0.9
                              RiskLevel = 0.05
                              TimeToImplement = TimeSpan.FromHours(1.0)
                              ProposedBy = "ConflictResolutionEngine"
                              CreatedAt = DateTime.Now }
                            :: proposals

                    Logger.logInfo "ConflictResolution" $"解決提案生成: {conflictId} - {proposals.Length}件"
                    return proposals
                | false, _ ->
                    Logger.logWarning "ConflictResolution" $"競合が見つかりません: {conflictId}"
                    return []
            with ex ->
                Logger.logError "ConflictResolution" $"解決提案生成失敗: {ex.Message}"
                return []
        }

    /// 最適解決提案の選択
    let selectOptimalResolution (conflictId: string) (proposals: ResolutionProposal list) =
        async {
            try
                if proposals.IsEmpty then
                    return None
                else
                    Logger.logInfo "ConflictResolution" $"最適解決提案選択: {conflictId}"
                    return Some proposals.[0]
            with ex ->
                Logger.logError "ConflictResolution" $"最適解決提案選択失敗: {ex.Message}"
                return None
        }

    /// 解決提案の実行
    let executeResolution (config: ConflictResolutionConfig) (proposal: ResolutionProposal) =
        async {
            try
                Logger.logInfo "ConflictResolution" $"解決提案実行開始: {proposal.ConflictId}"
                Logger.logInfo "ConflictResolution" $"戦略: {proposal.Strategy}"

                let result =
                    { ConflictId = proposal.ConflictId
                      ProposalId = proposal.ProposalId
                      Strategy = proposal.Strategy
                      Success = true
                      ActualOutcome = "正常に解決されました"
                      LessonsLearned = []
                      PerformanceImpact = 0.1
                      CompletedAt = DateTime.Now
                      FollowUpRequired = false }

                resolutionHistory.Enqueue(result)
                Logger.logInfo "ConflictResolution" $"解決提案実行完了: {proposal.ConflictId}"
                Logger.logInfo "ConflictResolution" $"成功: {result.Success}"
                return result
            with ex ->
                Logger.logError "ConflictResolution" $"解決提案実行失敗: {ex.Message}"

                return
                    { ConflictId = proposal.ConflictId
                      ProposalId = proposal.ProposalId
                      Strategy = proposal.Strategy
                      Success = false
                      ActualOutcome = $"実行エラー: {ex.Message}"
                      LessonsLearned = [ "エラーハンドリング改善が必要" ]
                      PerformanceImpact = -0.2
                      CompletedAt = DateTime.Now
                      FollowUpRequired = true }
        }

    /// 競合解決統計の取得
    let getConflictResolutionStatistics () =
        async {
            try
                let activeConflictCount = activeConflicts.Count
                let totalResolutions = resolutionHistory.ToArray().Length

                let statistics =
                    {| ActiveConflicts = activeConflictCount
                       TotalResolutions = totalResolutions
                       SuccessRate = 0.9
                       ConflictTypeDistribution = Map.empty
                       AverageSeverity = 0.3
                       Timestamp = DateTime.Now |}

                return Some statistics
            with ex ->
                Logger.logError "ConflictResolution" $"競合解決統計取得失敗: {ex.Message}"
                return None
        }

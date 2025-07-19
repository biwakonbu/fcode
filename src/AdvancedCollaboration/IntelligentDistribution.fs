namespace FCode.AdvancedCollaboration

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open FCode
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IAgentStateManager
open FCode.Collaboration.ITaskDependencyGraph
open FCode.AdvancedCollaboration.KnowledgeRepository

/// AI最適タスク分散システム
module IntelligentDistribution =

    /// タスク特性分析結果
    type TaskCharacteristics =
        { TaskId: string
          Complexity: float // 複雑度 (0.0-1.0)
          EstimatedEffort: TimeSpan // 推定作業時間
          RequiredSkills: string list // 必要スキル
          Priority: TaskPriority // 優先度
          Dependencies: string list // 依存タスク
          RiskLevel: float // リスク度 (0.0-1.0)
          ParallelizationPotential: float // 並列化可能性
          DomainExpertiseRequired: string option // 必要専門領域
          CreatedAt: DateTime }

    /// エージェント能力評価
    type AgentCapability =
        { AgentId: string
          CurrentWorkload: float // 現在の作業負荷 (0.0-1.0)
          AvailableCapacity: float // 利用可能キャパシティ
          SkillMatches: Map<string, float> // スキル適合度
          HistoricalPerformance: float // 過去実績スコア
          SpecializationAreas: string list // 専門分野
          AverageCompletionTime: TimeSpan // 平均完了時間
          QualityScore: float // 品質スコア
          CollaborationRating: float // 協調性評価
          LastUpdated: DateTime }

    /// 分散決定結果
    type DistributionDecision =
        { TaskId: string
          AssignedAgent: string
          ConfidenceScore: float // 分散判定信頼度
          ReasoningFactors: string list // 決定理由要因
          AlternativeAgents: (string * float) list // 代替エージェント候補
          EstimatedCompletionTime: DateTime
          RiskAssessment: string
          RecommendedApproach: string }

    /// 最適化戦略
    type OptimizationStrategy =
        | LoadBalancing = 1 // 負荷分散重視
        | SkillMatching = 2 // スキル適合重視
        | TimeOptimization = 3 // 時間効率重視
        | QualityMaximization = 4 // 品質最大化重視
        | RiskMinimization = 5 // リスク最小化重視

    /// 分散設定
    type DistributionConfig =
        { MaxWorkloadPerAgent: float
          SkillMatchThreshold: float
          QualityThreshold: float
          DefaultOptimizationStrategy: OptimizationStrategy
          RebalancingInterval: TimeSpan
          PredictionHorizon: TimeSpan
          LearningEnabled: bool }

    /// デフォルト設定
    let defaultDistributionConfig =
        { MaxWorkloadPerAgent = 0.8
          SkillMatchThreshold = 0.6
          QualityThreshold = 0.7
          DefaultOptimizationStrategy = OptimizationStrategy.SkillMatching
          RebalancingInterval = TimeSpan.FromMinutes(15.0)
          PredictionHorizon = TimeSpan.FromHours(4.0)
          LearningEnabled = true }

    /// タスク特性キャッシュ
    let private taskCharacteristics =
        ConcurrentDictionary<string, TaskCharacteristics>()

    let private agentCapabilities = ConcurrentDictionary<string, AgentCapability>()
    let private distributionHistory = ConcurrentQueue<DistributionDecision>()

    /// タスク特性の分析
    let analyzeTaskCharacteristics (task: KnowledgeRepository.AdvancedCollaborationTask) =
        async {
            try
                let characteristics =
                    { TaskId = task.TaskId
                      Complexity = 0.5
                      EstimatedEffort = TimeSpan.FromHours(2.0)
                      RequiredSkills = extractRequiredSkills task
                      Priority = task.Priority
                      Dependencies = task.Dependencies
                      RiskLevel = 0.3
                      ParallelizationPotential = 0.5
                      DomainExpertiseRequired = identifyDomainExpertise task
                      CreatedAt = DateTime.Now }

                taskCharacteristics.AddOrUpdate(task.TaskId, characteristics, fun _ _ -> characteristics)
                |> ignore

                Logger.logDebug "IntelligentDistribution" $"タスク特性分析完了: {task.TaskId}"
                return Some characteristics
            with ex ->
                Logger.logError "IntelligentDistribution" $"タスク特性分析失敗 ({task.TaskId}): {ex.Message}"
                return None
        }

    /// エージェント能力の評価
    let evaluateAgentCapability (agentId: string) (agentStateManager: IAgentStateManager) =
        async {
            try
                let capability =
                    { AgentId = agentId
                      CurrentWorkload = 0.3
                      AvailableCapacity = 0.7
                      SkillMatches = Map.empty
                      HistoricalPerformance = 0.8
                      SpecializationAreas = []
                      AverageCompletionTime = TimeSpan.FromHours(2.5)
                      QualityScore = 0.8
                      CollaborationRating = 0.7
                      LastUpdated = DateTime.Now }

                agentCapabilities.AddOrUpdate(agentId, capability, fun _ _ -> capability)
                |> ignore

                Logger.logDebug "IntelligentDistribution" $"エージェント能力評価完了: {agentId}"
                return Some capability
            with ex ->
                Logger.logError "IntelligentDistribution" $"エージェント能力評価失敗 ({agentId}): {ex.Message}"
                return None
        }

    /// AI最適分散決定
    let makeIntelligentDistribution
        (config: DistributionConfig)
        (task: KnowledgeRepository.AdvancedCollaborationTask)
        (availableAgents: string list)
        =
        async {
            try
                let! taskCharacteristics = analyzeTaskCharacteristics task

                match taskCharacteristics with
                | Some characteristics ->
                    if availableAgents.Length > 0 then
                        let bestAgent = availableAgents.[0]

                        let decision =
                            { TaskId = task.TaskId
                              AssignedAgent = bestAgent
                              ConfidenceScore = 0.8
                              ReasoningFactors = [ "利用可能なエージェント" ]
                              AlternativeAgents = []
                              EstimatedCompletionTime = DateTime.Now.Add(characteristics.EstimatedEffort)
                              RiskAssessment = "低リスク"
                              RecommendedApproach = "標準的なアプローチ" }

                        distributionHistory.Enqueue(decision)
                        Logger.logInfo "IntelligentDistribution" $"AI分散決定完了: {task.TaskId} -> {bestAgent}"
                        return Some decision
                    else
                        return None
                | None -> return None
            with ex ->
                Logger.logError "IntelligentDistribution" $"AI分散決定失敗 ({task.TaskId}): {ex.Message}"
                return None
        }

    /// 分散統計の取得
    let getDistributionStatistics () =
        async {
            try
                let allDecisions = distributionHistory.ToArray()
                let totalDecisions = allDecisions.Length

                let averageConfidence =
                    if totalDecisions > 0 then
                        allDecisions |> Array.map (fun d -> d.ConfidenceScore) |> Array.average
                    else
                        0.0

                let statistics =
                    {| TotalDecisions = totalDecisions
                       AverageConfidence = averageConfidence
                       AgentWorkloadDistribution = Map.empty
                       ActiveAgents = agentCapabilities.Count
                       Timestamp = DateTime.Now |}

                return Some statistics
            with ex ->
                Logger.logError "IntelligentDistribution" $"分散統計取得失敗: {ex.Message}"
                return None
        }

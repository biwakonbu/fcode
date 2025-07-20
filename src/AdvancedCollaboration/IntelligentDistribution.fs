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

    /// テスト可能なランダムジェネレーター
    let private random = Random()

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

    /// タスクから必要スキルを抽出
    let private extractRequiredSkills (task: TaskInfo) =
        // タスクタイトル・説明から必要スキルを解析
        let keywords = [ "F#"; "SQL"; "UI"; "API"; "Test"; "DevOps" ]

        keywords
        |> List.filter (fun k -> task.Title.Contains(k) || task.Description.Contains(k))

    /// ドメイン専門知識を特定
    let private identifyDomainExpertise (task: TaskInfo) =
        if task.Title.Contains("Database") || task.Description.Contains("SQL") then
            Some "Database"
        elif task.Title.Contains("UI") || task.Description.Contains("Interface") then
            Some "Frontend"
        elif task.Title.Contains("API") || task.Description.Contains("Service") then
            Some "Backend"
        else
            None

    /// タスクの複雑度を分析
    let private analyzeComplexity (task: TaskInfo) =
        let baseComplexity = 0.3
        let dependencyFactor = 0.1 // TaskInfoに依存関係がないためデフォルト値
        let titleComplexity = if task.Title.Length > 50 then 0.2 else 0.0
        let descriptionComplexity = if task.Description.Length > 200 then 0.2 else 0.0

        min 1.0 (baseComplexity + dependencyFactor + titleComplexity + descriptionComplexity)

    /// タスク特性の分析
    let analyzeTaskCharacteristics (task: TaskInfo) =
        async {
            try
                let complexity = analyzeComplexity task
                let requiredSkills = extractRequiredSkills task
                let domainExpertise = identifyDomainExpertise task

                let estimatedEffort =
                    let baseHours = 1.0 + (complexity * 4.0)
                    let skillFactor = float requiredSkills.Length * 0.5
                    TimeSpan.FromHours(baseHours + skillFactor)

                let riskLevel =
                    let dependencyRisk = 0.1 // デフォルト依存リスク
                    let complexityRisk = complexity * 0.3
                    dependencyRisk + complexityRisk

                let characteristics =
                    { TaskId = task.TaskId
                      Complexity = complexity
                      EstimatedEffort = estimatedEffort
                      RequiredSkills = requiredSkills
                      Priority = task.Priority
                      Dependencies = []
                      RiskLevel = riskLevel
                      ParallelizationPotential = 0.8 // デフォルト並列化可能性
                      DomainExpertiseRequired = domainExpertise
                      CreatedAt = DateTime.Now }

                taskCharacteristics.AddOrUpdate(task.TaskId, characteristics, fun _ _ -> characteristics)
                |> ignore

                Logger.logDebug "IntelligentDistribution" $"タスク特性分析完了: {task.TaskId}"
                return Some characteristics
            with ex ->
                Logger.logError "IntelligentDistribution" $"タスク特性分析失敗 ({task.TaskId}): {ex.Message}"
                return None
        }

    /// エージェントのスキルマッチを計算
    let private calculateSkillMatches (agentId: string) (requiredSkills: string list) =
        // エージェントのスキルプロファイルをシミュレート
        let agentSkills =
            match agentId with
            | id when id.Contains("dev") -> [ "F#"; "SQL"; "API" ]
            | id when id.Contains("qa") -> [ "Test"; "Quality" ]
            | id when id.Contains("ux") -> [ "UI"; "Design" ]
            | id when id.Contains("pm") -> [ "Planning"; "Coordination" ]
            | _ -> [ "General" ]

        requiredSkills
        |> List.map (fun skill ->
            let matchScore = if agentSkills |> List.contains skill then 1.0 else 0.2
            (skill, matchScore))
        |> Map.ofList

    /// エージェントの現在の作業負荷を計算
    let private calculateCurrentWorkload (agentId: string) (agentStateManager: IAgentStateManager) =
        async {
            try
                let agentStateResult = agentStateManager.GetAgentState(agentId)

                match agentStateResult with
                | Result.Ok(Some state) ->
                    // アクティブタスク数から作業負荷を推定
                    let activeTasks = state.ActiveTasks.Length
                    let workloadScore = min 1.0 (float activeTasks * 0.2)
                    return workloadScore
                | Result.Ok None -> return 0.0
                | Result.Error _ -> return 0.3 // デフォルト値
            with _ ->
                return 0.3 // デフォルト値
        }

    /// エージェント能力の評価
    let evaluateAgentCapability
        (agentId: string)
        (agentStateManager: IAgentStateManager)
        (requiredSkills: string list)
        =
        async {
            try
                let! currentWorkload = calculateCurrentWorkload agentId agentStateManager
                let skillMatches = calculateSkillMatches agentId requiredSkills

                // エージェントタイプによる特性設定
                let (specializationAreas, basePerformance, qualityScore) =
                    match agentId with
                    | id when id.Contains("dev") -> ([ "Development"; "Backend" ], 0.85, 0.8)
                    | id when id.Contains("qa") -> ([ "Testing"; "Quality" ], 0.80, 0.9)
                    | id when id.Contains("ux") -> ([ "Design"; "Frontend" ], 0.75, 0.85)
                    | id when id.Contains("pm") -> ([ "Management"; "Coordination" ], 0.70, 0.75)
                    | _ -> ([ "General" ], 0.60, 0.70)

                let capability =
                    { AgentId = agentId
                      CurrentWorkload = currentWorkload
                      AvailableCapacity = 1.0 - currentWorkload
                      SkillMatches = skillMatches
                      HistoricalPerformance = basePerformance
                      SpecializationAreas = specializationAreas
                      AverageCompletionTime = TimeSpan.FromHours(2.0 + (currentWorkload * 2.0))
                      QualityScore = qualityScore
                      CollaborationRating = 0.7 + (random.NextDouble() * 0.2)
                      LastUpdated = DateTime.Now }

                agentCapabilities.AddOrUpdate(agentId, capability, fun _ _ -> capability)
                |> ignore

                Logger.logDebug "IntelligentDistribution" $"エージェント能力評価完了: {agentId}"
                return Some capability
            with ex ->
                Logger.logError "IntelligentDistribution" $"エージェント能力評価失敗 ({agentId}): {ex.Message}"
                return None
        }

    /// エージェントの適合度スコアを計算
    let private calculateAgentFitScore
        (agentCapability: AgentCapability)
        (taskCharacteristics: TaskCharacteristics)
        (config: DistributionConfig)
        =
        // スキルマッチスコア
        let skillScore =
            if agentCapability.SkillMatches.Count > 0 then
                agentCapability.SkillMatches.Values |> Seq.average
            else
                0.0

        // 作業負荷スコア
        let workloadScore = agentCapability.AvailableCapacity

        // 品質スコア
        let qualityScore = agentCapability.QualityScore

        // 経験スコア
        let experienceScore = agentCapability.HistoricalPerformance

        // 総合スコア算出 (最適化戦略によって重みづけ変更)
        match config.DefaultOptimizationStrategy with
        | OptimizationStrategy.SkillMatching -> skillScore * 0.5 + qualityScore * 0.3 + experienceScore * 0.2
        | OptimizationStrategy.LoadBalancing -> workloadScore * 0.6 + skillScore * 0.2 + qualityScore * 0.2
        | OptimizationStrategy.QualityMaximization -> qualityScore * 0.5 + experienceScore * 0.3 + skillScore * 0.2
        | OptimizationStrategy.TimeOptimization -> workloadScore * 0.4 + experienceScore * 0.4 + skillScore * 0.2
        | OptimizationStrategy.RiskMinimization -> experienceScore * 0.4 + qualityScore * 0.4 + skillScore * 0.2
        | _ -> skillScore * 0.4 + workloadScore * 0.3 + qualityScore * 0.3

    /// AI最適分散決定
    let makeIntelligentDistribution
        (config: DistributionConfig)
        (task: TaskInfo)
        (availableAgents: string list)
        (agentStateManager: IAgentStateManager)
        =
        async {
            try
                let! taskCharacteristics = analyzeTaskCharacteristics task

                match taskCharacteristics with
                | Some characteristics ->
                    if availableAgents.Length > 0 then
                        // 各エージェントの能力評価と適合度スコア算出
                        let! agentScores =
                            availableAgents
                            |> List.map (fun agentId ->
                                async {
                                    let! capability =
                                        evaluateAgentCapability
                                            agentId
                                            agentStateManager
                                            characteristics.RequiredSkills

                                    match capability with
                                    | Some cap ->
                                        let score = calculateAgentFitScore cap characteristics config
                                        return Some(agentId, cap, score)
                                    | None -> return None
                                })
                            |> Async.Parallel

                        // 最適エージェントを選出
                        let validAgents =
                            agentScores
                            |> Array.choose id
                            |> Array.filter (fun (_, cap, score) ->
                                cap.AvailableCapacity >= 0.1 && score >= config.SkillMatchThreshold)
                            |> Array.sortByDescending (fun (_, _, score) -> score)

                        if validAgents.Length > 0 then
                            let (bestAgentId, bestCapability, bestScore) = validAgents.[0]

                            let alternatives =
                                validAgents
                                |> Array.skip 1
                                |> Array.take (min 3 (validAgents.Length - 1))
                                |> Array.map (fun (id, _, score) -> (id, score))
                                |> Array.toList

                            // 意思決定理由を生成
                            let skillAvg = bestCapability.SkillMatches.Values |> Seq.average
                            let skillStr = sprintf "%.2f" skillAvg
                            let capacityStr = sprintf "%.2f" bestCapability.AvailableCapacity
                            let qualityStr = sprintf "%.2f" bestCapability.QualityScore
                            let performanceStr = sprintf "%.2f" bestCapability.HistoricalPerformance

                            let reasoningFactors =
                                [ $"スキルマッチスコア: {skillStr}"
                                  $"利用可能キャパシティ: {capacityStr}"
                                  $"品質スコア: {qualityStr}"
                                  $"経験スコア: {performanceStr}" ]

                            let riskAssessment =
                                if characteristics.RiskLevel > 0.7 then "高リスク"
                                elif characteristics.RiskLevel > 0.4 then "中リスク"
                                else "低リスク"

                            let decision =
                                { TaskId = task.TaskId
                                  AssignedAgent = bestAgentId
                                  ConfidenceScore = min 1.0 (bestScore * 1.2)
                                  ReasoningFactors = reasoningFactors
                                  AlternativeAgents = alternatives
                                  EstimatedCompletionTime = DateTime.Now.Add(characteristics.EstimatedEffort)
                                  RiskAssessment = riskAssessment
                                  RecommendedApproach =
                                    if characteristics.Complexity > 0.7 then
                                        "段階的アプローチ推奨"
                                    elif characteristics.ParallelizationPotential > 0.6 then
                                        "並列化可能"
                                    else
                                        "標準的なアプローチ" }

                            distributionHistory.Enqueue(decision)

                            Logger.logInfo
                                "IntelligentDistribution"
                                $"AI分散決定完了: {task.TaskId} -> {bestAgentId} (スコア: {bestScore:F2})"

                            return Some decision
                        else
                            Logger.logWarning "IntelligentDistribution" $"適合するエージェントが見つかりません: {task.TaskId}"
                            return None
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

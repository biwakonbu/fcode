module FCode.SpecialistAgentManager

open System
open System.Collections.Generic
open FCode.AgentCLI
open FCode.Logger

// ===============================================
// 専門エージェント能力拡張定義
// ===============================================

/// 専門分野能力定義（既存AgentCapabilityの拡張）
type SpecialistCapability =
    // データベース専門
    | DatabaseDesign // データベース設計・最適化
    | DatabaseMigration // マイグレーション・スキーマ管理
    | QueryOptimization // クエリ最適化・パフォーマンス改善
    | DataModeling // データモデリング・ER設計

    // API開発専門
    | APIDesign // REST API・GraphQL設計
    | APIDocumentation // OpenAPI・Swagger生成
    | APITesting // API負荷テスト・セキュリティテスト
    | APIIntegration // 外部API統合・認証実装

    // テスト自動化専門
    | TestFrameworkSetup // テストフレームワーク構築
    | E2ETesting // E2Eテスト・ブラウザ自動化
    | PerformanceTesting // パフォーマンステスト・負荷テスト
    | SecurityTesting // セキュリティテスト・脆弱性検査

    // DevOps専門
    | ContainerOrchestration // Docker・Kubernetes管理
    | ContinuousDeployment // CI/CD パイプライン構築
    | CloudInfrastructure // AWS・GCP・Azure統合
    | MonitoringAlerting // 監視・アラート・ログ分析

    // セキュリティ専門
    | SecurityAudit // セキュリティ監査・コード解析
    | VulnerabilityAssessment // 脆弱性評価・対策
    | ComplianceCheck // コンプライアンスチェック・GDPR等
    | IncidentResponse // インシデント対応・復旧

/// 専門エージェント定義
type SpecialistAgent =
    { Name: string
      SpecializedCapabilities: SpecialistCapability list
      GeneralCapabilities: AgentCapability list
      CliIntegration: IAgentCLI
      ExpertiseLevel: ExpertiseLevel
      CostPerHour: float
      AvailabilityStatus: AvailabilityStatus }

/// 専門性レベル定義
and ExpertiseLevel =
    | Junior // 基本的な作業
    | Mid // 標準的な実装
    | Senior // 複雑な設計・最適化
    | Expert // 高度な技術・アーキテクチャ

/// 利用可能性ステータス
and AvailabilityStatus =
    | Available // 利用可能
    | Busy // 作業中
    | Maintenance // メンテナンス中
    | Offline // オフライン

// ===============================================
// タスク・専門性マッチングエンジン
// ===============================================

/// タスク要件定義
type TaskRequirement =
    { TaskDescription: string
      RequiredCapabilities: SpecialistCapability list
      MinExpertiseLevel: ExpertiseLevel
      EstimatedDuration: TimeSpan
      Priority: TaskPriority
      Budget: float option }

/// タスク優先度
and TaskPriority =
    | Low
    | Medium
    | High
    | Critical

/// エージェントマッチング結果
type AgentMatchResult =
    { Agent: SpecialistAgent
      MatchScore: float // 0.0-1.0
      ReasoningExplanation: string
      EstimatedCost: float
      Confidence: float }

/// 専門性マッチングエンジン
type SpecialistMatchingEngine() =

    /// 専門性スコア計算
    member _.CalculateExpertiseScore (agent: SpecialistAgent) (requirement: TaskRequirement) =
        let capabilityMatch =
            requirement.RequiredCapabilities
            |> List.map (fun reqCap ->
                if agent.SpecializedCapabilities |> List.contains reqCap then
                    1.0
                else
                    0.0)
            |> List.average

        let expertiseScore =
            match (agent.ExpertiseLevel, requirement.MinExpertiseLevel) with
            | (Expert, _) -> 1.0
            | (Senior, Expert) -> 0.8
            | (Senior, _) -> 1.0
            | (Mid, Expert) -> 0.6
            | (Mid, Senior) -> 0.8
            | (Mid, _) -> 1.0
            | (Junior, Expert) -> 0.4
            | (Junior, Senior) -> 0.6
            | (Junior, Mid) -> 0.8
            | (Junior, Junior) -> 1.0

        capabilityMatch * 0.7 + expertiseScore * 0.3

    /// コスト効率性計算
    member _.CalculateCostEfficiency (agent: SpecialistAgent) (requirement: TaskRequirement) =
        let estimatedCost = agent.CostPerHour * requirement.EstimatedDuration.TotalHours

        match requirement.Budget with
        | Some budget when estimatedCost <= budget -> 1.0
        | Some budget -> budget / estimatedCost
        | None -> 1.0

    /// 利用可能性スコア
    member this.CalculateAvailabilityScore(agent: SpecialistAgent) =
        match agent.AvailabilityStatus with
        | Available -> 1.0
        | Busy -> 0.3
        | Maintenance -> 0.1
        | Offline -> 0.0

    /// 総合マッチングスコア計算
    member this.CalculateOverallScore (agent: SpecialistAgent) (requirement: TaskRequirement) =
        let expertiseScore = this.CalculateExpertiseScore agent requirement
        let costScore = this.CalculateCostEfficiency agent requirement
        let availabilityScore = this.CalculateAvailabilityScore agent

        // 重み付き平均（専門性50%、コスト30%、利用可能性20%）
        expertiseScore * 0.5 + costScore * 0.3 + availabilityScore * 0.2

    /// 最適エージェント選択
    member this.FindBestAgent (agents: SpecialistAgent list) (requirement: TaskRequirement) =
        agents
        |> List.map (fun agent ->
            let score = this.CalculateOverallScore agent requirement

            let reasoning =
                sprintf
                    "専門性: %.2f, コスト効率: %.2f, 利用可能性: %.2f"
                    (this.CalculateExpertiseScore agent requirement)
                    (this.CalculateCostEfficiency agent requirement)
                    (this.CalculateAvailabilityScore agent)

            { Agent = agent
              MatchScore = score
              ReasoningExplanation = reasoning
              EstimatedCost = agent.CostPerHour * requirement.EstimatedDuration.TotalHours
              Confidence = min 1.0 (score * 1.2) })
        |> List.filter (fun result -> result.MatchScore > 0.3) // 最低閾値
        |> List.sortByDescending (fun result -> result.MatchScore)
        |> List.tryHead

// ===============================================
// 専門エージェント管理システム
// ===============================================

/// 専門エージェント管理
type SpecialistAgentManager() =
    let agents = new Dictionary<string, SpecialistAgent>()
    let matchingEngine = SpecialistMatchingEngine()

    /// エージェント登録
    member _.RegisterAgent(agent: SpecialistAgent) =
        agents.[agent.Name] <- agent
        logInfo "SpecialistAgentManager" $"専門エージェント登録: {agent.Name} (専門分野: {agent.SpecializedCapabilities.Length})"

    /// エージェント一覧取得
    member _.GetAllAgents() = agents.Values |> List.ofSeq

    /// 専門分野別エージェント検索
    member _.GetAgentsByCapability(capability: SpecialistCapability) =
        agents.Values
        |> Seq.filter (fun agent -> agent.SpecializedCapabilities |> List.contains capability)
        |> List.ofSeq

    /// 利用可能エージェント取得
    member _.GetAvailableAgents() =
        agents.Values
        |> Seq.filter (fun agent -> agent.AvailabilityStatus = Available)
        |> List.ofSeq

    /// タスクに最適なエージェント推奨
    member this.RecommendAgent(requirement: TaskRequirement) =
        let availableAgents = this.GetAvailableAgents()

        match availableAgents with
        | [] ->
            logWarning "SpecialistAgentManager" "利用可能な専門エージェントがありません"
            None
        | _ ->
            match matchingEngine.FindBestAgent availableAgents requirement with
            | Some result ->
                logInfo "SpecialistAgentManager" $"最適エージェント推奨: {result.Agent.Name} (スコア: {result.MatchScore:F3})"
                Some result
            | None ->
                logWarning "SpecialistAgentManager" "要件に適合する専門エージェントが見つかりません"
                None

    /// エージェント状態更新
    member _.UpdateAgentStatus (agentName: string) (status: AvailabilityStatus) =
        match agents.TryGetValue(agentName) with
        | true, agent ->
            agents.[agentName] <-
                { agent with
                    AvailabilityStatus = status }

            logInfo "SpecialistAgentManager" $"エージェント状態更新: {agentName} -> {status}"
            true
        | false, _ ->
            logWarning "SpecialistAgentManager" $"エージェントが見つかりません: {agentName}"
            false

    /// 専門分野レポート生成
    member this.GenerateCapabilityReport() =
        let capabilityGroups =
            this.GetAllAgents()
            |> List.collect (fun agent -> agent.SpecializedCapabilities |> List.map (fun cap -> (cap, agent)))
            |> List.groupBy fst
            |> List.map (fun (cap, agents) -> cap, agents |> List.map snd |> List.length)

        let report =
            [ "=== 専門エージェント能力レポート ==="
              ""
              yield!
                  capabilityGroups
                  |> List.map (fun (cap, count) -> sprintf "- %-30s: %d エージェント" (cap.ToString()) count)
              ""
              sprintf "総専門エージェント数: %d" agents.Count
              sprintf "利用可能エージェント数: %d" (this.GetAvailableAgents().Length) ]
            |> String.concat "\n"

        logInfo "SpecialistAgentManager" "専門分野レポート生成完了"
        report

// ===============================================
// 組み込みエージェント定義
// ===============================================

/// 標準専門エージェント生成
module StandardSpecialistAgents =

    /// データベース専門エージェント
    let createDatabaseSpecialist (cliIntegration: IAgentCLI) =
        { Name = "Database Specialist"
          SpecializedCapabilities = [ DatabaseDesign; DatabaseMigration; QueryOptimization; DataModeling ]
          GeneralCapabilities = [ CodeGeneration; Documentation; CodeReview ]
          CliIntegration = cliIntegration
          ExpertiseLevel = Senior
          CostPerHour = 120.0
          AvailabilityStatus = Available }

    /// API開発専門エージェント
    let createAPISpecialist (cliIntegration: IAgentCLI) =
        { Name = "API Development Specialist"
          SpecializedCapabilities = [ APIDesign; APIDocumentation; APITesting; APIIntegration ]
          GeneralCapabilities = [ CodeGeneration; Testing; Documentation; CodeReview ]
          CliIntegration = cliIntegration
          ExpertiseLevel = Senior
          CostPerHour = 110.0
          AvailabilityStatus = Available }

    /// テスト自動化専門エージェント
    let createTestAutomationSpecialist (cliIntegration: IAgentCLI) =
        { Name = "Test Automation Specialist"
          SpecializedCapabilities = [ TestFrameworkSetup; E2ETesting; PerformanceTesting; SecurityTesting ]
          GeneralCapabilities = [ Testing; QualityAssurance; CodeReview ]
          CliIntegration = cliIntegration
          ExpertiseLevel = Senior
          CostPerHour = 100.0
          AvailabilityStatus = Available }

    /// DevOps専門エージェント
    let createDevOpsSpecialist (cliIntegration: IAgentCLI) =
        { Name = "DevOps Specialist"
          SpecializedCapabilities =
            [ ContainerOrchestration
              ContinuousDeployment
              CloudInfrastructure
              MonitoringAlerting ]
          GeneralCapabilities = [ ArchitectureDesign; Debugging; Documentation ]
          CliIntegration = cliIntegration
          ExpertiseLevel = Expert
          CostPerHour = 150.0
          AvailabilityStatus = Available }

    /// セキュリティ専門エージェント
    let createSecuritySpecialist (cliIntegration: IAgentCLI) =
        { Name = "Security Specialist"
          SpecializedCapabilities = [ SecurityAudit; VulnerabilityAssessment; ComplianceCheck; IncidentResponse ]
          GeneralCapabilities = [ CodeReview; QualityAssurance; Documentation ]
          CliIntegration = cliIntegration
          ExpertiseLevel = Expert
          CostPerHour = 160.0
          AvailabilityStatus = Available }

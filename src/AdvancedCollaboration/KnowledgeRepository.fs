namespace FCode.AdvancedCollaboration

open System
open System.IO
open System.Text.Json
open System.Collections.Concurrent
open System.Threading.Tasks
open FCode.Collaboration.CollaborationTypes
open FCode

/// エージェント間知識共有・学習システム
module KnowledgeRepository =

    /// 知識エントリの種類
    type KnowledgeType =
        | TechnicalPattern = 1 // 技術パターン・実装手法
        | ProblemSolution = 2 // 問題解決事例
        | WorkflowOptimization = 3 // ワークフロー最適化
        | QualityStandard = 4 // 品質基準・ベストプラクティス
        | LessonLearned = 5 // 学習・改善事項

    /// 知識エントリ
    type KnowledgeEntry =
        { Id: string
          Type: KnowledgeType
          AgentId: string
          Title: string
          Content: string
          Keywords: string list
          Context: Map<string, string>
          UsageCount: int
          Effectiveness: float
          CreatedAt: DateTime
          UpdatedAt: DateTime
          RelatedEntries: string list
          Metadata: Map<string, obj> }

    /// 専門知識マッピング
    type ExpertiseArea =
        { Domain: string
          Skills: string list
          ExperienceLevel: float
          RecentActivity: DateTime
          SuccessRate: float }

    /// エージェント専門知識プロファイル
    type AgentExpertise =
        { AgentId: string
          ExpertiseAreas: ExpertiseArea list
          TotalExperience: TimeSpan
          KnowledgeContributions: int
          QualityRating: float
          LastUpdated: DateTime }

    /// 知識検索クエリ
    type KnowledgeQuery =
        { Keywords: string list
          KnowledgeType: KnowledgeType option
          Domain: string option
          MinEffectiveness: float option
          MaxResults: int }

    /// 知識推奨結果
    type KnowledgeRecommendation =
        { Entry: KnowledgeEntry
          RelevanceScore: float
          ExpertOpinion: string option
          SimilarCases: string list }

    /// ナレッジベース設定
    type KnowledgeRepositoryConfig =
        { StorageDirectory: string
          MaxEntries: int
          CleanupInterval: TimeSpan
          SimilarityThreshold: float
          EffectivenessDecayRate: float
          BackupEnabled: bool }

    /// デフォルト設定
    let defaultConfig =
        { StorageDirectory =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "fcode",
                "knowledge"
            )
          MaxEntries = 10000
          CleanupInterval = TimeSpan.FromDays(7.0)
          SimilarityThreshold = 0.7
          EffectivenessDecayRate = 0.95
          BackupEnabled = true }

    /// 知識エントリストレージ
    let private knowledgeEntries = ConcurrentDictionary<string, KnowledgeEntry>()
    let agentExpertise = ConcurrentDictionary<string, AgentExpertise>()
    let private usageStats = ConcurrentDictionary<string, int>()

    /// JSON シリアライゼーション設定
    let private jsonOptions = JsonSerializerOptions(WriteIndented = true)

    /// ストレージディレクトリの初期化
    let initializeStorage (config: KnowledgeRepositoryConfig) =
        async {
            try
                Directory.CreateDirectory(config.StorageDirectory) |> ignore
                Logger.logInfo "KnowledgeRepository" "ナレッジベースストレージ初期化完了"
                return true
            with ex ->
                Logger.logError "KnowledgeRepository" $"ストレージ初期化失敗: {ex.Message}"
                return false
        }

    /// 知識エントリの検索
    let searchKnowledge (query: KnowledgeQuery) =
        async {
            try
                let allEntries = knowledgeEntries.Values |> Seq.toList
                Logger.logDebug "KnowledgeRepository" $"知識検索実行: {allEntries.Length}件見つかりました"
                return allEntries |> List.take (min query.MaxResults allEntries.Length)
            with ex ->
                Logger.logError "KnowledgeRepository" $"知識検索失敗: {ex.Message}"
                return []
        }

    /// 知識エントリの追加
    let addKnowledgeEntry (config: KnowledgeRepositoryConfig) (entry: KnowledgeEntry) =
        async {
            try
                knowledgeEntries.TryAdd(entry.Id, entry) |> ignore
                usageStats.TryAdd(entry.Id, 0) |> ignore
                Logger.logInfo "KnowledgeRepository" $"知識エントリ追加: {entry.Title}"
                return true
            with ex ->
                Logger.logError "KnowledgeRepository" $"知識エントリ追加失敗: {ex.Message}"
                return false
        }

    /// エージェント専門知識の更新
    let updateAgentExpertise (agentId: string) (domain: string) (skills: string list) (successRate: float) =
        async {
            try
                let currentTime = DateTime.Now

                let newExpertise =
                    { AgentId = agentId
                      ExpertiseAreas =
                        [ { Domain = domain
                            Skills = skills
                            ExperienceLevel = 0.1
                            RecentActivity = currentTime
                            SuccessRate = successRate } ]
                      TotalExperience = TimeSpan.Zero
                      KnowledgeContributions = 1
                      QualityRating = successRate
                      LastUpdated = currentTime }

                agentExpertise.AddOrUpdate(agentId, newExpertise, fun _ _ -> newExpertise)
                |> ignore

                Logger.logInfo "KnowledgeRepository" $"エージェント専門知識更新: {agentId} - {domain}"
                return true
            with ex ->
                Logger.logError "KnowledgeRepository" $"専門知識更新失敗 ({agentId}): {ex.Message}"
                return false
        }

    /// 専門家推奨システム
    let recommendExpert (domain: string) (requiredSkills: string list) =
        async {
            try
                let experts = agentExpertise.Values |> Seq.toList
                Logger.logDebug "KnowledgeRepository" $"専門家推奨: {domain} - {experts.Length}名見つかりました"
                return experts
            with ex ->
                Logger.logError "KnowledgeRepository" $"専門家推奨失敗: {ex.Message}"
                return []
        }

    /// 知識推奨システム
    let recommendKnowledge (agentId: string) (context: Map<string, string>) =
        async {
            try
                let recommendations = []
                Logger.logDebug "KnowledgeRepository" $"知識推奨: {agentId} - {recommendations.Length}件"
                return recommendations
            with ex ->
                Logger.logError "KnowledgeRepository" $"知識推奨失敗: {ex.Message}"
                return []
        }

    /// 知識ベースの統計情報取得
    let getKnowledgeStatistics () =
        async {
            try
                let totalEntries = knowledgeEntries.Count
                let totalExperts = agentExpertise.Count

                let totalUsage =
                    if usageStats.Count > 0 then
                        usageStats.Values |> Seq.sum
                    else
                        0

                let statistics =
                    {| TotalEntries = totalEntries
                       TotalExperts = totalExperts
                       TotalUsage = totalUsage
                       AverageEffectiveness = 0.8
                       TypeDistribution = Map.empty
                       Timestamp = DateTime.Now |}

                return Some statistics
            with ex ->
                Logger.logError "KnowledgeRepository" $"統計情報取得失敗: {ex.Message}"
                return None
        }

    /// AdvancedCollaboration用のタスク型定義
    type AdvancedCollaborationTask =
        { TaskId: string
          Description: string
          AgentInstructions: string
          Dependencies: string list
          Priority: TaskPriority
          Status: TaskStatus
          AssignedAgent: string option
          Deadline: DateTime option
          StartedAt: DateTime option
          CompletedAt: DateTime option
          WorkflowId: string }

    /// 必要スキル抽出（他のモジュールで使用）
    let extractRequiredSkills (task: AdvancedCollaborationTask) =
        let keywords =
            [ "F#"
              "API"
              "UI"
              "Database"
              "Test"
              "Performance"
              "Security"
              "Integration"
              "Refactor"
              "Debug" ]

        keywords
        |> List.filter (fun keyword ->
            task.Description.ToLower().Contains(keyword.ToLower())
            || task.AgentInstructions.ToLower().Contains(keyword.ToLower()))

    /// 専門領域識別（他のモジュールで使用）
    let identifyDomainExpertise (task: AdvancedCollaborationTask) =
        let domains =
            [ ("UI", [ "interface"; "user"; "design"; "frontend" ])
              ("Backend", [ "api"; "service"; "database"; "server" ])
              ("Testing", [ "test"; "qa"; "validation"; "verification" ])
              ("Performance", [ "performance"; "optimization"; "speed"; "memory" ])
              ("Security", [ "security"; "auth"; "encryption"; "permission" ]) ]

        let taskText = (task.Description + " " + task.AgentInstructions).ToLower()

        domains
        |> List.tryFind (fun (domain, keywords) -> keywords |> List.exists (fun keyword -> taskText.Contains(keyword)))
        |> Option.map fst

    /// スキル専門性チェック（他のモジュールで使用）
    let hasSkillExpertise (agentId: string) (skill: string) =
        match agentExpertise.TryGetValue(agentId) with
        | true, expertise ->
            expertise.ExpertiseAreas
            |> List.exists (fun area -> area.Skills |> List.exists (fun s -> s.ToLower().Contains(skill.ToLower())))
        | false, _ -> false

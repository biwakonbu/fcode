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
    let private agentExpertise = ConcurrentDictionary<string, AgentExpertise>()
    let private usageStats = ConcurrentDictionary<string, int>()

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

    /// キーワードマッチングスコアを計算
    let private calculateKeywordScore (entryKeywords: string list) (queryKeywords: string list) =
        if queryKeywords.IsEmpty then
            0.0
        else
            let matches =
                queryKeywords
                |> List.filter (fun qk -> entryKeywords |> List.exists (fun ek -> ek.ToLower().Contains(qk.ToLower())))
                |> List.length

            float matches / float queryKeywords.Length

    /// コンテンツ的適度を計算
    let private calculateContentRelevance (entry: KnowledgeEntry) (query: KnowledgeQuery) =
        let keywordScore = calculateKeywordScore entry.Keywords query.Keywords

        let typeMatch =
            match query.KnowledgeType with
            | Some kt when kt = entry.Type -> 0.3
            | _ -> 0.0

        let domainMatch =
            match query.Domain with
            | Some domain when
                entry.Context.ContainsKey("domain")
                && entry.Context.["domain"].ToString().Contains(domain)
                ->
                0.2
            | _ -> 0.0

        let effectivenessScore =
            match query.MinEffectiveness with
            | Some minEff when entry.Effectiveness >= minEff -> entry.Effectiveness * 0.2
            | None -> entry.Effectiveness * 0.2
            | _ -> 0.0

        keywordScore * 0.5 + typeMatch + domainMatch + effectivenessScore

    /// 知識エントリの検索
    let searchKnowledge (query: KnowledgeQuery) =
        async {
            try
                let allEntries = knowledgeEntries.Values |> Seq.toList

                // 関連度スコアでソートして結果を返す
                let scoredEntries =
                    allEntries
                    |> List.map (fun entry ->
                        let relevanceScore = calculateContentRelevance entry query
                        (entry, relevanceScore))
                    |> List.filter (fun (_, score) -> score > 0.1) // 関連性が低いものをフィルタリング
                    |> List.sortByDescending (fun (_, score) -> score)
                    |> List.take (min query.MaxResults (allEntries.Length))
                    |> List.map fst

                Logger.logDebug "KnowledgeRepository" $"知識検索実行: {scoredEntries.Length}/{allEntries.Length}件見つかりました"
                return scoredEntries
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

    /// 専門家の適合度を計算
    let private calculateExpertFitScore (expert: AgentExpertise) (domain: string) (requiredSkills: string list) =
        // ドメインマッチスコア
        let domainScore =
            expert.ExpertiseAreas
            |> List.tryFind (fun area -> area.Domain.ToLower().Contains(domain.ToLower()))
            |> function
                | Some area -> area.ExperienceLevel * 0.4
                | None -> 0.0

        // スキルマッチスコア
        let skillScore =
            let allExpertSkills =
                expert.ExpertiseAreas |> List.collect (fun area -> area.Skills)

            let matchedSkills =
                requiredSkills
                |> List.filter (fun skill ->
                    allExpertSkills
                    |> List.exists (fun expertSkill -> expertSkill.ToLower().Contains(skill.ToLower())))
                |> List.length

            if requiredSkills.Length > 0 then
                float matchedSkills / float requiredSkills.Length * 0.3
            else
                0.0

        // 品質・経験スコア
        let qualityScore = expert.QualityRating * 0.2
        let contributionScore = min 0.1 (float expert.KnowledgeContributions / 100.0)

        domainScore + skillScore + qualityScore + contributionScore

    /// 専門家推奨システム
    let recommendExpert (domain: string) (requiredSkills: string list) =
        async {
            try
                let experts = agentExpertise.Values |> Seq.toList

                // 適合度スコアでソート
                let rankedExperts =
                    experts
                    |> List.map (fun expert ->
                        let score = calculateExpertFitScore expert domain requiredSkills
                        (expert, score))
                    |> List.filter (fun (_, score) -> score > 0.2) // 一定以上の適合度を持つ専門家のみ
                    |> List.sortByDescending (fun (_, score) -> score)
                    |> List.take (min 5 experts.Length)
                    |> List.map fst

                Logger.logDebug
                    "KnowledgeRepository"
                    $"専門家推奨: {domain} - {rankedExperts.Length}/{experts.Length}名見つかりました"

                return rankedExperts
            with ex ->
                Logger.logError "KnowledgeRepository" $"専門家推奨失敗: {ex.Message}"
                return []
        }

    /// コンテキストベースの推奨スコアを計算
    let private calculateRecommendationScore (entry: KnowledgeEntry) (agentId: string) (context: Map<string, string>) =
        // 現在のタスクコンテキストとの関連性
        let contextScore =
            context
            |> Map.toList
            |> List.sumBy (fun (key, value) ->
                if
                    entry.Context.ContainsKey(key)
                    && entry.Context.[key].ToString().ToLower().Contains(value.ToLower())
                then
                    0.2
                else
                    0.0)

        // エージェントの過去の利用履歴
        let usageScore =
            if usageStats.ContainsKey(entry.Id) && entry.AgentId = agentId then
                min 0.3 (float usageStats.[entry.Id] / 10.0)
            else
                0.0

        // 有効性と新鮮度
        let effectivenessScore = entry.Effectiveness * 0.3

        let freshnessScore =
            let daysSinceCreated = (DateTime.Now - entry.CreatedAt).TotalDays
            max 0.0 (0.2 - (daysSinceCreated / 365.0) * 0.1) // 1年で新鮮度スコアが減衰

        contextScore + usageScore + effectivenessScore + freshnessScore

    /// 知識推奨システム
    let recommendKnowledge (agentId: string) (context: Map<string, string>) =
        async {
            try
                let allEntries = knowledgeEntries.Values |> Seq.toList

                // 推奨スコアでソートして上位5件を返す
                let recommendations =
                    allEntries
                    |> List.map (fun entry ->
                        let score = calculateRecommendationScore entry agentId context

                        let recommendation =
                            { Entry = entry
                              RelevanceScore = score
                              ExpertOpinion =
                                if entry.Effectiveness > 0.8 then Some "高品質なナレッジ"
                                elif entry.UsageCount > 10 then Some "実績豊富"
                                else None
                              SimilarCases =
                                allEntries
                                |> List.filter (fun e ->
                                    e.Id <> entry.Id
                                    && e.Type = entry.Type
                                    && (e.Keywords |> List.exists (fun k -> entry.Keywords |> List.contains k)))
                                |> List.take 3
                                |> List.map (fun e -> e.Id) }

                        (recommendation, score))
                    |> List.filter (fun (_, score) -> score > 0.1)
                    |> List.sortByDescending (fun (_, score) -> score)
                    |> List.take 5
                    |> List.map fst

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

    /// 必要スキル抽出（他のモジュールで使用）
    let extractRequiredSkills (task: TaskInfo) =
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
            || task.Title.ToLower().Contains(keyword.ToLower()))

    /// 専門領域識別（他のモジュールで使用）
    let identifyDomainExpertise (task: TaskInfo) =
        let domains =
            [ ("UI", [ "interface"; "user"; "design"; "frontend" ])
              ("Backend", [ "api"; "service"; "database"; "server" ])
              ("Testing", [ "test"; "qa"; "validation"; "verification" ])
              ("Performance", [ "performance"; "optimization"; "speed"; "memory" ])
              ("Security", [ "security"; "auth"; "encryption"; "permission" ]) ]

        let taskText = (task.Description + " " + task.Title).ToLower()

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

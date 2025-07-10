module FCode.AIModelProvider

open System
open FCode.AgentCLI
open FCode.Logger

// ===============================================
// AIモデル統合定義
// ===============================================

/// AIモデル種別定義
type AIModelType =
    | Claude3Sonnet // Claude 3 Sonnet - バランス型
    | Claude3Opus // Claude 3 Opus - 高性能型
    | Claude3Haiku // Claude 3 Haiku - 高速型
    | GPT4Turbo // GPT-4 Turbo - OpenAI最新
    | GPT4Vision // GPT-4 Vision - 画像解析対応
    | GPT35Turbo // GPT-3.5 Turbo - コスト効率
    | GeminiPro // Gemini Pro - Google最新
    | GeminiUltra // Gemini Ultra - Google最高性能
    | CodeLlama // Code Llama - コード特化
    | DeepSeekCoder // DeepSeek Coder - コード生成特化

/// レスポンス速度分類
type ResponseSpeed =
    | VeryFast // < 2秒
    | Fast // 2-5秒
    | Medium // 5-10秒
    | Slow // 10-20秒
    | VerySlow // > 20秒

/// タスク緊急度
type TaskUrgency =
    | Immediate // 即座に必要
    | Urgent // 1時間以内
    | Normal // 数時間以内
    | LowPriority // 1日以内

/// AIモデル特性定義
type AIModelCharacteristics =
    { ModelType: AIModelType
      MaxTokens: int
      CostPerInputToken: float
      CostPerOutputToken: float
      ResponseSpeed: ResponseSpeed
      CodeGeneration: float // 0.0-1.0
      Reasoning: float // 0.0-1.0
      CreativeWriting: float // 0.0-1.0
      TechnicalAccuracy: float // 0.0-1.0
      ContextRetention: float // 0.0-1.0
      MultilingualSupport: float } // 0.0-1.0

/// タスク特性分析結果
type TaskCharacteristics =
    { RequiredCapabilities: string list
      CodeGeneration: bool
      ComplexReasoning: bool
      CreativeElements: bool
      TechnicalDepth: float
      ExpectedTokens: int
      Urgency: TaskUrgency }

/// AIモデル選択結果
type ModelSelectionResult =
    { SelectedModel: AIModelType
      SelectionReason: string
      EstimatedCost: float
      EstimatedTime: TimeSpan
      Confidence: float
      AlternativeModels: (AIModelType * float) list }

// ===============================================
// AIモデル最適選択エンジン
// ===============================================

/// モデル選択戦略エンジン
type ModelSelectionEngine() =

    /// 組み込みモデル特性データベース
    let modelDatabase =
        [ { ModelType = Claude3Sonnet
            MaxTokens = 200000
            CostPerInputToken = 0.003
            CostPerOutputToken = 0.015
            ResponseSpeed = Medium
            CodeGeneration = 0.9
            Reasoning = 0.95
            CreativeWriting = 0.85
            TechnicalAccuracy = 0.9
            ContextRetention = 0.95
            MultilingualSupport = 0.8 }

          { ModelType = Claude3Opus
            MaxTokens = 200000
            CostPerInputToken = 0.015
            CostPerOutputToken = 0.075
            ResponseSpeed = Slow
            CodeGeneration = 0.95
            Reasoning = 0.98
            CreativeWriting = 0.9
            TechnicalAccuracy = 0.95
            ContextRetention = 0.98
            MultilingualSupport = 0.85 }

          { ModelType = Claude3Haiku
            MaxTokens = 200000
            CostPerInputToken = 0.00025
            CostPerOutputToken = 0.00125
            ResponseSpeed = VeryFast
            CodeGeneration = 0.7
            Reasoning = 0.75
            CreativeWriting = 0.7
            TechnicalAccuracy = 0.75
            ContextRetention = 0.8
            MultilingualSupport = 0.7 }

          { ModelType = GPT4Turbo
            MaxTokens = 128000
            CostPerInputToken = 0.01
            CostPerOutputToken = 0.03
            ResponseSpeed = Medium
            CodeGeneration = 0.85
            Reasoning = 0.9
            CreativeWriting = 0.85
            TechnicalAccuracy = 0.85
            ContextRetention = 0.9
            MultilingualSupport = 0.9 }

          { ModelType = GPT35Turbo
            MaxTokens = 16000
            CostPerInputToken = 0.0005
            CostPerOutputToken = 0.0015
            ResponseSpeed = Fast
            CodeGeneration = 0.7
            Reasoning = 0.7
            CreativeWriting = 0.75
            TechnicalAccuracy = 0.7
            ContextRetention = 0.7
            MultilingualSupport = 0.85 }

          { ModelType = GeminiPro
            MaxTokens = 30720
            CostPerInputToken = 0.00035
            CostPerOutputToken = 0.00105
            ResponseSpeed = Fast
            CodeGeneration = 0.8
            Reasoning = 0.85
            CreativeWriting = 0.8
            TechnicalAccuracy = 0.8
            ContextRetention = 0.85
            MultilingualSupport = 0.95 }

          { ModelType = CodeLlama
            MaxTokens = 16000
            CostPerInputToken = 0.0001
            CostPerOutputToken = 0.0003
            ResponseSpeed = Fast
            CodeGeneration = 0.95
            Reasoning = 0.7
            CreativeWriting = 0.5
            TechnicalAccuracy = 0.9
            ContextRetention = 0.75
            MultilingualSupport = 0.6 } ]

    /// タスク特性からモデル適合度計算
    member _.CalculateModelFitness (model: AIModelCharacteristics) (task: TaskCharacteristics) =
        let codeWeight = if task.CodeGeneration then 0.4 else 0.1
        let reasoningWeight = if task.ComplexReasoning then 0.3 else 0.15
        let creativeWeight = if task.CreativeElements then 0.2 else 0.05
        let technicalWeight = task.TechnicalDepth * 0.25

        let speedWeight =
            match task.Urgency with
            | Immediate -> 0.3
            | Urgent -> 0.2
            | Normal -> 0.1
            | LowPriority -> 0.05

        let capabilityScore =
            model.CodeGeneration * codeWeight
            + model.Reasoning * reasoningWeight
            + model.CreativeWriting * creativeWeight
            + model.TechnicalAccuracy * technicalWeight

        let speedScore =
            match model.ResponseSpeed, task.Urgency with
            | VeryFast, _ -> 1.0
            | Fast, Immediate -> 0.9
            | Fast, _ -> 1.0
            | Medium, Immediate -> 0.7
            | Medium, Urgent -> 0.8
            | Medium, _ -> 1.0
            | Slow, LowPriority -> 1.0
            | Slow, _ -> 0.6
            | VerySlow, LowPriority -> 0.8
            | VerySlow, _ -> 0.3

        let contextScore =
            if task.ExpectedTokens > model.MaxTokens then
                0.0
            else
                1.0 - (float task.ExpectedTokens / float model.MaxTokens * 0.2)

        // 重み付き総合スコア（正規化済み）
        // 固定重み: capability(0.5) + context(0.3) = 0.8
        // 残りの0.2をspeedWeight範囲(0.05～0.3)に正規化
        let maxSpeedWeight = 0.3
        let normalizedSpeedWeight = (speedWeight / maxSpeedWeight) * 0.2 // 0.2の範囲内に正規化

        capabilityScore * 0.5 + speedScore * normalizedSpeedWeight + contextScore * 0.3

    /// コスト効率計算
    member _.CalculateCostEfficiency (model: AIModelCharacteristics) (task: TaskCharacteristics) =
        let estimatedCost =
            (float task.ExpectedTokens * model.CostPerInputToken)
            + (float task.ExpectedTokens * 0.3 * model.CostPerOutputToken) // 出力は入力の30%と仮定

        // コスト効率 = 1 / コスト (正規化)
        1.0 / (estimatedCost + 0.01)

    /// 最適モデル選択
    member this.SelectOptimalModel(task: TaskCharacteristics) =
        let scoredModels =
            modelDatabase
            |> List.map (fun model ->
                let fitnessScore = this.CalculateModelFitness model task
                let costScore = this.CalculateCostEfficiency model task
                let totalScore = fitnessScore * 0.7 + costScore * 0.3

                let estimatedCost =
                    (float task.ExpectedTokens * model.CostPerInputToken)
                    + (float task.ExpectedTokens * 0.3 * model.CostPerOutputToken)

                let estimatedTime =
                    match model.ResponseSpeed with
                    | VeryFast -> TimeSpan.FromSeconds(1.5)
                    | Fast -> TimeSpan.FromSeconds(3.5)
                    | Medium -> TimeSpan.FromSeconds(7.5)
                    | Slow -> TimeSpan.FromSeconds(15.0)
                    | VerySlow -> TimeSpan.FromSeconds(25.0)

                (model.ModelType, totalScore, estimatedCost, estimatedTime, fitnessScore))
            |> List.sortByDescending (fun (_, score, _, _, _) -> score)

        match scoredModels with
        | (selectedModel, score, cost, time, fitness) :: alternatives ->
            let reason =
                sprintf
                    "適合度: %f, コスト効率: %f, 予想コスト: $%f"
                    fitness
                    (this.CalculateCostEfficiency
                        (modelDatabase |> List.find (fun m -> m.ModelType = selectedModel))
                        task)
                    cost

            Some
                { SelectedModel = selectedModel
                  SelectionReason = reason
                  EstimatedCost = cost
                  EstimatedTime = time
                  Confidence = min 1.0 (score * 1.2)
                  AlternativeModels =
                    alternatives
                    |> List.take (min 3 alternatives.Length)
                    |> List.map (fun (model, score, _, _, _) -> (model, score)) }
        | [] -> None

// ===============================================
// マルチモデル管理システム
// ===============================================

/// マルチAIモデル管理
/// モデル使用統計
type ModelUsageStats =
    { TotalRequests: int
      TotalCost: float
      AverageResponseTime: TimeSpan
      SuccessRate: float
      LastUsed: DateTime }

type MultiModelManager() =
    let selectionEngine = ModelSelectionEngine()
    let mutable modelUsageStats = Map.empty<AIModelType, ModelUsageStats>

    /// タスク特性自動分析
    member _.AnalyzeTaskCharacteristics (taskDescription: string) (agentCapabilities: AgentCapability list) =
        let codeKeywords = [ "コード"; "実装"; "プログラム"; "関数"; "クラス"; "API"; "バグ"; "リファクタリング" ]
        let reasoningKeywords = [ "設計"; "アーキテクチャ"; "分析"; "最適化"; "戦略"; "計画" ]
        let creativeKeywords = [ "アイデア"; "提案"; "創造"; "革新"; "ブレインストーミング" ]

        let hasCodeGeneration =
            agentCapabilities |> List.contains CodeGeneration
            || codeKeywords |> List.exists (fun keyword -> taskDescription.Contains(keyword))

        let hasComplexReasoning =
            agentCapabilities |> List.contains ArchitectureDesign
            || reasoningKeywords
               |> List.exists (fun keyword -> taskDescription.Contains(keyword))

        let hasCreativeElements =
            agentCapabilities |> List.contains Documentation
            || creativeKeywords
               |> List.exists (fun keyword -> taskDescription.Contains(keyword))

        let technicalDepth =
            if agentCapabilities |> List.contains ArchitectureDesign then
                0.9
            elif agentCapabilities |> List.contains CodeGeneration then
                0.7
            elif agentCapabilities |> List.contains Testing then
                0.6
            else
                0.4

        let expectedTokens = Math.Max(1000, Math.Min(50000, taskDescription.Length * 10))

        { RequiredCapabilities = agentCapabilities |> List.map (fun cap -> cap.ToString())
          CodeGeneration = hasCodeGeneration
          ComplexReasoning = hasComplexReasoning
          CreativeElements = hasCreativeElements
          TechnicalDepth = technicalDepth
          ExpectedTokens = expectedTokens
          Urgency = Normal }

    /// 最適モデル推奨
    member this.RecommendModel
        (taskDescription: string)
        (agentCapabilities: AgentCapability list)
        (urgency: TaskUrgency)
        =
        let taskCharacteristics =
            this.AnalyzeTaskCharacteristics taskDescription agentCapabilities

        let adjustedTask =
            { taskCharacteristics with
                Urgency = urgency }

        match selectionEngine.SelectOptimalModel adjustedTask with
        | Some result ->
            logInfo "MultiModelManager" $"モデル推奨: {result.SelectedModel} (信頼度: {result.Confidence})"
            Some result
        | None ->
            logWarning "MultiModelManager" "適切なAIモデルが見つかりませんでした"
            None

    /// モデル使用統計更新
    member _.UpdateUsageStats (modelType: AIModelType) (responseTime: TimeSpan) (cost: float) (success: bool) =
        let currentStats =
            match modelUsageStats.TryFind(modelType) with
            | Some stats -> stats
            | None ->
                { TotalRequests = 0
                  TotalCost = 0.0
                  AverageResponseTime = TimeSpan.Zero
                  SuccessRate = 0.0
                  LastUsed = DateTime.MinValue }

        let newTotalRequests = currentStats.TotalRequests + 1
        let newTotalCost = currentStats.TotalCost + cost

        let newAvgResponseTime =
            if currentStats.TotalRequests = 0 then
                responseTime
            else
                TimeSpan.FromMilliseconds(
                    (currentStats.AverageResponseTime.TotalMilliseconds
                     * float currentStats.TotalRequests
                     + responseTime.TotalMilliseconds)
                    / float newTotalRequests
                )

        let newSuccessRate =
            let successCount =
                int (currentStats.SuccessRate * float currentStats.TotalRequests)
                + (if success then 1 else 0)

            float successCount / float newTotalRequests

        modelUsageStats <-
            modelUsageStats.Add(
                modelType,
                { TotalRequests = newTotalRequests
                  TotalCost = newTotalCost
                  AverageResponseTime = newAvgResponseTime
                  SuccessRate = newSuccessRate
                  LastUsed = DateTime.Now }
            )

        logDebug "MultiModelManager" $"モデル使用統計更新: {modelType} (成功率: {newSuccessRate})"

    /// 使用統計レポート生成
    member _.GenerateUsageReport() =
        let totalCost =
            modelUsageStats |> Map.toSeq |> Seq.sumBy (fun (_, stats) -> stats.TotalCost)

        let totalRequests =
            modelUsageStats
            |> Map.toSeq
            |> Seq.sumBy (fun (_, stats) -> stats.TotalRequests)

        let report =
            [ "=== AIモデル使用統計レポート ==="
              ""
              sprintf "総リクエスト数: %d" totalRequests
              sprintf "総コスト: $%.4f" totalCost
              ""
              "=== モデル別統計 ==="
              ""
              yield!
                  modelUsageStats
                  |> Seq.map (fun kvp ->
                      let model, stats = kvp.Key, kvp.Value

                      [ sprintf "【%A】" model
                        sprintf "  リクエスト数: %d" stats.TotalRequests
                        sprintf "  総コスト: $%.4f" stats.TotalCost
                        sprintf "  平均応答時間: %.2f秒" stats.AverageResponseTime.TotalSeconds
                        sprintf "  成功率: %.1f%%" (stats.SuccessRate * 100.0)
                        sprintf "  最終使用: %s" (stats.LastUsed.ToString("yyyy-MM-dd HH:mm:ss"))
                        "" ])
                  |> List.concat ]
            |> String.concat "\n"

        logInfo "MultiModelManager" "AIモデル使用統計レポート生成完了"
        report

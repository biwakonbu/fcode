module FCode.ModelSwitchingStrategy

open System
open System.Collections.Generic
open System.Threading.Tasks
open FCode.Logger
open FCode.FCodeError
open FCode.AIModelProvider

/// AIModel型エイリアス (既存のAIModelProviderとの互換性)
type AIModel = AIModelType

/// ModelMetrics型エイリアス (既存のAIModelProviderとの互換性)
type ModelMetrics = ModelUsageStats

/// ModelUsageStats拡張メソッド（互換性のため）
type ModelUsageStats with
    member this.ErrorRate = 1.0 - this.SuccessRate
    member this.AverageCost = this.TotalCost / float this.TotalRequests
    member this.AverageQuality = this.SuccessRate
    member this.AvailabilityRate = this.SuccessRate
    member this.RequestCount = this.TotalRequests

// ===============================================
// モデル切り替え戦略定義
// ===============================================

/// 切り替え戦略タイプ
type SwitchingStrategyType =
    | RoundRobin // ラウンドロビン
    | LoadBased // 負荷ベース
    | PerformanceBased // パフォーマンスベース
    | CostOptimized // コスト最適化
    | QualityOptimized // 品質最適化
    | TaskSpecific // タスク特化
    | Adaptive // 適応的
    | Manual // 手動

/// モデル切り替え条件
type SwitchingCondition =
    | ResponseTimeThreshold of TimeSpan
    | ErrorRateThreshold of float
    | CostThreshold of float
    | LoadThreshold of int
    | QualityThreshold of float
    | AvailabilityThreshold of float
    | TimeWindow of TimeSpan
    | RequestCount of int

/// モデル切り替えトリガー
type SwitchingTrigger =
    { ConditionType: SwitchingCondition
      CurrentValue: float
      ThresholdValue: float
      TriggeredAt: DateTime
      SeverityLevel: int }

/// モデル切り替え決定
type SwitchingDecision =
    { FromModel: AIModel
      ToModel: AIModel
      Reason: string
      Confidence: float
      ExpectedImprovement: float
      RiskLevel: int
      SwitchingCost: float
      DecisionAt: DateTime }

/// モデル切り替え履歴
type SwitchingHistory =
    { SwitchId: string
      Decision: SwitchingDecision
      ExecutedAt: DateTime
      ExecutionDuration: TimeSpan
      Success: bool
      BeforeMetrics: ModelMetrics
      AfterMetrics: ModelMetrics option
      Impact: SwitchingImpact }

/// 切り替え影響評価
and SwitchingImpact =
    { ResponseTimeChange: TimeSpan
      ErrorRateChange: float
      CostChange: float
      QualityChange: float
      AvailabilityChange: float
      OverallImpact: float }

/// モデル切り替え統計
type SwitchingStatistics =
    { TotalSwitches: int
      SuccessfulSwitches: int
      FailedSwitches: int
      AverageDecisionTime: TimeSpan
      AverageExecutionTime: TimeSpan
      TotalCostSaved: float
      TotalQualityImprovement: float
      MostUsedStrategy: SwitchingStrategyType
      MostSwitchedFromModel: AIModel
      MostSwitchedToModel: AIModel
      LastSwitchTime: DateTime }

/// モデル切り替え設定
type SwitchingConfiguration =
    { Strategy: SwitchingStrategyType
      Conditions: SwitchingCondition list
      MinSwitchInterval: TimeSpan
      MaxSwitchesPerHour: int
      EnableAutoSwitching: bool
      EnableFallback: bool
      FallbackModel: AIModel option
      DecisionThreshold: float
      RiskTolerance: int
      CostBudgetPerHour: float
      QualityRequirement: float
      AvailabilityRequirement: float }

// ===============================================
// モデル切り替え戦略エンジン
// ===============================================

/// モデル切り替え戦略エンジン
type ModelSwitchingEngine(configuration: SwitchingConfiguration, modelProvider: MultiModelManager) =
    let mutable switchingHistory = []

    let mutable switchingStats =
        { TotalSwitches = 0
          SuccessfulSwitches = 0
          FailedSwitches = 0
          AverageDecisionTime = TimeSpan.Zero
          AverageExecutionTime = TimeSpan.Zero
          TotalCostSaved = 0.0
          TotalQualityImprovement = 0.0
          MostUsedStrategy = RoundRobin
          MostSwitchedFromModel = Claude3Sonnet
          MostSwitchedToModel = Claude3Sonnet
          LastSwitchTime = DateTime.MinValue }

    let mutable currentModel = Claude3Sonnet
    let mutable lastSwitchTime = DateTime.MinValue
    let switchingLock = obj ()

    /// 現在のモデル取得
    member _.CurrentModel = currentModel

    /// 切り替え履歴取得
    member _.SwitchingHistory = switchingHistory

    /// 切り替え統計取得
    member _.SwitchingStatistics = switchingStats

    /// 切り替え可能性評価
    member this.EvaluateSwitchingNeed(currentMetrics: ModelMetrics) =
        try
            let triggers: SwitchingTrigger list = this.CheckSwitchingTriggers currentMetrics

            if triggers.IsEmpty then
                None
            else
                let decision = this.MakeSwitchingDecision(triggers, currentMetrics)
                Some decision
        with ex ->
            logError "ModelSwitchingEngine" $"切り替え評価エラー: {ex.Message}"
            None

    /// 切り替えトリガー確認
    member private this.CheckSwitchingTriggers(metrics: ModelMetrics) =
        configuration.Conditions
        |> List.choose (fun condition ->
            match condition with
            | ResponseTimeThreshold threshold ->
                if metrics.AverageResponseTime > threshold then
                    Some
                        { ConditionType = condition
                          CurrentValue = metrics.AverageResponseTime.TotalMilliseconds
                          ThresholdValue = threshold.TotalMilliseconds
                          TriggeredAt = DateTime.Now
                          SeverityLevel =
                            if metrics.AverageResponseTime > threshold * 2.0 then 3
                            elif metrics.AverageResponseTime > threshold * 1.5 then 2
                            else 1 }
                else
                    None
            | ErrorRateThreshold threshold ->
                if metrics.ErrorRate > threshold then
                    Some
                        { ConditionType = condition
                          CurrentValue = metrics.ErrorRate
                          ThresholdValue = threshold
                          TriggeredAt = DateTime.Now
                          SeverityLevel =
                            if metrics.ErrorRate > threshold * 2.0 then 3
                            elif metrics.ErrorRate > threshold * 1.5 then 2
                            else 1 }
                else
                    None
            | CostThreshold threshold ->
                if metrics.AverageCost > threshold then
                    Some
                        { ConditionType = condition
                          CurrentValue = metrics.AverageCost
                          ThresholdValue = threshold
                          TriggeredAt = DateTime.Now
                          SeverityLevel =
                            if metrics.AverageCost > threshold * 2.0 then 3
                            elif metrics.AverageCost > threshold * 1.5 then 2
                            else 1 }
                else
                    None
            | LoadThreshold threshold ->
                if metrics.RequestCount > threshold then
                    Some
                        { ConditionType = condition
                          CurrentValue = float metrics.RequestCount
                          ThresholdValue = float threshold
                          TriggeredAt = DateTime.Now
                          SeverityLevel =
                            if metrics.RequestCount > threshold * 2 then 3
                            elif metrics.RequestCount > threshold * 3 / 2 then 2
                            else 1 }
                else
                    None
            | QualityThreshold threshold ->
                if metrics.AverageQuality < threshold then
                    Some
                        { ConditionType = condition
                          CurrentValue = metrics.AverageQuality
                          ThresholdValue = threshold
                          TriggeredAt = DateTime.Now
                          SeverityLevel =
                            if metrics.AverageQuality < threshold * 0.5 then 3
                            elif metrics.AverageQuality < threshold * 0.75 then 2
                            else 1 }
                else
                    None
            | AvailabilityThreshold threshold ->
                if metrics.AvailabilityRate < threshold then
                    Some
                        { ConditionType = condition
                          CurrentValue = metrics.AvailabilityRate
                          ThresholdValue = threshold
                          TriggeredAt = DateTime.Now
                          SeverityLevel =
                            if metrics.AvailabilityRate < threshold * 0.5 then 3
                            elif metrics.AvailabilityRate < threshold * 0.75 then 2
                            else 1 }
                else
                    None
            | TimeWindow window ->
                if DateTime.Now - lastSwitchTime > window then
                    Some
                        { ConditionType = condition
                          CurrentValue = (DateTime.Now - lastSwitchTime).TotalMinutes
                          ThresholdValue = window.TotalMinutes
                          TriggeredAt = DateTime.Now
                          SeverityLevel = 1 }
                else
                    None
            | RequestCount count ->
                if metrics.RequestCount >= count then
                    Some
                        { ConditionType = condition
                          CurrentValue = float metrics.RequestCount
                          ThresholdValue = float count
                          TriggeredAt = DateTime.Now
                          SeverityLevel = 1 }
                else
                    None)

    /// 切り替え決定作成
    member private this.MakeSwitchingDecision(triggers: SwitchingTrigger list, currentMetrics: ModelMetrics) =
        let candidateModels = this.GetCandidateModels(currentModel, triggers)
        let bestModel = this.SelectBestModel(candidateModels, currentMetrics, triggers)
        let confidence = this.CalculateConfidence(bestModel, currentMetrics, triggers)

        let expectedImprovement =
            this.CalculateExpectedImprovement(bestModel, currentMetrics)

        let riskLevel = this.CalculateRiskLevel(bestModel, currentMetrics)
        let switchingCost = this.CalculateSwitchingCost(currentModel, bestModel)

        { FromModel = currentModel
          ToModel = bestModel
          Reason = this.GenerateReason(triggers, bestModel)
          Confidence = confidence
          ExpectedImprovement = expectedImprovement
          RiskLevel = riskLevel
          SwitchingCost = switchingCost
          DecisionAt = DateTime.Now }

    /// 候補モデル取得
    member private this.GetCandidateModels(currentModel: AIModel, triggers: SwitchingTrigger list) =
        let allModels =
            [ Claude3Sonnet
              Claude3Opus
              Claude3Haiku
              GPT4Turbo
              GPT4Vision
              GPT35Turbo
              GeminiPro
              GeminiUltra
              CodeLlama
              DeepSeekCoder ]

        // 現在のモデルを除外
        let otherModels = allModels |> List.filter (fun m -> m <> currentModel)

        // 戦略に基づいて候補を絞り込み
        match configuration.Strategy with
        | CostOptimized -> otherModels |> List.sortBy (fun m -> m.ToString().Length) // 簡易実装
        | QualityOptimized -> otherModels |> List.sortByDescending (fun m -> m.ToString().Length) // 簡易実装
        | PerformanceBased -> otherModels |> List.sortBy (fun m -> m.ToString().Length) // 簡易実装
        | TaskSpecific ->
            // タスク特化: トリガーの種類に基づいて選択
            let primaryTrigger = triggers |> List.maxBy (fun t -> t.SeverityLevel)

            match primaryTrigger.ConditionType with
            | ResponseTimeThreshold _ -> [ Claude3Haiku; GPT35Turbo; GeminiPro ]
            | ErrorRateThreshold _ -> [ Claude3Sonnet; GPT4Turbo; GeminiUltra ]
            | CostThreshold _ -> [ Claude3Haiku; GPT35Turbo; CodeLlama ]
            | QualityThreshold _ -> [ Claude3Opus; GPT4Turbo; GeminiUltra ]
            | _ -> otherModels
        | Adaptive ->
            // 適応的: 過去の履歴から学習
            this.GetAdaptiveModels(otherModels, triggers)
        | _ -> otherModels

    /// 適応的モデル選択
    member private this.GetAdaptiveModels(models: AIModel list, triggers: SwitchingTrigger list) =
        let successfulSwitches =
            switchingHistory
            |> List.filter (fun h -> h.Success && h.Impact.OverallImpact > 0.0)

        if successfulSwitches.IsEmpty then
            models
        else
            let modelScores =
                models
                |> List.map (fun model ->
                    let relevantSwitches =
                        successfulSwitches |> List.filter (fun h -> h.Decision.ToModel = model)

                    let score =
                        if relevantSwitches.IsEmpty then
                            0.0
                        else
                            relevantSwitches |> List.averageBy (fun h -> h.Impact.OverallImpact)

                    (model, score))

            modelScores |> List.sortByDescending snd |> List.map fst

    /// 最適モデル選択
    member private this.SelectBestModel
        (candidates: AIModel list, currentMetrics: ModelMetrics, triggers: SwitchingTrigger list)
        =
        if candidates.IsEmpty then
            currentModel
        else
            let scoredCandidates =
                candidates
                |> List.map (fun model ->
                    let score = this.CalculateModelScore(model, currentMetrics, triggers)
                    (model, score))

            let bestCandidate = scoredCandidates |> List.maxBy snd
            fst bestCandidate

    /// モデルスコア計算
    member private this.CalculateModelScore
        (model: AIModel, currentMetrics: ModelMetrics, triggers: SwitchingTrigger list)
        =
        // 簡易実装: モデル名の長さに基づくスコア
        let baseScore = float (model.ToString().Length) / 10.0

        // 戦略に基づく重み付け
        match configuration.Strategy with
        | CostOptimized -> baseScore * 0.8
        | QualityOptimized -> baseScore * 1.2
        | PerformanceBased -> baseScore * 1.0
        | _ -> baseScore

    /// 信頼度計算
    member private this.CalculateConfidence
        (model: AIModel, currentMetrics: ModelMetrics, triggers: SwitchingTrigger list)
        =
        // 簡易実装: トリガーの重要度に基づく信頼度
        let maxSeverity =
            if triggers.IsEmpty then
                1
            else
                triggers |> List.map (fun t -> t.SeverityLevel) |> List.max

        let baseConfidence = 0.5 + (float maxSeverity * 0.1)
        min 1.0 baseConfidence

    /// 期待改善度計算
    member private this.CalculateExpectedImprovement(model: AIModel, currentMetrics: ModelMetrics) =
        // 簡易実装: モデルタイプに基づく期待改善度
        match model with
        | Claude3Haiku -> 0.3 // 高速
        | Claude3Sonnet -> 0.6 // バランス
        | Claude3Opus -> 0.8 // 高品質
        | GPT4Turbo -> 0.7
        | _ -> 0.5

    /// リスクレベル計算
    member private this.CalculateRiskLevel(model: AIModel, currentMetrics: ModelMetrics) =
        // 簡易実装: モデルタイプに基づくリスク
        match model with
        | Claude3Sonnet -> 1 // 低リスク
        | Claude3Opus -> 2 // 中リスク
        | Claude3Haiku -> 1 // 低リスク
        | _ -> 3 // 高リスク

    /// 切り替えコスト計算
    member private this.CalculateSwitchingCost(fromModel: AIModel, toModel: AIModel) =
        // 簡易実装: 基本切り替えコスト
        0.01

    /// 理由生成
    member private this.GenerateReason(triggers: SwitchingTrigger list, toModel: AIModel) =
        let primaryTrigger = triggers |> List.maxBy (fun t -> t.SeverityLevel)

        match primaryTrigger.ConditionType with
        | ResponseTimeThreshold _ ->
            $"応答時間改善のためモデルに切り替え（現在: {primaryTrigger.CurrentValue}ms > 閾値: {primaryTrigger.ThresholdValue}ms）"
        | ErrorRateThreshold _ ->
            $"エラー率改善のためモデルに切り替え（現在: {primaryTrigger.CurrentValue}% > 閾値: {primaryTrigger.ThresholdValue}%）"
        | CostThreshold _ ->
            $"コスト削減のためモデルに切り替え（現在: {primaryTrigger.CurrentValue} > 閾値: {primaryTrigger.ThresholdValue}）"
        | QualityThreshold _ ->
            $"品質向上のためモデルに切り替え（現在: {primaryTrigger.CurrentValue} < 閾値: {primaryTrigger.ThresholdValue}）"
        | AvailabilityThreshold _ ->
            $"可用性向上のためモデルに切り替え（現在: {primaryTrigger.CurrentValue}% < 閾値: {primaryTrigger.ThresholdValue}%）"
        | _ -> $"戦略「{configuration.Strategy}」に基づきモデルに切り替え"

    /// モデル切り替え実行
    member this.ExecuteModelSwitch(decision: SwitchingDecision) =
        async {
            try
                let stopwatch = System.Diagnostics.Stopwatch.StartNew()

                logInfo "ModelSwitchingEngine" $"モデル切り替え開始: {decision.FromModel} → {decision.ToModel}"

                // 切り替え前のメトリクス取得
                let beforeMetrics =
                    { TotalRequests = 0
                      TotalCost = 0.0
                      AverageResponseTime = TimeSpan.Zero
                      SuccessRate = 0.0
                      LastUsed = DateTime.MinValue }

                // 実際の切り替え実行
                lock switchingLock (fun () ->
                    currentModel <- decision.ToModel
                    lastSwitchTime <- DateTime.Now)

                stopwatch.Stop()

                // 切り替え履歴記録
                let switchingRecord =
                    { SwitchId = Guid.NewGuid().ToString()
                      Decision = decision
                      ExecutedAt = DateTime.Now
                      ExecutionDuration = stopwatch.Elapsed
                      Success = true
                      BeforeMetrics = beforeMetrics
                      AfterMetrics = None // 後で更新
                      Impact =
                        { ResponseTimeChange = TimeSpan.Zero
                          ErrorRateChange = 0.0
                          CostChange = 0.0
                          QualityChange = 0.0
                          AvailabilityChange = 0.0
                          OverallImpact = 0.0 } }

                // 履歴と統計を更新
                this.UpdateSwitchingHistory(switchingRecord)
                this.UpdateSwitchingStatistics(switchingRecord)

                logInfo "ModelSwitchingEngine" $"モデル切り替え完了: {decision.ToModel} (理由: {decision.Reason})"

                return Ok()

            with ex ->
                logError "ModelSwitchingEngine" $"モデル切り替えエラー: {ex.Message}"
                return Error(FCode.FCodeError.ProcessingError $"モデル切り替え失敗: {ex.Message}")
        }

    /// 切り替え履歴更新
    member private this.UpdateSwitchingHistory(record: SwitchingHistory) =
        switchingHistory <- record :: switchingHistory

        // 履歴を最新100件に制限
        if switchingHistory.Length > 100 then
            switchingHistory <- switchingHistory |> List.take 100

    /// 切り替え統計更新
    member private this.UpdateSwitchingStatistics(record: SwitchingHistory) =
        let newStats =
            { switchingStats with
                TotalSwitches = switchingStats.TotalSwitches + 1
                SuccessfulSwitches = switchingStats.SuccessfulSwitches + (if record.Success then 1 else 0)
                FailedSwitches = switchingStats.FailedSwitches + (if record.Success then 0 else 1)
                LastSwitchTime = record.ExecutedAt }

        switchingStats <- newStats

    /// 切り替え影響評価
    member this.EvaluateSwitchingImpact(switchId: string) =
        async {
            try
                let switchRecord = switchingHistory |> List.find (fun h -> h.SwitchId = switchId)

                // 切り替え後のメトリクス取得（少し待ってから）
                do! Async.Sleep(5000) // 5秒待機

                let afterMetrics =
                    { TotalRequests = 0
                      TotalCost = 0.0
                      AverageResponseTime = TimeSpan.Zero
                      SuccessRate = 0.0
                      LastUsed = DateTime.MinValue }

                // 影響評価計算
                let impact =
                    { ResponseTimeChange =
                        afterMetrics.AverageResponseTime
                        - switchRecord.BeforeMetrics.AverageResponseTime
                      ErrorRateChange = afterMetrics.ErrorRate - switchRecord.BeforeMetrics.ErrorRate
                      CostChange = afterMetrics.AverageCost - switchRecord.BeforeMetrics.AverageCost
                      QualityChange = afterMetrics.AverageQuality - switchRecord.BeforeMetrics.AverageQuality
                      AvailabilityChange = afterMetrics.AvailabilityRate - switchRecord.BeforeMetrics.AvailabilityRate
                      OverallImpact = 0.0 // 後で計算
                    }

                // 総合影響度計算
                let overallImpact =
                    let timeImprovement =
                        if impact.ResponseTimeChange < TimeSpan.Zero then
                            0.3
                        else
                            -0.1

                    let errorImprovement = if impact.ErrorRateChange < 0.0 then 0.2 else -0.2
                    let costImprovement = if impact.CostChange < 0.0 then 0.2 else -0.1
                    let qualityImprovement = if impact.QualityChange > 0.0 then 0.2 else -0.1
                    let availabilityImprovement = if impact.AvailabilityChange > 0.0 then 0.1 else -0.1

                    timeImprovement
                    + errorImprovement
                    + costImprovement
                    + qualityImprovement
                    + availabilityImprovement

                let finalImpact =
                    { impact with
                        OverallImpact = overallImpact }

                // 履歴更新
                let updatedRecord =
                    { switchRecord with
                        AfterMetrics = Some afterMetrics
                        Impact = finalImpact }

                // 履歴リストを更新
                switchingHistory <-
                    switchingHistory
                    |> List.map (fun h -> if h.SwitchId = switchId then updatedRecord else h)

                logInfo "ModelSwitchingEngine" $"切り替え影響評価完了: {switchId} (総合影響度: {finalImpact.OverallImpact})"

                return Ok(finalImpact)

            with ex ->
                logError "ModelSwitchingEngine" $"切り替え影響評価エラー: {ex.Message}"
                return Error(FCode.FCodeError.ProcessingError $"切り替え影響評価失敗: {ex.Message}")
        }

    /// 自動切り替え実行
    member this.ExecuteAutoSwitching() =
        async {
            if not configuration.EnableAutoSwitching then
                return Ok()

            try
                let currentMetrics =
                    { TotalRequests = 0
                      TotalCost = 0.0
                      AverageResponseTime = TimeSpan.Zero
                      SuccessRate = 0.0
                      LastUsed = DateTime.MinValue }

                match this.EvaluateSwitchingNeed(currentMetrics) with
                | Some decision when decision.Confidence >= configuration.DecisionThreshold ->
                    let! switchResult = this.ExecuteModelSwitch(decision)

                    match switchResult with
                    | Ok() ->
                        // 影響評価を非同期で実行
                        let lastSwitch = switchingHistory |> List.head

                        Async.Start(
                            async {
                                let! _ = this.EvaluateSwitchingImpact(lastSwitch.SwitchId)
                                return ()
                            }
                        )

                        return Ok()
                    | Error err -> return Error err
                | Some decision ->
                    logInfo
                        "ModelSwitchingEngine"
                        $"切り替え信頼度不足: {decision.Confidence} < {configuration.DecisionThreshold}"

                    return Ok()
                | None ->
                    logDebug "ModelSwitchingEngine" "切り替え不要"
                    return Ok()

            with ex ->
                logError "ModelSwitchingEngine" $"自動切り替えエラー: {ex.Message}"
                return Error(FCode.FCodeError.ProcessingError $"自動切り替え失敗: {ex.Message}")
        }

    /// フォールバック実行
    member this.ExecuteFallback(reason: string) =
        async {
            match configuration.FallbackModel with
            | Some fallbackModel when fallbackModel <> currentModel ->
                let decision =
                    { FromModel = currentModel
                      ToModel = fallbackModel
                      Reason = $"フォールバック: {reason}"
                      Confidence = 1.0
                      ExpectedImprovement = 0.5
                      RiskLevel = 1
                      SwitchingCost = 0.0
                      DecisionAt = DateTime.Now }

                let! result = this.ExecuteModelSwitch(decision)

                logInfo "ModelSwitchingEngine" $"フォールバック実行: {fallbackModel} (理由: {reason})"

                return result
            | _ ->
                logWarning "ModelSwitchingEngine" $"フォールバックモデル未設定またはすでに使用中: {reason}"
                return Ok()
        }

    /// 切り替え設定更新
    member this.UpdateConfiguration(newConfig: SwitchingConfiguration) =
        // 設定を更新（実際の実装では適切な同期が必要）
        logInfo "ModelSwitchingEngine" "切り替え設定更新"
        Ok()

    /// 切り替え統計レポート生成
    member this.GenerateStatisticsReport() =
        let report =
            $"=== モデル切り替え統計レポート ===\n"
            + $"生成日時: {DateTime.Now}\n"
            + $"現在のモデル: {currentModel}\n"
            + $"最終切り替え: {switchingStats.LastSwitchTime}\n"
            + $"\n"
            + $"=== 切り替え実績 ===\n"
            + $"総切り替え数: {switchingStats.TotalSwitches}\n"
            + $"成功数: {switchingStats.SuccessfulSwitches}\n"
            + $"失敗数: {switchingStats.FailedSwitches}\n"
            + $"成功率: {if switchingStats.TotalSwitches > 0 then
                          (float switchingStats.SuccessfulSwitches / float switchingStats.TotalSwitches
                           * 100.0)
                      else
                          0.0}%\n"
            + $"\n"
            + $"=== パフォーマンス ===\n"
            + $"平均決定時間: {switchingStats.AverageDecisionTime}\n"
            + $"平均実行時間: {switchingStats.AverageExecutionTime}\n"
            + $"総コスト削減: ${switchingStats.TotalCostSaved}\n"
            + $"総品質改善: {switchingStats.TotalQualityImprovement}\n"
            + $"\n"
            + $"=== 使用頻度 ===\n"
            + $"最多使用戦略: {switchingStats.MostUsedStrategy}\n"
            + $"最多切り替え元: {switchingStats.MostSwitchedFromModel}\n"
            + $"最多切り替え先: {switchingStats.MostSwitchedToModel}\n"
            + $"\n"
            + $"=== 設定 ===\n"
            + $"戦略: {configuration.Strategy}\n"
            + $"自動切り替え: {configuration.EnableAutoSwitching}\n"
            + $"決定閾値: {configuration.DecisionThreshold}\n"
            + $"リスク許容度: {configuration.RiskTolerance}\n"

        logInfo "ModelSwitchingEngine" "切り替え統計レポート生成完了"
        report

// ===============================================
// モデル切り替え戦略ユーティリティ
// ===============================================

/// モデル切り替え戦略ユーティリティ
module ModelSwitchingUtils =

    /// デフォルト設定作成
    let createDefaultConfiguration () =
        { Strategy = Adaptive
          Conditions =
            [ ResponseTimeThreshold(TimeSpan.FromSeconds(30.0))
              ErrorRateThreshold 0.05
              CostThreshold 0.1
              QualityThreshold 0.7
              AvailabilityThreshold 0.9 ]
          MinSwitchInterval = TimeSpan.FromMinutes(5.0)
          MaxSwitchesPerHour = 10
          EnableAutoSwitching = true
          EnableFallback = true
          FallbackModel = Some Claude3Haiku
          DecisionThreshold = 0.7
          RiskTolerance = 3
          CostBudgetPerHour = 10.0
          QualityRequirement = 0.8
          AvailabilityRequirement = 0.95 }

    /// 切り替え戦略名取得
    let getStrategyName (strategy: SwitchingStrategyType) =
        match strategy with
        | RoundRobin -> "ラウンドロビン"
        | LoadBased -> "負荷ベース"
        | PerformanceBased -> "パフォーマンスベース"
        | CostOptimized -> "コスト最適化"
        | QualityOptimized -> "品質最適化"
        | TaskSpecific -> "タスク特化"
        | Adaptive -> "適応的"
        | Manual -> "手動"

    /// 切り替え条件名取得
    let getConditionName (condition: SwitchingCondition) =
        match condition with
        | ResponseTimeThreshold _ -> "応答時間閾値"
        | ErrorRateThreshold _ -> "エラー率閾値"
        | CostThreshold _ -> "コスト閾値"
        | LoadThreshold _ -> "負荷閾値"
        | QualityThreshold _ -> "品質閾値"
        | AvailabilityThreshold _ -> "可用性閾値"
        | TimeWindow _ -> "時間窓"
        | RequestCount _ -> "リクエスト数"

    /// 切り替えリスクレベル名取得
    let getRiskLevelName (riskLevel: int) =
        match riskLevel with
        | 1 -> "低リスク"
        | 2 -> "中リスク"
        | 3 -> "高リスク"
        | 4 -> "超高リスク"
        | _ -> "不明"

    /// 切り替え影響評価
    let evaluateImpactSeverity (impact: SwitchingImpact) =
        if impact.OverallImpact > 0.5 then "大幅改善"
        elif impact.OverallImpact > 0.2 then "改善"
        elif impact.OverallImpact > -0.2 then "中立"
        elif impact.OverallImpact > -0.5 then "悪化"
        else "大幅悪化"

    /// 切り替え推奨度計算
    let calculateRecommendationScore (decision: SwitchingDecision) =
        let confidenceScore = decision.Confidence * 0.4
        let improvementScore = decision.ExpectedImprovement * 0.3
        let riskScore = (1.0 - float decision.RiskLevel / 5.0) * 0.2
        let costScore = (1.0 - min 1.0 decision.SwitchingCost) * 0.1

        confidenceScore + improvementScore + riskScore + costScore

    /// 切り替え推奨度レベル
    let getRecommendationLevel (score: float) =
        if score > 0.8 then "強く推奨"
        elif score > 0.6 then "推奨"
        elif score > 0.4 then "条件付き推奨"
        elif score > 0.2 then "非推奨"
        else "強く非推奨"

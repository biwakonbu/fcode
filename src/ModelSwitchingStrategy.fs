module FCode.ModelSwitchingStrategy

open System
open System.Threading
open FCode.Logger
open FCode.FCodeError
open FCode.AIModelProvider

// ===============================================
// モデル切り替え戦略定義 (最小実装)
// ===============================================

/// 切り替え戦略タイプ
type SwitchingStrategyType = | Adaptive

/// モデル切り替え設定
type SwitchingConfiguration =
    { Strategy: SwitchingStrategyType
      EnableAutoSwitching: bool }

/// モデル切り替えエンジン (最小実装)
type ModelSwitchingEngine(configuration: SwitchingConfiguration) =
    let mutable currentModel = Claude3Sonnet
    let lockObj = obj ()

    /// 現在のモデル取得（スレッドセーフ）
    member _.CurrentModel = lock lockObj (fun () -> currentModel)

    /// 最適モデル推奨 (スタブ実装)
    member _.RecommendModelSwitch(taskDescription: string) =
        async {
            try
                logInfo "ModelSwitchingEngine" $"モデル推奨評価: {taskDescription}"

                // 簡易実装: 基本的に現在のモデルを推奨（スレッドセーフ）
                let recommendedModel = lock lockObj (fun () -> currentModel)

                return Ok(recommendedModel)
            with ex ->
                logError "ModelSwitchingEngine" $"モデル推奨エラー: {ex.Message}"
                return Result.Error(SystemError($"モデル推奨失敗: {ex.Message}"))
        }

    /// 設定更新
    member _.UpdateConfiguration(newConfig: SwitchingConfiguration) =
        logInfo "ModelSwitchingEngine" "切り替え設定更新"
        Ok()

/// モデル切り替え戦略ユーティリティ
module ModelSwitchingUtils =

    /// デフォルト設定作成
    let createDefaultConfiguration () =
        { Strategy = Adaptive
          EnableAutoSwitching = true }

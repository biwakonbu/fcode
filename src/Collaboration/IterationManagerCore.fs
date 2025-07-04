module FCode.Collaboration.IterationManagerCore

open System
open FCode.Collaboration.CollaborationTypes

/// 反復開発マネージャー
type IterationManagerCore() =

    /// 次フェーズへの進行
    member this.AdvanceToNextPhase(iterationPlan: IterationPlan) : IterationPlan =
        let currentIndex =
            iterationPlan.Phases |> List.findIndex (fun p -> p = iterationPlan.CurrentPhase)

        let nextIndex = currentIndex + 1

        if nextIndex < iterationPlan.Phases.Length then
            let nextPhase = iterationPlan.Phases.[nextIndex]
            let progressIncrement = 1.0 / float iterationPlan.Phases.Length

            { iterationPlan with
                CurrentPhase = nextPhase
                CompletionRate = min 1.0 (iterationPlan.CompletionRate + progressIncrement) }
        else
            { iterationPlan with
                CompletionRate = 1.0 }

    /// 反復完成チェック
    member this.CheckIterationCompletion(iterationPlan: IterationPlan) : IterationCompletionResult =
        let isComplete =
            iterationPlan.CurrentPhase = (iterationPlan.Phases |> List.last)
            && iterationPlan.CompletionRate >= 0.95

        { IsComplete = isComplete
          FinalCompletionRate = if isComplete then 1.0 else iterationPlan.CompletionRate
          NextIterationRecommended = isComplete }

// テスト用反復計画型
and IterationPlan =
    { IterationId: string
      Phases: string list
      CurrentPhase: string
      CompletionRate: float }

// 反復完成結果型
and IterationCompletionResult =
    { IsComplete: bool
      FinalCompletionRate: float
      NextIterationRecommended: bool }

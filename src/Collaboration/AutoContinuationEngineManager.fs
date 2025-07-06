module FCode.Collaboration.AutoContinuationEngineManager

open System
open FCode.Collaboration.CollaborationTypes

/// 自動継続判定エンジンマネージャー
type AutoContinuationEngineManager() =

    /// 継続判定決定
    member this.DecideContinuation(assessment: CompletionAssessment) : ContinuationDecision =
        // 高品質完成の場合は自動継続
        if
            assessment.OverallCompletionRate = 1.0
            && assessment.QualityScore >= 0.90
            && assessment.AcceptanceCriteriaMet
            && not assessment.RequiresPOApproval
        then
            AutoContinue "高品質完成"

        // 重大な品質課題がある場合は実行停止
        elif assessment.QualityScore < 0.5 || assessment.TasksBlocked > 5 then
            StopExecution "品質"

        // 品質課題があるがリカバリ可能な場合はPO承認要求
        elif
            not assessment.AcceptanceCriteriaMet
            || assessment.QualityScore < 0.80
            || assessment.RequiresPOApproval
        then
            RequirePOApproval "品質"

        // その他の場合は自動継続
        else
            AutoContinue "基準達成により自動継続"

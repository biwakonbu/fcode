module FCode.Collaboration.POApprovalRequirementAnalyzerManager

open System
open FCode.Collaboration.CollaborationTypes

/// PO承認要否分析マネージャー
type POApprovalRequirementAnalyzerManager() =
    
    /// PO承認要否分析
    member this.AnalyzePOApprovalRequirement(assessment: CompletionAssessment) : POApprovalAnalysis =
        let requiresApproval = 
            // 品質基準未達成
            not assessment.AcceptanceCriteriaMet ||
            // 品質スコア低下
            assessment.QualityScore < 0.80 ||
            // 完了率低下
            assessment.OverallCompletionRate < 0.80 ||
            // ブロックされたタスクあり
            assessment.TasksBlocked > 0 ||
            // 明示的なPO承認要求
            assessment.RequiresPOApproval
        
        let recommendedAction = 
            if requiresApproval then "PO承認要求"
            else "自動承認"
            
        let analysisReasons = this.GenerateAnalysisReasons(assessment)
        
        {
            RequiresPOApproval = requiresApproval
            RecommendedAction = recommendedAction
            AnalysisReasons = analysisReasons
            Confidence = if requiresApproval then 0.90 else 0.95
        }
    
    /// 分析理由生成
    member private this.GenerateAnalysisReasons(assessment: CompletionAssessment) : string list =
        let mutable reasons = []
        
        if not assessment.AcceptanceCriteriaMet then
            reasons <- "受け入れ基準未達成" :: reasons
            
        if assessment.QualityScore < 0.80 then
            reasons <- "品質スコア基準未達" :: reasons
            
        if assessment.TasksBlocked > 0 then
            reasons <- "ブロックされたタスクあり" :: reasons
            
        if assessment.OverallCompletionRate < 0.80 then
            reasons <- "完了率基準未達" :: reasons
            
        if reasons.IsEmpty then
            reasons <- ["全基準達成"]
            
        reasons |> List.rev

// PO承認分析結果型
and POApprovalAnalysis = {
    RequiresPOApproval: bool
    RecommendedAction: string
    AnalysisReasons: string list
    Confidence: float
}
module FCode.Collaboration.CompletionCriteriaCheckerManager

open System
open FCode.Collaboration.CollaborationTypes

/// 受け入れ基準チェッカーマネージャー
type CompletionCriteriaCheckerManager() =
    
    /// 受け入れ基準チェック
    member this.CheckCriteria(criteria: string list, completionData: CompletionData) : CriteriaCheckResult =
        let mutable metCriteria = []
        let mutable unmetCriteria = []
        
        for criterion in criteria do
            let isMet = this.EvaluateCriterion(criterion, completionData)
            if isMet then
                metCriteria <- criterion :: metCriteria
            else
                unmetCriteria <- criterion :: unmetCriteria
        
        {
            AllCriteriaMet = unmetCriteria.IsEmpty
            MetCriteria = metCriteria |> List.rev
            UnmetCriteria = unmetCriteria |> List.rev
        }
    
    /// 個別基準評価
    member private this.EvaluateCriterion(criterion: string, data: CompletionData) : bool =
        if criterion.Contains("テスト") && criterion.Contains("95%") then
            data.TestCoverage >= 0.95
        elif criterion.Contains("性能") then
            data.PerformanceScore >= 0.70
        elif criterion.Contains("セキュリティ") then
            data.SecurityCompliance
        elif criterion.Contains("品質") then
            data.CodeQuality >= 0.80
        else
            true // 未知の基準は達成とみなす

// テスト用完成データ型
and CompletionData = {
    TestCoverage: float
    PerformanceScore: float
    SecurityCompliance: bool
    CodeQuality: float
}

// 基準チェック結果型
and CriteriaCheckResult = {
    AllCriteriaMet: bool
    MetCriteria: string list
    UnmetCriteria: string list
}
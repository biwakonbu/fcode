module FCode.Collaboration.IEscalationManager

open System
open FCode.Collaboration.CollaborationTypes

/// エスカレーションマネージャーインターフェース
/// 致命度管理・PO判断システム・緊急対応フローを提供
type IEscalationManager =

    /// 5段階致命度評価: 影響度・時間制約・リスク分析
    abstract member EvaluateSeverity:
        taskId: string * agentId: string * error: string -> Async<Result<EscalationSeverity, CollaborationError>>

    /// PO通知レベル判定: 軽微(自動対応) / 重要(即座通知) / 致命(緊急停止)
    abstract member DetermineNotificationLevel:
        severity: EscalationSeverity * factors: EscalationFactors -> Result<bool * string, CollaborationError>

    /// エスカレーション発生時の初期処理: コンテキスト作成・分析・対応方針決定
    abstract member TriggerEscalation:
        taskId: string * agentId: string * error: string -> Async<Result<EscalationContext, CollaborationError>>

    /// 判断待機管理: 代替作業継続・ブロッカー迂回・優先順位調整
    abstract member ManageWaitingDecision:
        escalationId: string * maxWaitTime: TimeSpan -> Async<Result<EscalationAction, CollaborationError>>

    /// 緊急対応フロー: データ保護・復旧優先・影響最小化
    abstract member ExecuteEmergencyResponse:
        escalationContext: EscalationContext -> Async<Result<EscalationResult, CollaborationError>>

    /// 自動復旧試行: 回復可能な問題の自動解決
    abstract member AttemptAutoRecovery:
        escalationContext: EscalationContext -> Async<Result<bool * string, CollaborationError>>

    /// PO判断受信処理: 承認・却下・上位エスカレーション対応
    abstract member ProcessPODecision:
        escalationId: string * approved: bool * reason: string -> Async<Result<EscalationResult, CollaborationError>>

    /// エスカレーション履歴取得: 過去の対応パターン・学習データ
    abstract member GetEscalationHistory:
        agentId: string option * severity: EscalationSeverity option ->
            Async<Result<EscalationResult list, CollaborationError>>

    /// 現在アクティブなエスカレーション一覧
    abstract member GetActiveEscalations: unit -> Async<Result<EscalationContext list, CollaborationError>>

    /// エスカレーション統計情報
    abstract member GetEscalationStatistics: unit -> Async<Result<EscalationStatistics, CollaborationError>>

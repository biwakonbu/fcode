module FCode.Collaboration.IVirtualTimeManager

open System
open FCode.Collaboration.CollaborationTypes

/// VirtualTimeManager インターフェース
type IVirtualTimeManager =

    // ========================================
    // 基本時間管理
    // ========================================

    /// スプリント開始
    abstract member StartSprint: sprintId: string -> Async<Result<VirtualTimeContext, CollaborationError>>

    /// スプリント停止
    abstract member StopSprint: sprintId: string -> Async<Result<unit, CollaborationError>>

    /// 現在の仮想時間取得
    abstract member GetCurrentVirtualTime: sprintId: string -> Async<Result<VirtualTimeUnit, CollaborationError>>

    /// 実経過時間から仮想時間計算
    abstract member CalculateVirtualTime: realElapsed: TimeSpan -> VirtualTimeUnit

    /// 仮想時間から実時間計算
    abstract member CalculateRealDuration: virtualTime: VirtualTimeUnit -> TimeSpan

    // ========================================
    // スタンドアップMTG管理
    // ========================================

    /// 次回スタンドアップスケジュール
    abstract member ScheduleNextStandup:
        sprintId: string * participants: string list -> Async<Result<StandupMeeting, CollaborationError>>

    /// スタンドアップMTG実行
    abstract member ExecuteStandup:
        meetingId: string * progressReports: (string * string) list -> Async<Result<StandupMeeting, CollaborationError>>

    /// スタンドアップ履歴取得
    abstract member GetStandupHistory: sprintId: string -> Async<Result<StandupMeeting list, CollaborationError>>

    // ========================================
    // レビューMTG管理
    // ========================================

    /// 72分レビューMTGトリガー
    abstract member TriggerReviewMeeting: sprintId: string -> Async<Result<ReviewMeeting, CollaborationError>>

    /// 完成度評価実行
    abstract member AssessCompletion:
        sprintId: string * taskIds: string list -> Async<Result<CompletionAssessment, CollaborationError>>

    /// 継続判定実行
    abstract member DecideContinuation:
        sprintId: string * assessment: CompletionAssessment -> Async<Result<ContinuationDecision, CollaborationError>>

    // ========================================
    // イベント管理
    // ========================================

    /// タイムイベント登録
    abstract member RegisterTimeEvent: sprintId: string * event: TimeEvent -> Async<Result<unit, CollaborationError>>

    /// 保留中イベント取得
    abstract member GetPendingEvents: sprintId: string -> Async<Result<TimeEvent list, CollaborationError>>

    /// イベント発火処理
    abstract member ProcessEvents: sprintId: string -> Async<Result<TimeEvent list, CollaborationError>>

    // ========================================
    // 統計・監視
    // ========================================

    /// アクティブスプリント一覧
    abstract member GetActiveSprints: unit -> Async<Result<VirtualTimeContext list, CollaborationError>>

    /// スプリント統計取得
    abstract member GetSprintStatistics: sprintId: string -> Async<Result<(string * obj) list, CollaborationError>>

    /// システム健全性チェック
    abstract member PerformHealthCheck: unit -> Async<Result<bool * string, CollaborationError>>

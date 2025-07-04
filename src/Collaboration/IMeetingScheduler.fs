module FCode.Collaboration.IMeetingScheduler

open System
open FCode.Collaboration.CollaborationTypes

/// ミーティングスケジューラーインターフェース
type IMeetingScheduler =

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

    /// スタンドアップMTG更新
    abstract member UpdateStandupMeeting:
        meetingId: string * updates: StandupMeeting -> Async<Result<StandupMeeting, CollaborationError>>

    // ========================================
    // レビューMTG管理
    // ========================================

    /// レビューMTGトリガー
    abstract member TriggerReviewMeeting: sprintId: string -> Async<Result<ReviewMeeting, CollaborationError>>

    /// 完成度評価実行
    abstract member AssessCompletion:
        sprintId: string * taskIds: string list -> Async<Result<CompletionAssessment, CollaborationError>>

    /// 継続判定実行
    abstract member DecideContinuation:
        sprintId: string * assessment: CompletionAssessment -> Async<Result<ContinuationDecision, CollaborationError>>

    /// レビューMTG履歴取得
    abstract member GetReviewHistory: sprintId: string -> Async<Result<ReviewMeeting list, CollaborationError>>

    // ========================================
    // MTGスケジュール管理
    // ========================================

    /// 次回MTG時刻計算
    abstract member CalculateNextMeetingTime: currentTime: VirtualTimeUnit * intervalVH: int -> VirtualTimeUnit

    /// MTG競合チェック
    abstract member CheckMeetingConflicts:
        sprintId: string * proposedTime: VirtualTimeUnit -> Async<Result<bool, CollaborationError>>

    /// MTGキャンセル
    abstract member CancelMeeting: meetingId: string -> Async<Result<unit, CollaborationError>>

    // ========================================
    // 統計・監視
    // ========================================

    /// MTG統計取得
    abstract member GetMeetingStatistics: sprintId: string -> Async<Result<(string * int) list, CollaborationError>>

    /// 健全性チェック
    abstract member PerformHealthCheck: unit -> Async<Result<bool * string, CollaborationError>>

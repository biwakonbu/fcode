module FCode.Collaboration.IEventProcessor

open System
open FCode.Collaboration.CollaborationTypes

/// イベントプロセッサーインターフェース
type IEventProcessor =

    // ========================================
    // イベント管理
    // ========================================

    /// タイムイベント登録
    abstract member RegisterTimeEvent: sprintId: string * event: TimeEvent -> Async<Result<unit, CollaborationError>>

    /// 保留中イベント取得
    abstract member GetPendingEvents: sprintId: string -> Async<Result<TimeEvent list, CollaborationError>>

    /// イベント発火処理
    abstract member ProcessEvents: sprintId: string -> Async<Result<TimeEvent list, CollaborationError>>

    /// 特定イベント削除
    abstract member RemoveEvent: sprintId: string * eventPattern: TimeEvent -> Async<Result<bool, CollaborationError>>

    // ========================================
    // イベントフィルタリング
    // ========================================

    /// イベント種別フィルタ
    abstract member FilterEventsByType:
        sprintId: string * eventType: string -> Async<Result<TimeEvent list, CollaborationError>>

    /// 時刻範囲フィルタ
    abstract member FilterEventsByTimeRange:
        sprintId: string * startTime: VirtualTimeUnit * endTime: VirtualTimeUnit ->
            Async<Result<TimeEvent list, CollaborationError>>

    /// 重複イベントチェック
    abstract member CheckDuplicateEvents: sprintId: string -> Async<Result<TimeEvent list, CollaborationError>>

    // ========================================
    // イベント実行
    // ========================================

    /// スタンドアップイベント実行
    abstract member ExecuteStandupEvent:
        sprintId: string * participants: string list -> Async<Result<unit, CollaborationError>>

    /// レビューイベント実行
    abstract member ExecuteReviewEvent: sprintId: string -> Async<Result<unit, CollaborationError>>

    /// 期限アプローチイベント実行
    abstract member ExecuteDeadlineEvent: sprintId: string * taskId: string -> Async<Result<unit, CollaborationError>>

    /// 緊急停止イベント実行
    abstract member ExecuteEmergencyStopEvent:
        sprintId: string * reason: string -> Async<Result<unit, CollaborationError>>

    // ========================================
    // 統計・監視
    // ========================================

    /// イベント統計取得
    abstract member GetEventStatistics: sprintId: string -> Async<Result<(string * int) list, CollaborationError>>

    /// イベント履歴取得
    abstract member GetEventHistory: sprintId: string -> Async<Result<(DateTime * TimeEvent) list, CollaborationError>>

    /// イベント健全性チェック
    abstract member PerformEventHealthCheck: unit -> Async<Result<bool * string, CollaborationError>>

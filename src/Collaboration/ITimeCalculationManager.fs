module FCode.Collaboration.ITimeCalculationManager

open System
open FCode.Collaboration.CollaborationTypes

/// 時間計算管理インターフェース
type ITimeCalculationManager =

    // ========================================
    // 基本時間計算
    // ========================================

    /// 実経過時間から仮想時間計算
    abstract member CalculateVirtualTime: realElapsed: TimeSpan -> VirtualTimeUnit

    /// 仮想時間から実時間計算
    abstract member CalculateRealDuration: virtualTime: VirtualTimeUnit -> TimeSpan

    /// 現在の仮想時間取得
    abstract member GetCurrentVirtualTime: sprintId: string -> Async<Result<VirtualTimeUnit, CollaborationError>>

    /// 仮想時間コンテキスト更新
    abstract member UpdateVirtualTimeContext: sprintId: string -> Async<Result<VirtualTimeContext, CollaborationError>>

    // ========================================
    // スプリント時間管理
    // ========================================

    /// スプリント時間コンテキスト作成
    abstract member CreateSprintContext: sprintId: string * config: VirtualTimeConfig -> VirtualTimeContext

    /// スプリント経過時間計算
    abstract member CalculateSprintProgress: sprintId: string -> Async<Result<float, CollaborationError>>

    /// スプリント残り時間計算
    abstract member CalculateRemainingTime: sprintId: string -> Async<Result<TimeSpan, CollaborationError>>

    // ========================================
    // 時間単位変換
    // ========================================

    /// 仮想時間単位をVirtualHour換算
    abstract member ToVirtualHours: virtualTime: VirtualTimeUnit -> int

    /// VirtualHourから仮想時間単位作成
    abstract member FromVirtualHours: hours: int * config: VirtualTimeConfig -> VirtualTimeUnit

    /// 時間単位の妥当性検証
    abstract member ValidateTimeUnit: virtualTime: VirtualTimeUnit * config: VirtualTimeConfig -> bool

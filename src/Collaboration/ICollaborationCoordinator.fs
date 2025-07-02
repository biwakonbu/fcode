module FCode.Collaboration.ICollaborationCoordinator

open System
open System.Threading.Tasks
open FCode.Collaboration.CollaborationTypes

/// 競合解決戦略
type ConflictResolution =
    | Queue
    | Parallel
    | Delegate of string
    | Merge

/// 協調作業制御・競合回避インターフェース
type ICollaborationCoordinator =
    /// タスク開始前の競合チェック・許可
    abstract member RequestTaskExecution:
        agentId: string * taskId: string * requiredResources: string list -> Result<unit, CollaborationError>

    /// タスク完了通知・リソース解放
    abstract member NotifyTaskCompletion:
        agentId: string * taskId: string * releasedResources: string list -> Result<unit, CollaborationError>

    /// 同期ポイントでの協調制御
    abstract member RequestSynchronization:
        participatingAgents: string list * reason: string -> Async<Result<bool, CollaborationError>>

    /// 競合自動解決戦略の実行
    abstract member ResolveConflict: conflict: ConflictType -> Result<ConflictResolution, CollaborationError>

    /// 並列作業効率分析
    abstract member AnalyzeCollaborationEfficiency:
        unit ->
            Result<
                {| ActiveOperations: int
                   TotalAgents: int
                   ParallelEfficiency: float
                   ResourceUtilization: float
                   BottleneckDetected: bool |},
                CollaborationError
             >

    /// デッドロック検出
    abstract member DetectDeadlock: unit -> Result<string list option, CollaborationError>

    /// 協調作業統計取得
    abstract member GetCollaborationStatistics:
        unit ->
            Result<
                {| ActiveOperations: int
                   LockedResources: int
                   AverageOperationDuration: TimeSpan |},
                CollaborationError
             >

    /// 協調作業イベント
    abstract member CollaborationEvent: IEvent<CollaborationEvent>

    /// システム状態のリセット（緊急時用）
    abstract member ResetCoordinationState: unit -> Result<unit, CollaborationError>

    /// リソース解放
    inherit IDisposable

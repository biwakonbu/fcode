module FCode.Collaboration.IProgressAggregator

open System
open FCode.Collaboration.CollaborationTypes

/// 進捗集計・可視化インターフェース
type IProgressAggregator =
    /// 現在の進捗サマリーを取得
    abstract member GetCurrentSummary: unit -> Result<ProgressSummary, CollaborationError>

    /// エージェント別進捗詳細を取得
    abstract member GetAgentProgressDetails:
        unit ->
            Result<
                {| AgentId: string
                   Status: string
                   Progress: float
                   CurrentTask: string
                   LastUpdate: DateTime
                   WorkingTime: TimeSpan |} list,
                CollaborationError
             >

    /// タスク別進捗詳細を取得
    abstract member GetTaskProgressDetails:
        unit ->
            Result<
                {| TaskId: string
                   Status: string
                   AssignedAgent: string
                   IsBlocked: bool
                   IsExecutable: bool
                   Priority: TaskPriority |} list,
                CollaborationError
             >

    /// 進捗トレンド分析
    abstract member AnalyzeProgressTrend:
        unit ->
            Result<
                {| CurrentVelocity: float
                   Efficiency: float
                   BottleneckRisk: bool
                   RecommendedActions: string list |},
                CollaborationError
             >

    /// マイルストーン進捗チェック
    abstract member CheckMilestones:
        milestones: (string * float) list ->
            Result<
                {| Milestone: string
                   TargetProgress: float
                   Achieved: bool
                   Gap: float |} list,
                CollaborationError
             >

    /// 進捗レポート生成
    abstract member GenerateProgressReport: unit -> Result<string, CollaborationError>

    /// リアルタイム進捗監視開始
    abstract member StartMonitoring: intervalSeconds: int -> Result<IDisposable, CollaborationError>

    /// 進捗変更イベント
    abstract member ProgressChanged: IEvent<ProgressSummary>

    /// 手動進捗更新トリガー
    abstract member TriggerProgressUpdate: unit -> Result<ProgressSummary, CollaborationError>

    /// システムリセット
    abstract member Reset: unit -> Result<unit, CollaborationError>

    /// リソース解放
    inherit IDisposable

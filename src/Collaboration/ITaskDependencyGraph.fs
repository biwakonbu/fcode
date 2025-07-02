module FCode.Collaboration.ITaskDependencyGraph

open System
open FCode.Collaboration.CollaborationTypes

/// タスク依存関係管理インターフェース
type ITaskDependencyGraph =
    /// タスクを追加
    abstract member AddTask: task: TaskInfo -> Result<unit, CollaborationError>

    /// タスクを取得
    abstract member GetTask: taskId: string -> Result<TaskInfo option, CollaborationError>

    /// 全タスクを取得
    abstract member GetAllTasks: unit -> Result<TaskInfo list, CollaborationError>

    /// 依存関係を追加（循環依存検出含む）
    abstract member AddDependency: taskId: string * dependsOnTaskId: string -> Result<unit, CollaborationError>

    /// 依存関係を削除
    abstract member RemoveDependency: taskId: string * dependsOnTaskId: string -> Result<unit, CollaborationError>

    /// タスクの依存関係を取得
    abstract member GetDependencies: taskId: string -> Result<TaskDependency option, CollaborationError>

    /// 実行可能タスク一覧を取得（依存関係が解決済み）
    abstract member GetExecutableTasks: unit -> Result<TaskInfo list, CollaborationError>

    /// ブロックされているタスク一覧を取得
    abstract member GetBlockedTasks: unit -> Result<(TaskInfo * string list) list, CollaborationError>

    /// タスク完了時の処理
    abstract member CompleteTask: taskId: string -> Result<TaskInfo list, CollaborationError>

    /// タスク状態更新
    abstract member UpdateTaskStatus: taskId: string * status: TaskStatus -> Result<unit, CollaborationError>

    /// 循環依存検出
    abstract member DetectCircularDependencies: unit -> Result<string list list, CollaborationError>

    /// 重要パス分析（最長実行時間パス）
    abstract member GetCriticalPath: unit -> Result<(TimeSpan * string list), CollaborationError>

    /// 依存関係グラフの可視化用データ取得
    abstract member GetGraphData:
        unit -> Result<(string * TaskStatus * string) list * (string * string) list, CollaborationError>

    /// 統計情報取得
    abstract member GetStatistics: unit -> Result<TaskStatistics, CollaborationError>

    /// タスク変更イベント
    abstract member TaskChanged: IEvent<TaskInfo>

    /// システムリセット
    abstract member Reset: unit -> Result<unit, CollaborationError>

    /// リソース解放
    inherit IDisposable

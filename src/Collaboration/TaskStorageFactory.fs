module FCode.Collaboration.TaskStorageFactory

open System
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.TaskStorageManager
open FCode.Logger

/// タスクストレージ設計選択の列挙型
type TaskStorageDesign =
    | FullTableDesign // 包括的テーブル設計（標準）
    | OptimizedDesign // 最適化設計（将来拡張用）

/// タスクストレージの統合インターフェース
type ITaskStorage =
    abstract member SaveTask: TaskInfo -> Async<Result<int, CollaborationError>>
    abstract member GetTask: string -> Async<Result<TaskInfo option, CollaborationError>>
    abstract member SaveTaskDependency: string * string * string -> Async<Result<int, CollaborationError>>
    abstract member GetExecutableTasks: unit -> Async<Result<TaskInfo list, CollaborationError>>
    abstract member GetProgressSummary: unit -> Async<Result<ProgressSummary, CollaborationError>>
    abstract member InitializeDatabase: unit -> Async<Result<unit, CollaborationError>>
    inherit IDisposable

/// TaskStorageManagerアダプター（統一実装）
type TaskStorageAdapter(manager: TaskStorageManager) =
    interface ITaskStorage with
        member _.SaveTask(task) = manager.SaveTask(task)
        member _.GetTask(taskId) = manager.GetTask(taskId)

        member _.SaveTaskDependency(taskId, dependsOn, depType) =
            manager.SaveTaskDependency(taskId, dependsOn, depType)

        member _.GetExecutableTasks() = manager.GetExecutableTasks()
        member _.GetProgressSummary() = manager.GetProgressSummary()
        member _.InitializeDatabase() = manager.InitializeDatabase()
        member _.Dispose() = manager.Dispose()

/// タスクストレージファクトリー
type TaskStorageFactory() =

    /// 環境変数からストレージ設計を取得
    static member GetStorageDesignFromEnvironment() =
        let envVar = Environment.GetEnvironmentVariable("FCODE_TASK_STORAGE_DESIGN")

        match envVar with
        | "6table" -> OptimizedDesign
        | "3table"
        | "full"
        | null
        | "" -> FullTableDesign // デフォルトは包括設計
        | "optimized" -> OptimizedDesign // 後方互換性のため残す
        | _ ->
            logWarning "TaskStorageFactory" $"Unknown storage design: {envVar}, using full design"
            FullTableDesign

    /// タスクストレージインスタンスを作成（統一実装）
    static member CreateTaskStorage(connectionString: string, ?design: TaskStorageDesign) =
        let selectedDesign =
            match design with
            | Some d -> d
            | None -> TaskStorageFactory.GetStorageDesignFromEnvironment()

        match selectedDesign with
        | FullTableDesign
        | OptimizedDesign ->
            logInfo "TaskStorageFactory" $"Using {selectedDesign} TaskStorageManager"
            let manager = new TaskStorageManager(connectionString)
            new TaskStorageAdapter(manager) :> ITaskStorage

    /// 設計情報の取得
    static member GetDesignInfo(design: TaskStorageDesign) =
        match design with
        | FullTableDesign ->
            { Name = "Full Table Design"
              TableCount = 3
              IndexCount = 7
              EstimatedComplexity = "Low"
              Description = "Comprehensive design with normalized tables for maximum functionality" }
        | OptimizedDesign ->
            { Name = "Optimized Design"
              TableCount = 6
              IndexCount = 16
              EstimatedComplexity = "High"
              Description = "Performance-optimized variant with reduced indexing overhead" }

/// 設計情報の型
and DesignInfo =
    { Name: string
      TableCount: int
      IndexCount: int
      EstimatedComplexity: string
      Description: string }

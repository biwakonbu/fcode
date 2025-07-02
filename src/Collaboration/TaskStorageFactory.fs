module FCode.Collaboration.TaskStorageFactory

open System
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.TaskStorageManager
open FCode.Collaboration.SimplifiedTaskStorageManager
open FCode.Logger

/// タスクストレージ設計選択の列挙型
type TaskStorageDesign =
    | SixTableDesign    // 既存の6テーブル設計
    | ThreeTableDesign  // 新しい3テーブル設計

/// タスクストレージの統合インターフェース
type ITaskStorage =
    abstract member SaveTask: TaskInfo -> Async<Result<int, CollaborationError>>
    abstract member GetTask: string -> Async<Result<TaskInfo option, CollaborationError>>
    abstract member SaveTaskDependency: string * string * string -> Async<Result<int, CollaborationError>>
    abstract member GetExecutableTasks: unit -> Async<Result<TaskInfo list, CollaborationError>>
    abstract member GetProgressSummary: unit -> Async<Result<ProgressSummary, CollaborationError>>
    abstract member InitializeDatabase: unit -> Async<Result<unit, CollaborationError>>
    inherit IDisposable

/// 6テーブル設計のアダプター
type SixTableStorageAdapter(manager: TaskStorageManager) =
    interface ITaskStorage with
        member _.SaveTask(task) = manager.SaveTask(task)
        member _.GetTask(taskId) = manager.GetTask(taskId)
        member _.SaveTaskDependency(taskId, dependsOn, depType) = 
            manager.SaveTaskDependency(taskId, dependsOn, depType)
        member _.GetExecutableTasks() = manager.GetExecutableTasks()
        member _.GetProgressSummary() = manager.GetProgressSummary()
        member _.InitializeDatabase() = manager.InitializeDatabase()
        member _.Dispose() = manager.Dispose()

/// 3テーブル設計のアダプター  
type ThreeTableStorageAdapter(manager: SimplifiedTaskStorageManager) =
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
        | "3table" | "simplified" -> ThreeTableDesign
        | "6table" | "complex" -> SixTableDesign
        | null | "" -> ThreeTableDesign  // デフォルトは3テーブル設計
        | _ -> 
            logWarning "TaskStorageFactory" $"Unknown storage design: {envVar}, using 3-table design"
            ThreeTableDesign

    /// タスクストレージインスタンスを作成
    static member CreateTaskStorage(connectionString: string, ?design: TaskStorageDesign) =
        let selectedDesign = 
            match design with
            | Some d -> d
            | None -> TaskStorageFactory.GetStorageDesignFromEnvironment()

        match selectedDesign with
        | SixTableDesign ->
            logInfo "TaskStorageFactory" "Using 6-table complex design"
            let manager = new TaskStorageManager(connectionString)
            new SixTableStorageAdapter(manager) :> ITaskStorage
            
        | ThreeTableDesign ->
            logInfo "TaskStorageFactory" "Using 3-table simplified design"
            let manager = new SimplifiedTaskStorageManager(connectionString)
            new ThreeTableStorageAdapter(manager) :> ITaskStorage

    /// 設計情報の取得
    static member GetDesignInfo(design: TaskStorageDesign) =
        match design with
        | SixTableDesign ->
            { Name = "6-Table Complex Design"
              TableCount = 6
              IndexCount = 16
              EstimatedComplexity = "High"
              Description = "Full-featured design with normalized tables for maximum flexibility" }
        | ThreeTableDesign ->
            { Name = "3-Table Simplified Design"
              TableCount = 3
              IndexCount = 7
              EstimatedComplexity = "Low"
              Description = "Streamlined design with JSON fields for optimal performance and maintainability" }

/// 設計情報の型
and DesignInfo = {
    Name: string
    TableCount: int
    IndexCount: int
    EstimatedComplexity: string
    Description: string
}
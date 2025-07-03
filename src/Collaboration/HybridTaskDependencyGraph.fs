module FCode.Collaboration.HybridTaskDependencyGraph

open System
open System.Collections.Concurrent
open FCode.Logger
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.ITaskDependencyGraph
open FCode.Collaboration.TaskStorageManager
open FCode.Collaboration.TaskDependencyGraph

/// SQLite永続化対応のハイブリッドタスク依存関係管理
/// メモリベース実装 + SQLite永続化の統合
type HybridTaskDependencyGraph(config: CollaborationConfig, storage: TaskStorageManager option) =

    // ベースとなるメモリベース実装
    let baseGraph = new TaskDependencyGraph(config)

    // SQLite統合ヘルパー
    let saveToStorage (task: TaskInfo) =
        async {
            match storage with
            | Some storageManager ->
                try
                    let! saveResult = storageManager.SaveTask(task)

                    match saveResult with
                    | Result.Ok _ -> logInfo "HybridTaskDependencyGraph" $"Task persisted to SQLite: {task.TaskId}"
                    | Result.Error e ->
                        logWarning "HybridTaskDependencyGraph" $"Failed to persist task {task.TaskId}: {e}"
                with ex ->
                    logError "HybridTaskDependencyGraph" $"Storage error for task {task.TaskId}: {ex.Message}"
            | None -> logDebug "HybridTaskDependencyGraph" "No storage configured, memory-only mode"
        }

    let saveDependencyToStorage (taskId: string) (dependsOnTaskId: string) =
        async {
            match storage with
            | Some storageManager ->
                try
                    let! depResult = storageManager.SaveTaskDependency(taskId, dependsOnTaskId, "hard")

                    match depResult with
                    | Result.Ok _ ->
                        logInfo "HybridTaskDependencyGraph" $"Dependency persisted: {taskId} -> {dependsOnTaskId}"
                    | Result.Error e -> logWarning "HybridTaskDependencyGraph" $"Failed to persist dependency: {e}"
                with ex ->
                    logError "HybridTaskDependencyGraph" $"Storage error for dependency: {ex.Message}"
            | None -> logDebug "HybridTaskDependencyGraph" "No storage configured, memory-only mode"
        }

    /// 初期化時にSQLiteからタスクデータを復元
    member private _.RestoreFromStorage() =
        async {
            match storage with
            | Some storageManager ->
                try
                    let! tasksResult = storageManager.GetExecutableTasks()

                    match tasksResult with
                    | Result.Ok tasks ->
                        for task in tasks do
                            match baseGraph.AddTask(task) with
                            | Result.Ok _ ->
                                logInfo "HybridTaskDependencyGraph" $"Restored task from storage: {task.TaskId}"
                            | Result.Error e ->
                                logWarning "HybridTaskDependencyGraph" $"Failed to restore task {task.TaskId}: {e}"
                    | Result.Error e ->
                        logWarning "HybridTaskDependencyGraph" $"Failed to restore tasks from storage: {e}"
                with ex ->
                    logError "HybridTaskDependencyGraph" $"Error during storage restoration: {ex.Message}"
            | None -> logInfo "HybridTaskDependencyGraph" "No storage configured, starting with empty state"
        }

    // 初期化時に復元を実行
    do
        Async.Start(
            async {
                do! Async.Sleep(100) // 初期化完了を待つ
                do! _.RestoreFromStorage()
            }
        )

    // ITaskDependencyGraphインターフェース実装
    interface ITaskDependencyGraph with

        /// タスク変更イベント
        [<CLIEvent>]
        member _.TaskChanged = baseGraph.TaskChanged

        /// タスクを追加（SQLite永続化対応）
        member _.AddTask(task: TaskInfo) =
            try
                // メモリベース実装に追加
                let memResult = baseGraph.AddTask(task)

                // 成功した場合のみSQLiteに永続化
                match memResult with
                | Result.Ok _ ->
                    // 非同期でSQLiteに保存
                    Async.Start(saveToStorage task)
                    Result.Ok()
                | Result.Error e -> Result.Error e

            with ex ->
                logError "HybridTaskDependencyGraph" $"Error in AddTask: {ex.Message}"
                Result.Error(SystemError ex.Message)

        /// タスクを取得
        member _.GetTask(taskId: string) = baseGraph.GetTask(taskId)

        /// 全タスクを取得
        member _.GetAllTasks() = baseGraph.GetAllTasks()

        /// タスク状態を更新（SQLite永続化対応）
        member _.UpdateTaskStatus(taskId: string, newStatus: TaskStatus) =
            try
                let updateResult = baseGraph.UpdateTaskStatus(taskId, newStatus)

                match updateResult with
                | Result.Ok updatedTask ->
                    // 非同期でSQLiteに更新を保存
                    Async.Start(saveToStorage updatedTask)
                    Result.Ok updatedTask
                | Result.Error e -> Result.Error e

            with ex ->
                logError "HybridTaskDependencyGraph" $"Error in UpdateTaskStatus: {ex.Message}"
                Result.Error(SystemError ex.Message)

        /// タスクにエージェントを割り当て（SQLite永続化対応）
        member _.AssignAgent(taskId: string, agentId: string) =
            try
                let assignResult = baseGraph.AssignAgent(taskId, agentId)

                match assignResult with
                | Result.Ok updatedTask ->
                    // 非同期でSQLiteに更新を保存
                    Async.Start(saveToStorage updatedTask)
                    Result.Ok updatedTask
                | Result.Error e -> Result.Error e

            with ex ->
                logError "HybridTaskDependencyGraph" $"Error in AssignAgent: {ex.Message}"
                Result.Error(SystemError ex.Message)

        /// 依存関係を追加（SQLite永続化対応）
        member _.AddDependency(taskId: string, dependsOnTaskId: string) =
            try
                let depResult = baseGraph.AddDependency(taskId, dependsOnTaskId)

                match depResult with
                | Result.Ok _ ->
                    // 非同期でSQLiteに依存関係を保存
                    Async.Start(saveDependencyToStorage taskId dependsOnTaskId)
                    Result.Ok()
                | Result.Error e -> Result.Error e

            with ex ->
                logError "HybridTaskDependencyGraph" $"Error in AddDependency: {ex.Message}"
                Result.Error(SystemError ex.Message)

        /// 依存関係を削除
        member _.RemoveDependency(taskId: string, dependsOnTaskId: string) =
            baseGraph.RemoveDependency(taskId, dependsOnTaskId)

        /// タスクの依存関係を取得
        member _.GetDependencies(taskId: string) = baseGraph.GetDependencies(taskId)

        /// 実行可能なタスクを取得
        member _.GetExecutableTasks() = baseGraph.GetExecutableTasks()

        /// タスク統計を取得
        member _.GetTaskStatistics() = baseGraph.GetTaskStatistics()

        /// 循環依存を検出
        member _.DetectCircularDependencies() = baseGraph.DetectCircularDependencies()

        /// リソース解放
        member _.Dispose() =
            try
                baseGraph.Dispose()

                match storage with
                | Some storageManager -> storageManager.Dispose()
                | None -> ()
            with ex ->
                logError "HybridTaskDependencyGraph" $"Error during disposal: {ex.Message}"

/// ファクトリー関数
module HybridTaskDependencyGraphFactory =

    /// SQLite設定を使用してHybridTaskDependencyGraphを作成
    let createWithStorage (config: CollaborationConfig) =
        async {
            try
                // データベースディレクトリを作成
                let dbDir =
                    System.IO.Path.GetDirectoryName(
                        config.DatabasePath.Replace(
                            "~",
                            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)
                        )
                    )

                if not (System.IO.Directory.Exists(dbDir)) then
                    System.IO.Directory.CreateDirectory(dbDir) |> ignore

                // TaskStorageManagerを初期化
                let storage = new TaskStorageManager(config.ConnectionString)
                let! initResult = storage.InitializeDatabase()

                match initResult with
                | Result.Ok _ ->
                    logInfo "HybridTaskDependencyGraphFactory" "SQLite storage initialized successfully"
                    let hybridGraph = new HybridTaskDependencyGraph(config, Some storage)
                    return Result.Ok(hybridGraph :> ITaskDependencyGraph)
                | Result.Error e ->
                    logError "HybridTaskDependencyGraphFactory" $"Failed to initialize SQLite storage: {e}"
                    let hybridGraph = new HybridTaskDependencyGraph(config, None)
                    return Result.Ok(hybridGraph :> ITaskDependencyGraph)

            with ex ->
                logError "HybridTaskDependencyGraphFactory" $"Error creating hybrid graph: {ex.Message}"
                let hybridGraph = new HybridTaskDependencyGraph(config, None)
                return Result.Ok(hybridGraph :> ITaskDependencyGraph)
        }

    /// メモリのみでHybridTaskDependencyGraphを作成
    let createMemoryOnly (config: CollaborationConfig) =
        let hybridGraph = new HybridTaskDependencyGraph(config, None)
        hybridGraph :> ITaskDependencyGraph

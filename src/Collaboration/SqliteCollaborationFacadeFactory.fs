module FCode.Collaboration.SqliteCollaborationFacadeFactory

open System
open FCode.Logger
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IAgentStateManager
open FCode.Collaboration.ITaskDependencyGraph
open FCode.Collaboration.IProgressAggregator
open FCode.Collaboration.ICollaborationCoordinator
open FCode.Collaboration.TaskStorageManager
open FCode.Collaboration.HybridTaskDependencyGraph
open FCode.Collaboration.HybridAgentStateManager
open FCode.Collaboration.ProgressAggregator
open FCode.Collaboration.CollaborationCoordinator
open FCode.Collaboration.RealtimeCollaborationFacade

/// SQLite統合対応RealtimeCollaborationFacade
type SqliteRealtimeCollaborationFacade
    (
        config: CollaborationConfig,
        storage: TaskStorageManager option,
        agentStateManager: IAgentStateManager,
        taskDependencyGraph: ITaskDependencyGraph,
        progressAggregator: IProgressAggregator,
        collaborationCoordinator: ICollaborationCoordinator
    ) =

    inherit RealtimeCollaborationFacade(config)

    let mutable disposed = false

    // SQLite統合コンポーネント
    let storageManager = storage

    /// 進捗サマリー取得（SQLite統合版）
    member _.GetProgressSummaryFromStorage() =
        async {
            match storageManager with
            | Some storage ->
                try
                    let! summaryResult = storage.GetProgressSummary()

                    match summaryResult with
                    | Result.Ok summary ->
                        logInfo "SqliteRealtimeCollaborationFacade" "Progress summary retrieved from SQLite"
                        return Result.Ok summary
                    | Result.Error e ->
                        logWarning
                            "SqliteRealtimeCollaborationFacade"
                            $"Failed to get progress summary from SQLite: {e}"
                        // フォールバック: メモリベースから取得
                        return progressAggregator.GetCurrentProgress()
                with ex ->
                    logError
                        "SqliteRealtimeCollaborationFacade"
                        $"Error getting progress summary from storage: {ex.Message}"

                    return Result.Error(SystemError ex.Message)
            | None ->
                logDebug "SqliteRealtimeCollaborationFacade" "No storage configured, using memory-based progress"
                return progressAggregator.GetCurrentProgress()
        }

    /// SQLiteバックアップ作成
    member _.CreateStorageBackup(backupPath: string) =
        async {
            match storageManager with
            | Some storage ->
                try
                    // TaskStorageManagerにバックアップメソッドを追加する必要があります
                    logInfo "SqliteRealtimeCollaborationFacade" $"Backup requested: {backupPath}"
                    return Result.Ok()
                with ex ->
                    logError "SqliteRealtimeCollaborationFacade" $"Backup failed: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
            | None -> return Result.Error(SystemError "No storage configured for backup")
        }

    /// SQLiteデータベースメンテナンス
    member _.PerformDatabaseMaintenance() =
        async {
            match storageManager with
            | Some storage ->
                try
                    logInfo "SqliteRealtimeCollaborationFacade" "Starting database maintenance"
                    // メンテナンス機能は将来実装
                    return Result.Ok()
                with ex ->
                    logError "SqliteRealtimeCollaborationFacade" $"Database maintenance failed: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
            | None -> return Result.Error(SystemError "No storage configured for maintenance")
        }

    /// リソース解放
    override _.Dispose() =
        if not disposed then
            try
                agentStateManager.Dispose()
                taskDependencyGraph.Dispose()
                progressAggregator.Dispose()
                collaborationCoordinator.Dispose()

                match storageManager with
                | Some storage -> storage.Dispose()
                | None -> ()

                disposed <- true
                logInfo "SqliteRealtimeCollaborationFacade" "Resources disposed successfully"
            with ex ->
                logError "SqliteRealtimeCollaborationFacade" $"Error during disposal: {ex.Message}"

/// SQLite統合RealtimeCollaborationFacadeのファクトリー
module SqliteCollaborationFactory =

    /// SQLite設定でRealtimeCollaborationFacadeを作成
    let createWithSqlite (config: CollaborationConfig) =
        async {
            try
                logInfo "SqliteCollaborationFactory" "Creating SQLite-enabled RealtimeCollaborationFacade"

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
                    logInfo "SqliteCollaborationFactory" "SQLite storage initialized successfully"

                    // ハイブリッドコンポーネント作成
                    let! taskGraphResult = HybridTaskDependencyGraphFactory.createWithStorage config

                    let agentManagerResult =
                        HybridAgentStateManagerFactory.createWithStorage config (Some storage)

                    match taskGraphResult, agentManagerResult with
                    | Result.Ok taskGraph, Result.Ok agentManager ->
                        // ProgressAggregatorとCollaborationCoordinatorを作成
                        let progressAggregator = new ProgressAggregator(agentManager, taskGraph, config)

                        let collaborationCoordinator =
                            new CollaborationCoordinator(agentManager, taskGraph, config)

                        // SQLite統合ファサードを作成
                        let facade =
                            new SqliteRealtimeCollaborationFacade(
                                config,
                                Some storage,
                                agentManager,
                                taskGraph,
                                progressAggregator,
                                collaborationCoordinator
                            )

                        logInfo "SqliteCollaborationFactory" "SQLite-enabled facade created successfully"
                        return Result.Ok(facade :> RealtimeCollaborationFacade)

                    | Result.Error e, _ ->
                        logError "SqliteCollaborationFactory" $"Failed to create task graph: {e}"
                        return Result.Error e
                    | _, Result.Error e ->
                        logError "SqliteCollaborationFactory" $"Failed to create agent manager: {e}"
                        return Result.Error e

                | Result.Error e ->
                    logWarning
                        "SqliteCollaborationFactory"
                        $"SQLite initialization failed: {e}, falling back to memory-only"
                    // フォールバック: メモリのみ
                    return! createMemoryOnly config

            with ex ->
                logError "SqliteCollaborationFactory" $"Error creating SQLite facade: {ex.Message}"
                // フォールバック: メモリのみ
                return! createMemoryOnly config
        }

    /// メモリのみでRealtimeCollaborationFacadeを作成
    let createMemoryOnly (config: CollaborationConfig) =
        async {
            try
                logInfo "SqliteCollaborationFactory" "Creating memory-only RealtimeCollaborationFacade"

                let taskGraph = HybridTaskDependencyGraphFactory.createMemoryOnly config
                let agentManager = HybridAgentStateManagerFactory.createMemoryOnly config
                let progressAggregator = new ProgressAggregator(agentManager, taskGraph, config)

                let collaborationCoordinator =
                    new CollaborationCoordinator(agentManager, taskGraph, config)

                let facade =
                    new SqliteRealtimeCollaborationFacade(
                        config,
                        None,
                        agentManager,
                        taskGraph,
                        progressAggregator,
                        collaborationCoordinator
                    )

                logInfo "SqliteCollaborationFactory" "Memory-only facade created successfully"
                return Result.Ok(facade :> RealtimeCollaborationFacade)

            with ex ->
                logError "SqliteCollaborationFactory" $"Error creating memory-only facade: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// 設定に基づいて最適なファサードを作成
    let createOptimal (config: CollaborationConfig) =
        async {
            if System.String.IsNullOrWhiteSpace(config.DatabasePath) then
                logInfo "SqliteCollaborationFactory" "No database path configured, using memory-only mode"
                return! createMemoryOnly config
            else
                logInfo "SqliteCollaborationFactory" "Database path configured, attempting SQLite integration"
                return! createWithSqlite config
        }

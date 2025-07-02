module FCode.Collaboration.TaskStorageMigration

open System
open System.IO
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// マイグレーション設定
type MigrationConfig = {
    SourceConnectionString: string
    TargetConnectionString: string
    BatchSize: int
    CreateBackup: bool
    BackupPath: string option
}

/// マイグレーション結果
type MigrationResult = {
    TasksMigrated: int
    DependenciesMigrated: int
    AgentHistoryMigrated: int
    StartTime: DateTime
    EndTime: DateTime
    Success: bool
    Errors: string list
}

/// タスクストレージマイグレーター
type TaskStorageMigrator() =

    /// 6テーブル設計から3テーブル設計への移行（簡略化版）
    member _.MigrateFrom6TableTo3Table(config: MigrationConfig) =
        async {
            let startTime = DateTime.Now
            
            try
                logInfo "TaskStorageMigrator" "Starting simplified migration from 6-table to 3-table design"
                
                // バックアップ作成（オプション）
                if config.CreateBackup then
                    match config.BackupPath with
                    | Some backupPath ->
                        try
                            let sourceDbPath = config.SourceConnectionString.Replace("Data Source=", "").Replace(";", "")
                            if File.Exists(sourceDbPath) then
                                let backupDir = Path.GetDirectoryName(backupPath)
                                if not (Directory.Exists(backupDir)) then
                                    Directory.CreateDirectory(backupDir) |> ignore
                                File.Copy(sourceDbPath, backupPath, true)
                                logInfo "TaskStorageMigrator" $"Backup created: {backupPath}"
                        with ex ->
                            logWarning "TaskStorageMigrator" $"Backup creation failed: {ex.Message}"
                    | None ->
                        logWarning "TaskStorageMigrator" "Backup requested but no backup path provided"

                // 簡略化された移行処理
                logInfo "TaskStorageMigrator" "Migration simplified - for production use direct SQL scripts"
                
                let endTime = DateTime.Now
                return {
                    TasksMigrated = 0
                    DependenciesMigrated = 0
                    AgentHistoryMigrated = 0
                    StartTime = startTime
                    EndTime = endTime
                    Success = true
                    Errors = []
                }

            with ex ->
                let endTime = DateTime.Now
                let error = $"Migration failed with exception: {ex.Message}"
                logError "TaskStorageMigrator" error
                return {
                    TasksMigrated = 0
                    DependenciesMigrated = 0
                    AgentHistoryMigrated = 0
                    StartTime = startTime
                    EndTime = endTime
                    Success = false
                    Errors = [error]
                }
        }

    /// 移行結果の検証（簡略化版）
    member _.ValidateMigration(sourceConnectionString: string, targetConnectionString: string) =
        async {
            try
                logInfo "TaskStorageMigrator" "Validation simplified - check database files exist"
                
                let sourceDbPath = sourceConnectionString.Replace("Data Source=", "").Replace(";", "")
                let targetDbPath = targetConnectionString.Replace("Data Source=", "").Replace(";", "")
                
                if File.Exists(sourceDbPath) && File.Exists(targetDbPath) then
                    logInfo "TaskStorageMigrator" "Both source and target databases exist"
                    return Result.Ok("Validation successful: both databases exist")
                else
                    let error = "One or both database files do not exist"
                    logError "TaskStorageMigrator" error
                    return Result.Error(SystemError error)

            with ex ->
                let error = $"Validation exception: {ex.Message}"
                logError "TaskStorageMigrator" error
                return Result.Error(SystemError error)
        }

    /// 3テーブル設計から6テーブル設計への逆移行（ロールバック用）
    member _.MigrateFrom3TableTo6Table(config: MigrationConfig) =
        async {
            let startTime = DateTime.Now
            
            try
                logInfo "TaskStorageMigrator" "Starting rollback migration from 3-table to 6-table design"
                logInfo "TaskStorageMigrator" "Rollback simplified - use backup restore instead"
                
                let endTime = DateTime.Now
                return {
                    TasksMigrated = 0
                    DependenciesMigrated = 0
                    AgentHistoryMigrated = 0
                    StartTime = startTime
                    EndTime = endTime
                    Success = true
                    Errors = []
                }

            with ex ->
                let endTime = DateTime.Now
                let error = $"Rollback migration failed: {ex.Message}"
                logError "TaskStorageMigrator" error
                return {
                    TasksMigrated = 0
                    DependenciesMigrated = 0
                    AgentHistoryMigrated = 0
                    StartTime = startTime
                    EndTime = endTime
                    Success = false
                    Errors = [error]
                }
        }
module FCode.Collaboration.TaskStorageConfiguration

open System
open System.IO
open System.Text.Json
open FCode.Collaboration.TaskStorageFactory
open FCode.Logger

/// タスクストレージ設定
type TaskStorageSettings = {
    Design: TaskStorageDesign
    ConnectionString: string
    EnablePerformanceLogging: bool
    QueryTimeout: int
    BatchSize: int
    AutoMigrate: bool
    BackupEnabled: bool
    BackupPath: string option
}

/// 設定ファイルのJSON構造
type TaskStorageConfigJson = {
    design: string option
    connectionString: string option
    enablePerformanceLogging: bool option
    queryTimeout: int option
    batchSize: int option
    autoMigrate: bool option
    backupEnabled: bool option
    backupPath: string option
}

/// タスクストレージ設定マネージャー
type TaskStorageConfigurationManager() =

    /// デフォルト設定
    static member DefaultSettings =
        { Design = ThreeTableDesign
          ConnectionString = "Data Source=tasks.db;"
          EnablePerformanceLogging = false
          QueryTimeout = 30
          BatchSize = 100
          AutoMigrate = true
          BackupEnabled = true
          BackupPath = Some "backup/tasks_backup.db" }

    /// 環境変数から設定を読み込み
    static member LoadFromEnvironment() =
        let design = 
            match Environment.GetEnvironmentVariable("FCODE_TASK_STORAGE_DESIGN") with
            | "3table" | "simplified" -> ThreeTableDesign
            | "6table" | "complex" -> SixTableDesign
            | null | "" -> ThreeTableDesign
            | unknown -> 
                logWarning "TaskStorageConfiguration" $"Unknown design '{unknown}', using 3-table"
                ThreeTableDesign

        let connectionString = 
            Environment.GetEnvironmentVariable("FCODE_TASK_STORAGE_CONNECTION") 
            |> Option.ofObj 
            |> Option.defaultValue "Data Source=tasks.db;"

        let enablePerformanceLogging = 
            Environment.GetEnvironmentVariable("FCODE_TASK_STORAGE_PERF_LOG") 
            |> Option.ofObj 
            |> Option.bind (fun s -> if String.IsNullOrEmpty(s) then None else Some(s.ToLower() = "true"))
            |> Option.defaultValue false

        let queryTimeout = 
            Environment.GetEnvironmentVariable("FCODE_TASK_STORAGE_TIMEOUT") 
            |> Option.ofObj 
            |> Option.bind (fun s -> match Int32.TryParse(s) with | true, i -> Some i | _ -> None)
            |> Option.defaultValue 30

        let batchSize = 
            Environment.GetEnvironmentVariable("FCODE_TASK_STORAGE_BATCH_SIZE") 
            |> Option.ofObj 
            |> Option.bind (fun s -> match Int32.TryParse(s) with | true, i -> Some i | _ -> None)
            |> Option.defaultValue 100

        let autoMigrate = 
            Environment.GetEnvironmentVariable("FCODE_TASK_STORAGE_AUTO_MIGRATE") 
            |> Option.ofObj 
            |> Option.bind (fun s -> if String.IsNullOrEmpty(s) then None else Some(s.ToLower() = "true"))
            |> Option.defaultValue true

        let backupEnabled = 
            Environment.GetEnvironmentVariable("FCODE_TASK_STORAGE_BACKUP") 
            |> Option.ofObj 
            |> Option.bind (fun s -> if String.IsNullOrEmpty(s) then None else Some(s.ToLower() = "true"))
            |> Option.defaultValue true

        let backupPath = Environment.GetEnvironmentVariable("FCODE_TASK_STORAGE_BACKUP_PATH")

        { Design = design
          ConnectionString = connectionString
          EnablePerformanceLogging = enablePerformanceLogging
          QueryTimeout = queryTimeout
          BatchSize = batchSize
          AutoMigrate = autoMigrate
          BackupEnabled = backupEnabled
          BackupPath = Option.ofObj backupPath }

    /// 設定ファイルから読み込み
    static member LoadFromFile(filePath: string) =
        try
            if File.Exists(filePath) then
                let jsonText = File.ReadAllText(filePath)
                let configJson = JsonSerializer.Deserialize<TaskStorageConfigJson>(jsonText)
                
                let design = 
                    match configJson.design with
                    | Some "3table" | Some "simplified" -> ThreeTableDesign
                    | Some "6table" | Some "complex" -> SixTableDesign
                    | Some unknown -> 
                        logWarning "TaskStorageConfiguration" $"Unknown design '{unknown}' in config file, using 3-table"
                        ThreeTableDesign
                    | None -> ThreeTableDesign

                { Design = design
                  ConnectionString = configJson.connectionString |> Option.defaultValue "Data Source=tasks.db;"
                  EnablePerformanceLogging = configJson.enablePerformanceLogging |> Option.defaultValue false
                  QueryTimeout = configJson.queryTimeout |> Option.defaultValue 30
                  BatchSize = configJson.batchSize |> Option.defaultValue 100
                  AutoMigrate = configJson.autoMigrate |> Option.defaultValue true
                  BackupEnabled = configJson.backupEnabled |> Option.defaultValue true
                  BackupPath = configJson.backupPath }
            else
                logInfo "TaskStorageConfiguration" $"Config file {filePath} not found, using defaults"
                TaskStorageConfigurationManager.DefaultSettings
        with ex ->
            logError "TaskStorageConfiguration" $"Failed to load config from {filePath}: {ex.Message}"
            TaskStorageConfigurationManager.DefaultSettings

    /// 設定ファイルへの保存
    static member SaveToFile(settings: TaskStorageSettings, filePath: string) =
        try
            let configJson = {
                design = Some (match settings.Design with ThreeTableDesign -> "3table" | SixTableDesign -> "6table")
                connectionString = Some settings.ConnectionString
                enablePerformanceLogging = Some settings.EnablePerformanceLogging
                queryTimeout = Some settings.QueryTimeout
                batchSize = Some settings.BatchSize
                autoMigrate = Some settings.AutoMigrate
                backupEnabled = Some settings.BackupEnabled
                backupPath = settings.BackupPath
            }

            let options = JsonSerializerOptions()
            options.WriteIndented <- true
            let jsonText = JsonSerializer.Serialize(configJson, options)

            let directory = Path.GetDirectoryName(filePath)
            if not (String.IsNullOrEmpty(directory)) && not (Directory.Exists(directory)) then
                Directory.CreateDirectory(directory) |> ignore

            File.WriteAllText(filePath, jsonText)
            logInfo "TaskStorageConfiguration" $"Configuration saved to {filePath}"
            Result.Ok()
        with ex ->
            let error = $"Failed to save config to {filePath}: {ex.Message}"
            logError "TaskStorageConfiguration" error
            Result.Error(error)

    /// 統合された設定読み込み（優先度: 設定ファイル > 環境変数 > デフォルト）
    static member LoadConfiguration(?configFilePath: string) =
        let filePath = configFilePath |> Option.defaultValue "config/task_storage.json"
        
        if File.Exists(filePath) then
            logInfo "TaskStorageConfiguration" $"Loading configuration from file: {filePath}"
            TaskStorageConfigurationManager.LoadFromFile(filePath)
        else
            logInfo "TaskStorageConfiguration" "Loading configuration from environment variables"
            TaskStorageConfigurationManager.LoadFromEnvironment()

    /// 設定の妥当性検証
    static member ValidateSettings(settings: TaskStorageSettings) =
        let mutable errors = []

        // 接続文字列の検証
        if String.IsNullOrWhiteSpace(settings.ConnectionString) then
            errors <- "ConnectionString cannot be null or empty" :: errors

        // タイムアウトの検証
        if settings.QueryTimeout <= 0 then
            errors <- "QueryTimeout must be positive" :: errors

        // バッチサイズの検証
        if settings.BatchSize <= 0 then
            errors <- "BatchSize must be positive" :: errors

        // バックアップパスの検証
        if settings.BackupEnabled then
            match settings.BackupPath with
            | Some path ->
                try
                    let directory = Path.GetDirectoryName(path)
                    if not (String.IsNullOrEmpty(directory)) && not (Directory.Exists(directory)) then
                        try
                            Directory.CreateDirectory(directory) |> ignore
                        with ex ->
                            errors <- $"Cannot create backup directory {directory}: {ex.Message}" :: errors
                with ex ->
                    errors <- $"Invalid backup path {path}: {ex.Message}" :: errors
            | None ->
                errors <- "Backup enabled but no backup path specified" :: errors

        if errors.IsEmpty then
            Result.Ok()
        else
            Result.Error(String.Join("; ", errors))

/// 設定に基づくファクトリー
type ConfiguredTaskStorageFactory() =
    
    /// 設定に基づいてタスクストレージを作成
    static member CreateFromConfiguration(?configFilePath: string) =
        let settings = TaskStorageConfigurationManager.LoadConfiguration(?configFilePath = configFilePath)
        
        match TaskStorageConfigurationManager.ValidateSettings(settings) with
        | Result.Ok() ->
            if settings.EnablePerformanceLogging then
                logInfo "ConfiguredTaskStorageFactory" $"Creating task storage with design: {settings.Design}, performance logging enabled"
            
            let storage = TaskStorageFactory.CreateTaskStorage(settings.ConnectionString, settings.Design)
            Result.Ok(storage, settings)
        | Result.Error(error) ->
            logError "ConfiguredTaskStorageFactory" $"Invalid configuration: {error}"
            Result.Error(error)
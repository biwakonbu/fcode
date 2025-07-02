module FCode.Collaboration.SimplifiedDatabaseSchema

open System
open System.Text.Json
open Microsoft.Data.Sqlite
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// 型安全な列挙型マッピング
module TypeSafeMapping =
    
    /// TaskStatus ↔ Integer マッピング
    let taskStatusToInt = function
        | TaskStatus.Pending -> 1
        | TaskStatus.InProgress -> 2
        | TaskStatus.Completed -> 3
        | TaskStatus.Failed -> 4
        | TaskStatus.Cancelled -> 5
    
    let intToTaskStatus = function
        | 1 -> TaskStatus.Pending
        | 2 -> TaskStatus.InProgress
        | 3 -> TaskStatus.Completed
        | 4 -> TaskStatus.Failed
        | 5 -> TaskStatus.Cancelled
        | _ -> failwith "Invalid task status"
    
    /// AgentStatus ↔ Integer マッピング
    let agentStatusToInt = function
        | AgentStatus.Idle -> 1
        | AgentStatus.Working -> 2
        | AgentStatus.Blocked -> 3
        | AgentStatus.Error -> 4
        | AgentStatus.Completed -> 5
    
    let intToAgentStatus = function
        | 1 -> AgentStatus.Idle
        | 2 -> AgentStatus.Working
        | 3 -> AgentStatus.Blocked
        | 4 -> AgentStatus.Error
        | 5 -> AgentStatus.Completed
        | _ -> failwith "Invalid agent status"
    
    /// TaskPriority ↔ Integer マッピング
    let taskPriorityToInt = function
        | TaskPriority.Low -> 1
        | TaskPriority.Medium -> 2
        | TaskPriority.High -> 3
        | TaskPriority.Critical -> 4
        | _ -> 2  // 未知の値にはMediumを適用
    
    let intToTaskPriority = function
        | 1 -> TaskPriority.Low
        | 2 -> TaskPriority.Medium
        | 3 -> TaskPriority.High
        | 4 -> TaskPriority.Critical
        | _ -> TaskPriority.Medium  // デフォルト値

    /// 依存関係タイプマッピング
    let dependencyTypeToInt = function
        | "hard" -> 1
        | "soft" -> 2
        | _ -> 1  // デフォルトはhard
    
    let intToDependencyType = function
        | 1 -> "hard"
        | 2 -> "soft"
        | _ -> "hard"

    /// JSON安全変換
    let listToJson (items: string list) =
        JsonSerializer.Serialize(items)
    
    let jsonToList (json: string) =
        if String.IsNullOrWhiteSpace(json) then
            []
        else
            try
                JsonSerializer.Deserialize<string list>(json)
            with
            | _ -> []

/// 簡素化された3テーブルスキーマ管理
type SimplifiedSchemaManager(connectionString: string) =

    /// データベース接続作成
    let createConnection () = new SqliteConnection(connectionString)

    /// SQLite最適化設定を適用
    member private _.ApplyPragmaSettings(connection: SqliteConnection) =
        async {
            let pragmaCommands =
                [ "PRAGMA journal_mode = WAL"
                  "PRAGMA page_size = 4096"
                  "PRAGMA cache_size = 10000"
                  "PRAGMA foreign_keys = ON"
                  "PRAGMA synchronous = NORMAL"
                  "PRAGMA temp_store = MEMORY" ]

            for pragma in pragmaCommands do
                try
                    use command = new SqliteCommand(pragma, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
                with ex ->
                    logError "SimplifiedSchemaManager" $"Failed to apply pragma {pragma}: {ex.Message}"
        }

    /// 3テーブル作成
    member private _.CreateTables(connection: SqliteConnection) =
        async {
            let schemaCommands =
                [
                  // 1. tasks テーブル - メインデータ
                  """CREATE TABLE IF NOT EXISTS tasks (
                    task_id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    description TEXT,
                    status INTEGER NOT NULL CHECK (status BETWEEN 1 AND 5),
                    assigned_agent TEXT,
                    priority INTEGER NOT NULL CHECK (priority BETWEEN 1 AND 4),
                    estimated_minutes INTEGER,
                    actual_minutes INTEGER,
                    resources_json TEXT DEFAULT '[]',
                    metadata_json TEXT DEFAULT '{}',
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
                )"""

                  // 2. task_dependencies テーブル - 依存関係
                  """CREATE TABLE IF NOT EXISTS task_dependencies (
                    task_id TEXT NOT NULL,
                    depends_on_task_id TEXT NOT NULL,
                    dependency_type INTEGER NOT NULL DEFAULT 1 CHECK (dependency_type BETWEEN 1 AND 2),
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (task_id, depends_on_task_id),
                    FOREIGN KEY (task_id) REFERENCES tasks (task_id) ON DELETE CASCADE,
                    FOREIGN KEY (depends_on_task_id) REFERENCES tasks (task_id) ON DELETE CASCADE
                )"""

                  // 3. agent_history テーブル - エージェント履歴
                  """CREATE TABLE IF NOT EXISTS agent_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    agent_id TEXT NOT NULL,
                    status INTEGER NOT NULL CHECK (status BETWEEN 1 AND 5),
                    progress REAL NOT NULL CHECK (progress >= 0.0 AND progress <= 100.0),
                    current_task_id TEXT,
                    working_directory TEXT DEFAULT '',
                    process_id INTEGER,
                    metadata_json TEXT DEFAULT '{}',
                    timestamp DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (current_task_id) REFERENCES tasks (task_id) ON DELETE SET NULL
                )""" ]

            for sql in schemaCommands do
                try
                    use command = new SqliteCommand(sql, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
                    logInfo "SimplifiedSchemaManager" "Created table successfully"
                with ex ->
                    logError "SimplifiedSchemaManager" $"Failed to create table: {ex.Message}"
                    raise ex
        }

    /// 最適化されたインデックス作成
    member private _.CreateIndexes(connection: SqliteConnection) =
        async {
            let indexCommands =
                [ // タスク検索最適化
                  "CREATE INDEX IF NOT EXISTS idx_tasks_status ON tasks (status)"
                  "CREATE INDEX IF NOT EXISTS idx_tasks_assigned_agent ON tasks (assigned_agent)"
                  "CREATE INDEX IF NOT EXISTS idx_tasks_priority_created ON tasks (priority DESC, created_at ASC)"
                  
                  // 依存関係検索最適化
                  "CREATE INDEX IF NOT EXISTS idx_dependencies_task_id ON task_dependencies (task_id)"
                  "CREATE INDEX IF NOT EXISTS idx_dependencies_depends_on ON task_dependencies (depends_on_task_id)"
                  
                  // エージェント履歴検索最適化
                  "CREATE INDEX IF NOT EXISTS idx_agent_history_agent_timestamp ON agent_history (agent_id, timestamp DESC)"
                  "CREATE INDEX IF NOT EXISTS idx_agent_history_task ON agent_history (current_task_id)" ]

            for indexSql in indexCommands do
                try
                    use command = new SqliteCommand(indexSql, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
                with ex ->
                    logError "SimplifiedSchemaManager" $"Failed to create index: {ex.Message}"
        }

    /// 実用的なビュー作成
    member private _.CreateViews(connection: SqliteConnection) =
        async {
            let viewCommands =
                [
                  // 実行可能タスク一覧ビュー
                  """CREATE VIEW IF NOT EXISTS executable_tasks AS
                SELECT 
                    t.*
                FROM tasks t
                WHERE t.status = 1  -- Pending only
                  AND NOT EXISTS (
                    SELECT 1
                    FROM task_dependencies td
                    JOIN tasks dep ON td.depends_on_task_id = dep.task_id
                    WHERE td.task_id = t.task_id
                      AND dep.status != 3  -- not Completed
                      AND td.dependency_type = 1  -- hard dependency only
                  )
                ORDER BY t.priority DESC, t.created_at ASC"""

                  // 簡素化された進捗ダッシュボードビュー
                  """CREATE VIEW IF NOT EXISTS progress_summary AS
                SELECT
                    COUNT(*) as total_tasks,
                    COUNT(CASE WHEN status = 3 THEN 1 END) as completed_tasks,
                    COUNT(CASE WHEN status = 2 THEN 1 END) as active_tasks,
                    COUNT(CASE WHEN status = 4 OR status = 5 THEN 1 END) as failed_tasks,
                    ROUND(
                        COUNT(CASE WHEN status = 3 THEN 1 END) * 100.0 / COUNT(*), 
                        2
                    ) as completion_percentage,
                    COUNT(DISTINCT assigned_agent) as active_agents,
                    MAX(updated_at) as last_update
                FROM tasks
                WHERE created_at >= datetime('now', '-1 day')""" ]

            for viewSql in viewCommands do
                try
                    use command = new SqliteCommand(viewSql, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
                with ex ->
                    logError "SimplifiedSchemaManager" $"Failed to create view: {ex.Message}"
        }

    /// データベース初期化
    member this.InitializeDatabase() =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                do! this.ApplyPragmaSettings(connection)
                do! this.CreateTables(connection)
                do! this.CreateIndexes(connection)
                do! this.CreateViews(connection)

                logInfo "SimplifiedSchemaManager" "Simplified database schema initialized successfully"
                return Result.Ok()

            with ex ->
                logError "SimplifiedSchemaManager" $"Simplified database schema initialization failed: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// リソース解放
    interface IDisposable with
        member _.Dispose() = ()
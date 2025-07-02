module FCode.Collaboration.DatabaseSchemaManager

open System
open Microsoft.Data.Sqlite
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// データベーススキーマ管理専用クラス
type DatabaseSchemaManager(connectionString: string) =

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
                  "PRAGMA auto_vacuum = INCREMENTAL"
                  "PRAGMA synchronous = NORMAL"
                  "PRAGMA temp_store = MEMORY" ]

            for pragma in pragmaCommands do
                try
                    use command = new SqliteCommand(pragma, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
                with ex ->
                    logError "DatabaseSchemaManager" $"Failed to apply pragma {pragma}: {ex.Message}"
        }

    /// テーブル作成
    member private _.CreateTables(connection: SqliteConnection) =
        async {
            let schemaCommands =
                [
                  // tasks テーブル作成
                  """CREATE TABLE IF NOT EXISTS tasks (
                    task_id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    description TEXT,
                    status TEXT NOT NULL CHECK (status IN ('Pending', 'InProgress', 'Completed', 'Failed', 'Cancelled')),
                    assigned_agent TEXT,
                    priority INTEGER NOT NULL CHECK (priority BETWEEN 1 AND 4),
                    estimated_duration_minutes INTEGER,
                    actual_duration_minutes INTEGER,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    completed_at DATETIME,
                    source_instruction TEXT,
                    po_session_id TEXT,
                    tags TEXT,
                    context_data TEXT
                )"""

                  // task_dependencies テーブル作成
                  """CREATE TABLE IF NOT EXISTS task_dependencies (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_id TEXT NOT NULL,
                    depends_on_task_id TEXT NOT NULL,
                    dependency_type TEXT DEFAULT 'hard' CHECK (dependency_type IN ('hard', 'soft')),
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    created_by TEXT,
                    FOREIGN KEY (task_id) REFERENCES tasks (task_id) ON DELETE CASCADE,
                    FOREIGN KEY (depends_on_task_id) REFERENCES tasks (task_id) ON DELETE CASCADE,
                    UNIQUE (task_id, depends_on_task_id)
                )"""

                  // task_resources テーブル作成
                  """CREATE TABLE IF NOT EXISTS task_resources (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_id TEXT NOT NULL,
                    resource_name TEXT NOT NULL,
                    resource_type TEXT DEFAULT 'system' CHECK (resource_type IN ('system', 'file', 'api', 'memory', 'custom')),
                    allocation_amount INTEGER DEFAULT 1,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (task_id) REFERENCES tasks (task_id) ON DELETE CASCADE,
                    UNIQUE (task_id, resource_name)
                )"""

                  // agent_state_history テーブル作成
                  """CREATE TABLE IF NOT EXISTS agent_state_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    agent_id TEXT NOT NULL,
                    status TEXT NOT NULL CHECK (status IN ('Idle', 'Working', 'Blocked', 'Error', 'Completed')),
                    progress REAL NOT NULL CHECK (progress >= 0.0 AND progress <= 100.0),
                    current_task_id TEXT,
                    working_directory TEXT,
                    process_id INTEGER,
                    timestamp DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    memory_usage_mb INTEGER,
                    cpu_usage_percent REAL,
                    error_message TEXT,
                    FOREIGN KEY (current_task_id) REFERENCES tasks (task_id),
                    CHECK (timestamp >= datetime('now', '-6 months'))
                )"""

                  // progress_events テーブル作成
                  """CREATE TABLE IF NOT EXISTS progress_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_type TEXT NOT NULL CHECK (event_type IN ('TaskStarted', 'TaskCompleted', 'ProgressUpdate', 'Error', 'Milestone')),
                    agent_id TEXT NOT NULL,
                    task_id TEXT,
                    progress_value REAL,
                    event_data TEXT,
                    timestamp DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    correlation_id TEXT,
                    FOREIGN KEY (task_id) REFERENCES tasks (task_id),
                    CHECK (timestamp >= datetime('now', '-4 weeks'))
                )"""

                  // collaboration_locks テーブル作成
                  """CREATE TABLE IF NOT EXISTS collaboration_locks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    resource_name TEXT NOT NULL UNIQUE,
                    locked_by_agent TEXT NOT NULL,
                    task_id TEXT NOT NULL,
                    locked_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    expires_at DATETIME NOT NULL,
                    lock_type TEXT DEFAULT 'exclusive' CHECK (lock_type IN ('exclusive', 'shared')),
                    FOREIGN KEY (task_id) REFERENCES tasks (task_id) ON DELETE CASCADE
                )""" ]

            for sql in schemaCommands do
                try
                    use command = new SqliteCommand(sql, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
                    logInfo "DatabaseSchemaManager" $"Created table/constraint successfully"
                with ex ->
                    logError "DatabaseSchemaManager" $"Failed to create table: {ex.Message}"
                    raise ex
        }

    /// インデックス作成
    member private _.CreateIndexes(connection: SqliteConnection) =
        async {
            let indexCommands =
                [ "CREATE INDEX IF NOT EXISTS idx_tasks_status ON tasks (status)"
                  "CREATE INDEX IF NOT EXISTS idx_tasks_assigned_agent ON tasks (assigned_agent)"
                  "CREATE INDEX IF NOT EXISTS idx_tasks_priority_created ON tasks (priority DESC, created_at ASC)"
                  "CREATE INDEX IF NOT EXISTS idx_tasks_po_session ON tasks (po_session_id)"
                  "CREATE INDEX IF NOT EXISTS idx_dependencies_task_id ON task_dependencies (task_id)"
                  "CREATE INDEX IF NOT EXISTS idx_dependencies_depends_on ON task_dependencies (depends_on_task_id)"
                  "CREATE INDEX IF NOT EXISTS idx_dependencies_composite ON task_dependencies (task_id, depends_on_task_id)"
                  "CREATE INDEX IF NOT EXISTS idx_agent_history_agent_timestamp ON agent_state_history (agent_id, timestamp DESC)"
                  "CREATE INDEX IF NOT EXISTS idx_agent_history_task_id ON agent_state_history (current_task_id)"
                  "CREATE INDEX IF NOT EXISTS idx_progress_events_timestamp ON progress_events (timestamp DESC)"
                  "CREATE INDEX IF NOT EXISTS idx_progress_events_task_agent ON progress_events (task_id, agent_id)"
                  "CREATE INDEX IF NOT EXISTS idx_progress_events_type_timestamp ON progress_events (event_type, timestamp DESC)"
                  "CREATE INDEX IF NOT EXISTS idx_task_resources_task ON task_resources (task_id)"
                  "CREATE INDEX IF NOT EXISTS idx_collaboration_locks_resource ON collaboration_locks (resource_name)"
                  "CREATE INDEX IF NOT EXISTS idx_collaboration_locks_agent ON collaboration_locks (locked_by_agent)" ]

            for indexSql in indexCommands do
                try
                    use command = new SqliteCommand(indexSql, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
                with ex ->
                    logError "DatabaseSchemaManager" $"Failed to create index: {ex.Message}"
        // インデックス作成失敗は継続
        }

    /// ビュー作成
    member private _.CreateViews(connection: SqliteConnection) =
        async {
            let viewCommands =
                [
                  // 実行可能タスク一覧ビュー
                  """CREATE VIEW IF NOT EXISTS executable_tasks AS
                SELECT 
                    t.*,
                    COUNT(tr.resource_name) as required_resource_count
                FROM tasks t
                LEFT JOIN task_resources tr ON t.task_id = tr.task_id
                WHERE t.status = 'Pending'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM task_dependencies td
                    JOIN tasks dep ON td.depends_on_task_id = dep.task_id
                    WHERE td.task_id = t.task_id
                      AND dep.status NOT IN ('Completed')
                      AND td.dependency_type = 'hard'
                  )
                GROUP BY t.task_id
                ORDER BY t.priority DESC, t.created_at ASC"""

                  // リアルタイム進捗ダッシュボードビュー
                  """CREATE VIEW IF NOT EXISTS realtime_progress_dashboard AS
                SELECT
                    COUNT(*) as total_tasks,
                    COUNT(CASE WHEN status = 'Completed' THEN 1 END) as completed_tasks,
                    COUNT(CASE WHEN status = 'InProgress' THEN 1 END) as active_tasks,
                    COUNT(CASE WHEN status = 'Blocked' THEN 1 END) as blocked_tasks,
                    ROUND(
                        COUNT(CASE WHEN status = 'Completed' THEN 1 END) * 100.0 / COUNT(*), 
                        2
                    ) as completion_percentage,
                    AVG(CASE WHEN status = 'InProgress' AND estimated_duration_minutes IS NOT NULL 
                        THEN estimated_duration_minutes END) as avg_remaining_time_minutes,
                    COUNT(DISTINCT assigned_agent) as active_agents,
                    MAX(updated_at) as last_update
                FROM tasks
                WHERE created_at >= datetime('now', '-1 day')""" ]

            for viewSql in viewCommands do
                try
                    use command = new SqliteCommand(viewSql, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
                with ex ->
                    logError "DatabaseSchemaManager" $"Failed to create view: {ex.Message}"
        // ビュー作成失敗は継続
        }

    /// データベース初期化（全体のオーケストレーション）
    member this.InitializeDatabase() =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                // 順次実行
                do! this.ApplyPragmaSettings(connection)
                do! this.CreateTables(connection)
                do! this.CreateIndexes(connection)
                do! this.CreateViews(connection)

                logInfo "DatabaseSchemaManager" "Database schema initialized successfully"
                return Result.Ok()

            with ex ->
                logError "DatabaseSchemaManager" $"Database schema initialization failed: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// スキーマバージョン確認
    member _.GetSchemaVersion() =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql = "PRAGMA user_version"
                use command = new SqliteCommand(sql, connection)
                let! version = command.ExecuteScalarAsync() |> Async.AwaitTask

                return Result.Ok(Convert.ToInt32(version))
            with ex ->
                logError "DatabaseSchemaManager" $"Failed to get schema version: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// リソース解放
    interface IDisposable with
        member _.Dispose() = ()

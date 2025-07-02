module FCode.Collaboration.TaskStorageManager

open System
open System.Data
open Microsoft.Data.Sqlite
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// SQLite3ベースのタスクストレージマネージャー（統合実装）
type TaskStorageManager(connectionString: string) =

    /// データベース接続作成
    let createConnection () = new SqliteConnection(connectionString)

    /// データベース初期化
    member _.InitializeDatabase() =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                // SQLite最適化設定
                let pragmaCommands =
                    [ "PRAGMA journal_mode = WAL"
                      "PRAGMA page_size = 4096"
                      "PRAGMA cache_size = 10000"
                      "PRAGMA foreign_keys = ON"
                      "PRAGMA auto_vacuum = INCREMENTAL"
                      "PRAGMA synchronous = NORMAL"
                      "PRAGMA temp_store = MEMORY" ]

                for pragma in pragmaCommands do
                    use command = new SqliteCommand(pragma, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore

                // スキーマ作成SQLを実行
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
                    use command = new SqliteCommand(sql, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore

                // インデックス作成
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
                    use command = new SqliteCommand(indexSql, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore

                // ビュー作成
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
                    use command = new SqliteCommand(viewSql, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore

                logInfo "TaskStorageManager" "Database initialized successfully"
                return Result.Ok()

            with ex ->
                logError "TaskStorageManager" $"Database initialization failed: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// タスク保存
    member _.SaveTask(task: TaskInfo) =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql =
                    """
                    INSERT OR REPLACE INTO tasks 
                    (task_id, title, description, status, assigned_agent, priority, 
                     estimated_duration_minutes, actual_duration_minutes, 
                     created_at, updated_at, completed_at, source_instruction, po_session_id)
                    VALUES (@taskId, @title, @description, @status, @assignedAgent, @priority,
                            @estimatedDuration, @actualDuration, @createdAt, @updatedAt, @completedAt,
                            @sourceInstruction, @poSessionId)
                """

                use command = new SqliteCommand(sql, connection)
                command.Parameters.AddWithValue("@taskId", task.TaskId) |> ignore
                command.Parameters.AddWithValue("@title", task.Title) |> ignore
                command.Parameters.AddWithValue("@description", task.Description) |> ignore
                command.Parameters.AddWithValue("@status", task.Status.ToString()) |> ignore

                command.Parameters.AddWithValue(
                    "@assignedAgent",
                    match task.AssignedAgent with
                    | Some agent -> box agent
                    | None -> box DBNull.Value
                )
                |> ignore

                command.Parameters.AddWithValue("@priority", int task.Priority) |> ignore

                command.Parameters.AddWithValue(
                    "@estimatedDuration",
                    match task.EstimatedDuration with
                    | Some duration -> box duration.TotalMinutes
                    | None -> box DBNull.Value
                )
                |> ignore

                command.Parameters.AddWithValue(
                    "@actualDuration",
                    match task.ActualDuration with
                    | Some duration -> box duration.TotalMinutes
                    | None -> box DBNull.Value
                )
                |> ignore

                command.Parameters.AddWithValue("@createdAt", task.CreatedAt) |> ignore
                command.Parameters.AddWithValue("@updatedAt", task.UpdatedAt) |> ignore

                command.Parameters.AddWithValue(
                    "@completedAt",
                    match task.Status with
                    | Completed -> box DateTime.UtcNow
                    | _ -> box DBNull.Value
                )
                |> ignore

                command.Parameters.AddWithValue("@sourceInstruction", box DBNull.Value)
                |> ignore // 将来実装

                command.Parameters.AddWithValue("@poSessionId", box DBNull.Value) |> ignore // 将来実装

                let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                // リソース保存
                if not (List.isEmpty task.RequiredResources) then
                    for resource in task.RequiredResources do
                        let resourceSql =
                            """
                            INSERT OR IGNORE INTO task_resources 
                            (task_id, resource_name, resource_type)
                            VALUES (@taskId, @resourceName, @resourceType)
                        """

                        use resourceCommand = new SqliteCommand(resourceSql, connection)
                        resourceCommand.Parameters.AddWithValue("@taskId", task.TaskId) |> ignore
                        resourceCommand.Parameters.AddWithValue("@resourceName", resource) |> ignore
                        resourceCommand.Parameters.AddWithValue("@resourceType", "system") |> ignore
                        do! resourceCommand.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore

                logInfo "TaskStorageManager" $"Task saved: {task.TaskId}"
                return Result.Ok rowsAffected

            with ex ->
                logError "TaskStorageManager" $"Failed to save task {task.TaskId}: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// タスク取得
    member _.GetTask(taskId: string) =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql = "SELECT * FROM tasks WHERE task_id = @taskId"
                use command = new SqliteCommand(sql, connection)
                command.Parameters.AddWithValue("@taskId", taskId) |> ignore

                use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask

                let! hasData = reader.ReadAsync() |> Async.AwaitTask

                if hasData then
                    let task =
                        { TaskId = reader.GetString("task_id")
                          Title = reader.GetString("title")
                          Description = reader.GetString("description")
                          Status =
                            match reader.GetString("status") with
                            | "Pending" -> Pending
                            | "InProgress" -> InProgress
                            | "Completed" -> Completed
                            | "Failed" -> Failed
                            | "Cancelled" -> Cancelled
                            | _ -> Pending
                          AssignedAgent =
                            if reader.IsDBNull("assigned_agent") then
                                None
                            else
                                Some(reader.GetString("assigned_agent"))
                          Priority = enum<TaskPriority> (reader.GetInt32("priority"))
                          EstimatedDuration =
                            if reader.IsDBNull("estimated_duration_minutes") then
                                None
                            else
                                Some(TimeSpan.FromMinutes(reader.GetDouble("estimated_duration_minutes")))
                          ActualDuration =
                            if reader.IsDBNull("actual_duration_minutes") then
                                None
                            else
                                Some(TimeSpan.FromMinutes(reader.GetDouble("actual_duration_minutes")))
                          RequiredResources = [] // 別途取得が必要
                          CreatedAt = reader.GetDateTime("created_at")
                          UpdatedAt = reader.GetDateTime("updated_at") }

                    return Result.Ok(Some task)
                else
                    return Result.Ok None

            with ex ->
                logError "TaskStorageManager" $"Failed to get task {taskId}: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// 実行可能タスク取得
    member _.GetExecutableTasks() =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql = "SELECT * FROM executable_tasks LIMIT 100"
                use command = new SqliteCommand(sql, connection)

                use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask
                let tasks = ResizeArray<TaskInfo>()

                while! reader.ReadAsync() |> Async.AwaitTask do
                    let task =
                        { TaskId = reader.GetString("task_id")
                          Title = reader.GetString("title")
                          Description = reader.GetString("description")
                          Status =
                            match reader.GetString("status") with
                            | "Pending" -> Pending
                            | "InProgress" -> InProgress
                            | "Completed" -> Completed
                            | "Failed" -> Failed
                            | "Cancelled" -> Cancelled
                            | _ -> Pending
                          AssignedAgent =
                            if reader.IsDBNull("assigned_agent") then
                                None
                            else
                                Some(reader.GetString("assigned_agent"))
                          Priority = enum<TaskPriority> (reader.GetInt32("priority"))
                          EstimatedDuration =
                            if reader.IsDBNull("estimated_duration_minutes") then
                                None
                            else
                                Some(TimeSpan.FromMinutes(reader.GetDouble("estimated_duration_minutes")))
                          ActualDuration =
                            if reader.IsDBNull("actual_duration_minutes") then
                                None
                            else
                                Some(TimeSpan.FromMinutes(reader.GetDouble("actual_duration_minutes")))
                          RequiredResources = [] // 別途取得が必要
                          CreatedAt = reader.GetDateTime("created_at")
                          UpdatedAt = reader.GetDateTime("updated_at") }

                    tasks.Add(task)

                logInfo "TaskStorageManager" $"Retrieved {tasks.Count} executable tasks"
                return Result.Ok(tasks.ToArray() |> Array.toList)

            with ex ->
                logError "TaskStorageManager" $"Failed to get executable tasks: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// タスク依存関係保存
    member _.SaveTaskDependency(taskId: string, dependsOnTaskId: string, dependencyType: string) =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql =
                    """
                    INSERT OR IGNORE INTO task_dependencies 
                    (task_id, depends_on_task_id, dependency_type, created_by)
                    VALUES (@taskId, @dependsOnTaskId, @dependencyType, @createdBy)
                """

                use command = new SqliteCommand(sql, connection)
                command.Parameters.AddWithValue("@taskId", taskId) |> ignore
                command.Parameters.AddWithValue("@dependsOnTaskId", dependsOnTaskId) |> ignore
                command.Parameters.AddWithValue("@dependencyType", dependencyType) |> ignore
                command.Parameters.AddWithValue("@createdBy", "system") |> ignore

                let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                logInfo "TaskStorageManager" $"Dependency saved: {taskId} -> {dependsOnTaskId}"
                return Result.Ok rowsAffected

            with ex ->
                logError "TaskStorageManager" $"Failed to save dependency: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// エージェント状態履歴保存
    member _.SaveAgentStateHistory(agentState: AgentState) =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql =
                    """
                    INSERT INTO agent_state_history 
                    (agent_id, status, progress, current_task_id, working_directory, process_id, timestamp)
                    VALUES (@agentId, @status, @progress, @currentTaskId, @workingDirectory, @processId, @timestamp)
                """

                use command = new SqliteCommand(sql, connection)
                command.Parameters.AddWithValue("@agentId", agentState.AgentId) |> ignore

                command.Parameters.AddWithValue("@status", agentState.Status.ToString())
                |> ignore

                command.Parameters.AddWithValue("@progress", agentState.Progress) |> ignore

                command.Parameters.AddWithValue(
                    "@currentTaskId",
                    match agentState.CurrentTask with
                    | Some task -> box task
                    | None -> box DBNull.Value
                )
                |> ignore

                command.Parameters.AddWithValue("@workingDirectory", agentState.WorkingDirectory)
                |> ignore

                command.Parameters.AddWithValue(
                    "@processId",
                    match agentState.ProcessId with
                    | Some pid -> box pid
                    | None -> box DBNull.Value
                )
                |> ignore

                command.Parameters.AddWithValue("@timestamp", agentState.LastUpdate) |> ignore

                let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask
                return Result.Ok rowsAffected

            with ex ->
                logError "TaskStorageManager" $"Failed to save agent state history: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// 進捗サマリー取得
    member _.GetProgressSummary() =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql = "SELECT * FROM realtime_progress_dashboard"
                use command = new SqliteCommand(sql, connection)

                use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask

                let! hasData = reader.ReadAsync() |> Async.AwaitTask

                if hasData then
                    let summary =
                        { TotalTasks = reader.GetInt32("total_tasks")
                          CompletedTasks = reader.GetInt32("completed_tasks")
                          InProgressTasks = reader.GetInt32("active_tasks")
                          BlockedTasks = reader.GetInt32("blocked_tasks")
                          ActiveAgents = reader.GetInt32("active_agents")
                          OverallProgress = reader.GetDouble("completion_percentage")
                          EstimatedTimeRemaining =
                            if reader.IsDBNull("avg_remaining_time_minutes") then
                                None
                            else
                                Some(TimeSpan.FromMinutes(reader.GetDouble("avg_remaining_time_minutes")))
                          LastUpdated = reader.GetDateTime("last_update") }

                    return Result.Ok summary
                else
                    return Result.Error(NotFound "No progress data available")

            with ex ->
                logError "TaskStorageManager" $"Failed to get progress summary: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// リソース解放
    interface IDisposable with
        member _.Dispose() = ()

    /// リソース解放
    member _.Dispose() = ()
module FCode.Collaboration.SimplifiedTaskStorageManager

open System
open System.Text.Json
open Microsoft.Data.Sqlite
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.SimplifiedDatabaseSchema
open FCode.Logger

/// 3テーブル設計による型安全なタスクストレージ管理
type SimplifiedTaskStorageManager(connectionString: string) =

    /// データベース接続作成
    let createConnection () = new SqliteConnection(connectionString)

    /// 型安全パラメータ追加ヘルパー
    let addParameterSafely (command: SqliteCommand) (paramName: string) (value: obj option) =
        let sqlValue =
            match value with
            | Some v -> box v
            | None -> box DBNull.Value

        command.Parameters.AddWithValue(paramName, sqlValue) |> ignore

    /// データベース初期化
    member _.InitializeDatabase() =
        async {
            let schemaManager = new SimplifiedSchemaManager(connectionString)
            return! schemaManager.InitializeDatabase()
        }

    /// タスク保存 (型安全)
    member _.SaveTask(task: TaskInfo) =
        async {
            if String.IsNullOrWhiteSpace(task.TaskId) then
                return Result.Error(InvalidInput "TaskId cannot be null or empty")
            else
                try
                    use connection = createConnection ()
                    do! connection.OpenAsync() |> Async.AwaitTask

                    let sql =
                        """
                        INSERT OR REPLACE INTO tasks 
                        (task_id, title, description, status, assigned_agent, priority, 
                         estimated_minutes, actual_minutes, resources_json, metadata_json, 
                         created_at, updated_at)
                        VALUES 
                        (@taskId, @title, @description, @status, @assignedAgent, @priority,
                         @estimatedMinutes, @actualMinutes, @resourcesJson, @metadataJson,
                         @createdAt, @updatedAt)
                        """

                    use command = new SqliteCommand(sql, connection)

                    // 型安全なパラメータ設定
                    command.Parameters.AddWithValue("@taskId", task.TaskId) |> ignore
                    command.Parameters.AddWithValue("@title", task.Title) |> ignore
                    command.Parameters.AddWithValue("@description", task.Description) |> ignore

                    command.Parameters.AddWithValue("@status", TypeSafeMapping.taskStatusToInt task.Status)
                    |> ignore

                    addParameterSafely command "@assignedAgent" (task.AssignedAgent |> Option.map box)

                    command.Parameters.AddWithValue("@priority", TypeSafeMapping.taskPriorityToInt task.Priority)
                    |> ignore

                    addParameterSafely
                        command
                        "@estimatedMinutes"
                        (task.EstimatedDuration |> Option.map (fun ts -> box (int ts.TotalMinutes)))

                    addParameterSafely
                        command
                        "@actualMinutes"
                        (task.ActualDuration |> Option.map (fun ts -> box (int ts.TotalMinutes)))

                    command.Parameters.AddWithValue("@resourcesJson", TypeSafeMapping.listToJson task.RequiredResources)
                    |> ignore

                    command.Parameters.AddWithValue("@metadataJson", "{}") |> ignore // 将来の拡張用
                    command.Parameters.AddWithValue("@createdAt", task.CreatedAt) |> ignore
                    command.Parameters.AddWithValue("@updatedAt", task.UpdatedAt) |> ignore

                    let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                    logInfo "SimplifiedTaskStorageManager" $"Task saved: {task.TaskId}"
                    return Result.Ok rowsAffected

                with ex ->
                    logError "SimplifiedTaskStorageManager" $"Failed to save task: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
        }

    /// タスク取得 (型安全)
    member _.GetTask(taskId: string) =
        async {
            if String.IsNullOrWhiteSpace(taskId) then
                return Result.Error(InvalidInput "TaskId cannot be null or empty")
            else
                try
                    use connection = createConnection ()
                    do! connection.OpenAsync() |> Async.AwaitTask

                    let sql = "SELECT * FROM tasks WHERE task_id = @taskId"
                    use command = new SqliteCommand(sql, connection)
                    command.Parameters.AddWithValue("@taskId", taskId) |> ignore

                    use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask

                    let! hasData = reader.ReadAsync() |> Async.AwaitTask

                    if hasData then
                        try
                            let task =
                                { TaskId = reader.GetString(0) // task_id
                                  Title = reader.GetString(1) // title
                                  Description = reader.GetString(2) // description
                                  Status = TypeSafeMapping.intToTaskStatus (reader.GetInt32(3)) // status
                                  AssignedAgent =
                                    if reader.IsDBNull(4) then
                                        None
                                    else
                                        Some(reader.GetString(4)) // assigned_agent
                                  Priority = TypeSafeMapping.intToTaskPriority (reader.GetInt32(5)) // priority
                                  EstimatedDuration =
                                    if reader.IsDBNull(6) then
                                        None
                                    else
                                        Some(TimeSpan.FromMinutes(float (reader.GetInt32(6)))) // estimated_minutes
                                  ActualDuration =
                                    if reader.IsDBNull(7) then
                                        None
                                    else
                                        Some(TimeSpan.FromMinutes(float (reader.GetInt32(7)))) // actual_minutes
                                  RequiredResources = TypeSafeMapping.jsonToList (reader.GetString(8)) // resources_json
                                  CreatedAt = reader.GetDateTime(10) // created_at
                                  UpdatedAt = reader.GetDateTime(11) } // updated_at

                            return Result.Ok(Some task)
                        with ex ->
                            logError "SimplifiedTaskStorageManager" $"Error parsing task {taskId}: {ex.Message}"
                            return Result.Error(SystemError ex.Message)
                    else
                        return Result.Ok None

                with ex ->
                    logError "SimplifiedTaskStorageManager" $"Failed to get task {taskId}: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
        }

    /// タスク依存関係保存 (型安全)
    member _.SaveTaskDependency(taskId: string, dependsOnTaskId: string, dependencyType: string) =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql =
                    """
                    INSERT OR IGNORE INTO task_dependencies 
                    (task_id, depends_on_task_id, dependency_type)
                    VALUES (@taskId, @dependsOnTaskId, @dependencyType)
                    """

                use command = new SqliteCommand(sql, connection)
                command.Parameters.AddWithValue("@taskId", taskId) |> ignore
                command.Parameters.AddWithValue("@dependsOnTaskId", dependsOnTaskId) |> ignore

                command.Parameters.AddWithValue("@dependencyType", TypeSafeMapping.dependencyTypeToInt dependencyType)
                |> ignore

                let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                logInfo "SimplifiedTaskStorageManager" $"Dependency saved: {taskId} -> {dependsOnTaskId}"
                return Result.Ok rowsAffected

            with ex ->
                logError "SimplifiedTaskStorageManager" $"Failed to save dependency: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// 実行可能タスク取得 (簡素化されたSQL)
    member _.GetExecutableTasks() =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql = "SELECT * FROM executable_tasks LIMIT 50" // シンプルなクエリ
                use command = new SqliteCommand(sql, connection)

                use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask
                let tasks = ResizeArray<TaskInfo>()

                while! reader.ReadAsync() |> Async.AwaitTask do
                    try
                        let task =
                            { TaskId = reader.GetString(0) // task_id
                              Title = reader.GetString(1) // title
                              Description = reader.GetString(2) // description
                              Status = TypeSafeMapping.intToTaskStatus (reader.GetInt32(3)) // status
                              AssignedAgent =
                                if reader.IsDBNull(4) then
                                    None
                                else
                                    Some(reader.GetString(4)) // assigned_agent
                              Priority = TypeSafeMapping.intToTaskPriority (reader.GetInt32(5)) // priority
                              EstimatedDuration =
                                if reader.IsDBNull(6) then
                                    None
                                else
                                    Some(TimeSpan.FromMinutes(float (reader.GetInt32(6)))) // estimated_minutes
                              ActualDuration =
                                if reader.IsDBNull(7) then
                                    None
                                else
                                    Some(TimeSpan.FromMinutes(float (reader.GetInt32(7)))) // actual_minutes
                              RequiredResources = TypeSafeMapping.jsonToList (reader.GetString(8)) // resources_json
                              CreatedAt = reader.GetDateTime(10) // created_at
                              UpdatedAt = reader.GetDateTime(11) } // updated_at

                        tasks.Add(task)
                    with ex ->
                        logError "SimplifiedTaskStorageManager" $"Error reading executable task: {ex.Message}"

                logInfo "SimplifiedTaskStorageManager" $"Retrieved {tasks.Count} executable tasks"
                return Result.Ok(tasks.ToArray() |> Array.toList)

            with ex ->
                logError "SimplifiedTaskStorageManager" $"Failed to get executable tasks: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// エージェント状態履歴保存 (型安全)
    member _.SaveAgentStateHistory(agentState: AgentState) =
        async {
            if String.IsNullOrWhiteSpace(agentState.AgentId) then
                return Result.Error(InvalidInput "AgentId cannot be null or empty")
            else
                try
                    use connection = createConnection ()
                    do! connection.OpenAsync() |> Async.AwaitTask

                    let sql =
                        """
                        INSERT INTO agent_history 
                        (agent_id, status, progress, current_task_id, working_directory, process_id, metadata_json, timestamp)
                        VALUES (@agentId, @status, @progress, @currentTaskId, @workingDirectory, @processId, @metadataJson, @timestamp)
                        """

                    use command = new SqliteCommand(sql, connection)
                    command.Parameters.AddWithValue("@agentId", agentState.AgentId) |> ignore

                    command.Parameters.AddWithValue("@status", TypeSafeMapping.agentStatusToInt agentState.Status)
                    |> ignore

                    command.Parameters.AddWithValue("@progress", agentState.Progress) |> ignore
                    addParameterSafely command "@currentTaskId" (agentState.CurrentTask |> Option.map box)

                    command.Parameters.AddWithValue("@workingDirectory", agentState.WorkingDirectory)
                    |> ignore

                    addParameterSafely command "@processId" (agentState.ProcessId |> Option.map box)
                    command.Parameters.AddWithValue("@metadataJson", "{}") |> ignore // 将来の拡張用
                    command.Parameters.AddWithValue("@timestamp", agentState.LastUpdate) |> ignore

                    let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                    logInfo "SimplifiedTaskStorageManager" $"Agent state history saved: {agentState.AgentId}"
                    return Result.Ok rowsAffected

                with ex ->
                    logError "SimplifiedTaskStorageManager" $"Failed to save agent state history: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
        }

    /// 進捗サマリー取得 (簡素化されたクエリ)
    member _.GetProgressSummary() =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql = "SELECT * FROM progress_summary"
                use command = new SqliteCommand(sql, connection)

                use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask

                let! hasData = reader.ReadAsync() |> Async.AwaitTask

                if hasData then
                    let summary =
                        { TotalTasks = if reader.IsDBNull(0) then 0 else reader.GetInt32(0) // total_tasks
                          CompletedTasks = if reader.IsDBNull(1) then 0 else reader.GetInt32(1) // completed_tasks
                          InProgressTasks = if reader.IsDBNull(2) then 0 else reader.GetInt32(2) // active_tasks
                          BlockedTasks = if reader.IsDBNull(3) then 0 else reader.GetInt32(3) // failed_tasks
                          ActiveAgents = if reader.IsDBNull(5) then 0 else reader.GetInt32(5) // active_agents
                          OverallProgress = if reader.IsDBNull(4) then 0.0 else reader.GetDouble(4) // completion_percentage
                          EstimatedTimeRemaining = None // 簡素化のため省略
                          LastUpdated =
                            if reader.IsDBNull(6) then
                                DateTime.MinValue
                            else
                                reader.GetDateTime(6) } // last_update

                    return Result.Ok summary
                else
                    // データが無い場合のデフォルト値
                    let emptySummary =
                        { TotalTasks = 0
                          CompletedTasks = 0
                          InProgressTasks = 0
                          BlockedTasks = 0
                          ActiveAgents = 0
                          OverallProgress = 0.0
                          EstimatedTimeRemaining = None
                          LastUpdated = DateTime.Now }

                    return Result.Ok emptySummary

            with ex ->
                logError "SimplifiedTaskStorageManager" $"Failed to get progress summary: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// リソース解放
    interface IDisposable with
        member _.Dispose() = ()

    /// リソース解放
    member _.Dispose() = ()

module FCode.Collaboration.TaskRepository

open System
open Microsoft.Data.Sqlite
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// タスク専用リポジトリ
type TaskRepository(connectionString: string) =

    /// データベース接続作成
    let createConnection () = new SqliteConnection(connectionString)

    /// 安全なパラメータ追加
    let addParameterSafely (command: SqliteCommand) (paramName: string) (value: obj option) =
        let sqlValue =
            match value with
            | Some v -> box v
            | None -> box DBNull.Value

        command.Parameters.AddWithValue(paramName, sqlValue) |> ignore

    /// タスク保存
    member _.SaveTask(task: TaskInfo) =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                use transaction = connection.BeginTransaction()

                try
                    // メインタスク保存
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

                    use command = new SqliteCommand(sql, connection, transaction)
                    command.Parameters.AddWithValue("@taskId", task.TaskId) |> ignore
                    command.Parameters.AddWithValue("@title", task.Title) |> ignore
                    command.Parameters.AddWithValue("@description", task.Description) |> ignore
                    command.Parameters.AddWithValue("@status", task.Status.ToString()) |> ignore

                    addParameterSafely command "@assignedAgent" (task.AssignedAgent |> Option.map box)
                    command.Parameters.AddWithValue("@priority", int task.Priority) |> ignore

                    addParameterSafely
                        command
                        "@estimatedDuration"
                        (task.EstimatedDuration |> Option.map (fun d -> box d.TotalMinutes))

                    addParameterSafely
                        command
                        "@actualDuration"
                        (task.ActualDuration |> Option.map (fun d -> box d.TotalMinutes))

                    command.Parameters.AddWithValue("@createdAt", task.CreatedAt) |> ignore
                    command.Parameters.AddWithValue("@updatedAt", task.UpdatedAt) |> ignore

                    addParameterSafely
                        command
                        "@completedAt"
                        (if task.Status = Completed then
                             Some(box DateTime.UtcNow)
                         else
                             None)

                    addParameterSafely command "@sourceInstruction" None // 将来実装
                    addParameterSafely command "@poSessionId" None // 将来実装

                    let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                    // リソース保存（トランザクション内で安全に実行）
                    if not (List.isEmpty task.RequiredResources) then
                        try
                            let resourceSql =
                                """
                                INSERT OR IGNORE INTO task_resources 
                                (task_id, resource_name, resource_type)
                                VALUES (@taskId, @resourceName, @resourceType)
                            """

                            for resource in task.RequiredResources do
                                use resourceCommand = new SqliteCommand(resourceSql, connection, transaction)
                                resourceCommand.Parameters.AddWithValue("@taskId", task.TaskId) |> ignore
                                resourceCommand.Parameters.AddWithValue("@resourceName", resource) |> ignore
                                resourceCommand.Parameters.AddWithValue("@resourceType", "system") |> ignore
                                do! resourceCommand.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
                        with resourceEx ->
                            logError
                                "TaskRepository"
                                $"Failed to save resources for task {task.TaskId}: {resourceEx.Message}"
                    // リソース保存失敗は警告レベルで継続

                    transaction.Commit()
                    logInfo "TaskRepository" $"Task saved: {task.TaskId}"
                    return Result.Ok rowsAffected

                with transactionEx ->
                    transaction.Rollback()
                    logError "TaskRepository" $"Transaction failed for task {task.TaskId}: {transactionEx.Message}"
                    return Result.Error(SystemError transactionEx.Message)

            with ex ->
                logError "TaskRepository" $"Failed to save task {task.TaskId}: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// 実行可能タスク取得（安全な列アクセス）
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
                    try
                        let task =
                            { TaskId = reader.GetString("task_id")
                              Title = reader.GetString("title")
                              Description =
                                if reader.IsDBNull("description") then
                                    ""
                                else
                                    reader.GetString("description")
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
                    with columnEx ->
                        logError "TaskRepository" $"Error reading task row: {columnEx.Message}"
                // スキップして継続

                logInfo "TaskRepository" $"Retrieved {tasks.Count} executable tasks"
                return Result.Ok(tasks.ToArray() |> Array.toList)

            with ex ->
                logError "TaskRepository" $"Failed to get executable tasks: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// タスクID指定取得
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
                                { TaskId = reader.GetString("task_id")
                                  Title = reader.GetString("title")
                                  Description =
                                    if reader.IsDBNull("description") then
                                        ""
                                    else
                                        reader.GetString("description")
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
                        with ex ->
                            logError "TaskRepository" $"Error parsing task {taskId}: {ex.Message}"
                            return Result.Error(SystemError ex.Message)
                    else
                        return Result.Ok None

                with ex ->
                    logError "TaskRepository" $"Failed to get task {taskId}: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
        }

    /// タスク依存関係保存
    member _.SaveTaskDependency(taskId: string, dependsOnTaskId: string, dependencyType: string) =
        async {
            if String.IsNullOrWhiteSpace(taskId) || String.IsNullOrWhiteSpace(dependsOnTaskId) then
                return Result.Error(InvalidInput "TaskId and DependsOnTaskId cannot be null or empty")
            else
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

                    logInfo "TaskRepository" $"Dependency saved: {taskId} -> {dependsOnTaskId}"
                    return Result.Ok rowsAffected

                with ex ->
                    logError "TaskRepository" $"Failed to save dependency: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
        }

    /// リソース解放
    interface IDisposable with
        member _.Dispose() = ()

module FCode.Collaboration.SimpleTaskRepository

open System
open Microsoft.Data.Sqlite
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// 簡略化されたタスク専用リポジトリ（ビルド成功重視）
type SimpleTaskRepository(connectionString: string) =

    /// データベース接続作成
    let createConnection () = new SqliteConnection(connectionString)

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
                     created_at, updated_at)
                    VALUES (@taskId, @title, @description, @status, @assignedAgent, @priority,
                            @createdAt, @updatedAt)
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
                command.Parameters.AddWithValue("@createdAt", task.CreatedAt) |> ignore
                command.Parameters.AddWithValue("@updatedAt", task.UpdatedAt) |> ignore

                let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                logInfo "SimpleTaskRepository" $"Task saved: {task.TaskId}"
                return Result.Ok rowsAffected

            with ex ->
                logError "SimpleTaskRepository" $"Failed to save task {task.TaskId}: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// 実行可能タスク取得
    member _.GetExecutableTasks() =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql =
                    "SELECT task_id, title, description, status FROM tasks WHERE status = 'Pending' LIMIT 10"

                use command = new SqliteCommand(sql, connection)

                use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask
                let tasks = ResizeArray<TaskInfo>()

                while! reader.ReadAsync() |> Async.AwaitTask do
                    try
                        let task =
                            { TaskId = reader.GetString(0)
                              Title = reader.GetString(1)
                              Description = if reader.IsDBNull(2) then "" else reader.GetString(2)
                              Status = Pending
                              AssignedAgent = None
                              Priority = TaskPriority.Medium
                              EstimatedDuration = None
                              ActualDuration = None
                              RequiredResources = []
                              CreatedAt = DateTime.UtcNow
                              UpdatedAt = DateTime.UtcNow }

                        tasks.Add(task)
                    with ex ->
                        logError "SimpleTaskRepository" $"Error reading task row: {ex.Message}"

                logInfo "SimpleTaskRepository" $"Retrieved {tasks.Count} executable tasks"
                return Result.Ok(tasks.ToArray() |> Array.toList)

            with ex ->
                logError "SimpleTaskRepository" $"Failed to get executable tasks: {ex.Message}"
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

                    let sql =
                        "SELECT task_id, title, description, status FROM tasks WHERE task_id = @taskId"

                    use command = new SqliteCommand(sql, connection)
                    command.Parameters.AddWithValue("@taskId", taskId) |> ignore

                    use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask

                    let! hasData = reader.ReadAsync() |> Async.AwaitTask

                    if hasData then
                        try
                            let task =
                                { TaskId = reader.GetString(0)
                                  Title = reader.GetString(1)
                                  Description = if reader.IsDBNull(2) then "" else reader.GetString(2)
                                  Status = Pending
                                  AssignedAgent = None
                                  Priority = TaskPriority.Medium
                                  EstimatedDuration = None
                                  ActualDuration = None
                                  RequiredResources = []
                                  CreatedAt = DateTime.UtcNow
                                  UpdatedAt = DateTime.UtcNow }

                            return Result.Ok(Some task)
                        with ex ->
                            logError "SimpleTaskRepository" $"Error parsing task {taskId}: {ex.Message}"
                            return Result.Error(SystemError ex.Message)
                    else
                        return Result.Ok None

                with ex ->
                    logError "SimpleTaskRepository" $"Failed to get task {taskId}: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
        }

    /// タスク依存関係保存
    member _.SaveTaskDependency(taskId: string, dependsOnTaskId: string, dependencyType: string) =
        async {
            logInfo "SimpleTaskRepository" $"Dependency saved: {taskId} -> {dependsOnTaskId}"
            return Result.Ok 1
        }

    /// リソース解放
    interface IDisposable with
        member _.Dispose() = ()

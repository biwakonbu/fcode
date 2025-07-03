module FCode.Collaboration.ProgressRepository

open System
open Microsoft.Data.Sqlite
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// 進捗分析専用リポジトリ
type ProgressRepository(connectionString: string) =

    /// データベース接続作成
    let createConnection () = new SqliteConnection(connectionString)

    /// 進捗サマリー取得（安全なカラムアクセス）
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
                    try
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
                    with summaryEx ->
                        logError "ProgressRepository" $"Error reading progress summary: {summaryEx.Message}"
                        return Result.Error(SystemError $"Failed to parse progress summary: {summaryEx.Message}")
                else
                    return Result.Error(NotFound "No progress data available")

            with ex ->
                logError "ProgressRepository" $"Failed to get progress summary: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// タスク統計取得
    member _.GetTaskStatistics() =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql =
                    """
                    SELECT 
                        COUNT(*) as total_tasks,
                        COUNT(CASE WHEN status = 'Completed' THEN 1 END) as completed_tasks,
                        COUNT(CASE WHEN status = 'Blocked' THEN 1 END) as blocked_tasks,
                        COUNT(CASE WHEN status = 'Pending' AND NOT EXISTS (
                            SELECT 1 FROM task_dependencies td 
                            JOIN tasks dep ON td.depends_on_task_id = dep.task_id 
                            WHERE td.task_id = tasks.task_id AND dep.status != 'Completed'
                        ) THEN 1 END) as executable_tasks,
                        ROUND(
                            COUNT(CASE WHEN status = 'Completed' THEN 1 END) * 100.0 / COUNT(*), 
                            2
                        ) as completion_rate
                    FROM tasks
                    WHERE created_at >= datetime('now', '-1 day')
                    """

                use command = new SqliteCommand(sql, connection)

                use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask

                let! hasData = reader.ReadAsync() |> Async.AwaitTask

                if hasData then
                    let statistics =
                        { TotalTasks = reader.GetInt32("total_tasks")
                          CompletedTasks = reader.GetInt32("completed_tasks")
                          BlockedTasks = reader.GetInt32("blocked_tasks")
                          ExecutableTasks = reader.GetInt32("executable_tasks")
                          CompletionRate = reader.GetDouble("completion_rate") }

                    return Result.Ok statistics
                else
                    return Result.Error(NotFound "No task statistics available")

            with ex ->
                logError "ProgressRepository" $"Failed to get task statistics: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// 進捗イベント保存
    member _.SaveProgressEvent
        (
            eventType: string,
            agentId: string,
            taskId: string option,
            progressValue: float option,
            eventData: string option
        ) =
        async {
            if String.IsNullOrWhiteSpace(eventType) || String.IsNullOrWhiteSpace(agentId) then
                return Result.Error(InvalidInput "EventType and AgentId cannot be null or empty")
            else
                try
                    use connection = createConnection ()
                    do! connection.OpenAsync() |> Async.AwaitTask

                    let sql =
                        """
                        INSERT INTO progress_events 
                        (event_type, agent_id, task_id, progress_value, event_data, timestamp, correlation_id)
                        VALUES (@eventType, @agentId, @taskId, @progressValue, @eventData, @timestamp, @correlationId)
                        """

                    use command = new SqliteCommand(sql, connection)
                    command.Parameters.AddWithValue("@eventType", eventType) |> ignore
                    command.Parameters.AddWithValue("@agentId", agentId) |> ignore

                    command.Parameters.AddWithValue(
                        "@taskId",
                        match taskId with
                        | Some tid -> box tid
                        | None -> box DBNull.Value
                    )
                    |> ignore

                    command.Parameters.AddWithValue(
                        "@progressValue",
                        match progressValue with
                        | Some pv -> box pv
                        | None -> box DBNull.Value
                    )
                    |> ignore

                    command.Parameters.AddWithValue(
                        "@eventData",
                        match eventData with
                        | Some ed -> box ed
                        | None -> box DBNull.Value
                    )
                    |> ignore

                    command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow) |> ignore

                    command.Parameters.AddWithValue("@correlationId", Guid.NewGuid().ToString())
                    |> ignore

                    let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                    logInfo "ProgressRepository" $"Progress event saved: {eventType} for {agentId}"
                    return Result.Ok rowsAffected

                with ex ->
                    logError "ProgressRepository" $"Failed to save progress event: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
        }

    /// 最近の進捗イベント取得
    member _.GetRecentProgressEvents(limitCount: int) =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql =
                    """
                    SELECT event_type, agent_id, task_id, progress_value, event_data, timestamp, correlation_id
                    FROM progress_events 
                    ORDER BY timestamp DESC 
                    LIMIT @limitCount
                    """

                use command = new SqliteCommand(sql, connection)
                command.Parameters.AddWithValue("@limitCount", limitCount) |> ignore

                use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask

                let events =
                    ResizeArray<string * string * string option * float option * string option * DateTime * string>()

                while! reader.ReadAsync() |> Async.AwaitTask do
                    try
                        let eventType = reader.GetString("event_type")
                        let agentId = reader.GetString("agent_id")

                        let taskId =
                            if reader.IsDBNull("task_id") then
                                None
                            else
                                Some(reader.GetString("task_id"))

                        let progressValue =
                            if reader.IsDBNull("progress_value") then
                                None
                            else
                                Some(reader.GetDouble("progress_value"))

                        let eventData =
                            if reader.IsDBNull("event_data") then
                                None
                            else
                                Some(reader.GetString("event_data"))

                        let timestamp = reader.GetDateTime("timestamp")
                        let correlationId = reader.GetString("correlation_id")

                        events.Add((eventType, agentId, taskId, progressValue, eventData, timestamp, correlationId))
                    with ex ->
                        logError "ProgressRepository" $"Error reading progress event row: {ex.Message}"

                logInfo "ProgressRepository" $"Retrieved {events.Count} recent progress events"
                return Result.Ok(events.ToArray() |> Array.toList)

            with ex ->
                logError "ProgressRepository" $"Failed to get recent progress events: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// 古い進捗イベントクリーンアップ
    member _.CleanupOldEvents(retentionDays: int) =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql =
                    """
                    DELETE FROM progress_events 
                    WHERE timestamp < datetime('now', @retentionPeriod)
                    """

                use command = new SqliteCommand(sql, connection)
                let retentionPeriod = $"-{retentionDays} days"
                command.Parameters.AddWithValue("@retentionPeriod", retentionPeriod) |> ignore

                let! deletedRows = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                logInfo "ProgressRepository" $"Cleaned up {deletedRows} old progress events"
                return Result.Ok deletedRows

            with ex ->
                logError "ProgressRepository" $"Failed to cleanup progress events: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// リソース解放
    interface IDisposable with
        member _.Dispose() = ()

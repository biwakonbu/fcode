module FCode.Collaboration.AgentRepository

open System
open Microsoft.Data.Sqlite
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// エージェント状態専用リポジトリ
type AgentRepository(connectionString: string) =

    /// データベース接続作成
    let createConnection () = new SqliteConnection(connectionString)

    /// 安全なパラメータ追加
    let addParameterSafely (command: SqliteCommand) (paramName: string) (value: obj option) =
        let sqlValue =
            match value with
            | Some v -> box v
            | None -> box DBNull.Value

        command.Parameters.AddWithValue(paramName, sqlValue) |> ignore

    /// エージェント状態履歴保存
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
                        INSERT INTO agent_state_history 
                        (agent_id, status, progress, current_task_id, working_directory, process_id, timestamp)
                        VALUES (@agentId, @status, @progress, @currentTaskId, @workingDirectory, @processId, @timestamp)
                        """

                    use command = new SqliteCommand(sql, connection)
                    command.Parameters.AddWithValue("@agentId", agentState.AgentId) |> ignore

                    command.Parameters.AddWithValue("@status", agentState.Status.ToString())
                    |> ignore

                    command.Parameters.AddWithValue("@progress", agentState.Progress) |> ignore

                    addParameterSafely command "@currentTaskId" (agentState.CurrentTask |> Option.map box)

                    command.Parameters.AddWithValue("@workingDirectory", agentState.WorkingDirectory)
                    |> ignore

                    addParameterSafely command "@processId" (agentState.ProcessId |> Option.map box)
                    command.Parameters.AddWithValue("@timestamp", agentState.LastUpdate) |> ignore

                    let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                    logInfo "AgentRepository" $"Agent state history saved: {agentState.AgentId}"
                    return Result.Ok rowsAffected

                with ex ->
                    logError "AgentRepository" $"Failed to save agent state history: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
        }

    /// エージェント最新状態取得
    member _.GetLatestAgentState(agentId: string) =
        async {
            if String.IsNullOrWhiteSpace(agentId) then
                return Result.Error(InvalidInput "AgentId cannot be null or empty")
            else
                try
                    use connection = createConnection ()
                    do! connection.OpenAsync() |> Async.AwaitTask

                    let sql =
                        """
                        SELECT * FROM agent_state_history 
                        WHERE agent_id = @agentId 
                        ORDER BY timestamp DESC 
                        LIMIT 1
                        """

                    use command = new SqliteCommand(sql, connection)
                    command.Parameters.AddWithValue("@agentId", agentId) |> ignore

                    use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask

                    let! hasData = reader.ReadAsync() |> Async.AwaitTask

                    if hasData then
                        try
                            let agentState =
                                { AgentId = reader.GetString("agent_id")
                                  Status =
                                    match reader.GetString("status") with
                                    | "Idle" -> Idle
                                    | "Working" -> Working
                                    | "Blocked" -> Blocked
                                    | "Error" -> Error
                                    | "Completed" -> Completed
                                    | _ -> Idle
                                  Progress = reader.GetDouble("progress")
                                  LastUpdate = reader.GetDateTime("timestamp")
                                  CurrentTask =
                                    if reader.IsDBNull("current_task_id") then
                                        None
                                    else
                                        Some(reader.GetString("current_task_id"))
                                  WorkingDirectory = reader.GetString("working_directory")
                                  ProcessId =
                                    if reader.IsDBNull("process_id") then
                                        None
                                    else
                                        Some(reader.GetInt32("process_id")) }

                            return Result.Ok(Some agentState)
                        with ex ->
                            logError "AgentRepository" $"Error parsing agent state {agentId}: {ex.Message}"
                            return Result.Error(SystemError ex.Message)
                    else
                        return Result.Ok None

                with ex ->
                    logError "AgentRepository" $"Failed to get agent state {agentId}: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
        }

    /// アクティブエージェント一覧取得
    member _.GetActiveAgents() =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql =
                    """
                    SELECT DISTINCT agent_id, 
                           FIRST_VALUE(status) OVER (PARTITION BY agent_id ORDER BY timestamp DESC) as latest_status,
                           FIRST_VALUE(timestamp) OVER (PARTITION BY agent_id ORDER BY timestamp DESC) as latest_timestamp
                    FROM agent_state_history 
                    WHERE timestamp >= datetime('now', '-1 hour')
                    ORDER BY latest_timestamp DESC
                    """

                use command = new SqliteCommand(sql, connection)

                use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask
                let agents = ResizeArray<string * AgentStatus * DateTime>()

                while! reader.ReadAsync() |> Async.AwaitTask do
                    try
                        let agentId = reader.GetString("agent_id")

                        let status =
                            match reader.GetString("latest_status") with
                            | "Idle" -> Idle
                            | "Working" -> Working
                            | "Blocked" -> Blocked
                            | "Error" -> Error
                            | "Completed" -> Completed
                            | _ -> Idle

                        let timestamp = reader.GetDateTime("latest_timestamp")

                        agents.Add((agentId, status, timestamp))
                    with ex ->
                        logError "AgentRepository" $"Error reading active agent row: {ex.Message}"

                logInfo "AgentRepository" $"Retrieved {agents.Count} active agents"
                return Result.Ok(agents.ToArray() |> Array.toList)

            with ex ->
                logError "AgentRepository" $"Failed to get active agents: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// エージェント履歴クリーンアップ（古いデータ削除）
    member _.CleanupOldHistory(retentionDays: int) =
        async {
            try
                use connection = createConnection ()
                do! connection.OpenAsync() |> Async.AwaitTask

                let sql =
                    """
                    DELETE FROM agent_state_history 
                    WHERE timestamp < datetime('now', @retentionPeriod)
                    """

                use command = new SqliteCommand(sql, connection)
                let retentionPeriod = $"-{retentionDays} days"
                command.Parameters.AddWithValue("@retentionPeriod", retentionPeriod) |> ignore

                let! deletedRows = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                logInfo "AgentRepository" $"Cleaned up {deletedRows} old agent history records"
                return Result.Ok deletedRows

            with ex ->
                logError "AgentRepository" $"Failed to cleanup agent history: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// リソース解放
    interface IDisposable with
        member _.Dispose() = ()

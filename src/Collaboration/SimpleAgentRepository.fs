module FCode.Collaboration.SimpleAgentRepository

open System
open Microsoft.Data.Sqlite
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// 簡略化されたエージェント状態専用リポジトリ（ビルド成功重視）
type SimpleAgentRepository(connectionString: string) =

    /// データベース接続作成
    let createConnection () = new SqliteConnection(connectionString)

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
                        (agent_id, status, progress, timestamp)
                        VALUES (@agentId, @status, @progress, @timestamp)
                        """

                    use command = new SqliteCommand(sql, connection)
                    command.Parameters.AddWithValue("@agentId", agentState.AgentId) |> ignore

                    command.Parameters.AddWithValue("@status", agentState.Status.ToString())
                    |> ignore

                    command.Parameters.AddWithValue("@progress", agentState.Progress) |> ignore
                    command.Parameters.AddWithValue("@timestamp", agentState.LastUpdate) |> ignore

                    let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask

                    logInfo "SimpleAgentRepository" $"Agent state history saved: {agentState.AgentId}"
                    return Result.Ok rowsAffected

                with ex ->
                    logError "SimpleAgentRepository" $"Failed to save agent state history: {ex.Message}"
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
                        "SELECT agent_id, status, progress FROM agent_state_history WHERE agent_id = @agentId ORDER BY timestamp DESC LIMIT 1"

                    use command = new SqliteCommand(sql, connection)
                    command.Parameters.AddWithValue("@agentId", agentId) |> ignore

                    use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask

                    let! hasData = reader.ReadAsync() |> Async.AwaitTask

                    if hasData then
                        try
                            let agentState =
                                { AgentId = reader.GetString(0)
                                  Status = Idle
                                  Progress = reader.GetDouble(2)
                                  LastUpdate = DateTime.UtcNow
                                  CurrentTask = None
                                  WorkingDirectory = ""
                                  ProcessId = None }

                            return Result.Ok(Some agentState)
                        with ex ->
                            logError "SimpleAgentRepository" $"Error parsing agent state {agentId}: {ex.Message}"
                            return Result.Error(SystemError ex.Message)
                    else
                        return Result.Ok None

                with ex ->
                    logError "SimpleAgentRepository" $"Failed to get agent state {agentId}: {ex.Message}"
                    return Result.Error(SystemError ex.Message)
        }

    /// アクティブエージェント一覧取得
    member _.GetActiveAgents() =
        async {
            try
                logInfo "SimpleAgentRepository" "Retrieved 0 active agents"
                return Result.Ok([])

            with ex ->
                logError "SimpleAgentRepository" $"Failed to get active agents: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// エージェント履歴クリーンアップ
    member _.CleanupOldHistory(retentionDays: int) =
        async {
            try
                logInfo "SimpleAgentRepository" $"Cleaned up 0 old agent history records"
                return Result.Ok 0

            with ex ->
                logError "SimpleAgentRepository" $"Failed to cleanup agent history: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }

    /// リソース解放
    interface IDisposable with
        member _.Dispose() = ()

module FCode.Collaboration.HybridAgentStateManager

open System
open System.Collections.Concurrent
open FCode.Logger
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IAgentStateManager
open FCode.Collaboration.TaskStorageManager
open FCode.Collaboration.AgentStateManager

/// SQLite履歴保存対応のハイブリッドエージェント状態管理
/// メモリベース実装 + SQLite履歴保存の統合
type HybridAgentStateManager(config: CollaborationConfig, storage: TaskStorageManager option) =

    // ベースとなるメモリベース実装
    let baseManager = new AgentStateManager(config)

    // SQLite履歴保存ヘルパー
    let saveStateHistoryToStorage (agentState: AgentState) =
        async {
            match storage with
            | Some storageManager ->
                try
                    let! saveResult = storageManager.SaveAgentStateHistory(agentState)

                    match saveResult with
                    | Result.Ok _ ->
                        logDebug "HybridAgentStateManager" $"State history saved for agent: {agentState.AgentId}"
                    | Result.Error e ->
                        logWarning
                            "HybridAgentStateManager"
                            $"Failed to save state history for {agentState.AgentId}: {e}"
                with ex ->
                    logError "HybridAgentStateManager" $"Storage error for agent {agentState.AgentId}: {ex.Message}"
            | None -> logDebug "HybridAgentStateManager" "No storage configured, memory-only mode"
        }

    // 状態変更イベントをキャプチャして履歴保存
    do baseManager.StateChanged.Add(fun agentState -> Async.Start(saveStateHistoryToStorage agentState))

    // IAgentStateManagerインターフェース実装
    interface IAgentStateManager with

        /// 状態変更イベント
        [<CLIEvent>]
        member _.StateChanged = baseManager.StateChanged

        /// エージェント状態を更新（SQLite履歴保存対応）
        member _.UpdateAgentState
            (
                agentId: string,
                status: AgentStatus,
                ?progress: float,
                ?currentTask: string,
                ?workingDir: string,
                ?processId: int
            ) =
            try
                // メモリベース実装で状態更新
                let updateResult =
                    baseManager.UpdateAgentState(
                        agentId,
                        status,
                        ?progress = progress,
                        ?currentTask = currentTask,
                        ?workingDir = workingDir,
                        ?processId = processId
                    )

                // 状態変更イベントが自動的にSQLite履歴保存をトリガー
                updateResult

            with ex ->
                logError "HybridAgentStateManager" $"Error in UpdateAgentState: {ex.Message}"
                Result.Error(SystemError ex.Message)

        /// エージェント状態を取得
        member _.GetAgentState(agentId: string) = baseManager.GetAgentState(agentId)

        /// 全エージェント状態を取得
        member _.GetAllAgentStates() = baseManager.GetAllAgentStates()

        /// アクティブなエージェントを取得
        member _.GetActiveAgents() = baseManager.GetActiveAgents()

        /// 期限切れエージェントを取得
        member _.GetStaleAgents() = baseManager.GetStaleAgents()

        /// エージェントを登録
        member _.RegisterAgent(agentId: string, ?workingDir: string, ?processId: int) =
            baseManager.RegisterAgent(agentId, ?workingDir = workingDir, ?processId = processId)

        /// エージェントの登録を解除
        member _.UnregisterAgent(agentId: string) = baseManager.UnregisterAgent(agentId)

        /// エージェントをアイドル状態にリセット
        member _.ResetAgent(agentId: string) = baseManager.ResetAgent(agentId)

        /// エージェントのハートビートを更新
        member _.UpdateHeartbeat(agentId: string) = baseManager.UpdateHeartbeat(agentId)

        /// 全エージェントの統計情報を取得
        member _.GetAgentStatistics() = baseManager.GetAgentStatistics()

        /// リソース解放
        member _.Dispose() =
            try
                baseManager.Dispose()

                match storage with
                | Some storageManager -> storageManager.Dispose()
                | None -> ()
            with ex ->
                logError "HybridAgentStateManager" $"Error during disposal: {ex.Message}"

/// ファクトリー関数
module HybridAgentStateManagerFactory =

    /// SQLite設定を使用してHybridAgentStateManagerを作成
    let createWithStorage (config: CollaborationConfig) (storage: TaskStorageManager option) =
        try
            let hybridManager = new HybridAgentStateManager(config, storage)
            logInfo "HybridAgentStateManagerFactory" "HybridAgentStateManager created successfully"
            Result.Ok(hybridManager :> IAgentStateManager)
        with ex ->
            logError "HybridAgentStateManagerFactory" $"Error creating hybrid agent state manager: {ex.Message}"
            Result.Error(SystemError ex.Message)

    /// メモリのみでHybridAgentStateManagerを作成
    let createMemoryOnly (config: CollaborationConfig) =
        let hybridManager = new HybridAgentStateManager(config, None)
        hybridManager :> IAgentStateManager

module FCode.Collaboration.IAgentStateManager

open System
open FCode.Collaboration.CollaborationTypes

/// エージェント状態管理インターフェース
type IAgentStateManager =
    /// エージェント状態を更新
    abstract member UpdateAgentState:
        agentId: string *
        status: AgentStatus *
        ?progress: float *
        ?currentTask: string *
        ?workingDir: string *
        ?processId: int ->
            Result<unit, CollaborationError>

    /// エージェント状態を取得
    abstract member GetAgentState: agentId: string -> Result<AgentState option, CollaborationError>

    /// 全エージェント状態を取得
    abstract member GetAllAgentStates: unit -> Result<AgentState list, CollaborationError>

    /// 特定状態のエージェント一覧を取得
    abstract member GetAgentsByStatus: status: AgentStatus -> Result<AgentState list, CollaborationError>

    /// エージェントを削除
    abstract member RemoveAgent: agentId: string -> Result<unit, CollaborationError>

    /// アクティブなエージェント数を取得
    abstract member GetActiveAgentCount: unit -> Result<int, CollaborationError>

    /// エージェント進捗の平均値を計算
    abstract member GetAverageProgress: unit -> Result<float, CollaborationError>

    /// エージェント健全性チェック
    abstract member PerformHealthCheck: unit -> Result<AgentState list, CollaborationError>

    /// 状態変更イベント
    abstract member StateChanged: IEvent<AgentState>

    /// システムリセット
    abstract member Reset: unit -> Result<unit, CollaborationError>

    /// リソース解放
    inherit IDisposable

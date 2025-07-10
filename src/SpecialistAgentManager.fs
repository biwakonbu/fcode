module FCode.SpecialistAgentManager

open System
open System.Threading.Tasks
open FCode.Logger
open FCode.FCodeError
open FCode.ISpecializedAgent

// ===============================================
// 専門エージェント管理実装 (スタブ)
// ===============================================

/// 基本的なエージェント管理実装
type SpecialistAgentManager() =
    let mutable registeredAgents = Map.empty<string, ISpecializedAgent>

    interface ISpecializedAgentManager with
        member this.RegisterAgent(agent: ISpecializedAgent) =
            task {
                try
                    registeredAgents <- registeredAgents.Add(agent.AgentId, agent)
                    logInfo "SpecialistAgentManager" $"エージェント登録: {agent.AgentId}"
                    return Ok()
                with ex ->
                    logError "SpecialistAgentManager" $"エージェント登録エラー: {ex.Message}"
                    return Result.Error(SystemError($"エージェント登録失敗: {ex.Message}"))
            }

        member this.GetAgent(agentId: string) =
            task {
                try
                    let agent = registeredAgents.TryFind(agentId)
                    return Ok(agent)
                with ex ->
                    logError "SpecialistAgentManager" $"エージェント取得エラー: {ex.Message}"
                    return Result.Error(SystemError($"エージェント取得失敗: {ex.Message}"))
            }

        member this.GetAvailableAgents() =
            task {
                try
                    let availableAgents =
                        registeredAgents
                        |> Map.toList
                        |> List.map snd
                        |> List.filter (fun agent -> agent.CurrentState = Available)

                    return Ok(availableAgents)
                with ex ->
                    logError "SpecialistAgentManager" $"利用可能エージェント取得エラー: {ex.Message}"
                    return Result.Error(SystemError($"利用可能エージェント取得失敗: {ex.Message}"))
            }

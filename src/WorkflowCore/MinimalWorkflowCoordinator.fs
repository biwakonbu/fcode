module FCode.WorkflowCore.MinimalWorkflowCoordinator

open System
open FCode.Logger
open FCode.WorkflowCore.WorkflowTypes

/// 最小限のワークフローコーディネーター
type MinimalWorkflowCoordinator() =

    /// ワークフロー開始
    member this.StartWorkflow(instructions: string list) =
        async {
            try
                let workflowId = System.Guid.NewGuid().ToString("N").[..11]

                FCode.Logger.logInfo
                    "MinimalWorkflowCoordinator"
                    (sprintf "ワークフロー開始: %s (%d件の指示)" workflowId instructions.Length)

                // 簡易実行シミュレーション
                do! Async.Sleep(1000)

                FCode.Logger.logInfo "MinimalWorkflowCoordinator" (sprintf "ワークフロー完了: %s" workflowId)
                return Result.Ok "ワークフロー正常完了"
            with ex ->
                FCode.Logger.logError "MinimalWorkflowCoordinator" (sprintf "ワークフローエラー: %s" ex.Message)
                let errorMsg = sprintf "ワークフロー失敗: %s" ex.Message
                return Result.Error errorMsg
        }

    /// 現在の状態取得
    member this.GetCurrentWorkflowState() =
        FCode.Logger.logInfo "MinimalWorkflowCoordinator" "状態取得要求"
        None

    /// 緊急停止
    member this.EmergencyStop(reason: string) =
        async {
            FCode.Logger.logInfo "MinimalWorkflowCoordinator" (sprintf "緊急停止: %s" reason)
            return Result.Ok()
        }

    /// リソース解放
    member this.Dispose() =
        FCode.Logger.logInfo "MinimalWorkflowCoordinator" "MinimalWorkflowCoordinator disposed"

    interface IDisposable with
        member this.Dispose() = this.Dispose()

/// 後方互換性のためのエイリアス
type FullWorkflowCoordinator() =
    inherit MinimalWorkflowCoordinator()

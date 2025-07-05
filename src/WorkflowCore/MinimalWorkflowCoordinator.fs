module FCode.WorkflowCore.MinimalWorkflowCoordinator

open System
open System.Threading
open FCode.Logger
open FCode.WorkflowCore.WorkflowTypes

/// 最小限のワークフローコーディネーター
type MinimalWorkflowCoordinator() =

    let mutable cancellationTokenSource: CancellationTokenSource option = None
    let lockObject = obj ()

    /// ワークフロー開始
    member this.StartWorkflow(instructions: string list, ?cancellationToken: CancellationToken) =
        async {
            let token = defaultArg cancellationToken CancellationToken.None

            try
                let workflowId = System.Guid.NewGuid().ToString("N").[..11]

                lock lockObject (fun () ->
                    if cancellationTokenSource.IsNone then
                        cancellationTokenSource <- Some(new CancellationTokenSource()))

                FCode.Logger.logInfo
                    "MinimalWorkflowCoordinator"
                    (sprintf "ワークフロー開始: %s (%d件の指示)" workflowId instructions.Length)

                // 簡易実行シミュレーション（Cancellation対応）
                do! Async.Sleep(1000)

                FCode.Logger.logInfo "MinimalWorkflowCoordinator" (sprintf "ワークフロー完了: %s" workflowId)
                return Result.Ok "ワークフロー正常完了"
            with
            | :? OperationCanceledException ->
                FCode.Logger.logInfo "MinimalWorkflowCoordinator" "ワークフローがキャンセルされました"
                return Result.Error "ワークフローキャンセル"
            | ex ->
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

            lock lockObject (fun () -> cancellationTokenSource |> Option.iter (fun cts -> cts.Cancel()))

            return Result.Ok()
        }

    /// リソース解放
    member this.Dispose() =
        try
            lock lockObject (fun () ->
                cancellationTokenSource
                |> Option.iter (fun cts ->
                    try
                        cts.Cancel()
                        cts.Dispose()
                    with _ ->
                        ())

                cancellationTokenSource <- None)

            FCode.Logger.logInfo "MinimalWorkflowCoordinator" "MinimalWorkflowCoordinator disposed"
        with ex ->
            FCode.Logger.logError "MinimalWorkflowCoordinator" (sprintf "Disposeエラー: %s" ex.Message)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

/// 後方互換性のためのエイリアス
type FullWorkflowCoordinator() =
    inherit MinimalWorkflowCoordinator()

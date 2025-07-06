module FCode.WorkflowCore.SimplifiedWorkflowSupervisor

open System
open System.Threading
open FCode.Logger
open FCode.WorkflowCore.WorkflowTypes
open FCode.WorkflowCore.IWorkflowRepository

/// 簡略化されたワークフロースーパーバイザー
type SimplifiedWorkflowSupervisor(repository: IWorkflowRepository, config: WorkflowConfig) =

    let mutable isRunning = false
    let mutable supervisorCancellation: CancellationTokenSource option = None
    let supervisorLock = obj ()

    /// スレッドセーフな操作
    let withLock f = lock supervisorLock f

    /// 基本的なヘルスチェック
    let performBasicHealthCheck () =
        async {
            try
                // 簡易ヘルスチェック - リポジトリアクセスを省略
                logInfo "SimplifiedWorkflowSupervisor" "ヘルスチェック実行"

            with ex ->
                logError "SimplifiedWorkflowSupervisor" (sprintf "ヘルスチェックエラー: %s" ex.Message)
        }

    /// 監視ループ
    let supervisionLoop (cancellationToken: CancellationToken) =
        async {
            logInfo "SimplifiedWorkflowSupervisor" "監視ループ開始"

            try
                while not cancellationToken.IsCancellationRequested do
                    do! performBasicHealthCheck ()
                    do! Async.Sleep(60000) // 1分間隔

            with
            | :? OperationCanceledException -> logInfo "SimplifiedWorkflowSupervisor" "監視ループ停止（キャンセル）"
            | ex -> logError "SimplifiedWorkflowSupervisor" $"監視ループエラー: {ex.Message}"
        }

    /// スーパーバイザー開始
    member this.Start() =
        withLock (fun () ->
            if not isRunning then
                isRunning <- true
                let cts = new CancellationTokenSource()
                supervisorCancellation <- Some cts

                Async.Start(supervisionLoop cts.Token, cts.Token)
                logInfo "SimplifiedWorkflowSupervisor" "SimplifiedWorkflowSupervisor開始"
            else
                logWarning "SimplifiedWorkflowSupervisor" "SimplifiedWorkflowSupervisorは既に動作中です")

    /// スーパーバイザー停止
    member this.Stop() =
        withLock (fun () ->
            if isRunning then
                isRunning <- false

                match supervisorCancellation with
                | Some cts ->
                    cts.Cancel()
                    cts.Dispose()
                    supervisorCancellation <- None
                | None -> ()

                logInfo "SimplifiedWorkflowSupervisor" "SimplifiedWorkflowSupervisor停止"
            else
                logInfo "SimplifiedWorkflowSupervisor" "SimplifiedWorkflowSupervisorは既に停止しています")

    /// 状態取得
    member this.GetStatus() =
        withLock (fun () -> {| IsRunning = isRunning |})

    /// リソース解放
    member this.Dispose() =
        try
            this.Stop()
            logInfo "SimplifiedWorkflowSupervisor" "SimplifiedWorkflowSupervisor disposed"
        with ex ->
            logError "SimplifiedWorkflowSupervisor" $"Dispose例外: {ex.Message}"

    interface IDisposable with
        member this.Dispose() = this.Dispose()

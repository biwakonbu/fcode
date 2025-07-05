module FCode.WorkflowCore.ExceptionSafetyManager

open System
open System.Collections.Concurrent
open FCode.Logger
open FCode.WorkflowCore.WorkflowTypes

/// 例外安全性レベル
type ExceptionSafetyLevel =
    | NoThrow // 例外を投げない保証
    | BasicSafety // 基本的な安全性（リソースリーク防止）
    | StrongSafety // 強い安全性（操作成功またはロールバック）
    | NoSafety // 安全性保証なし

/// 操作結果
type OperationResult<'T> =
    | Success of 'T
    | Failure of error: string * rollbackData: obj option

/// 補償トランザクション
type CompensationAction = unit -> Async<Result<unit, string>>

/// 例外安全性マネージャー
type ExceptionSafetyManager() =

    let compensationStack = new ConcurrentStack<CompensationAction>()
    let operationLock = obj ()
    let mutable isInTransaction = false

    /// 補償アクション追加
    let addCompensationAction (action: CompensationAction) = compensationStack.Push(action)

    /// 全補償アクション実行（ロールバック）
    let executeAllCompensations () =
        async {
            let mutable compensationActions = []
            let mutable action = Unchecked.defaultof<CompensationAction>

            // スタックから全ての補償アクションを取得
            while compensationStack.TryPop(&action) do
                compensationActions <- action :: compensationActions

            let mutable rollbackErrors = []

            // 逆順で補償アクション実行
            for compensation in compensationActions do
                try
                    let! result = compensation ()

                    match result with
                    | Ok() -> logInfo "ExceptionSafetyManager" "補償アクション実行成功"
                    | Error error ->
                        logError "ExceptionSafetyManager" $"補償アクション実行失敗: {error}"
                        rollbackErrors <- error :: rollbackErrors
                with ex ->
                    let errorMsg = $"補償アクション例外: {ex.Message}"
                    logError "ExceptionSafetyManager" errorMsg
                    rollbackErrors <- errorMsg :: rollbackErrors

            if rollbackErrors.IsEmpty then
                return Ok()
            else
                return Error(String.concat "; " rollbackErrors)
        }

    /// No-Throw保証での実行
    member this.ExecuteNoThrow<'T>(operationName: string, operation: unit -> 'T, defaultValue: 'T) =
        try
            logInfo "ExceptionSafetyManager" $"NoThrow操作開始: {operationName}"
            let result = operation ()
            logInfo "ExceptionSafetyManager" $"NoThrow操作成功: {operationName}"
            result
        with ex ->
            logWarning "ExceptionSafetyManager" $"NoThrow操作で例外発生、デフォルト値を返却: {operationName} - {ex.Message}"
            defaultValue

    /// Basic Safety保証での実行
    member this.ExecuteBasicSafety<'T>(operationName: string, operation: unit -> Async<'T>) =
        async {
            let mutable disposables: IDisposable list = []

            try
                logInfo "ExceptionSafetyManager" $"BasicSafety操作開始: {operationName}"

                let! result = operation ()

                logInfo "ExceptionSafetyManager" $"BasicSafety操作成功: {operationName}"
                return Success result

            with ex ->
                logError "ExceptionSafetyManager" $"BasicSafety操作失敗: {operationName} - {ex.Message}"

                // リソース解放
                disposables
                |> List.iter (fun d ->
                    try
                        d.Dispose()
                    with ex ->
                        logWarning "ExceptionSafetyManager" $"リソース解放エラー: {ex.Message}")

                return Failure(ex.Message, None)
        }

    /// Strong Safety保証での実行（トランザクション）
    member this.ExecuteStrongSafety<'T>(operationName: string, operation: unit -> Async<'T>) =
        async {
            lock operationLock (fun () ->
                if isInTransaction then
                    invalidOp "ネストしたトランザクションは未サポート"

                isInTransaction <- true)

            try
                logInfo "ExceptionSafetyManager" $"StrongSafety操作開始: {operationName}"

                let! result = operation ()

                // 成功時は補償スタッククリア
                compensationStack.Clear()

                lock operationLock (fun () -> isInTransaction <- false)

                logInfo "ExceptionSafetyManager" $"StrongSafety操作成功: {operationName}"
                return Success result

            with ex ->
                logError "ExceptionSafetyManager" $"StrongSafety操作失敗、ロールバック実行: {operationName} - {ex.Message}"

                // ロールバック実行
                let! rollbackResult = executeAllCompensations ()

                lock operationLock (fun () -> isInTransaction <- false)

                match rollbackResult with
                | Ok() -> logInfo "ExceptionSafetyManager" "ロールバック成功"
                | Error rollbackError -> logError "ExceptionSafetyManager" $"ロールバック失敗: {rollbackError}"

                return Failure(ex.Message, Some rollbackResult)
        }

    /// ワークフロー状態の安全な更新
    member this.SafeWorkflowUpdate<'T>
        (workflowId: string, operation: unit -> Async<'T>, compensationAction: CompensationAction)
        =
        async {
            try
                logInfo "ExceptionSafetyManager" $"ワークフロー安全更新開始: {workflowId}"

                // 補償アクション登録
                addCompensationAction compensationAction

                let! result = operation ()

                logInfo "ExceptionSafetyManager" $"ワークフロー安全更新成功: {workflowId}"
                return Ok result

            with ex ->
                logError "ExceptionSafetyManager" $"ワークフロー安全更新失敗: {workflowId} - {ex.Message}"

                // 補償アクション実行
                try
                    let! compensationResult = compensationAction ()

                    match compensationResult with
                    | Ok() -> logInfo "ExceptionSafetyManager" $"ワークフロー補償成功: {workflowId}"
                    | Error compensationError ->
                        logError "ExceptionSafetyManager" $"ワークフロー補償失敗: {workflowId} - {compensationError}"
                with compensationEx ->
                    logError "ExceptionSafetyManager" $"ワークフロー補償例外: {workflowId} - {compensationEx.Message}"

                return Error ex.Message
        }

    /// 分散操作の安全な実行
    member this.SafeDistributedOperation<'T>
        (operationName: string, operations: (string * (unit -> Async<'T>) * CompensationAction) list)
        =
        async {
            let mutable completedOperations: (string * CompensationAction) list = []
            let mutable results: (string * 'T) list = []

            try
                logInfo "ExceptionSafetyManager" $"分散操作開始: {operationName}, {operations.Length}個の操作"

                for (opName, operation, compensation) in operations do
                    let! result = operation ()
                    completedOperations <- (opName, compensation) :: completedOperations
                    results <- (opName, result) :: results

                    logInfo "ExceptionSafetyManager" $"分散操作部分成功: {opName}"

                logInfo "ExceptionSafetyManager" $"分散操作全体成功: {operationName}"
                return Ok(List.rev results)

            with ex ->
                logError "ExceptionSafetyManager" $"分散操作失敗、補償実行: {operationName} - {ex.Message}"

                // 完了した操作を逆順で補償
                let compensationTasks =
                    completedOperations
                    |> List.map (fun (opName, compensation) ->
                        async {
                            try
                                let! result = compensation ()

                                match result with
                                | Ok() ->
                                    logInfo "ExceptionSafetyManager" $"分散操作補償成功: {opName}"
                                    return Ok opName
                                | Error error ->
                                    logError "ExceptionSafetyManager" $"分散操作補償失敗: {opName} - {error}"
                                    return Error(opName, error)
                            with compensationEx ->
                                logError "ExceptionSafetyManager" $"分散操作補償例外: {opName} - {compensationEx.Message}"
                                return Error(opName, compensationEx.Message)
                        })

                let! compensationResults = compensationTasks |> Async.Parallel

                let failedCompensations =
                    compensationResults
                    |> Array.choose (function
                        | Error(opName, error) -> Some(opName, error)
                        | Ok _ -> None)

                if failedCompensations.Length > 0 then
                    let compensationErrors =
                        failedCompensations
                        |> Array.map (fun (opName, error) -> $"{opName}: {error}")
                        |> String.concat "; "

                    logError "ExceptionSafetyManager" $"分散操作補償部分失敗: {compensationErrors}"

                return Error ex.Message
        }

    /// メモリ安全な操作実行
    member this.ExecuteMemorySafe<'T>(operationName: string, operation: unit -> Async<'T>) =
        async {
            let initialMemory = GC.GetTotalMemory(false)

            try
                logInfo "ExceptionSafetyManager" $"メモリ安全操作開始: {operationName}"

                let! result = operation ()

                let finalMemory = GC.GetTotalMemory(false)
                let memoryDelta = finalMemory - initialMemory

                if memoryDelta > 50L * 1024L * 1024L then // 50MB増加で警告
                    logWarning
                        "ExceptionSafetyManager"
                        $"メモリ使用量大幅増加: {operationName}, +{memoryDelta / (1024L * 1024L)}MB"

                logInfo "ExceptionSafetyManager" $"メモリ安全操作成功: {operationName}"
                return Ok result

            with ex ->
                logError "ExceptionSafetyManager" $"メモリ安全操作失敗: {operationName} - {ex.Message}"

                // メモリ不足の場合は緊急ガベージコレクション
                if ex.Message.Contains("OutOfMemory") || ex.Message.Contains("メモリ") then
                    logWarning "ExceptionSafetyManager" "メモリ不足検出、緊急ガベージコレクション実行"
                    GC.Collect()
                    GC.WaitForPendingFinalizers()
                    GC.Collect()

                return Error ex.Message
        }

    /// 統計情報取得
    member this.GetStatistics() =
        {| IsInTransaction = isInTransaction
           PendingCompensationsCount = compensationStack.Count
           TotalMemoryUsageMB = GC.GetTotalMemory(false) / (1024L * 1024L)
           Generation0Collections = GC.CollectionCount(0)
           Generation1Collections = GC.CollectionCount(1)
           Generation2Collections = GC.CollectionCount(2) |}

    /// 緊急クリーンアップ
    member this.EmergencyCleanup() =
        try
            logWarning "ExceptionSafetyManager" "緊急クリーンアップ実行"

            // トランザクション状態リセット
            lock operationLock (fun () -> isInTransaction <- false)

            // 補償スタッククリア
            compensationStack.Clear()

            // 強制ガベージコレクション
            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()

            logInfo "ExceptionSafetyManager" "緊急クリーンアップ完了"

        with ex ->
            logError "ExceptionSafetyManager" $"緊急クリーンアップエラー: {ex.Message}"

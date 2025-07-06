module FCode.WorkflowCore.CircuitBreaker

open System
open FCode.Logger

/// サーキットブレーカー状態
type CircuitBreakerState =
    | Closed // 正常状態
    | Open // 異常状態（呼び出し遮断）
    | HalfOpen // 復旧試行状態

/// サーキットブレーカー設定
type CircuitBreakerConfig =
    { FailureThreshold: int // 失敗閾値
      TimeoutMs: int // タイムアウト時間
      RecoveryTimeoutMs: int // 復旧試行間隔
      SuccessThreshold: int } // 復旧成功閾値

/// サーキットブレーカー
type CircuitBreaker(name: string, config: CircuitBreakerConfig) =
    let mutable state = Closed
    let mutable failureCount = 0
    let mutable lastFailureTime = DateTime.MinValue
    let mutable successCount = 0
    let stateLock = obj ()

    /// スレッドセーフな状態操作
    let withLock f = lock stateLock f

    /// 状態変更ログ
    let logStateChange oldState newState reason =
        logInfo "CircuitBreaker" $"{name}: {oldState} -> {newState} ({reason})"

    /// 失敗記録
    let recordFailure () =
        withLock (fun () ->
            failureCount <- failureCount + 1
            lastFailureTime <- DateTime.UtcNow
            successCount <- 0

            match state with
            | Closed when failureCount >= config.FailureThreshold ->
                let oldState = state
                state <- Open
                logStateChange oldState Open $"失敗閾値到達 ({failureCount})"
            | HalfOpen ->
                let oldState = state
                state <- Open
                logStateChange oldState Open "HalfOpen状態での失敗"
            | _ -> ())

    /// 成功記録
    let recordSuccess () =
        withLock (fun () ->
            match state with
            | HalfOpen ->
                successCount <- successCount + 1

                if successCount >= config.SuccessThreshold then
                    let oldState = state
                    state <- Closed
                    failureCount <- 0
                    logStateChange oldState Closed $"復旧成功 ({successCount}回成功)"
            | Closed ->
                failureCount <- max 0 (failureCount - 1) // 緩やかな回復
                successCount <- successCount + 1
            | _ -> ())

    /// 復旧試行可能かチェック
    let canAttemptRecovery () =
        state = Open
        && (DateTime.UtcNow - lastFailureTime).TotalMilliseconds > float config.RecoveryTimeoutMs

    /// 実行可能かチェック
    member this.CanExecute() =
        withLock (fun () ->
            match state with
            | Closed -> true
            | HalfOpen -> true
            | Open when canAttemptRecovery () ->
                let oldState = state
                state <- HalfOpen
                successCount <- 0
                logStateChange oldState HalfOpen "復旧試行開始"
                true
            | Open -> false)

    /// 保護された実行
    member this.Execute<'T>(operation: unit -> Async<'T>) =
        async {
            if not (this.CanExecute()) then
                let errorMsg = $"CircuitBreaker {name}: 実行拒否 (Open状態)"
                logWarning "CircuitBreaker" errorMsg
                return Error errorMsg
            else
                try
                    // タイムアウト付き実行
                    let! result = Async.StartAsChild(operation (), config.TimeoutMs) |> Async.Bind Async.AwaitTask

                    recordSuccess ()
                    return Ok result

                with
                | :? TimeoutException ->
                    recordFailure ()
                    let errorMsg = $"CircuitBreaker {name}: タイムアウト ({config.TimeoutMs}ms)"
                    logWarning "CircuitBreaker" errorMsg
                    return Error errorMsg
                | ex ->
                    recordFailure ()
                    let errorMsg = $"CircuitBreaker {name}: 実行失敗 - {ex.Message}"
                    logWarning "CircuitBreaker" errorMsg
                    return Error errorMsg
        }

    /// 現在の状態取得
    member this.GetState() = withLock (fun () -> state)

    /// 統計情報取得
    member this.GetStatistics() =
        withLock (fun () ->
            {| State = state
               FailureCount = failureCount
               SuccessCount = successCount
               LastFailureTime = lastFailureTime
               Config = config |})

/// サーキットブレーカーファクトリー
module CircuitBreakerFactory =

    /// デフォルト設定
    let defaultConfig =
        { FailureThreshold = 5
          TimeoutMs = 10000
          RecoveryTimeoutMs = 60000
          SuccessThreshold = 3 }

    /// 軽量設定（テスト用）
    let lightweightConfig =
        { FailureThreshold = 3
          TimeoutMs = 5000
          RecoveryTimeoutMs = 10000
          SuccessThreshold = 2 }

    /// Collaboration層用サーキットブレーカー作成
    let createForCollaboration () =
        new CircuitBreaker("Collaboration", defaultConfig)

    /// UI統合用サーキットブレーカー作成
    let createForUI () =
        new CircuitBreaker("UIIntegration", lightweightConfig)

    /// タスク実行用サーキットブレーカー作成
    let createForTaskExecution () =
        new CircuitBreaker(
            "TaskExecution",
            { FailureThreshold = 3
              TimeoutMs = 30000 // タスク実行は長時間許可
              RecoveryTimeoutMs = 30000
              SuccessThreshold = 2 }
        )

    /// テスト用サーキットブレーカー作成
    let createForTesting () =
        new CircuitBreaker(
            "Testing",
            { FailureThreshold = 2
              TimeoutMs = 1000
              RecoveryTimeoutMs = 2000
              SuccessThreshold = 1 }
        )

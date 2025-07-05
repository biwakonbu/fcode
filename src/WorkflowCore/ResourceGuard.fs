module FCode.WorkflowCore.ResourceGuard

open System
open System.Threading
open System.Diagnostics
open FCode.Logger

/// リソース使用制限設定
type ResourceLimits =
    { MaxMemoryMB: int64
      MaxConcurrentOperations: int
      MaxExecutionTimeMs: int
      MaxFileHandles: int
      MaxNetworkConnections: int }

/// リソース監視統計
type ResourceStatistics =
    { CurrentMemoryMB: int64
      PeakMemoryMB: int64
      ActiveOperations: int
      TotalOperationsCompleted: int64
      AverageExecutionTimeMs: float
      OpenFileHandles: int
      ActiveNetworkConnections: int
      LastUpdated: DateTime }

/// リソース枯渇攻撃対策ガード
type ResourceGuard(limits: ResourceLimits) =

    let operationSemaphore =
        new SemaphoreSlim(limits.MaxConcurrentOperations, limits.MaxConcurrentOperations)

    let statisticsLock = obj ()

    let mutable statistics =
        { CurrentMemoryMB = 0L
          PeakMemoryMB = 0L
          ActiveOperations = 0
          TotalOperationsCompleted = 0L
          AverageExecutionTimeMs = 0.0
          OpenFileHandles = 0
          ActiveNetworkConnections = 0
          LastUpdated = DateTime.UtcNow }

    let mutable totalExecutionTimeMs = 0L
    let mutable isMonitoring = false
    let mutable monitoringCancellation: CancellationTokenSource option = None

    /// スレッドセーフな統計更新
    let updateStatistics (updater: ResourceStatistics -> ResourceStatistics) =
        lock statisticsLock (fun () -> statistics <- updater statistics)

    /// メモリ使用量チェック
    let checkMemoryUsage () =
        let currentMemoryBytes = GC.GetTotalMemory(false)
        let currentMemoryMB = currentMemoryBytes / (1024L * 1024L)

        updateStatistics (fun stats ->
            { stats with
                CurrentMemoryMB = currentMemoryMB
                PeakMemoryMB = max stats.PeakMemoryMB currentMemoryMB
                LastUpdated = DateTime.UtcNow })

        if currentMemoryMB > limits.MaxMemoryMB then
            // 強制ガベージコレクション実行
            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()

            let afterGCMemoryMB = GC.GetTotalMemory(true) / (1024L * 1024L)

            if afterGCMemoryMB > limits.MaxMemoryMB then
                let errorMsg = $"メモリ制限超過: {afterGCMemoryMB}MB > {limits.MaxMemoryMB}MB"
                logError "ResourceGuard" errorMsg
                raise (InvalidOperationException(errorMsg))

    /// プロセス統計取得
    let getProcessStatistics () =
        try
            let currentProcess = Process.GetCurrentProcess()
            let handleCount = currentProcess.HandleCount
            // ネットワーク接続数は簡易版として0を返す（実装は環境依存）
            let networkConnections = 0

            updateStatistics (fun stats ->
                { stats with
                    OpenFileHandles = handleCount
                    ActiveNetworkConnections = networkConnections
                    LastUpdated = DateTime.UtcNow })

        with ex ->
            logWarning "ResourceGuard" $"プロセス統計取得エラー: {ex.Message}"

    /// リソース監視ループ
    let resourceMonitoringLoop (cancellationToken: CancellationToken) =
        async {
            logInfo "ResourceGuard" "リソース監視開始"

            try
                while not cancellationToken.IsCancellationRequested do
                    checkMemoryUsage ()
                    getProcessStatistics ()

                    // 監視間隔（5秒）
                    do! Async.Sleep(5000)

            with
            | :? OperationCanceledException -> logInfo "ResourceGuard" "リソース監視停止（キャンセル）"
            | ex -> logError "ResourceGuard" $"リソース監視エラー: {ex.Message}"
        }

    /// 保護された操作実行
    member this.ExecuteWithProtection<'T>(operationName: string, operation: unit -> Async<'T>) =
        async {
            // 同時実行数制限
            let! acquired = operationSemaphore.WaitAsync(limits.MaxExecutionTimeMs) |> Async.AwaitTask

            if not acquired then
                let errorMsg =
                    $"操作 '{operationName}': 同時実行数制限に達しました ({limits.MaxConcurrentOperations})"

                logWarning "ResourceGuard" errorMsg
                return Error errorMsg
            else
                try
                    updateStatistics (fun stats ->
                        { stats with
                            ActiveOperations = stats.ActiveOperations + 1 })

                    let stopwatch = Stopwatch.StartNew()

                    try
                        // メモリチェック
                        checkMemoryUsage ()

                        // タイムアウト付き実行
                        let! result =
                            Async.StartAsChild(operation (), limits.MaxExecutionTimeMs)
                            |> Async.Bind Async.AwaitTask

                        stopwatch.Stop()

                        // 統計更新
                        updateStatistics (fun stats ->
                            let newTotalCompleted = stats.TotalOperationsCompleted + 1L
                            let newTotalTime = totalExecutionTimeMs + stopwatch.ElapsedMilliseconds
                            totalExecutionTimeMs <- newTotalTime

                            { stats with
                                ActiveOperations = stats.ActiveOperations - 1
                                TotalOperationsCompleted = newTotalCompleted
                                AverageExecutionTimeMs = float newTotalTime / float newTotalCompleted
                                LastUpdated = DateTime.UtcNow })

                        logInfo "ResourceGuard" $"操作 '{operationName}' 完了: {stopwatch.ElapsedMilliseconds}ms"
                        return Ok result

                    with
                    | :? TimeoutException ->
                        stopwatch.Stop()

                        updateStatistics (fun stats ->
                            { stats with
                                ActiveOperations = stats.ActiveOperations - 1 })

                        let errorMsg = $"操作 '{operationName}': タイムアウト ({limits.MaxExecutionTimeMs}ms)"
                        logWarning "ResourceGuard" errorMsg
                        return Error errorMsg

                    | ex ->
                        stopwatch.Stop()

                        updateStatistics (fun stats ->
                            { stats with
                                ActiveOperations = stats.ActiveOperations - 1 })

                        let errorMsg = $"操作 '{operationName}': 実行失敗 - {ex.Message}"
                        logError "ResourceGuard" errorMsg
                        return Error errorMsg

                finally
                    operationSemaphore.Release() |> ignore

        }

    /// リソース監視開始
    member this.StartMonitoring() =
        if not isMonitoring then
            isMonitoring <- true
            let cts = new CancellationTokenSource()
            monitoringCancellation <- Some cts

            Async.Start(resourceMonitoringLoop cts.Token, cts.Token)
            logInfo "ResourceGuard" "リソース監視開始"

    /// リソース監視停止
    member this.StopMonitoring() =
        if isMonitoring then
            isMonitoring <- false

            match monitoringCancellation with
            | Some cts ->
                cts.Cancel()
                cts.Dispose()
                monitoringCancellation <- None
            | None -> ()

            logInfo "ResourceGuard" "リソース監視停止"

    /// 現在の統計取得
    member this.GetStatistics() =
        lock statisticsLock (fun () -> statistics)

    /// リソース制限取得
    member this.GetLimits() = limits

    /// 緊急リソース解放
    member this.EmergencyResourceCleanup() =
        try
            logWarning "ResourceGuard" "緊急リソース解放実行"

            // 強制ガベージコレクション
            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()

            // 統計リセット
            updateStatistics (fun stats ->
                { stats with
                    CurrentMemoryMB = GC.GetTotalMemory(true) / (1024L * 1024L)
                    LastUpdated = DateTime.UtcNow })

            logInfo "ResourceGuard" "緊急リソース解放完了"

        with ex ->
            logError "ResourceGuard" $"緊急リソース解放エラー: {ex.Message}"

    /// リソース解放
    member this.Dispose() =
        try
            this.StopMonitoring()
            operationSemaphore.Dispose()

            match monitoringCancellation with
            | Some cts -> cts.Dispose()
            | None -> ()

            logInfo "ResourceGuard" "ResourceGuard disposed"
        with ex ->
            logError "ResourceGuard" $"ResourceGuard Dispose例外: {ex.Message}"

    interface IDisposable with
        member this.Dispose() = this.Dispose()

/// リソース制限ファクトリー
module ResourceLimitsFactory =

    /// 開発環境用制限
    let development =
        { MaxMemoryMB = 512L
          MaxConcurrentOperations = 10
          MaxExecutionTimeMs = 30000
          MaxFileHandles = 100
          MaxNetworkConnections = 50 }

    /// 本番環境用制限
    let production =
        { MaxMemoryMB = 2048L
          MaxConcurrentOperations = 50
          MaxExecutionTimeMs = 60000
          MaxFileHandles = 1000
          MaxNetworkConnections = 500 }

    /// テスト環境用制限
    let testing =
        { MaxMemoryMB = 256L
          MaxConcurrentOperations = 5
          MaxExecutionTimeMs = 10000
          MaxFileHandles = 50
          MaxNetworkConnections = 20 }

    /// CI環境用制限
    let ci =
        { MaxMemoryMB = 128L
          MaxConcurrentOperations = 3
          MaxExecutionTimeMs = 5000
          MaxFileHandles = 30
          MaxNetworkConnections = 10 }

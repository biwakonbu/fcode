module FCode.PerformanceOptimizer

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open FCode.Logger
open FCode.InputValidation

// ===============================================
// パフォーマンス最適化システム
// ===============================================

/// パフォーマンスメトリクス
type PerformanceMetrics =
    { OperationName: string
      ExecutionTimeMs: int64
      MemoryUsageMB: int64
      CpuUsagePercent: float
      Timestamp: DateTime
      ThreadId: int
      IsOptimized: bool }

/// 最適化戦略
type OptimizationStrategy =
    | Batching of batchSize: int * timeoutMs: int
    | Caching of ttlSeconds: int * maxItems: int
    | Pooling of poolSize: int * maxLifetime: TimeSpan
    | Throttling of maxConcurrent: int * rateLimitPerSecond: int
    | LazyLoading of threshold: int

/// パフォーマンス閾値設定
type PerformanceThresholds =
    { MaxExecutionTimeMs: int64
      MaxMemoryUsageMB: int64
      MaxCpuUsagePercent: float
      MaxConcurrentOperations: int
      WarningThresholdPercent: float } // 閾値の何%で警告を出すか

// ===============================================
// パフォーマンス監視システム
// ===============================================

/// パフォーマンス監視クラス
type PerformanceMonitor() =
    let metrics = ConcurrentQueue<PerformanceMetrics>()
    let maxMetricsHistory = 1000
    let performanceCounters = System.Collections.Generic.Dictionary<string, int64>()
    let lockObj = obj ()

    /// パフォーマンスメトリクス記録
    member this.RecordMetrics(operationName: string, executionTime: TimeSpan, isOptimized: bool) =
        let currentProcess = Process.GetCurrentProcess()
        let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)

        let metric =
            { OperationName = operationName
              ExecutionTimeMs = int64 executionTime.TotalMilliseconds
              MemoryUsageMB = memoryMB
              CpuUsagePercent = 0.0 // 簡略化のため0.0に設定
              Timestamp = DateTime.Now
              ThreadId = Thread.CurrentThread.ManagedThreadId
              IsOptimized = isOptimized }

        metrics.Enqueue(metric)

        // 履歴サイズ制限
        while metrics.Count > maxMetricsHistory do
            metrics.TryDequeue() |> ignore

        // カウンター更新
        lock lockObj (fun () ->
            let optimizedStr = if isOptimized then "optimized" else "standard"
            let key = $"{operationName}_{optimizedStr}"

            let currentValue =
                if performanceCounters.ContainsKey(key) then
                    performanceCounters.[key]
                else
                    0L

            performanceCounters.[key] <- currentValue + 1L)

        logDebug
            "PerformanceOptimizer"
            $"Performance recorded: {operationName} - {executionTime.TotalMilliseconds:F2}ms (optimized: {isOptimized})"

    /// 閾値チェック
    member this.CheckThresholds(thresholds: PerformanceThresholds) : ValidationResult<unit> =
        let recentMetrics =
            metrics.ToArray()
            |> Array.filter (fun m -> DateTime.Now - m.Timestamp < TimeSpan.FromMinutes(5.0))

        if recentMetrics.Length = 0 then
            Valid()
        else
            let avgExecutionTime =
                recentMetrics |> Array.averageBy (fun m -> float m.ExecutionTimeMs)

            let maxMemoryUsage =
                recentMetrics
                |> Array.maxBy (fun m -> m.MemoryUsageMB)
                |> fun m -> m.MemoryUsageMB

            let avgCpuUsage = recentMetrics |> Array.averageBy (fun m -> m.CpuUsagePercent)

            let errors =
                [ if avgExecutionTime > float thresholds.MaxExecutionTimeMs then
                      let avgTimeMsg = $"平均実行時間が閾値を超過: {avgExecutionTime:F2}ms"
                      let thresholdMsg = $"{thresholds.MaxExecutionTimeMs}ms"
                      yield $"{avgTimeMsg} > {thresholdMsg}"

                  if maxMemoryUsage > thresholds.MaxMemoryUsageMB then
                      let memMsg = $"メモリ使用量が閾値を超過: {maxMemoryUsage}MB"
                      let thresholdMsg = $"{thresholds.MaxMemoryUsageMB}MB"
                      yield $"{memMsg} > {thresholdMsg}"

                  if avgCpuUsage > thresholds.MaxCpuUsagePercent then
                      let cpuMsg = $"CPU使用率が閾値を超過: {avgCpuUsage:F2}"
                      let cpuThresholdMsg = $"{thresholds.MaxCpuUsagePercent}"
                      yield $"{cpuMsg}%% > {cpuThresholdMsg}%%" ]

            if errors.Length > 0 then Invalid errors else Valid()

    /// パフォーマンス統計取得
    member this.GetStatistics(operationName: string option) : string * string * string =
        let filteredMetrics =
            metrics.ToArray()
            |> Array.filter (fun m ->
                match operationName with
                | Some name -> m.OperationName = name
                | None -> true)

        if filteredMetrics.Length = 0 then
            ("統計なし", "統計なし", "統計なし")
        else
            let avgTime = filteredMetrics |> Array.averageBy (fun m -> float m.ExecutionTimeMs)

            let optimizedCount =
                filteredMetrics |> Array.filter (fun m -> m.IsOptimized) |> Array.length

            let optimizationRate = (float optimizedCount / float filteredMetrics.Length) * 100.0

            let ratePart = $"最適化率: {optimizationRate:F1}" + "%"
            let countPart = $"({optimizedCount}/{filteredMetrics.Length})"
            ($"平均実行時間: {avgTime:F2}ms", $"{ratePart} {countPart}", $"総実行回数: {filteredMetrics.Length}")

// ===============================================
// バッチ処理最適化システム
// ===============================================

/// バッチ処理最適化クラス
type BatchProcessor<'T>() =
    let pendingItems = ConcurrentQueue<'T>()
    let mutable isProcessing = false
    let lockObj = obj ()

    /// バッチサイズ動的調整
    member this.CalculateOptimalBatchSize(itemCount: int, targetProcessingTimeMs: int) : int =
        let baseBatchSize = 50

        let adjustmentFactor =
            match itemCount with
            | x when x < 100 -> 1.0
            | x when x < 1000 -> 1.5
            | x when x < 10000 -> 2.0
            | _ -> 2.5

        let adjustedSize = int (float baseBatchSize * adjustmentFactor)
        Math.Min(adjustedSize, itemCount)

    /// バッチ処理実行
    member this.ProcessBatch
        (items: 'T[], processor: 'T[] -> Result<unit, string>, batchSize: int)
        : Result<unit, string> =
        if items.Length = 0 then
            Result.Ok()
        else
            let chunks = items |> Array.chunkBySize batchSize

            let mutable errors = []
            let sw = Stopwatch.StartNew()

            for chunk in chunks do
                match processor chunk with
                | Result.Ok() -> ()
                | Result.Error error -> errors <- error :: errors

            sw.Stop()

            if errors.Length > 0 then
                Result.Error(String.concat "; " errors)
            else
                logInfo
                    "PerformanceOptimizer"
                    $"Batch processing completed: {chunks.Length} batches, {items.Length} items in {sw.ElapsedMilliseconds}ms"

                Result.Ok()

// ===============================================
// キャッシュ最適化システム
// ===============================================

/// キャッシュエントリ
type CacheEntry<'T> =
    { Value: 'T
      ExpiryTime: DateTime
      AccessCount: int64
      LastAccessed: DateTime }

/// LRUキャッシュ最適化クラス
type OptimizedCache<'K, 'V when 'K: comparison>() =
    let cache = ConcurrentDictionary<'K, CacheEntry<'V>>()
    let accessOrder = ConcurrentQueue<'K>()
    let maxSize = 1000
    let defaultTtl = TimeSpan.FromMinutes(30.0)

    /// キャッシュアイテム取得
    member this.Get(key: 'K) : 'V option =
        match cache.TryGetValue(key) with
        | true, entry when entry.ExpiryTime > DateTime.Now ->
            // アクセス情報更新
            let updatedEntry =
                { entry with
                    AccessCount = entry.AccessCount + 1L
                    LastAccessed = DateTime.Now }

            cache.[key] <- updatedEntry
            Some entry.Value
        | true, _ ->
            // 期限切れエントリ削除
            cache.TryRemove(key) |> ignore
            None
        | false, _ -> None

    /// キャッシュアイテム設定
    member this.Set(key: 'K, value: 'V, ?ttl: TimeSpan) : unit =
        let expiryTime = DateTime.Now + (ttl |> Option.defaultValue defaultTtl)

        let entry =
            { Value = value
              ExpiryTime = expiryTime
              AccessCount = 1L
              LastAccessed = DateTime.Now }

        cache.[key] <- entry
        accessOrder.Enqueue(key)

        // サイズ制限チェック
        this.EvictIfNecessary()

    /// 必要に応じてアイテム削除
    member private this.EvictIfNecessary() : unit =
        while cache.Count > maxSize do
            match accessOrder.TryDequeue() with
            | true, oldestKey -> cache.TryRemove(oldestKey) |> ignore
            | false, _ -> ()

    /// キャッシュ統計取得
    member this.GetStatistics() : string =
        let totalEntries = cache.Count

        let expiredCount =
            cache.Values
            |> Seq.filter (fun entry -> entry.ExpiryTime <= DateTime.Now)
            |> Seq.length

        let avgAccessCount =
            if totalEntries > 0 then
                cache.Values |> Seq.averageBy (fun entry -> float entry.AccessCount)
            else
                0.0

        $"キャッシュエントリ数: {totalEntries}, 期限切れ: {expiredCount}, 平均アクセス回数: {avgAccessCount:F1}"

// ===============================================
// 並行処理最適化システム
// ===============================================

/// 並行処理最適化クラス
type ConcurrencyOptimizer() =
    let semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2)
    let rateLimiter = new SemaphoreSlim(100) // 1秒間に100リクエスト

    /// 最適な並行度計算
    member this.CalculateOptimalConcurrency(taskComplexity: string) : int =
        let processorCount = Environment.ProcessorCount

        match taskComplexity.ToLowerInvariant() with
        | "io" -> processorCount * 4 // IO集約的タスク
        | "cpu" -> processorCount // CPU集約的タスク
        | "mixed" -> processorCount * 2 // 混合タスク
        | _ -> processorCount

    /// レート制限付き並行実行
    member this.ExecuteConcurrentlyWithLimit<'T>
        (items: 'T[], processor: 'T -> Task<Result<unit, string>>, maxConcurrency: int)
        : Task<Result<unit, string>> =
        task {
            use semaphore = new SemaphoreSlim(maxConcurrency)
            let errors = ConcurrentQueue<string>()

            let tasks =
                items
                |> Array.map (fun item ->
                    task {
                        do! semaphore.WaitAsync()

                        try
                            let! result = processor item

                            match result with
                            | Result.Ok() -> ()
                            | Result.Error error -> errors.Enqueue(error)
                        finally
                            semaphore.Release() |> ignore
                    })

            let! _ = Task.WhenAll(tasks)

            if errors.Count > 0 then
                let allErrors = errors.ToArray() |> String.concat "; "
                return Result.Error allErrors
            else
                return Result.Ok()
        }

// ===============================================
// メモリ最適化システム
// ===============================================

/// メモリ最適化クラス
type MemoryOptimizer() =

    /// メモリ使用量監視
    member this.MonitorMemoryUsage() : int64 * string =
        let currentProcess = Process.GetCurrentProcess()
        let workingSetMB = currentProcess.WorkingSet64 / (1024L * 1024L)
        let privateMemoryMB = currentProcess.PrivateMemorySize64 / (1024L * 1024L)

        GC.Collect(0, GCCollectionMode.Optimized) // 軽量なGC実行

        let status =
            match workingSetMB with
            | x when x < 100L -> "最適"
            | x when x < 500L -> "良好"
            | x when x < 1000L -> "注意"
            | _ -> "警告"

        (workingSetMB, $"作業セット: {workingSetMB}MB, プライベート: {privateMemoryMB}MB, 状態: {status}")

    /// オブジェクトプール最適化
    member this.OptimizeObjectPool<'T when 'T: (new: unit -> 'T) and 'T :> IDisposable>(poolSize: int) =
        let pool = ConcurrentQueue<'T>()
        let createdCount = ref 0
        let borrowedCount = ref 0
        let returnedCount = ref 0

        let borrowObject () =
            match pool.TryDequeue() with
            | true, obj ->
                Interlocked.Increment(borrowedCount) |> ignore
                obj
            | false, _ ->
                Interlocked.Increment(createdCount) |> ignore
                Interlocked.Increment(borrowedCount) |> ignore
                new 'T()

        let returnObject (obj: 'T) =
            if pool.Count < poolSize then
                pool.Enqueue(obj)
                Interlocked.Increment(returnedCount) |> ignore
            else
                obj.Dispose()

        let getStatistics () =
            $"作成: {!createdCount}, 借用: {!borrowedCount}, 返却: {!returnedCount}, プール内: {pool.Count}"

        (borrowObject, returnObject, getStatistics)

// ===============================================
// 統合パフォーマンス最適化管理
// ===============================================

/// 統合パフォーマンス最適化管理クラス
type PerformanceOptimizationManager() =
    let monitor = PerformanceMonitor()
    let batchProcessor = BatchProcessor<obj>()
    let cache = OptimizedCache<string, obj>()
    let concurrencyOptimizer = ConcurrencyOptimizer()
    let memoryOptimizer = MemoryOptimizer()

    let defaultThresholds =
        { MaxExecutionTimeMs = 5000L
          MaxMemoryUsageMB = 1000L
          MaxCpuUsagePercent = 80.0
          MaxConcurrentOperations = Environment.ProcessorCount * 4
          WarningThresholdPercent = 75.0 }

    /// 操作の最適化実行
    member this.OptimizedExecute<'T>
        (operationName: string, operation: unit -> 'T, strategy: OptimizationStrategy option)
        : Result<'T, string> =
        let sw = Stopwatch.StartNew()

        try
            // 最適化戦略適用
            let result =
                match strategy with
                | Some(Caching(ttl, _)) ->
                    // キャッシュ戦略
                    match cache.Get(operationName) with
                    | Some cachedResult -> cachedResult :?> 'T
                    | None ->
                        let result = operation ()
                        cache.Set(operationName, box result, TimeSpan.FromSeconds(float ttl))
                        result
                | _ ->
                    // 標準実行
                    operation ()

            sw.Stop()
            monitor.RecordMetrics(operationName, sw.Elapsed, strategy.IsSome)

            // 閾値チェック
            match monitor.CheckThresholds(defaultThresholds) with
            | Valid _ -> Result.Ok result
            | Invalid warnings ->
                for warning in warnings do
                    logWarning "PerformanceOptimizer" warning

                Result.Ok result

        with ex ->
            sw.Stop()
            monitor.RecordMetrics(operationName, sw.Elapsed, false)
            let errorMsg = $"最適化実行エラー: {ex.Message}"
            logError "PerformanceOptimizer" errorMsg
            Result.Error errorMsg

    /// パフォーマンス診断実行
    member this.RunDiagnostics() : string =
        let (memUsage, memStatus) = memoryOptimizer.MonitorMemoryUsage()
        let (avgTime, optimizationRate, totalExec) = monitor.GetStatistics(None)
        let cacheStats = cache.GetStatistics()
        let timeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")

        let diagnosticReport =
            [ "=== パフォーマンス診断レポート ==="
              ""
              "メモリ使用状況:"
              $"  {memStatus}"
              ""
              "実行統計:"
              $"  {avgTime}"
              $"  {optimizationRate}"
              $"  {totalExec}"
              ""
              "キャッシュ統計:"
              $"  {cacheStats}"
              ""
              $"診断実行時刻: {timeStr}" ]

        String.concat Environment.NewLine diagnosticReport

// ===============================================
// グローバルインスタンス
// ===============================================

/// グローバル最適化管理インスタンス
let performanceManager = PerformanceOptimizationManager()

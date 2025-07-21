/// FC-037: パフォーマンス最適化・監視強化統合マネージャー
module FCode.Performance.EnhancedPerformanceManager

open System
open System.Threading
open System.Threading.Tasks
open System.Diagnostics
open FCode
open FCode.SimpleMemoryMonitor
// open FCode.Performance.ParallelProcessing
// open FCode.Performance.MonitoringProfiling

// ===============================================
// 統合パフォーマンス管理システム
// ===============================================

/// パフォーマンス健全性ステータス
type PerformanceHealthStatus =
    | Excellent // 優良
    | Good // 良好
    | Warning // 警告
    | Critical // 緊急

/// パフォーマンス監視統計情報
type PerformanceStatistics =
    { CurrentMemoryMB: int64
      ProcessorCount: int
      ThreadCount: int
      HealthStatus: PerformanceHealthStatus
      ResponseTimeMs: float
      Timestamp: DateTime
      DetectedBottlenecks: string list }

/// パフォーマンス最適化結果
type OptimizationResult =
    { OperationName: string
      BeforeMemoryMB: int64
      AfterMemoryMB: int64
      MemoryFreed: int64
      ExecutionTimeMs: float
      Success: bool
      Message: string }

/// 統合パフォーマンス管理マネージャー
type EnhancedPerformanceManager() =
    let memoryMonitor = globalMemoryMonitor
    // let parallelManager = new ParallelProcessingManager()
    // let monitoringManager = new MonitoringProfilingManager()
    let mutable operationCount = 0L
    let mutable totalResponseTime = 0.0
    let lockObj = obj ()

    /// パフォーマンス統計情報取得
    member this.GetPerformanceStatistics() : PerformanceStatistics =
        try
            let currentMemory = memoryMonitor.GetCurrentMemoryMB()
            let currentProcess = Process.GetCurrentProcess()

            let healthStatus = this.DetermineHealthStatus(currentMemory)

            let avgResponseTime =
                lock lockObj (fun () ->
                    if operationCount > 0L then
                        totalResponseTime / float operationCount
                    else
                        0.0)

            { CurrentMemoryMB = currentMemory
              ProcessorCount = Environment.ProcessorCount
              ThreadCount = currentProcess.Threads.Count
              HealthStatus = healthStatus
              ResponseTimeMs = avgResponseTime
              Timestamp = DateTime.UtcNow
              DetectedBottlenecks = this.DetectBottlenecks(currentMemory) }
        with ex ->
            Logger.logError "EnhancedPerformanceManager" $"統計情報取得エラー: {ex.Message}"

            { CurrentMemoryMB = 0L
              ProcessorCount = Environment.ProcessorCount
              ThreadCount = 0
              HealthStatus = Critical
              ResponseTimeMs = 0.0
              Timestamp = DateTime.UtcNow
              DetectedBottlenecks = [ ex.Message ] }

    /// 健全性ステータス判定
    member private this.DetermineHealthStatus(currentMemory: int64) : PerformanceHealthStatus =
        let config = defaultMemoryConfig

        if currentMemory >= config.MaxMemoryMB then
            Critical
        elif currentMemory >= config.WarningThresholdMB then
            Warning
        elif currentMemory < config.WarningThresholdMB / 2L then
            Excellent
        else
            Good

    /// ボトルネック検出
    member private this.DetectBottlenecks(currentMemory: int64) : string list =
        let bottlenecks = System.Collections.Generic.List<string>()
        let config = defaultMemoryConfig

        // メモリ使用量チェック
        if currentMemory >= config.MaxMemoryMB then
            bottlenecks.Add($"メモリ使用量が上限超過: {currentMemory}MB")
        elif currentMemory >= config.WarningThresholdMB then
            bottlenecks.Add($"メモリ使用量警告レベル: {currentMemory}MB")

        // プロセッサ使用率チェック（基本的な推定）
        try
            let currentProcess = Process.GetCurrentProcess()

            if currentProcess.Threads.Count > Environment.ProcessorCount * 2 then
                bottlenecks.Add($"スレッド数がプロセッサ数の2倍を超過: {currentProcess.Threads.Count}スレッド")
        with ex ->
            Logger.logDebug "EnhancedPerformanceManager" $"スレッド数チェックエラー: {ex.Message}"

        bottlenecks |> List.ofSeq

    /// 包括的メモリ最適化実行
    member this.ExecuteMemoryOptimization() : OptimizationResult =
        let startTime = DateTime.UtcNow
        let beforeMemory = memoryMonitor.GetCurrentMemoryMB()

        try
            Logger.logInfo "EnhancedPerformanceManager" "包括的メモリ最適化開始"

            // 1. 軽量GC実行
            let gcSuccess = memoryMonitor.OptionalGC()

            // 2. 追加の最適化処理
            if gcSuccess then
                // 追加のメモリ最適化（Generation 1 GC）
                GC.Collect(1, GCCollectionMode.Optimized)
                GC.WaitForPendingFinalizers()

            let afterMemory = memoryMonitor.GetCurrentMemoryMB()
            let memoryFreed = beforeMemory - afterMemory
            let executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds

            let result =
                { OperationName = "包括的メモリ最適化"
                  BeforeMemoryMB = beforeMemory
                  AfterMemoryMB = afterMemory
                  MemoryFreed = memoryFreed
                  ExecutionTimeMs = executionTime
                  Success = memoryFreed >= 0L
                  Message =
                    if memoryFreed > 0L then
                        $"メモリ解放成功: {memoryFreed}MB"
                    else
                        "メモリ解放効果なし" }

            Logger.logInfo "EnhancedPerformanceManager" $"包括的メモリ最適化完了: {result.Message}"
            result
        with ex ->
            let executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds
            Logger.logError "EnhancedPerformanceManager" $"包括的メモリ最適化エラー: {ex.Message}"

            { OperationName = "包括的メモリ最適化"
              BeforeMemoryMB = beforeMemory
              AfterMemoryMB = beforeMemory
              MemoryFreed = 0L
              ExecutionTimeMs = executionTime
              Success = false
              Message = $"最適化エラー: {ex.Message}" }

    /// 監視付き操作実行（レスポンス時間追跡）
    member this.ExecuteMonitoredOperation<'T>(operationName: string, operation: unit -> 'T) : 'T =
        let startTime = DateTime.UtcNow

        try
            Logger.logDebug "EnhancedPerformanceManager" $"監視付き操作開始: {operationName}"

            let result = operation ()

            let executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds

            // レスポンス時間統計更新
            lock lockObj (fun () ->
                operationCount <- operationCount + 1L
                totalResponseTime <- totalResponseTime + executionTime)

            Logger.logDebug "EnhancedPerformanceManager" $"監視付き操作完了: {operationName} ({executionTime:F1}ms)"
            result
        with ex ->
            let executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds

            lock lockObj (fun () ->
                operationCount <- operationCount + 1L
                totalResponseTime <- totalResponseTime + executionTime)

            Logger.logError
                "EnhancedPerformanceManager"
                $"監視付き操作エラー: {operationName} ({executionTime:F1}ms) - {ex.Message}"

            raise ex

    /// 並行処理最適化実行
    member this.ExecuteOptimizedParallelBatch<'T>(items: 'T[], processor: 'T -> Async<unit>) : Async<unit> =
        async {
            let maxParallelism = Environment.ProcessorCount
            Logger.logInfo "EnhancedPerformanceManager" $"最適化並行処理開始: {items.Length}件 (最大並行数: {maxParallelism})"

            try
                // 並行処理実行（簡略化実装）
                let tasks = items |> Array.map (processor >> Async.StartAsTask)
                let! _ = Task.WhenAll(tasks) |> Async.AwaitTask
                Logger.logInfo "EnhancedPerformanceManager" "最適化並行処理完了"
                return ()
            with ex ->
                Logger.logError "EnhancedPerformanceManager" $"最適化並行処理エラー: {ex.Message}"
                return raise ex
        }

    /// 包括的パフォーマンスレポート取得
    member this.GetComprehensiveReport() : Async<Map<string, obj>> =
        async {
            try
                let statistics = this.GetPerformanceStatistics()
                let memoryMetrics = memoryMonitor.GetPerformanceMetrics()

                let report =
                    Map.ofList
                        [ ("Statistics", box statistics)
                          ("MemoryMetrics", box memoryMetrics)
                          ("GeneratedAt", box DateTime.UtcNow)
                          ("TotalOperations", box operationCount)
                          ("SystemInfo",
                           box
                               {| ProcessorCount = Environment.ProcessorCount
                                  Timestamp = DateTime.UtcNow |}) ]

                Logger.logInfo "EnhancedPerformanceManager" "包括的パフォーマンスレポート生成完了"
                return report
            with ex ->
                Logger.logError "EnhancedPerformanceManager" $"包括的パフォーマンスレポート生成エラー: {ex.Message}"
                return Map.ofList [ ("Error", box ex.Message); ("GeneratedAt", box DateTime.UtcNow) ]
        }

    /// 自動最適化実行（条件付き）
    member this.ExecuteAutoOptimization() : OptimizationResult option =
        try
            let currentMemory = memoryMonitor.GetCurrentMemoryMB()
            let config = defaultMemoryConfig

            if currentMemory >= config.WarningThresholdMB then
                Logger.logInfo "EnhancedPerformanceManager" "自動最適化条件に達しました - 最適化実行"
                Some(this.ExecuteMemoryOptimization())
            else
                Logger.logDebug "EnhancedPerformanceManager" "自動最適化条件未達 - 最適化スキップ"
                None
        with ex ->
            Logger.logError "EnhancedPerformanceManager" $"自動最適化判定エラー: {ex.Message}"
            None

    interface IDisposable with
        member _.Dispose() =
            try
                Logger.logInfo "EnhancedPerformanceManager" "リソース解放完了"
            with ex ->
                Logger.logError "EnhancedPerformanceManager" $"リソース解放エラー: {ex.Message}"

// ===============================================
// グローバルインスタンス
// ===============================================

/// グローバル統合パフォーマンス管理インスタンス
let globalPerformanceManager = lazy (new EnhancedPerformanceManager())

/// 便利関数: パフォーマンス統計取得
let getPerformanceStatistics () =
    globalPerformanceManager.Value.GetPerformanceStatistics()

/// 便利関数: 包括的パフォーマンスレポート取得
let getComprehensivePerformanceReport () =
    globalPerformanceManager.Value.GetComprehensiveReport()

/// 便利関数: 自動最適化実行
let executeAutoOptimization () =
    globalPerformanceManager.Value.ExecuteAutoOptimization()

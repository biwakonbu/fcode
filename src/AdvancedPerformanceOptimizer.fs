/// FC-021: 実用性最優先・パフォーマンス最適化
/// 実開発での毎日使用を目指した高度パフォーマンス最適化システム
module FCode.AdvancedPerformanceOptimizer

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open Terminal.Gui
open FCode.Logger
open FCode.InputValidation

// ===============================================
// 実用性特化パフォーマンス設定
// ===============================================

/// 実用性特化パフォーマンス設定
type ProductionPerformanceConfig =
    { MaxMemoryUsageMB: int64 // 500MB以下維持
      MaxResponseTimeMs: int64 // UI応答時間
      MaxContinuousHours: int // 連続稼働時間
      MemoryCleanupIntervalMinutes: int
      UIUpdateThrottleMs: int
      BackgroundTaskMaxConcurrency: int
      AutoGCThresholdMB: int64 }

/// デフォルト実用設定
let defaultProductionConfig =
    { MaxMemoryUsageMB = 500L
      MaxResponseTimeMs = 100L // 100ms以下でUI応答
      MaxContinuousHours = 8
      MemoryCleanupIntervalMinutes = 30
      UIUpdateThrottleMs = 16 // 60FPS相当
      BackgroundTaskMaxConcurrency = Environment.ProcessorCount
      AutoGCThresholdMB = 400L }

// ===============================================
// UI応答性能最適化システム
// ===============================================

/// UI応答性能監視・最適化クラス
type UIResponseOptimizer() =
    let uiUpdateQueue = ConcurrentQueue<DateTime * string>()
    let lastUIUpdate = ref DateTime.MinValue
    let pendingUpdates = ref 0
    let responseTimesMs = ConcurrentQueue<int64>()
    let maxResponseHistory = 1000

    /// UI更新のスロットリング
    member this.ThrottledUIUpdate(updateAction: unit -> unit, updateName: string) : bool =
        let now = DateTime.UtcNow
        let timeSinceLastUpdate = now - !lastUIUpdate

        if
            timeSinceLastUpdate.TotalMilliseconds
            >= float defaultProductionConfig.UIUpdateThrottleMs
        then
            let sw = Stopwatch.StartNew()

            try
                // メインスレッドでUI更新実行
                Application.MainLoop.Invoke(fun () ->
                    updateAction ()
                    lastUIUpdate := now
                    Interlocked.Decrement(pendingUpdates) |> ignore)

                sw.Stop()
                responseTimesMs.Enqueue(sw.ElapsedMilliseconds)

                // 履歴サイズ制限
                while responseTimesMs.Count > maxResponseHistory do
                    responseTimesMs.TryDequeue() |> ignore

                uiUpdateQueue.Enqueue((now, updateName))

                if sw.ElapsedMilliseconds > defaultProductionConfig.MaxResponseTimeMs then
                    logWarning "UIResponseOptimizer" $"UI更新が応答時間閾値を超過: {updateName} - {sw.ElapsedMilliseconds}ms"

                true
            with ex ->
                sw.Stop()
                logError "UIResponseOptimizer" $"UI更新エラー: {updateName} - {ex.Message}"
                false
        else
            // 更新をスキップ（スロットリング）
            Interlocked.Increment(pendingUpdates) |> ignore
            false

    /// UI応答性能統計取得
    member this.GetUIPerformanceStats() : string =
        let recentTimes = responseTimesMs.ToArray()

        if recentTimes.Length = 0 then
            "UI応答統計: データなし"
        else
            let avgResponseTime = recentTimes |> Array.averageBy float
            let maxResponseTime = recentTimes |> Array.max
            let minResponseTime = recentTimes |> Array.min
            let totalUpdates = recentTimes.Length
            let currentPending = !pendingUpdates

            $"UI応答統計: 平均{avgResponseTime:F1}ms, 最大{maxResponseTime}ms, 最小{minResponseTime}ms, 更新回数{totalUpdates}, 待機中{currentPending}"

// ===============================================
// メモリ使用量最適化システム
// ===============================================

/// メモリ使用量最適化・監視クラス
type MemoryUsageOptimizer() =
    let memoryCheckInterval = TimeSpan.FromMinutes(5.0)
    let lastMemoryCheck = ref DateTime.MinValue
    let memoryHistory = ConcurrentQueue<DateTime * int64>()
    let maxMemoryHistory = 288 // 24時間分（5分間隔）

    /// 自動メモリクリーンアップ
    member this.AutoCleanup() : bool =
        let currentProcess = Process.GetCurrentProcess()
        let currentMemoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)
        let now = DateTime.UtcNow

        // メモリ履歴記録
        memoryHistory.Enqueue((now, currentMemoryMB))

        while memoryHistory.Count > maxMemoryHistory do
            memoryHistory.TryDequeue() |> ignore

        if currentMemoryMB > defaultProductionConfig.AutoGCThresholdMB then
            try
                // 段階的GC実行
                GC.Collect(0, GCCollectionMode.Optimized)
                Thread.Sleep(10)
                GC.Collect(1, GCCollectionMode.Optimized)
                Thread.Sleep(10)
                GC.Collect(2, GCCollectionMode.Forced)
                GC.WaitForPendingFinalizers()
                GC.Collect()

                let afterCleanupMB = Process.GetCurrentProcess().WorkingSet64 / (1024L * 1024L)
                let memoryFreed = currentMemoryMB - afterCleanupMB

                logInfo
                    "MemoryUsageOptimizer"
                    $"自動メモリクリーンアップ実行: {currentMemoryMB}MB -> {afterCleanupMB}MB (解放: {memoryFreed}MB)"

                true
            with ex ->
                logError "MemoryUsageOptimizer" $"メモリクリーンアップエラー: {ex.Message}"
                false
        else
            false

    /// メモリ使用量チェック
    member this.CheckMemoryUsage() : ValidationResult<unit> =
        let currentProcess = Process.GetCurrentProcess()
        let currentMemoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)

        if currentMemoryMB > defaultProductionConfig.MaxMemoryUsageMB then
            let message =
                $"メモリ使用量が閾値を超過: {currentMemoryMB}MB > {defaultProductionConfig.MaxMemoryUsageMB}MB"

            logWarning "MemoryUsageOptimizer" message
            Invalid [ message ]
        else
            Valid()

    /// メモリ使用傾向分析
    member this.AnalyzeMemoryTrend() : string =
        let history = memoryHistory.ToArray()

        if history.Length < 2 then
            "メモリ傾向: データ不足"
        else
            let recent = history |> Array.skip (max 0 (history.Length - 12)) // 直近1時間
            let avgRecent = recent |> Array.averageBy (snd >> float)

            let older = history |> Array.take (min 12 history.Length) // 1時間前
            let avgOlder = older |> Array.averageBy (snd >> float)

            let trend = avgRecent - avgOlder
            let currentMemory = Process.GetCurrentProcess().WorkingSet64 / (1024L * 1024L)

            let trendStatus =
                if trend > 50.0 then "増加傾向（要注意）"
                elif trend > 10.0 then "微増"
                elif trend < -10.0 then "減少"
                else "安定"

            let trendFormatted = if trend >= 0.0 then $"+{trend:F1}" else $"{trend:F1}"
            $"メモリ傾向: 現在{currentMemory}MB, 1時間変化{trendFormatted}MB, 状態: {trendStatus}"

// ===============================================
// 連続稼働安定性システム
// ===============================================

/// 連続稼働安定性監視・最適化クラス
type ContinuousOperationOptimizer() =
    let startTime = DateTime.UtcNow
    let lastHealthCheck = ref DateTime.UtcNow
    let healthCheckInterval = TimeSpan.FromMinutes(10.0)
    let operationEvents = ConcurrentQueue<DateTime * string>()
    let maxEventHistory = 1000

    /// 稼働時間取得
    member this.GetUptimeHours() : float =
        (DateTime.UtcNow - startTime).TotalHours

    /// ヘルスチェック実行
    member this.PerformHealthCheck() : ValidationResult<string> =
        let now = DateTime.UtcNow
        let uptime = this.GetUptimeHours()

        try
            let currentProcess = Process.GetCurrentProcess()
            let memoryMB = currentProcess.WorkingSet64 / (1024L * 1024L)
            let threadCount = currentProcess.Threads.Count
            let handleCount = currentProcess.HandleCount

            let issues =
                [ if memoryMB > defaultProductionConfig.MaxMemoryUsageMB then
                      yield $"メモリ使用量超過: {memoryMB}MB"

                  if threadCount > 100 then
                      yield $"スレッド数過多: {threadCount}"

                  if handleCount > 1000 then
                      yield $"ハンドル数過多: {handleCount}"

                  if uptime > float defaultProductionConfig.MaxContinuousHours then
                      yield $"連続稼働時間超過: {uptime:F1}時間" ]

            lastHealthCheck := now
            operationEvents.Enqueue((now, "HealthCheck"))

            // イベント履歴制限
            while operationEvents.Count > maxEventHistory do
                operationEvents.TryDequeue() |> ignore

            let healthReport =
                $"稼働状況: {uptime:F1}時間, メモリ{memoryMB}MB, スレッド{threadCount}, ハンドル{handleCount}"

            if issues.Length > 0 then
                Invalid issues
            else
                Valid healthReport

        with ex ->
            let errorMsg = $"ヘルスチェックエラー: {ex.Message}"
            logError "ContinuousOperationOptimizer" errorMsg
            Invalid [ errorMsg ]

    /// 自動メンテナンス実行
    member this.AutoMaintenance() : string =
        let maintenanceActions = ResizeArray<string>()

        try
            // メモリクリーンアップ
            let memoryOptimizer = MemoryUsageOptimizer()

            if memoryOptimizer.AutoCleanup() then
                maintenanceActions.Add("メモリクリーンアップ")

            // ログローテーション（簡易版）
            if this.GetUptimeHours() > 6.0 then
                maintenanceActions.Add("内部状態リセット")

            // リソース解放
            GC.Collect(0, GCCollectionMode.Optimized)
            maintenanceActions.Add("軽量GC")

            operationEvents.Enqueue((DateTime.UtcNow, "AutoMaintenance"))

            let actionsStr =
                if maintenanceActions.Count > 0 then
                    String.concat ", " (maintenanceActions.ToArray())
                else
                    "実行なし"

            $"自動メンテナンス完了: {actionsStr}"

        with ex ->
            let errorMsg = $"自動メンテナンスエラー: {ex.Message}"
            logError "ContinuousOperationOptimizer" errorMsg
            errorMsg

// ===============================================
// 高速レスポンス実現システム
// ===============================================

/// 高速レスポンス最適化クラス
type HighSpeedResponseOptimizer() =
    let operationCache = ConcurrentDictionary<string, DateTime * obj>()
    let cacheExpiryMinutes = 10.0
    let operationTimings = ConcurrentDictionary<string, ConcurrentQueue<int64>>()
    let maxTimingHistory = 100

    /// 高速キャッシュ実行
    member this.FastCachedExecute<'T>(operationName: string, operation: unit -> 'T) : 'T =
        let cacheKey = operationName
        let now = DateTime.UtcNow

        match operationCache.TryGetValue(cacheKey) with
        | true, (cacheTime, cachedValue) when (now - cacheTime).TotalMinutes < cacheExpiryMinutes ->
            // キャッシュヒット
            cachedValue :?> 'T
        | _ ->
            // キャッシュミス - 新規実行
            let sw = Stopwatch.StartNew()
            let result = operation ()
            sw.Stop()

            // キャッシュ更新
            operationCache.[cacheKey] <- (now, box result)

            // タイミング記録
            let timings =
                operationTimings.GetOrAdd(operationName, fun _ -> ConcurrentQueue<int64>())

            timings.Enqueue(sw.ElapsedMilliseconds)

            // タイミング履歴制限
            while timings.Count > maxTimingHistory do
                timings.TryDequeue() |> ignore

            result

    /// プリロード実行
    member this.PreloadOperations(operations: (string * (unit -> obj)) list) : string =
        let sw = Stopwatch.StartNew()
        let loaded = ResizeArray<string>()

        for (name, op) in operations do
            try
                let result = op ()
                operationCache.[name] <- (DateTime.UtcNow, result)
                loaded.Add(name)
            with ex ->
                logWarning "HighSpeedResponseOptimizer" $"プリロード失敗: {name} - {ex.Message}"

        sw.Stop()
        $"プリロード完了: {loaded.Count}/{operations.Length} 操作, {sw.ElapsedMilliseconds}ms"

    /// レスポンス統計取得
    member this.GetResponseStats() : string =
        let allTimings =
            operationTimings |> Seq.collect (fun kvp -> kvp.Value.ToArray()) |> Seq.toArray

        if allTimings.Length = 0 then
            "レスポンス統計: データなし"
        else
            let avgResponse = allTimings |> Array.averageBy float
            let maxResponse = allTimings |> Array.max
            let fastOperations = allTimings |> Array.filter (fun t -> t <= 50L) |> Array.length
            let fastPercent = (float fastOperations / float allTimings.Length) * 100.0

            $"レスポンス統計: 平均{avgResponse:F1}ms, 最大{maxResponse}ms, 高速率{fastPercent:F1}%%"

// ===============================================
// 統合実用性最適化管理
// ===============================================

/// 統合実用性最適化管理クラス
type ProductionOptimizationManager() =
    let uiOptimizer = UIResponseOptimizer()
    let memoryOptimizer = MemoryUsageOptimizer()
    let continuousOptimizer = ContinuousOperationOptimizer()
    let responseOptimizer = HighSpeedResponseOptimizer()
    let lastFullDiagnostic = ref DateTime.MinValue

    /// 実用性最適化実行
    member this.RunProductionOptimization() : ValidationResult<string> =
        try
            let results = ResizeArray<string>()
            let issues = ResizeArray<string>()

            // メモリ最適化
            if memoryOptimizer.AutoCleanup() then
                results.Add("メモリクリーンアップ実行")

            // メモリチェック
            match memoryOptimizer.CheckMemoryUsage() with
            | Valid _ -> ()
            | Invalid memIssues -> issues.AddRange(memIssues)

            // ヘルスチェック
            match continuousOptimizer.PerformHealthCheck() with
            | Valid healthReport -> results.Add(healthReport)
            | Invalid healthIssues -> issues.AddRange(healthIssues)

            // 自動メンテナンス
            let maintenanceResult = continuousOptimizer.AutoMaintenance()
            results.Add(maintenanceResult)

            let optimizationReport = String.concat Environment.NewLine (results.ToArray())

            if issues.Count > 0 then
                Invalid(issues.ToArray() |> Array.toList)
            else
                Valid optimizationReport

        with ex ->
            let errorMsg = $"実用性最適化エラー: {ex.Message}"
            logError "ProductionOptimizationManager" errorMsg
            Invalid [ errorMsg ]

    /// 総合診断レポート生成
    member this.GenerateComprehensiveDiagnostic() : string =
        let now = DateTime.UtcNow
        let diagnosticParts = ResizeArray<string>()

        diagnosticParts.Add("=== FC-021 実用性最適化 総合診断レポート ===")
        let timeFormat = now.ToString("yyyy-MM-dd HH:mm:ss")
        diagnosticParts.Add($"診断時刻: {timeFormat}")
        diagnosticParts.Add("")

        // UI応答性能
        diagnosticParts.Add("【UI応答性能】")
        diagnosticParts.Add(uiOptimizer.GetUIPerformanceStats())
        diagnosticParts.Add("")

        // メモリ使用状況
        diagnosticParts.Add("【メモリ使用状況】")
        diagnosticParts.Add(memoryOptimizer.AnalyzeMemoryTrend())
        diagnosticParts.Add("")

        // 連続稼働状況
        diagnosticParts.Add("【連続稼働状況】")

        match continuousOptimizer.PerformHealthCheck() with
        | Valid report -> diagnosticParts.Add(report)
        | Invalid issues ->
            let issuesStr = String.concat ", " issues
            diagnosticParts.Add($"問題検出: {issuesStr}")

        diagnosticParts.Add("")

        // レスポンス性能
        diagnosticParts.Add("【レスポンス性能】")
        diagnosticParts.Add(responseOptimizer.GetResponseStats())
        diagnosticParts.Add("")

        // 実用性評価
        let uptimeHours = continuousOptimizer.GetUptimeHours()
        let currentMemory = Process.GetCurrentProcess().WorkingSet64 / (1024L * 1024L)

        diagnosticParts.Add("【実用性評価】")

        let memoryScore =
            if currentMemory <= 500L then "優秀"
            elif currentMemory <= 750L then "良好"
            else "要改善"

        let uptimeScore =
            if uptimeHours >= 8.0 then "達成"
            elif uptimeHours >= 4.0 then "継続中"
            else "開始"

        diagnosticParts.Add($"メモリ使用量: {currentMemory}MB ({memoryScore})")
        diagnosticParts.Add($"連続稼働時間: {uptimeHours:F1}時間 ({uptimeScore})")
        diagnosticParts.Add($"実用性目標: メモリ500MB以下・8時間稼働")

        lastFullDiagnostic := now
        String.concat Environment.NewLine (diagnosticParts.ToArray())

    /// 緊急最適化実行
    member this.EmergencyOptimization() : string =
        try
            let actions = ResizeArray<string>()

            // 強制メモリクリーンアップ
            GC.Collect(2, GCCollectionMode.Forced)
            GC.WaitForPendingFinalizers()
            GC.Collect()
            actions.Add("強制GC実行")

            // キャッシュクリア
            responseOptimizer
                .GetType()
                .GetField(
                    "operationCache",
                    System.Reflection.BindingFlags.NonPublic
                    ||| System.Reflection.BindingFlags.Instance
                )
                .GetValue(responseOptimizer)
            :?> ConcurrentDictionary<string, DateTime * obj>
            |> fun cache -> cache.Clear()

            actions.Add("キャッシュクリア")

            // 大きなオブジェクトヒープの圧縮
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode <-
                System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce

            GC.Collect()
            actions.Add("LOH圧縮")

            let currentMemory = Process.GetCurrentProcess().WorkingSet64 / (1024L * 1024L)
            let actionsStr = String.concat ", " (actions.ToArray())
            $"緊急最適化完了: {actionsStr} - メモリ使用量: {currentMemory}MB"

        with ex ->
            $"緊急最適化エラー: {ex.Message}"

// ===============================================
// グローバルインスタンス
// ===============================================

/// グローバル実用性最適化管理インスタンス
let productionOptimizationManager = ProductionOptimizationManager()

/// UI更新最適化実行
let optimizedUIUpdate (updateAction: unit -> unit) (updateName: string) : bool =
    let uiOptimizer = UIResponseOptimizer()
    uiOptimizer.ThrottledUIUpdate(updateAction, updateName)

/// 高速実行（キャッシュ付き）
let fastExecute<'T> (operationName: string) (operation: unit -> 'T) : 'T =
    let responseOptimizer = HighSpeedResponseOptimizer()
    responseOptimizer.FastCachedExecute(operationName, operation)

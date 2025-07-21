/// FC-037: パフォーマンス監視・診断UI統合
module FCode.Performance.PerformanceMonitoringUI

open System
open System.Threading
open FCode
open FCode.Performance.EnhancedPerformanceManager

// ===============================================
// パフォーマンス監視UI統合機能
// ===============================================

/// パフォーマンス監視UI表示データ
type PerformanceDisplayData =
    { MemoryStatus: string
      ResponseTime: string
      HealthIndicator: string
      OptimizationSuggestions: string list
      LastUpdated: DateTime }

/// リアルタイムパフォーマンス監視UI
type PerformanceMonitoringUI() =
    let mutable isMonitoring = false
    let mutable monitoringTimer: Timer option = None
    let lockObj = obj ()

    /// パフォーマンス表示データ生成
    member private this.GenerateDisplayData(statistics: PerformanceStatistics) : PerformanceDisplayData =
        let memoryStatus =
            $"{statistics.CurrentMemoryMB}MB ({this.FormatHealthStatus(statistics.HealthStatus)})"

        let responseTime = $"{statistics.ResponseTimeMs:F1}ms"
        let healthIndicator = this.FormatHealthIndicator(statistics.HealthStatus)
        let optimizationSuggestions = this.GenerateOptimizationSuggestions(statistics)

        { MemoryStatus = memoryStatus
          ResponseTime = responseTime
          HealthIndicator = healthIndicator
          OptimizationSuggestions = optimizationSuggestions
          LastUpdated = DateTime.UtcNow }

    /// 健全性ステータスフォーマット
    member private this.FormatHealthStatus(status: PerformanceHealthStatus) : string =
        match status with
        | Excellent -> "優良"
        | Good -> "良好"
        | Warning -> "警告"
        | Critical -> "緊急"

    /// 健全性インジケーターフォーマット
    member private this.FormatHealthIndicator(status: PerformanceHealthStatus) : string =
        match status with
        | Excellent -> "🟢 EXCELLENT"
        | Good -> "🟡 GOOD"
        | Warning -> "🟠 WARNING"
        | Critical -> "🔴 CRITICAL"

    /// 最適化提案生成
    member private this.GenerateOptimizationSuggestions(statistics: PerformanceStatistics) : string list =
        let suggestions = System.Collections.Generic.List<string>()

        match statistics.HealthStatus with
        | Critical ->
            suggestions.Add("緊急: メモリ最適化を実行してください")
            suggestions.Add("不要なプロセスを終了してください")
        | Warning ->
            suggestions.Add("メモリ使用量が警告レベルです")
            suggestions.Add("定期的なGC実行を推奨します")
        | Good -> suggestions.Add("良好な状態です")
        | Excellent -> suggestions.Add("優良な状態です - 現在の設定を維持")

        if statistics.ResponseTimeMs > 500.0 then
            suggestions.Add($"レスポンス時間が遅延しています: {statistics.ResponseTimeMs:F1}ms")

        if not (List.isEmpty statistics.DetectedBottlenecks) then
            suggestions.AddRange(statistics.DetectedBottlenecks |> List.map (fun b -> $"ボトルネック: {b}"))

        suggestions |> List.ofSeq

    /// リアルタイム監視開始
    member this.StartRealtimeMonitoring(intervalSeconds: int) : bool =
        lock lockObj (fun () ->
            if isMonitoring then
                Logger.logWarning "PerformanceMonitoringUI" "リアルタイム監視は既に実行中です"
                false
            else
                try
                    let interval = TimeSpan.FromSeconds(float intervalSeconds)
                    monitoringTimer <- Some(new Timer(this.MonitoringCallback, null, TimeSpan.Zero, interval))
                    isMonitoring <- true
                    Logger.logInfo "PerformanceMonitoringUI" $"リアルタイム監視開始: {intervalSeconds}秒間隔"
                    true
                with ex ->
                    Logger.logError "PerformanceMonitoringUI" $"リアルタイム監視開始エラー: {ex.Message}"
                    false)

    /// 監視コールバック関数
    member private this.MonitoringCallback(state: obj) : unit =
        try
            let statistics = globalPerformanceManager.Value.GetPerformanceStatistics()
            let displayData = this.GenerateDisplayData(statistics)

            // UI更新処理（実際のUI更新は別途実装される）
            this.UpdatePerformanceUI(displayData)

            // 自動最適化チェック
            match globalPerformanceManager.Value.ExecuteAutoOptimization() with
            | Some result -> Logger.logInfo "PerformanceMonitoringUI" $"自動最適化実行: {result.Message}"
            | None -> ()

        with ex ->
            Logger.logError "PerformanceMonitoringUI" $"監視コールバックエラー: {ex.Message}"

    /// パフォーマンスUI更新（実装依存）
    member private this.UpdatePerformanceUI(displayData: PerformanceDisplayData) : unit =
        // ここでは基本的なログ出力のみ実行
        // 実際のUI統合は Program.fs や具体的なUIコンポーネントで実装される
        Logger.logDebug
            "PerformanceMonitoringUI"
            $"パフォーマンス状況: {displayData.MemoryStatus} | レスポンス: {displayData.ResponseTime} | {displayData.HealthIndicator}"

        if not (List.isEmpty displayData.OptimizationSuggestions) then
            displayData.OptimizationSuggestions
            |> List.iter (fun suggestion -> Logger.logInfo "PerformanceMonitoringUI" $"提案: {suggestion}")

    /// リアルタイム監視停止
    member this.StopRealtimeMonitoring() : bool =
        lock lockObj (fun () ->
            if not isMonitoring then
                Logger.logWarning "PerformanceMonitoringUI" "リアルタイム監視は実行されていません"
                false
            else
                try
                    match monitoringTimer with
                    | Some timer ->
                        timer.Dispose()
                        monitoringTimer <- None
                    | None -> ()

                    isMonitoring <- false
                    Logger.logInfo "PerformanceMonitoringUI" "リアルタイム監視停止"
                    true
                with ex ->
                    Logger.logError "PerformanceMonitoringUI" $"リアルタイム監視停止エラー: {ex.Message}"
                    false)

    /// 現在の監視状況取得
    member this.GetMonitoringStatus() : bool * PerformanceDisplayData option =
        try
            let displayData =
                if isMonitoring then
                    let statistics = globalPerformanceManager.Value.GetPerformanceStatistics()
                    Some(this.GenerateDisplayData(statistics))
                else
                    None

            (isMonitoring, displayData)
        with ex ->
            Logger.logError "PerformanceMonitoringUI" $"監視状況取得エラー: {ex.Message}"
            (false, None)

    /// 手動パフォーマンスチェック実行
    member this.ExecuteManualPerformanceCheck() : PerformanceDisplayData =
        try
            let statistics = globalPerformanceManager.Value.GetPerformanceStatistics()
            let displayData = this.GenerateDisplayData(statistics)

            Logger.logInfo "PerformanceMonitoringUI" "手動パフォーマンスチェック実行完了"
            displayData
        with ex ->
            Logger.logError "PerformanceMonitoringUI" $"手動パフォーマンスチェックエラー: {ex.Message}"

            { MemoryStatus = "取得エラー"
              ResponseTime = "N/A"
              HealthIndicator = "🔴 ERROR"
              OptimizationSuggestions = [ ex.Message ]
              LastUpdated = DateTime.UtcNow }

    interface IDisposable with
        member this.Dispose() = this.StopRealtimeMonitoring() |> ignore

// ===============================================
// グローバルインスタンス
// ===============================================

/// グローバルパフォーマンス監視UI インスタンス
let globalPerformanceMonitoringUI = lazy (new PerformanceMonitoringUI())

/// 便利関数: リアルタイム監視開始
let startPerformanceMonitoring intervalSeconds =
    globalPerformanceMonitoringUI.Value.StartRealtimeMonitoring(intervalSeconds)

/// 便利関数: リアルタイム監視停止
let stopPerformanceMonitoring () =
    globalPerformanceMonitoringUI.Value.StopRealtimeMonitoring()

/// 便利関数: 手動パフォーマンスチェック
let executeManualPerformanceCheck () =
    globalPerformanceMonitoringUI.Value.ExecuteManualPerformanceCheck()

module FCode.ProgressDashboard

open System
open System.Collections.Concurrent
open Terminal.Gui
open NStack
open FCode.Logger
open FCode.AgentMessaging
open FCode.ColorSchemes
// ProgressAggregatorは後で統合予定のため、現在はコメントアウト
// open FCode.Collaboration.ProgressAggregator

// ===============================================
// 進捗ダッシュボード型定義
// ===============================================

/// メトリクス種別
type MetricType =
    | TaskCompletion // タスク完了率
    | CodeQuality // コード品質スコア
    | TestCoverage // テストカバレッジ
    | BuildSuccess // ビルド成功率
    | AgentEfficiency // エージェント効率性
    | CollaborationScore // 協調性スコア

/// ダッシュボードメトリクス
type DashboardMetric =
    { MetricId: string // メトリクス一意ID
      MetricType: MetricType // メトリクス種別
      Name: string // メトリクス名
      Value: float // 現在値
      Target: float // 目標値
      Unit: string // 単位
      LastUpdated: DateTime // 最終更新日時
      Trend: string // トレンド（up/down/stable）
      HistoricalData: (DateTime * float) list } // 履歴データ

/// KPI状態
type KPIStatus =
    | OnTrack // 順調
    | AtRisk // リスク
    | Behind // 遅延
    | Exceeded // 超過達成

/// ダッシュボードKPI
type DashboardKPI =
    { KPIId: string // KPI一意ID
      Name: string // KPI名
      Description: string // 説明
      CurrentValue: float // 現在値
      TargetValue: float // 目標値
      Status: KPIStatus // 状態
      Unit: string // 単位
      Period: string // 期間（daily/weekly/sprint）
      RelatedMetrics: string list // 関連メトリクスID
      LastUpdated: DateTime } // 最終更新日時

// ===============================================
// 内部責務分離型 (SOLID準拠設計)
// ===============================================

/// メトリクス管理責務 (Single Responsibility)
type private MetricsManager() =
    let metrics = ConcurrentDictionary<string, DashboardMetric>()
    let maxHistoryEntries = 50

    /// メトリクス一意ID生成
    member private this.GenerateMetricId() =
        let timestamp = DateTime.Now.ToString("yyMMdd-HHmmss")
        let guidPart = Guid.NewGuid().ToString("N")[..3]
        $"metric-{timestamp}-{guidPart}"

    /// 新規メトリクス作成 - 基本的な入力検証
    member this.CreateMetric
        (metricType: MetricType, name: string, value: float, target: float, unit: string)
        : Result<string, string> =
        try
            // 基本的な入力検証
            if String.IsNullOrWhiteSpace(name) then
                Result.Error "メトリクス名が無効です"
            elif value < 0.0 || value > 1000000.0 then
                Result.Error "メトリクス値が範囲外です"
            elif String.IsNullOrWhiteSpace(unit) then
                Result.Error "単位が無効です"
            else
                let metricId = this.GenerateMetricId()

                let metric =
                    { MetricId = metricId
                      MetricType = metricType
                      Name = name
                      Value = value
                      Target = target
                      Unit = unit
                      LastUpdated = DateTime.Now
                      Trend = "stable"
                      HistoricalData = [ (DateTime.Now, value) ] }

                metrics.[metricId] <- metric
                logInfo "ProgressDashboard" $"Metric created: {metricId} - {name}: {value} {unit}"
                Result.Ok metricId
        with ex ->
            let errorMsg = $"Failed to create metric: {ex.Message}"
            logError "ProgressDashboard" errorMsg
            Result.Error errorMsg

    /// メトリクス値更新
    member this.UpdateMetric(metricId: string, newValue: float) : Result<unit, string> =
        try
            match metrics.TryGetValue(metricId) with
            | true, metric ->
                let trend =
                    if newValue > metric.Value then "up"
                    elif newValue < metric.Value then "down"
                    else "stable"

                let newHistoryEntry = (DateTime.Now, newValue)

                let updatedHistory =
                    (newHistoryEntry :: metric.HistoricalData)
                    |> List.take (min maxHistoryEntries (List.length metric.HistoricalData + 1))

                let updatedMetric =
                    { metric with
                        Value = newValue
                        LastUpdated = DateTime.Now
                        Trend = trend
                        HistoricalData = updatedHistory }

                metrics.[metricId] <- updatedMetric

                logInfo
                    "ProgressDashboard"
                    $"Metric updated: {metricId} - {metric.Name}: {newValue} {metric.Unit} ({trend})"

                Result.Ok()
            | false, _ ->
                let errorMsg = $"Metric not found: {metricId}"
                logWarning "ProgressDashboard" errorMsg
                Result.Error errorMsg
        with ex ->
            let errorMsg = $"Failed to update metric: {ex.Message}"
            logError "ProgressDashboard" errorMsg
            Result.Error errorMsg

    member this.GetAllMetrics() = metrics.Values |> Seq.toArray
    member this.GetMetricCount() = metrics.Count
    member this.Clear() = metrics.Clear()

/// KPI管理責務 (Single Responsibility)
type private KPIManager() =
    let kpis = ConcurrentDictionary<string, DashboardKPI>()

    /// KPI一意ID生成
    member private this.GenerateKPIId() =
        let timestamp = DateTime.Now.ToString("yyMMdd-HHmmss")
        let guidPart = Guid.NewGuid().ToString("N")[..3]
        $"kpi-{timestamp}-{guidPart}"

    /// KPI状態計算
    member private this.CalculateKPIStatus(currentValue: float, targetValue: float) =
        let percentage = (currentValue / targetValue) * 100.0

        if percentage >= 100.0 then Exceeded
        elif percentage >= 90.0 then OnTrack
        elif percentage >= 70.0 then AtRisk
        else Behind

    /// 新規KPI作成
    member this.CreateKPI
        (
            name: string,
            description: string,
            currentValue: float,
            targetValue: float,
            unit: string,
            period: string,
            relatedMetrics: string list
        ) : Result<string, string> =
        try
            let kpiId = this.GenerateKPIId()
            let status = this.CalculateKPIStatus(currentValue, targetValue)

            let kpi =
                { KPIId = kpiId
                  Name = name
                  Description = description
                  CurrentValue = currentValue
                  TargetValue = targetValue
                  Status = status
                  Unit = unit
                  Period = period
                  RelatedMetrics = relatedMetrics
                  LastUpdated = DateTime.Now }

            kpis.[kpiId] <- kpi

            logInfo "ProgressDashboard" $"KPI created: {kpiId} - {name}: {currentValue}/{targetValue} {unit} ({status})"
            Result.Ok kpiId
        with ex ->
            let errorMsg = $"Failed to create KPI: {ex.Message}"
            logError "ProgressDashboard" errorMsg
            Result.Error errorMsg

    /// KPI値更新
    member this.UpdateKPI(kpiId: string, newCurrentValue: float) : Result<unit, string> =
        try
            match kpis.TryGetValue(kpiId) with
            | true, kpi ->
                let newStatus = this.CalculateKPIStatus(newCurrentValue, kpi.TargetValue)

                let updatedKPI =
                    { kpi with
                        CurrentValue = newCurrentValue
                        Status = newStatus
                        LastUpdated = DateTime.Now }

                kpis.[kpiId] <- updatedKPI

                logInfo
                    "ProgressDashboard"
                    $"KPI updated: {kpiId} - {kpi.Name}: {newCurrentValue}/{kpi.TargetValue} {kpi.Unit} ({newStatus})"

                Result.Ok()
            | false, _ ->
                let errorMsg = $"KPI not found: {kpiId}"
                logWarning "ProgressDashboard" errorMsg
                Result.Error errorMsg
        with ex ->
            let errorMsg = $"Failed to update KPI: {ex.Message}"
            logError "ProgressDashboard" errorMsg
            Result.Error errorMsg

    member this.GetAllKPIs() = kpis.Values |> Seq.toArray
    member this.GetKPICount() = kpis.Count
    member this.Clear() = kpis.Clear()

/// ダッシュボードUI更新責務 (Single Responsibility)
type private DashboardUIUpdater() =
    let mutable dashboardTextView: TextView option = None

    member this.SetTextView(textView: TextView) = dashboardTextView <- Some textView

    member this.UpdateDisplay(topKPIs: DashboardKPI[], topMetrics: DashboardMetric[]) : Result<unit, string> =
        match dashboardTextView with
        | Some textView when not (isNull textView) ->
            try
                let displayText = this.FormatDashboardForDisplay(topKPIs, topMetrics)
                let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

                if not isCI then
                    this.SafeUIUpdate(textView, displayText)

                logDebug
                    "ProgressDashboard"
                    $"Dashboard display updated with {topKPIs.Length} KPIs and {topMetrics.Length} metrics"

                Result.Ok()
            with ex ->
                Result.Error $"UI update failed: {ex.Message}"
        | _ -> Result.Error "Dashboard TextView not set or is null"

    /// セキュリティ強化UI更新
    member private this.SafeUIUpdate(textView: TextView, content: string) =
        try
            // 一時的に従来の実装を使用（セキュリティ強化は後で適用）
            if not (isNull Application.MainLoop) then
                Application.MainLoop.Invoke(fun () ->
                    try
                        textView.Text <- ustring.Make(content)
                        textView.SetNeedsDisplay()
                    with ex ->
                        logException "ProgressDashboard" "UI thread update failed" ex)
            else
                textView.Text <- ustring.Make(content)
                textView.SetNeedsDisplay()
        with ex ->
            logException "ProgressDashboard" "Safe UI update failed" ex

    /// ダッシュボード表示フォーマット
    member private this.FormatDashboardForDisplay(topKPIs: DashboardKPI[], topMetrics: DashboardMetric[]) =
        let header = "=== Progress Dashboard ===\n\n"

        // KPIセクション
        let kpiSection =
            if topKPIs.Length > 0 then
                let kpiLines =
                    topKPIs
                    |> Array.map (fun kpi ->
                        let statusIcon = this.GetKPIStatusIcon(kpi.Status)
                        let percentage = (kpi.CurrentValue / kpi.TargetValue * 100.0).ToString("F1")

                        let namePreview =
                            if kpi.Name.Length > 18 then
                                kpi.Name.[..15] + "..."
                            else
                                kpi.Name.PadRight(18)

                        $"{statusIcon} {namePreview} {percentage}%% ({kpi.CurrentValue:F1}/{kpi.TargetValue:F1})")
                    |> String.concat "\n"

                $"📊 Key Performance Indicators\n{kpiLines}\n\n"
            else
                "📊 KPIデータなし\n\n"

        // メトリクスセクション
        let metricsSection =
            if topMetrics.Length > 0 then
                let metricLines =
                    topMetrics
                    |> Array.map (fun metric ->
                        let trendIcon = this.GetTrendIcon(metric.Trend)
                        let typeIcon = this.GetMetricTypeIcon(metric.MetricType)

                        let namePreview =
                            if metric.Name.Length > 15 then
                                metric.Name.[..12] + "..."
                            else
                                metric.Name.PadRight(15)

                        let valueStr = $"{metric.Value:F1}{metric.Unit}"
                        $"{typeIcon} {namePreview} {valueStr} {trendIcon}")
                    |> String.concat "\n"

                $"📈 Real-time Metrics\n{metricLines}\n\n"
            else
                "📈 メトリクスデータなし\n\n"

        let totalMetrics = topMetrics.Length
        let totalKPIs = topKPIs.Length

        let onTrackKPIs =
            topKPIs |> Array.filter (fun k -> k.Status = OnTrack) |> Array.length

        let footer =
            $"--- Metrics: {totalMetrics} | KPIs: {totalKPIs} | On Track: {onTrackKPIs} ---\nキーバインド: Ctrl+D(詳細) Ctrl+R(更新) ESC(終了)"

        header + kpiSection + metricsSection + footer

    /// KPI状態アイコン取得
    member private this.GetKPIStatusIcon(status: KPIStatus) =
        match status with
        | OnTrack -> "✅"
        | AtRisk -> "⚠️"
        | Behind -> "🔴"
        | Exceeded -> "🎯"

    /// トレンドアイコン取得
    member private this.GetTrendIcon(trend: string) =
        match trend with
        | "up" -> "📈"
        | "down" -> "📉"
        | "stable" -> "➡️"
        | _ -> "❔"

    /// メトリクス種別アイコン取得
    member private this.GetMetricTypeIcon(metricType: MetricType) =
        match metricType with
        | TaskCompletion -> "✅"
        | CodeQuality -> "🔧"
        | TestCoverage -> "🧪"
        | BuildSuccess -> "🏗️"
        | AgentEfficiency -> "⚡"
        | CollaborationScore -> "🤝"

// ===============================================
// 進捗ダッシュボード管理 (依存性注入によるSOLID設計)
// ===============================================

/// 進捗ダッシュボード管理クラス (リファクタリング版)
type ProgressDashboardManager() =
    // 依存性注入による責務分離
    let metricsManager = MetricsManager()
    let kpiManager = KPIManager()
    let uiUpdater = DashboardUIUpdater()

    /// ダッシュボードTextView設定
    member this.SetDashboardTextView(textView: TextView) =
        uiUpdater.SetTextView(textView)
        logInfo "ProgressDashboard" "Dashboard TextView set for progress monitoring"

    /// 新規メトリクス作成 - Result型対応
    member this.CreateMetric
        (metricType: MetricType, name: string, value: float, target: float, unit: string)
        : Result<string, string> =
        match metricsManager.CreateMetric(metricType, name, value, target, unit) with
        | Result.Ok metricId ->
            // UI更新
            this.UpdateDashboardDisplay() |> ignore
            Result.Ok metricId
        | Result.Error error -> Result.Error error

    /// メトリクス値更新 - Result型対応
    member this.UpdateMetric(metricId: string, newValue: float) : Result<unit, string> =
        match metricsManager.UpdateMetric(metricId, newValue) with
        | Result.Ok() ->
            // UI更新
            this.UpdateDashboardDisplay() |> ignore
            Result.Ok()
        | Result.Error error -> Result.Error error

    /// 新規KPI作成 - Result型対応
    member this.CreateKPI
        (
            name: string,
            description: string,
            currentValue: float,
            targetValue: float,
            unit: string,
            period: string,
            relatedMetrics: string list
        ) : Result<string, string> =
        match kpiManager.CreateKPI(name, description, currentValue, targetValue, unit, period, relatedMetrics) with
        | Result.Ok kpiId ->
            // UI更新
            this.UpdateDashboardDisplay() |> ignore
            Result.Ok kpiId
        | Result.Error error -> Result.Error error

    /// KPI値更新 - Result型対応
    member this.UpdateKPI(kpiId: string, newCurrentValue: float) : Result<unit, string> =
        match kpiManager.UpdateKPI(kpiId, newCurrentValue) with
        | Result.Ok() ->
            // UI更新
            this.UpdateDashboardDisplay() |> ignore
            Result.Ok()
        | Result.Error error -> Result.Error error

    /// AgentMessageから進捗データ処理 - Result型対応
    member this.ProcessProgressMessage(message: AgentMessage) : Result<unit, string> =
        if message.MessageType = MessageType.Progress then
            try
                // 進捗メッセージからメトリクス更新
                let metricType =
                    match message.Metadata.TryFind("metric_type") with
                    | Some "task_completion" -> TaskCompletion
                    | Some "code_quality" -> CodeQuality
                    | Some "test_coverage" -> TestCoverage
                    | Some "build_success" -> BuildSuccess
                    | Some "agent_efficiency" -> AgentEfficiency
                    | Some "collaboration_score" -> CollaborationScore
                    | _ -> TaskCompletion

                match message.Metadata.TryFind("metric_value") with
                | Some valueStr ->
                    match Double.TryParse(valueStr) with
                    | (true, value) ->
                        let metricName = $"{message.FromAgent} {this.GetMetricTypeName(metricType)}"
                        let unit = message.Metadata.TryFind("unit") |> Option.defaultValue "%"

                        let target =
                            message.Metadata.TryFind("target")
                            |> Option.bind (fun t ->
                                match Double.TryParse(t) with
                                | (true, v) -> Some v
                                | _ -> None)
                            |> Option.defaultValue 100.0

                        // 既存メトリクスを検索または新規作成
                        let allMetrics = metricsManager.GetAllMetrics()

                        let existingMetric =
                            allMetrics
                            |> Array.tryFind (fun m -> m.Name = metricName && m.MetricType = metricType)

                        match existingMetric with
                        | Some metric ->
                            match this.UpdateMetric(metric.MetricId, value) with
                            | Result.Ok() ->
                                logInfo "ProgressDashboard" $"Progress message processed: {metricName} = {value} {unit}"
                                Result.Ok()
                            | Result.Error error -> Result.Error error
                        | None ->
                            match this.CreateMetric(metricType, metricName, value, target, unit) with
                            | Result.Ok _ ->
                                logInfo "ProgressDashboard" $"Progress message processed: {metricName} = {value} {unit}"
                                Result.Ok()
                            | Result.Error error -> Result.Error error
                    | _ ->
                        let errorMsg = $"Invalid metric value in message: {valueStr}"
                        logWarning "ProgressDashboard" errorMsg
                        Result.Error errorMsg
                | None ->
                    let errorMsg = "Progress message missing metric_value"
                    logWarning "ProgressDashboard" errorMsg
                    Result.Error errorMsg
            with ex ->
                let errorMsg = $"Failed to process progress message: {ex.Message}"
                logError "ProgressDashboard" errorMsg
                Result.Error errorMsg
        else
            Result.Ok() // 進捗メッセージでない場合は成功として扱う

    /// ProgressAggregatorとの連携（将来の実装用プレースホルダー） - Result型対応
    member this.SyncWithProgressAggregator() : Result<unit, string> =
        try
            // 将来のProgressAggregator統合時に実装予定
            // 現在はサンプルメトリクス更新で代替
            let allAgents = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "pm" ]

            for agent in allAgents do
                // サンプル進捗データ生成
                let random = System.Random()
                let completionRate = float (random.Next(50, 95))

                // エージェント別タスク完了率メトリクス更新
                let metricName = $"{agent} Task Completion"
                let allMetrics = metricsManager.GetAllMetrics()

                let existingMetric =
                    allMetrics
                    |> Array.tryFind (fun m -> m.Name = metricName && m.MetricType = TaskCompletion)

                match existingMetric with
                | Some metric -> this.UpdateMetric(metric.MetricId, completionRate) |> ignore
                | None ->
                    this.CreateMetric(TaskCompletion, metricName, completionRate, 100.0, "%")
                    |> ignore

            // 全体進捗KPI更新
            let overallCompletionRate = 72.5 // サンプル値
            let allKPIs = kpiManager.GetAllKPIs()

            let overallKPI =
                allKPIs |> Array.tryFind (fun k -> k.Name = "Overall Project Progress")

            match overallKPI with
            | Some kpi -> this.UpdateKPI(kpi.KPIId, overallCompletionRate) |> ignore
            | None ->
                this.CreateKPI(
                    "Overall Project Progress",
                    "全体プロジェクト進捗率",
                    overallCompletionRate,
                    100.0,
                    "%",
                    "daily",
                    []
                )
                |> ignore

            logInfo "ProgressDashboard" "Sample progress data updated (ProgressAggregator integration pending)"
            Result.Ok()
        with ex ->
            let errorMsg = $"Failed to update sample progress data: {ex.Message}"
            logException "ProgressDashboard" "Failed to update sample progress data" ex
            Result.Error errorMsg

    /// ダッシュボード表示更新 - Result型対応
    member private this.UpdateDashboardDisplay() : Result<unit, string> =
        try
            // KPIとメトリクスを取得・フォーマット
            let allKPIs = kpiManager.GetAllKPIs()
            let allMetrics = metricsManager.GetAllMetrics()

            let topKPIs =
                allKPIs
                |> Array.sortByDescending (fun k -> k.LastUpdated)
                |> Array.take (min 3 allKPIs.Length)

            let topMetrics =
                allMetrics
                |> Array.sortByDescending (fun m -> m.LastUpdated)
                |> Array.take (min 6 allMetrics.Length)

            uiUpdater.UpdateDisplay(topKPIs, topMetrics)
        with ex ->
            let errorMsg = $"Failed to update dashboard display: {ex.Message}"
            logException "ProgressDashboard" "Failed to update dashboard display" ex
            Result.Error errorMsg

    /// メトリクス種別名取得
    member private this.GetMetricTypeName(metricType: MetricType) =
        match metricType with
        | TaskCompletion -> "Task Completion"
        | CodeQuality -> "Code Quality"
        | TestCoverage -> "Test Coverage"
        | BuildSuccess -> "Build Success"
        | AgentEfficiency -> "Agent Efficiency"
        | CollaborationScore -> "Collaboration Score"

    /// 全メトリクス取得
    member this.GetAllMetrics() = metricsManager.GetAllMetrics()

    /// 全KPI取得
    member this.GetAllKPIs() = kpiManager.GetAllKPIs()

    /// メトリクス数取得
    member this.GetMetricCount() = metricsManager.GetMetricCount()

    /// KPI数取得
    member this.GetKPICount() = kpiManager.GetKPICount()

    /// データクリア - Result型対応
    member this.ClearAllData() : Result<unit, string> =
        try
            metricsManager.Clear()
            kpiManager.Clear()

            match this.UpdateDashboardDisplay() with
            | Result.Ok() ->
                logInfo "ProgressDashboard" "All dashboard data cleared"
                Result.Ok()
            | Result.Error uiError ->
                logWarning "ProgressDashboard" $"UI update after clear failed: {uiError}"
                Result.Ok() // データクリアは成功
        with ex ->
            let errorMsg = $"Failed to clear dashboard data: {ex.Message}"
            logError "ProgressDashboard" errorMsg
            Result.Error errorMsg

    /// リソース解放
    member this.Dispose() =
        try
            this.ClearAllData() |> ignore
            GC.SuppressFinalize(this)
        with ex ->
            logError "ProgressDashboard" $"Error during disposal: {ex.Message}"

    interface IDisposable with
        member this.Dispose() = this.Dispose()

// ===============================================
// 依存性注入対応グローバル管理インスタンス
// ===============================================

/// 依存性注入対応進捗ダッシュボード管理インスタンス（遅延初期化）
let mutable private dashboardManagerInstance: ProgressDashboardManager option = None

/// 進捗ダッシュボード管理インスタンス取得または作成
let private getOrCreateDashboardManager () =
    match dashboardManagerInstance with
    | Some manager -> manager
    | None ->
        let manager = new ProgressDashboardManager()
        dashboardManagerInstance <- Some manager
        manager

/// 新規メトリクス作成 (グローバル関数) - Result型対応
let createMetric
    (metricType: MetricType)
    (name: string)
    (value: float)
    (target: float)
    (unit: string)
    : Result<string, string> =
    (getOrCreateDashboardManager ()).CreateMetric(metricType, name, value, target, unit)

/// メトリクス更新 (グローバル関数) - Result型対応
let updateMetric (metricId: string) (newValue: float) : Result<unit, string> =
    (getOrCreateDashboardManager ()).UpdateMetric(metricId, newValue)

/// 新規KPI作成 (グローバル関数) - Result型対応
let createKPI
    (name: string)
    (description: string)
    (currentValue: float)
    (targetValue: float)
    (unit: string)
    (period: string)
    (relatedMetrics: string list)
    : Result<string, string> =
    (getOrCreateDashboardManager ())
        .CreateKPI(name, description, currentValue, targetValue, unit, period, relatedMetrics)

/// KPI更新 (グローバル関数) - Result型対応
let updateKPI (kpiId: string) (newCurrentValue: float) : Result<unit, string> =
    (getOrCreateDashboardManager ()).UpdateKPI(kpiId, newCurrentValue)

/// ダッシュボードTextView設定 (グローバル関数)
let setDashboardTextView (textView: TextView) =
    (getOrCreateDashboardManager ()).SetDashboardTextView(textView)

/// AgentMessageから進捗データ処理 (グローバル関数) - Result型対応
let processProgressMessage (message: AgentMessage) : Result<unit, string> =
    (getOrCreateDashboardManager ()).ProcessProgressMessage(message)

/// ProgressAggregatorとの連携 (グローバル関数・将来実装予定) - Result型対応
let syncWithProgressAggregator () : Result<unit, string> =
    (getOrCreateDashboardManager ()).SyncWithProgressAggregator()

/// 依存性注入: 既存のインスタンスを置き換え（テスト用）
let injectDashboardManager (manager: ProgressDashboardManager) =
    dashboardManagerInstance <- Some manager

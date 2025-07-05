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
// 進捗ダッシュボード管理
// ===============================================

/// 進捗ダッシュボード管理クラス
type ProgressDashboardManager() =
    let metrics = ConcurrentDictionary<string, DashboardMetric>()
    let kpis = ConcurrentDictionary<string, DashboardKPI>()
    let maxHistoryEntries = 50 // 最大履歴保持数
    let mutable dashboardTextView: TextView option = None

    /// メトリクス一意ID生成
    let generateMetricId () =
        let timestamp = DateTime.Now.ToString("yyMMdd-HHmmss")
        let guidPart = Guid.NewGuid().ToString("N")[..3]
        $"metric-{timestamp}-{guidPart}"

    /// KPI一意ID生成
    let generateKPIId () =
        let timestamp = DateTime.Now.ToString("yyMMdd-HHmmss")
        let guidPart = Guid.NewGuid().ToString("N")[..3]
        $"kpi-{timestamp}-{guidPart}"

    /// ダッシュボードTextView設定
    member this.SetDashboardTextView(textView: TextView) =
        dashboardTextView <- Some textView
        logInfo "ProgressDashboard" "Dashboard TextView set for progress monitoring"

    /// 新規メトリクス作成
    member this.CreateMetric(metricType: MetricType, name: string, value: float, target: float, unit: string) =
        let metricId = generateMetricId ()

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

        // UI更新
        this.UpdateDashboardDisplay()

        logInfo "ProgressDashboard" $"Metric created: {metricId} - {name}: {value} {unit}"
        metricId

    /// メトリクス値更新
    member this.UpdateMetric(metricId: string, newValue: float) =
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

            // UI更新
            this.UpdateDashboardDisplay()

            logInfo
                "ProgressDashboard"
                $"Metric updated: {metricId} - {metric.Name}: {newValue} {metric.Unit} ({trend})"

            true
        | false, _ ->
            logWarning "ProgressDashboard" $"Metric not found: {metricId}"
            false

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
        ) =
        let kpiId = generateKPIId ()

        let status =
            let percentage = (currentValue / targetValue) * 100.0

            if percentage >= 100.0 then Exceeded
            elif percentage >= 90.0 then OnTrack
            elif percentage >= 70.0 then AtRisk
            else Behind

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

        // UI更新
        this.UpdateDashboardDisplay()

        logInfo "ProgressDashboard" $"KPI created: {kpiId} - {name}: {currentValue}/{targetValue} {unit} ({status})"
        kpiId

    /// KPI値更新
    member this.UpdateKPI(kpiId: string, newCurrentValue: float) =
        match kpis.TryGetValue(kpiId) with
        | true, kpi ->
            let newStatus =
                let percentage = (newCurrentValue / kpi.TargetValue) * 100.0

                if percentage >= 100.0 then Exceeded
                elif percentage >= 90.0 then OnTrack
                elif percentage >= 70.0 then AtRisk
                else Behind

            let updatedKPI =
                { kpi with
                    CurrentValue = newCurrentValue
                    Status = newStatus
                    LastUpdated = DateTime.Now }

            kpis.[kpiId] <- updatedKPI

            // UI更新
            this.UpdateDashboardDisplay()

            logInfo
                "ProgressDashboard"
                $"KPI updated: {kpiId} - {kpi.Name}: {newCurrentValue}/{kpi.TargetValue} {kpi.Unit} ({newStatus})"

            true
        | false, _ ->
            logWarning "ProgressDashboard" $"KPI not found: {kpiId}"
            false

    /// AgentMessageから進捗データ処理
    member this.ProcessProgressMessage(message: AgentMessage) =
        if message.MessageType = MessageType.Progress then
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
                    let existingMetric =
                        metrics.Values
                        |> Seq.tryFind (fun m -> m.Name = metricName && m.MetricType = metricType)

                    match existingMetric with
                    | Some metric -> this.UpdateMetric(metric.MetricId, value) |> ignore
                    | None -> this.CreateMetric(metricType, metricName, value, target, unit) |> ignore

                    logInfo "ProgressDashboard" $"Progress message processed: {metricName} = {value} {unit}"
                | _ -> logWarning "ProgressDashboard" $"Invalid metric value in message: {valueStr}"
            | None -> logWarning "ProgressDashboard" "Progress message missing metric_value"

    /// ProgressAggregatorとの連携（将来の実装用プレースホルダー）
    member this.SyncWithProgressAggregator() =
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

                let existingMetric =
                    metrics.Values
                    |> Seq.tryFind (fun m -> m.Name = metricName && m.MetricType = TaskCompletion)

                match existingMetric with
                | Some metric -> this.UpdateMetric(metric.MetricId, completionRate) |> ignore
                | None ->
                    this.CreateMetric(TaskCompletion, metricName, completionRate, 100.0, "%")
                    |> ignore

            // 全体進捗KPI更新
            let overallCompletionRate = 72.5 // サンプル値

            let overallKPI =
                kpis.Values |> Seq.tryFind (fun k -> k.Name = "Overall Project Progress")

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
        with ex ->
            logException "ProgressDashboard" "Failed to update sample progress data" ex

    /// ダッシュボード表示更新
    member private this.UpdateDashboardDisplay() =
        match dashboardTextView with
        | Some textView ->
            try
                // KPIとメトリクスを取得・フォーマット
                let topKPIs =
                    kpis.Values
                    |> Seq.sortByDescending (fun k -> k.LastUpdated)
                    |> Seq.take (min 3 (Seq.length kpis.Values))
                    |> Seq.toArray

                let topMetrics =
                    metrics.Values
                    |> Seq.sortByDescending (fun m -> m.LastUpdated)
                    |> Seq.take (min 6 (Seq.length metrics.Values))
                    |> Seq.toArray

                let displayText = this.FormatDashboardForDisplay(topKPIs, topMetrics)

                // UI更新はメインスレッドで実行・CI環境では安全にスキップ
                let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

                if not isCI then
                    try
                        // Application.MainLoopの安全性チェック
                        if not (isNull Application.MainLoop) then
                            Application.MainLoop.Invoke(fun () ->
                                try
                                    if not (isNull textView) then
                                        textView.Text <- ustring.Make(displayText: string)
                                        textView.SetNeedsDisplay()
                                    else
                                        logWarning "ProgressDashboard" "TextView is null during UI update"
                                with ex ->
                                    logException "ProgressDashboard" "UI thread update failed" ex)
                        else
                            // MainLoopが利用できない場合は直接更新を試行
                            try
                                if not (isNull textView) then
                                    textView.Text <- ustring.Make(displayText: string)
                                    textView.SetNeedsDisplay()
                                else
                                    logWarning "ProgressDashboard" "TextView is null during direct UI update"
                            with ex ->
                                logWarning "ProgressDashboard" $"Direct UI update failed: {ex.Message}"
                    with ex ->
                        logException "ProgressDashboard" "MainLoop.Invoke failed" ex
                else
                    logDebug "ProgressDashboard" "CI environment detected - skipping UI update"

                logDebug "ProgressDashboard"
                <| $"Dashboard display updated with {topKPIs.Length} KPIs and {topMetrics.Length} metrics"

            with ex ->
                logException "ProgressDashboard" "Failed to update dashboard display" ex
        | None -> logWarning "ProgressDashboard" "Dashboard TextView not set - cannot update display"

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

        let totalMetrics = metrics.Count
        let totalKPIs = kpis.Count

        let onTrackKPIs =
            kpis.Values |> Seq.filter (fun k -> k.Status = OnTrack) |> Seq.length

        let footer =
            $"--- Metrics: {totalMetrics} | KPIs: {totalKPIs} | On Track: {onTrackKPIs} ---\nキーバインド: Ctrl+D(詳細) Ctrl+R(更新) ESC(終了)"

        header + kpiSection + metricsSection + footer

    /// メトリクス種別名取得
    member private this.GetMetricTypeName(metricType: MetricType) =
        match metricType with
        | TaskCompletion -> "Task Completion"
        | CodeQuality -> "Code Quality"
        | TestCoverage -> "Test Coverage"
        | BuildSuccess -> "Build Success"
        | AgentEfficiency -> "Agent Efficiency"
        | CollaborationScore -> "Collaboration Score"

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

    /// 全メトリクス取得
    member this.GetAllMetrics() = metrics.Values |> Seq.toArray

    /// 全KPI取得
    member this.GetAllKPIs() = kpis.Values |> Seq.toArray

    /// メトリクス数取得
    member this.GetMetricCount() = metrics.Count

    /// KPI数取得
    member this.GetKPICount() = kpis.Count

    /// データクリア
    member this.ClearAllData() =
        metrics.Clear()
        kpis.Clear()
        this.UpdateDashboardDisplay()
        logInfo "ProgressDashboard" "All dashboard data cleared"

// ===============================================
// グローバル進捗ダッシュボード管理インスタンス
// ===============================================

/// グローバル進捗ダッシュボード管理インスタンス
let globalProgressDashboardManager = new ProgressDashboardManager()

/// 新規メトリクス作成 (グローバル関数)
let createMetric (metricType: MetricType) (name: string) (value: float) (target: float) (unit: string) =
    globalProgressDashboardManager.CreateMetric(metricType, name, value, target, unit)

/// メトリクス更新 (グローバル関数)
let updateMetric (metricId: string) (newValue: float) =
    globalProgressDashboardManager.UpdateMetric(metricId, newValue)

/// 新規KPI作成 (グローバル関数)
let createKPI
    (name: string)
    (description: string)
    (currentValue: float)
    (targetValue: float)
    (unit: string)
    (period: string)
    (relatedMetrics: string list)
    =
    globalProgressDashboardManager.CreateKPI(name, description, currentValue, targetValue, unit, period, relatedMetrics)

/// KPI更新 (グローバル関数)
let updateKPI (kpiId: string) (newCurrentValue: float) =
    globalProgressDashboardManager.UpdateKPI(kpiId, newCurrentValue)

/// ダッシュボードTextView設定 (グローバル関数)
let setDashboardTextView (textView: TextView) =
    globalProgressDashboardManager.SetDashboardTextView(textView)

/// AgentMessageから進捗データ処理 (グローバル関数)
let processProgressMessage (message: AgentMessage) =
    globalProgressDashboardManager.ProcessProgressMessage(message)

/// ProgressAggregatorとの連携 (グローバル関数・将来実装予定)
let syncWithProgressAggregator () =
    globalProgressDashboardManager.SyncWithProgressAggregator()

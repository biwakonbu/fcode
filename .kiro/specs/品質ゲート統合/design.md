# Design Document

## Overview

品質ゲート統合機能は、既存のQualityGateManagerコンポーネントとTerminal.GuiベースのUIを統合し、POが品質状況をリアルタイムで把握できるダッシュボードを提供します。この設計では、F#の型安全性を活用しながら、非同期イベント処理とUI更新の効率的な統合を実現します。

## Architecture

### System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    fcode UI Layer                           │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐           │
│  │会話ペイン    │ │統合ダッシュ  │ │品質ゲート    │           │
│  │            │ │ボード       │ │詳細パネル    │           │
│  └─────────────┘ └─────────────┘ └─────────────┘           │
├─────────────────────────────────────────────────────────────┤
│                Quality Gate UI Integration                  │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐           │
│  │QualityGate  │ │Notification │ │Trend        │           │
│  │DisplayManager│ │Manager      │ │Analyzer     │           │
│  └─────────────┘ └─────────────┘ └─────────────┘           │
├─────────────────────────────────────────────────────────────┤
│              Existing Quality Gate System                   │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐           │
│  │QualityGate  │ │Progress     │ │Agent        │           │
│  │Manager      │ │Aggregator   │ │StateManager │           │
│  └─────────────┘ └─────────────┘ └─────────────┘           │
└─────────────────────────────────────────────────────────────┘
```

### Data Flow

```
QualityGateManager → QualityGateEvent → QualityGateDisplayManager → UI Update
                                     ↓
                              NotificationManager → PO Alert
                                     ↓
                              TrendAnalyzer → Historical Data
```

## Components and Interfaces

### QualityGateDisplayManager

```fsharp
type QualityGateStatus =
    | Passed of completionPercentage: float
    | Failed of reasons: string list
    | InProgress of progressInfo: string
    | Blocked of dependency: string * options: string list

type QualityMetrics = {
    TestCoverage: float
    CodeQualityGrade: char // A-F
    ResponseTimeMs: int
    MemoryUsageMB: float
    TechnicalDebtHours: float
}

type QualityGateDisplayManager(eventBus: IEventBus, uiUpdater: IUIUpdater) =
    member _.Initialize() : unit
    member _.HandleQualityGateEvent(event: QualityGateEvent) : Async<unit>
    member _.GetCurrentStatus() : QualityGateStatus
    member _.GetMetrics() : QualityMetrics
    member _.UpdateDashboard() : Async<unit>
```

### NotificationManager

```fsharp
type NotificationPriority = Critical | High | Medium | Low

type QualityNotification = {
    Id: string
    Priority: NotificationPriority
    Title: string
    UserMessage: string
    TechnicalDetails: string
    RecommendedActions: string list
    RequiresAcknowledgment: bool
    Timestamp: DateTime
}

type NotificationManager(displayManager: QualityGateDisplayManager) =
    member _.ProcessQualityEvent(event: QualityGateEvent) : QualityNotification option
    member _.ShowNotification(notification: QualityNotification) : Async<unit>
    member _.GetPendingNotifications() : QualityNotification list
    member _.AcknowledgeNotification(id: string, reason: string option) : unit
```

### TrendAnalyzer

```fsharp
type QualityTrend = {
    SprintNumber: int
    Timestamp: DateTime
    Metrics: QualityMetrics
    VelocitySP: int
}

type TrendAnalysis = {
    Direction: TrendDirection // Improving | Stable | Degrading
    ChangePercentage: float
    SignificantChanges: string list
    VelocityCorrelation: float
}

type TrendAnalyzer(historyStorage: IQualityHistoryStorage) =
    member _.RecordSprintQuality(trend: QualityTrend) : Async<unit>
    member _.AnalyzeTrends(sprintCount: int) : Async<TrendAnalysis>
    member _.GetQualityHistory() : Async<QualityTrend list>
    member _.DetectSignificantChanges(current: QualityMetrics, previous: QualityMetrics) : string list
```

## Data Models

### Quality Gate Configuration

```fsharp
type QualityThresholds = {
    MinTestCoverage: float // default 80%
    MinCodeQualityGrade: char // default 'C'
    MaxResponseTimeMs: int // default 2000
    MaxMemoryUsageMB: float // default 512
    MaxTechnicalDebtHours: float // default 8
}

type QualityGateConfig = {
    Thresholds: QualityThresholds
    NotificationSettings: NotificationSettings
    TrendAnalysisEnabled: bool
    HistoryRetentionDays: int
}
```

### UI State Management

```fsharp
type QualityDashboardState = {
    CurrentStatus: QualityGateStatus
    Metrics: QualityMetrics
    PendingNotifications: QualityNotification list
    TrendData: QualityTrend list
    LastUpdated: DateTime
}

type QualityUIState = {
    Dashboard: QualityDashboardState
    DetailsPanelVisible: bool
    SelectedMetric: string option
    NotificationsPanelExpanded: bool
}
```

## Error Handling

### Error Types

```fsharp
type QualityGateUIError =
    | QualityDataUnavailable of reason: string
    | UIUpdateFailed of component: string * error: string
    | NotificationDeliveryFailed of notificationId: string
    | TrendAnalysisFailed of error: string
    | ConfigurationError of setting: string * value: string
```

### Error Recovery

```fsharp
type ErrorRecoveryStrategy =
    | RetryWithBackoff of maxAttempts: int
    | FallbackToCache of cacheAgeLimit: TimeSpan
    | ShowErrorState of userMessage: string
    | DisableFeature of featureName: string

let handleQualityGateUIError error =
    match error with
    | QualityDataUnavailable _ -> FallbackToCache (TimeSpan.FromMinutes 5.0)
    | UIUpdateFailed _ -> RetryWithBackoff 3
    | NotificationDeliveryFailed _ -> ShowErrorState "通知の表示に失敗しました"
    | TrendAnalysisFailed _ -> DisableFeature "TrendAnalysis"
    | ConfigurationError _ -> ShowErrorState "設定に問題があります"
```

## Testing Strategy

### Unit Testing

```fsharp
[<Test>]
let ``QualityGateDisplayManager should update status within 2 seconds`` () =
    // Arrange
    let mockEventBus = Mock<IEventBus>()
    let mockUIUpdater = Mock<IUIUpdater>()
    let displayManager = QualityGateDisplayManager(mockEventBus.Object, mockUIUpdater.Object)
    
    // Act
    let stopwatch = Stopwatch.StartNew()
    let qualityEvent = QualityGateEvent.Passed(85.0)
    displayManager.HandleQualityGateEvent(qualityEvent) |> Async.RunSynchronously
    stopwatch.Stop()
    
    // Assert
    stopwatch.ElapsedMilliseconds |> should be (lessThan 2000L)
    displayManager.GetCurrentStatus() |> should equal (Passed 85.0)
```

### Integration Testing

```fsharp
[<Test>]
let ``End-to-end quality gate flow should work correctly`` () =
    // Test complete flow from QualityGateManager to UI display
    let qualityGateManager = createTestQualityGateManager()
    let displayManager = createTestDisplayManager()
    
    // Trigger quality check
    qualityGateManager.PerformQualityCheck() |> Async.RunSynchronously
    
    // Verify UI is updated
    let status = displayManager.GetCurrentStatus()
    status |> should not' (equal InProgress)
```

### Performance Testing

```fsharp
[<Test>]
let ``Dashboard should handle 100 quality updates per minute`` () =
    let displayManager = createTestDisplayManager()
    let events = generateQualityEvents 100
    
    let stopwatch = Stopwatch.StartNew()
    events
    |> List.map (displayManager.HandleQualityGateEvent)
    |> Async.Parallel
    |> Async.RunSynchronously
    |> ignore
    stopwatch.Stop()
    
    stopwatch.ElapsedMilliseconds |> should be (lessThan 60000L)
```

## UI Design Specifications

### Dashboard Layout

```
┌─ 品質ゲート統合ダッシュボード ─────────────────────────────────┐
│ 🟢 全体ステータス: 合格 (87%)     最終更新: 14:32:18        │
├─────────────────────────────────────────────────────────────┤
│ 📊 品質メトリクス                                           │
│ ├─ テストカバレッジ: 85% 🟢      ├─ コード品質: A 🟢       │
│ ├─ 応答時間: 1.2秒 🟢           ├─ メモリ使用: 256MB 🟢    │
│ └─ 技術的負債: 4時間 🟢                                     │
├─────────────────────────────────────────────────────────────┤
│ ⚠️ 通知 (1件)                                               │
│ └─ 中程度: パフォーマンステストで軽微な劣化を検出           │
├─────────────────────────────────────────────────────────────┤
│ 📈 品質トレンド (過去4スプリント)                           │
│ Sprint 1: B → Sprint 2: A → Sprint 3: A → Sprint 4: A      │
│ 改善傾向: +15% (ベロシティとの相関: 0.8)                   │
└─────────────────────────────────────────────────────────────┘
```

### Notification Panel

```
┌─ 品質ゲート通知 ─────────────────────────────────────────────┐
│ 🔴 重要: 外部API依存でブロック中                             │
│    推奨アクション:                                          │
│    • モックデータでテスト継続                               │
│    • 外部チームに連絡                                       │
│    • 代替実装の検討                                         │
│    [承認] [詳細] [後で対応]                                 │
├─────────────────────────────────────────────────────────────┤
│ 🟡 注意: テストカバレッジが閾値に近づいています              │
│    現在: 82% (閾値: 80%)                                    │
│    推奨: 新機能のテスト追加                                 │
│    [承認] [詳細]                                            │
└─────────────────────────────────────────────────────────────┘
```

## Implementation Considerations

### Performance Optimization

1. **非同期UI更新**: UI更新は非同期で実行し、メインスレッドをブロックしない
2. **イベント集約**: 短時間内の複数イベントを集約して処理
3. **キャッシュ戦略**: 品質データをメモリキャッシュし、不要な再計算を避ける
4. **遅延読み込み**: 詳細データは必要時のみ読み込み

### Scalability

1. **イベント駆動アーキテクチャ**: 疎結合な設計で拡張性を確保
2. **設定可能な更新間隔**: 負荷に応じて更新頻度を調整可能
3. **履歴データの効率的管理**: 古いデータの自動削除とアーカイブ

### Maintainability

1. **型安全な設計**: F#の型システムを活用したコンパイル時エラー検出
2. **明確な責務分離**: 各コンポーネントの役割を明確に定義
3. **包括的なテスト**: ユニット、統合、パフォーマンステストの実装
4. **設定の外部化**: 閾値や設定をファイルで管理

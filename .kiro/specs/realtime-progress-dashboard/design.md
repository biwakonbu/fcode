# Design Document - Real-time Progress Dashboard

## Overview

Real-time Progress Dashboardは、既存のProgressAggregator、AgentStateManager、TaskDependencyGraphシステムとfcodeのTUIを統合し、各エージェントの作業進捗をリアルタイムで可視化するシステムです。POが全体状況を即座に把握し、適切な判断を行えるよう、包括的で直感的な進捗情報を提供します。

## Architecture

### System Integration Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    fcode TUI Layer                          │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────┐ │
│ │リアルタイム │ │エージェント │ │依存関係     │ │統合     │ │
│ │進捗表示     │ │詳細ビュー   │ │ビュー       │ │メトリクス│ │
│ └─────────────┘ └─────────────┘ └─────────────┘ └─────────┘ │
├─────────────────────────────────────────────────────────────┤
│              Progress Dashboard Management Layer            │
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐             │
│ │ProgressDash │ │AgentStatus  │ │Dependency   │             │
│ │boardManager │ │Manager      │ │Visualizer   │             │
│ └─────────────┘ └─────────────┘ └─────────────┘             │
├─────────────────────────────────────────────────────────────┤
│              Existing Collaboration Layer                   │
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐             │
│ │Progress     │ │AgentState   │ │TaskDependency│             │
│ │Aggregator   │ │Manager      │ │Graph        │             │
│ └─────────────┘ └─────────────┘ └─────────────┘             │
└─────────────────────────────────────────────────────────────┘
```

### Data Flow Architecture

```
[AgentStateManager] → [AgentStatusUpdate] → [ProgressDashboardManager]
         ↓                    ↓                      ↓
[TaskExecution] → [ProgressAggregator] → [ProgressCalculation]
         ↓                    ↓                      ↓
[TaskDependencyGraph] → [DependencyAnalysis] → [BlockerDetection]
         ↓                    ↓                      ↓
[RealtimeUpdate] → [UIRefresh] → [DashboardDisplay]
```

## Components and Interfaces

### ProgressDashboardManager

リアルタイム進捗表示を管理するメインコンポーネント

```fsharp
type ProgressDashboardManager(progressAggregator: ProgressAggregator,
                             agentStateManager: AgentStateManager,
                             taskDependencyGraph: TaskDependencyGraph) =
    
    // 全体進捗状況の取得
    member _.GetOverallProgress() : OverallProgressStatus
    
    // エージェント別進捗の取得
    member _.GetAgentProgress(agentId: string) : AgentProgressStatus
    
    // リアルタイム更新の開始
    member _.StartRealtimeUpdates() : IDisposable
    
    // 依存関係・ブロッカーの分析
    member _.AnalyzeDependenciesAndBlockers() : DependencyAnalysis
    
    // パフォーマンス分析の実行
    member _.AnalyzePerf() : PerformanceAnalysis
    
    // カスタム表示設定の適用
    member _.ApplyDisplaySettings(settings: DisplaySettings) : unit
```

### AgentStatusManager

エージェント状況の詳細管理コンポーネント

```fsharp
type AgentStatusManager(agentStateManager: AgentStateManager) =
    
    // エージェント詳細ステータスの取得
    member _.GetDetailedAgentStatus(agentId: string) : DetailedAgentStatus
    
    // エージェント作業履歴の取得
    member _.GetAgentWorkHistory(agentId: string, period: TimeSpan) : WorkHistory
    
    // エージェント負荷分析
    member _.AnalyzeAgentWorkload(agentId: string) : WorkloadAnalysis
    
    // 協力要請状況の取得
    member _.GetCollaborationRequests(agentId: string) : CollaborationRequest list
    
    // エージェント間連携の分析
    member _.AnalyzeAgentCollaboration() : CollaborationAnalysis
```

### DependencyVisualizer

依存関係とブロッカーの可視化コンポーネント

```fsharp
type DependencyVisualizer(taskDependencyGraph: TaskDependencyGraph) =
    
    // 依存関係の可視化データ生成
    member _.GenerateDependencyVisualization() : DependencyVisualization
    
    // ブロッカーの検出・分析
    member _.DetectBlockers() : BlockerInfo list
    
    // 循環依存の検出
    member _.DetectCircularDependencies() : CircularDependency list
    
    // クリティカルパスの計算
    member _.CalculateCriticalPath() : CriticalPath
    
    // 依存関係の最適化提案
    member _.SuggestDependencyOptimization() : OptimizationSuggestion list
```

## Data Models

### OverallProgressStatus

```fsharp
type OverallProgressStatus = {
    TotalTasks: int
    CompletedTasks: int
    InProgressTasks: int
    BlockedTasks: int
    OverallCompletionRate: float
    EstimatedCompletionTime: DateTime option
    CurrentVelocity: float
    TrendAnalysis: ProgressTrend
    Milestones: MilestoneStatus list
    RiskFactors: RiskFactor list
}

and ProgressTrend =
    | Accelerating of float
    | OnTrack
    | Slowing of float
    | AtRisk of string

and MilestoneStatus = {
    MilestoneId: string
    Name: string
    TargetDate: DateTime
    CompletionRate: float
    Status: MilestoneStatusType
    RiskLevel: RiskLevel
}
```

### AgentProgressStatus

```fsharp
type AgentProgressStatus = {
    AgentId: string
    CurrentTask: TaskInfo option
    TaskProgress: float
    EstimatedTimeToCompletion: TimeSpan option
    RecentActivity: ActivityInfo list
    WorkloadLevel: WorkloadLevel
    CollaborationStatus: CollaborationStatus
    PerformanceMetrics: AgentPerformanceMetrics
    BlockerInfo: BlockerInfo option
}

and WorkloadLevel =
    | Underutilized of float
    | Optimal
    | Overloaded of float
    | Critical

and CollaborationStatus = {
    ActiveCollaborations: CollaborationInfo list
    PendingRequests: CollaborationRequest list
    ReviewsInProgress: ReviewInfo list
    KnowledgeSharing: KnowledgeSharingInfo list
}
```

### DetailedAgentStatus

```fsharp
type DetailedAgentStatus = {
    BasicStatus: AgentProgressStatus
    WorkHistory: WorkHistory
    SkillUtilization: SkillUtilizationInfo
    EfficiencyMetrics: EfficiencyMetrics
    CollaborationEffectiveness: CollaborationEffectiveness
    ImprovementSuggestions: ImprovementSuggestion list
}

and WorkHistory = {
    CompletedTasks: CompletedTaskInfo list
    AverageTaskDuration: TimeSpan
    TaskCompletionTrend: CompletionTrend
    QualityMetrics: QualityMetrics
    LearningProgress: LearningProgress
}

and EfficiencyMetrics = {
    TasksPerHour: float
    CodeLinesPerHour: float
    BugFixRate: float
    ReviewTurnaroundTime: TimeSpan
    KnowledgeSharingContribution: float
}
```

### DependencyAnalysis

```fsharp
type DependencyAnalysis = {
    TotalDependencies: int
    ResolvedDependencies: int
    PendingDependencies: int
    BlockedDependencies: int
    CriticalPath: CriticalPath
    Blockers: BlockerInfo list
    CircularDependencies: CircularDependency list
    OptimizationOpportunities: OptimizationOpportunity list
}

and BlockerInfo = {
    BlockerId: string
    BlockedTasks: string list
    BlockerType: BlockerType
    Severity: BlockerSeverity
    EstimatedResolutionTime: TimeSpan option
    ResolutionSuggestions: string list
    ImpactAnalysis: BlockerImpact
}

and BlockerType =
    | TechnicalBlocker of string
    | ResourceBlocker of string
    | DependencyBlocker of string
    | ExternalBlocker of string
```

### PerformanceAnalysis

```fsharp
type PerformanceAnalysis = {
    TeamVelocity: VelocityMetrics
    BottleneckAnalysis: BottleneckInfo list
    ResourceUtilization: ResourceUtilizationInfo
    EfficiencyTrends: EfficiencyTrend list
    OptimizationSuggestions: OptimizationSuggestion list
    PredictiveInsights: PredictiveInsight list
}

and VelocityMetrics = {
    CurrentSprint: float
    AverageVelocity: float
    VelocityTrend: VelocityTrend
    PredictedVelocity: float
    FactorsAffectingVelocity: VelocityFactor list
}

and BottleneckInfo = {
    BottleneckId: string
    Location: BottleneckLocation
    Impact: BottleneckImpact
    Causes: string list
    ResolutionStrategies: ResolutionStrategy list
}
```

## Error Handling

### Progress Dashboard Error Types

```fsharp
type ProgressDashboardError =
    | DataRetrievalError of string
    | CalculationError of string
    | VisualizationError of string
    | RealtimeUpdateError of string
    | DependencyAnalysisError of string
    | PerformanceAnalysisError of string

// エラーハンドリング戦略
let handleProgressDashboardError error =
    match error with
    | DataRetrievalError source ->
        // データ取得の再試行・代替データソース使用
        retryDataRetrieval source
    | CalculationError calculation ->
        // 計算エラーの代替アルゴリズム使用
        useAlternativeCalculation calculation
    | VisualizationError component ->
        // 可視化コンポーネントの簡易表示
        useSimplifiedVisualization component
    | RealtimeUpdateError reason ->
        // リアルタイム更新の一時停止・手動更新
        fallbackToManualUpdate reason
    | DependencyAnalysisError analysis ->
        // 依存関係分析の簡易版実行
        useSimplifiedDependencyAnalysis analysis
    | PerformanceAnalysisError metrics ->
        // パフォーマンス分析の基本メトリクスのみ表示
        showBasicPerformanceMetrics metrics
```

## UI Design Specifications

### Main Progress Dashboard

メイン進捗ダッシュボードの表示：

```
┌─ 📊 リアルタイム進捗ダッシュボード ────────────────────────────┐
│ 🎯 全体進捗: 67% (15/22タスク完了)  │ ⏱️ 予想完了: 明日 16:30  │
│ 📈 ベロシティ: 8.5pt/日 (↗️+12%)    │ 🚨 リスク: 中程度 (2件) │
├────────────────────────────────────────────────────────────────┤
│ 👥 エージェント状況                                            │
│ ┌──────────────────────────────────────────────────────────┐   │
│ │ dev1 🟢 │ UI実装中 (85%) │ 予想完了: 15:30 │ 負荷: 適正 │   │
│ │ dev2 🟡 │ API設計中 (45%) │ 予想完了: 17:00 │ 負荷: 高  │   │
│ │ qa1  🔴 │ ブロック中     │ 待機: 認証API  │ 負荷: 低  │   │
│ │ ux   🟢 │ デザイン中 (70%) │ 予想完了: 16:00 │ 負荷: 適正 │   │
│ └──────────────────────────────────────────────────────────┘   │
├────────────────────────────────────────────────────────────────┤
│ 🔗 依存関係・ブロッカー                                        │
│ • 🚫 AUTH-API → QA-TEST (qa1ブロック中)                       │
│ • ⏳ UI-DESIGN → UI-IMPL (dev1待機、ux作業中)                 │
│ • ✅ DB-SCHEMA → API-IMPL (完了、dev2作業中)                  │
├────────────────────────────────────────────────────────────────┤
│ 📋 今日の完了予定                                              │
│ • UI-005: ユーザープロフィール画面 (dev1, 15:30)              │
│ • UX-003: アイコンデザイン (ux, 16:00)                        │
│ • API-002: 認証エンドポイント (dev2, 17:00)                   │
├────────────────────────────────────────────────────────────────┤
│ ⚠️ 注意事項・推奨アクション                                    │
│ • qa1のブロック解消が優先 (認証API完了待ち)                   │
│ • dev2の負荷が高い - 他メンバーへの作業分散を検討             │
│ • 明日のスプリント完了は可能だが、リスク管理が必要            │
└────────────────────────────────────────────────────────────────┘
```

### Agent Detail View

エージェント詳細ビューの表示：

```
┌─ 👤 dev2 詳細ステータス ──────────────────────────────────────┐
│ 現在のタスク: API-002 認証エンドポイント実装                  │
│ 進捗: 45% │ 開始: 13:30 │ 予想完了: 17:00 │ 残り: 2.5時間   │
├────────────────────────────────────────────────────────────────┤
│ 📊 作業状況                                                    │
│ • 作業時間: 3.5時間 (今日)                                    │
│ • 集中度: 85% (高い集中状態)                                  │
│ • 中断回数: 2回 (レビュー依頼対応)                            │
│ • 効率指標: 120% (平均を上回る)                               │
├────────────────────────────────────────────────────────────────┤
│ 🤝 協力・連携状況                                              │
│ • レビュー依頼: dev1からのコードレビュー (15分前)             │
│ • 知識共有: qa1への認証仕様説明 (完了)                        │
│ • 待機中: uxからのUI仕様確認 (30分待機)                       │
├────────────────────────────────────────────────────────────────┤
│ 📈 パフォーマンス履歴 (過去7日)                                │
│ ┌──────────────────────────────────────────────────────────┐   │
│ │ 効率度 120% ┌─────────────────────────────────────┐     │   │
│ │            │                                *    │     │   │
│ │            │                          *          │     │   │
│ │            │                    *                │     │   │
│ │            │              *                      │     │   │
│ │     80%    └─────────────────────────────────────┘     │   │
│ │             月   火   水   木   金   土   今日         │   │
│ └──────────────────────────────────────────────────────────┘   │
├────────────────────────────────────────────────────────────────┤
│ 💡 最適化提案                                                  │
│ • 負荷軽減: UI仕様確認を優先し、並行作業を削減                │
│ • 効率向上: 午前中の集中時間を活用した複雑タスクの配置        │
│ • 協力促進: qa1との定期的な進捗共有で後続作業の準備           │
└────────────────────────────────────────────────────────────────┘
```

### Dependency Visualization

依存関係可視化の表示：

```
┌─ 🔗 タスク依存関係・クリティカルパス ────────────────────────┐
│ クリティカルパス: DB→API→TEST (予想: 6時間)                  │
│ 並行可能: UI, UX作業 (影響なし)                              │
├────────────────────────────────────────────────────────────────┤
│ 📊 依存関係マップ                                              │
│                                                                │
│  [DB-001]──→[API-001]──→[API-002]──→[QA-001]                 │
│     ✅         🟡         🟡         🔴                        │
│     完了      進行中      待機中     ブロック                  │
│                                                                │
│  [UX-001]──→[UI-001]──→[UI-002]                               │
│     🟡        🟢        ⏸️                                     │
│    進行中     進行中     待機中                                 │
│                                                                │
│  [DOC-001] (独立)                                              │
│     ⏸️                                                         │
│    未開始                                                       │
├────────────────────────────────────────────────────────────────┤
│ 🚫 現在のブロッカー                                            │
│ • QA-001: 認証API完了待ち (影響: テスト全体)                  │
│   - 解決策: API-002の優先完了 (dev2)                         │
│   - 代替案: モックAPIでのテスト先行実施                       │
│                                                                │
│ • UI-002: UX仕様確定待ち (影響: フロントエンド)               │
│   - 解決策: UX-001の優先完了 (ux)                            │
│   - 代替案: 暫定デザインでの実装先行                          │
├────────────────────────────────────────────────────────────────┤
│ 💡 最適化提案                                                  │
│ • 並行作業の促進: DOC-001を待機時間に実施                     │
│ • リスク軽減: API-002とUX-001の優先度上げ                     │
│ • 効率向上: モックAPIによるテスト先行実施の検討                │
└────────────────────────────────────────────────────────────────┘
```

## Testing Strategy

### Unit Testing

```fsharp
module ProgressDashboardTests =
    
    [<Fact>]
    let ``ProgressDashboardManager - 全体進捗の正常計算`` () =
        // Given
        let mockProgressAggregator = createMockProgressAggregator()
        let mockAgentStateManager = createMockAgentStateManager()
        let mockTaskDependencyGraph = createMockTaskDependencyGraph()
        let dashboardManager = ProgressDashboardManager(mockProgressAggregator, mockAgentStateManager, mockTaskDependencyGraph)
        
        // When
        let overallProgress = dashboardManager.GetOverallProgress()
        
        // Then
        Assert.NotNull(overallProgress)
        Assert.True(overallProgress.OverallCompletionRate >= 0.0 && overallProgress.OverallCompletionRate <= 1.0)
    
    [<Fact>]
    let ``AgentStatusManager - エージェント詳細ステータス取得`` () =
        // Given
        let mockAgentStateManager = createMockAgentStateManager()
        let statusManager = AgentStatusManager(mockAgentStateManager)
        
        // When
        let agentStatus = statusManager.GetDetailedAgentStatus("dev1")
        
        // Then
        Assert.NotNull(agentStatus)
        Assert.Equal("dev1", agentStatus.BasicStatus.AgentId)
```

### Integration Testing

```fsharp
[<Fact>]
let ``リアルタイム進捗ダッシュボード - エンドツーエンドフロー`` () =
    // Given
    let collaborationFacade = createTestCollaborationFacade()
    let progressDashboard = createProgressDashboard(collaborationFacade)
    
    // When - タスク実行から進捗更新まで
    let taskId = "test-task-001"
    collaborationFacade.StartTask(taskId, "dev1") |> Async.RunSynchronously
    
    // 進捗更新をシミュレート
    collaborationFacade.UpdateTaskProgress(taskId, 0.5) |> Async.RunSynchronously
    
    // Then
    let overallProgress = progressDashboard.GetOverallProgress()
    let agentProgress = progressDashboard.GetAgentProgress("dev1")
    
    Assert.True(overallProgress.OverallCompletionRate > 0.0)
    Assert.NotNull(agentProgress.CurrentTask)
    Assert.Equal(0.5, agentProgress.TaskProgress)
```

## Performance Considerations

### Real-time Updates

- **更新頻度**: 進捗情報は1秒間隔で更新
- **データ取得**: 差分更新による効率的なデータ取得
- **UI描画**: 変更部分のみの部分更新

### Memory Management

- **履歴データ**: 直近7日分の詳細履歴を保持
- **メトリクス**: リアルタイムメトリクスのローリング平均
- **キャッシュ**: 頻繁にアクセスされる計算結果のキャッシュ

### Scalability

- **エージェント数**: 最大20エージェントの同時監視
- **タスク数**: 最大200タスクの依存関係管理
- **履歴期間**: 効率的な履歴データの圧縮・アーカイブ

### Display Settings

```fsharp
type DisplaySettings = {
    UpdateInterval: TimeSpan
    ShowDetailedMetrics: bool
    AlertThresholds: AlertThreshold list
    VisibleComponents: ComponentVisibility list
    ColorScheme: ColorScheme
    AutoRefresh: bool
}

and AlertThreshold = {
    MetricType: MetricType
    WarningLevel: float
    CriticalLevel: float
    NotificationEnabled: bool
}

and ComponentVisibility = {
    ComponentId: string
    IsVisible: bool
    Priority: int
}
```

## Integration Points

### Existing System Integration

- **ProgressAggregator**: 進捗データの取得・集計
- **AgentStateManager**: エージェント状態の監視
- **TaskDependencyGraph**: 依存関係・ブロッカー分析
- **RealtimeCollaborationFacade**: リアルタイム更新の統合

### External System Integration

- **Quality Gate Integration**: 品質ゲート結果の進捗への反映
- **Escalation Management**: エスカレーション状況の進捗表示
- **Time Management**: 時間管理システムとの連携

## Security Considerations

- **データアクセス**: エージェント情報の適切なアクセス制御
- **履歴データ**: 機密性の高い作業履歴の保護
- **リアルタイム通信**: 安全な進捗データの送受信

## Extensibility

### Custom Metrics

```fsharp
type CustomMetric = {
    MetricId: string
    Name: string
    Description: string
    CalculationFunction: AgentProgressStatus -> float
    DisplayFormat: DisplayFormat
    AlertRules: AlertRule list
}
```

### Plugin Architecture

```fsharp
type IProgressDashboardPlugin =
    abstract member PluginId : string
    abstract member Name : string
    abstract member Description : string
    abstract member GetCustomVisualization : OverallProgressStatus -> View option
    abstract member GetCustomMetrics : AgentProgressStatus -> CustomMetric list
```

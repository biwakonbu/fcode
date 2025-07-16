# Design Document - Escalation Management UI

## Overview

Escalation Management UIは、既存のEscalationManagerシステムとfcodeのTUIを統合し、重要な判断が必要な状況をPOに効率的に提示するシステムです。致命度評価、判断待機管理、代替作業提案、チーム影響分析を通じて、POの意思決定を包括的に支援します。

## Architecture

### System Integration Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    fcode TUI Layer                          │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────┐ │
│ │エスカレー   │ │判断待機     │ │代替作業     │ │会話     │ │
│ │ション表示   │ │キュー       │ │提案         │ │ペイン   │ │
│ └─────────────┘ └─────────────┘ └─────────────┘ └─────────┘ │
├─────────────────────────────────────────────────────────────┤
│              Escalation UI Management Layer                 │
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐             │
│ │EscalationUI │ │DecisionQueue│ │Alternative  │             │
│ │Manager      │ │Manager      │ │WorkManager  │             │
│ └─────────────┘ └─────────────┘ └─────────────┘             │
├─────────────────────────────────────────────────────────────┤
│              Existing Collaboration Layer                   │
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐             │
│ │Escalation   │ │TaskAssignment│ │AgentState   │             │
│ │Manager      │ │Manager      │ │Manager      │             │
│ └─────────────┘ └─────────────┘ └─────────────┘             │
└─────────────────────────────────────────────────────────────┘
```

### Data Flow Architecture

```
[Problem Detection] → [EscalationManager] → [Severity Assessment]
         ↓                    ↓                      ↓
[EscalationUIManager] → [DecisionQueueManager] → [UI Notification]
         ↓                    ↓                      ↓
[PO Decision] → [AlternativeWorkManager] → [Task Reassignment]
         ↓                    ↓                      ↓
[Decision Execution] → [Team Notification] → [Progress Update]
```

## Components and Interfaces

### EscalationUIManager

エスカレーション表示を管理するメインコンポーネント

```fsharp
type EscalationUIManager(escalationManager: EscalationManager,
                        taskAssignmentManager: TaskAssignmentManager,
                        agentStateManager: AgentStateManager) =
    
    // エスカレーション通知の表示
    member _.ShowEscalationAlert(escalation: EscalationInfo) : unit
    
    // 問題詳細情報の表示
    member _.DisplayProblemDetails(problemId: string) : ProblemDetails
    
    // 判断オプションの提示
    member _.PresentDecisionOptions(escalation: EscalationInfo) : DecisionOption list
    
    // 判断結果の実行
    member _.ExecuteDecision(decision: PODecision) : Async<Result<unit, EscalationError>>
    
    // エスカレーション履歴の表示
    member _.ShowEscalationHistory(filter: HistoryFilter option) : EscalationHistory list
```

### DecisionQueueManager

PO判断待機キューを管理するコンポーネント

```fsharp
type DecisionQueueManager() =
    
    // 判断待機キューへの追加
    member _.AddToDecisionQueue(escalation: EscalationInfo) : unit
    
    // 判断待機キューの取得
    member _.GetDecisionQueue() : DecisionQueueItem list
    
    // 優先度順ソート
    member _.SortByPriority(items: DecisionQueueItem list) : DecisionQueueItem list
    
    // 判断完了時の削除
    member _.RemoveFromQueue(escalationId: string) : unit
    
    // 長期保留の検出
    member _.DetectLongPendingItems() : DecisionQueueItem list
```

### AlternativeWorkManager

代替作業提案を管理するコンポーネント

```fsharp
type AlternativeWorkManager(taskAssignmentManager: TaskAssignmentManager,
                           agentStateManager: AgentStateManager) =
    
    // 代替作業の提案
    member _.SuggestAlternativeWork(blockedTask: string, 
                                   availableAgents: string list) : AlternativeWork list
    
    // 代替作業の実行
    member _.ExecuteAlternativeWork(alternative: AlternativeWork) : Async<Result<unit, string>>
    
    // 元作業への復帰
    member _.ReturnToOriginalWork(originalTask: string) : Async<Result<unit, string>>
    
    // 作業効率の評価
    member _.EvaluateWorkEfficiency(completedWork: CompletedWork) : EfficiencyMetrics
```

## Data Models

### EscalationInfo

```fsharp
type EscalationInfo = {
    EscalationId: string
    ProblemId: string
    SeverityLevel: SeverityLevel
    Title: string
    Description: string
    ImpactAnalysis: ImpactAnalysis
    RecommendedActions: RecommendedAction list
    RelatedTasks: string list
    AffectedAgents: string list
    CreatedAt: DateTime
    Deadline: DateTime option
}

and SeverityLevel =
    | Level1 // 軽微 - 情報表示のみ
    | Level2 // 軽度 - 自動対応
    | Level3 // 中度 - PO通知
    | Level4 // 重度 - 即座判断要求
    | Level5 // 致命的 - 緊急停止

and ImpactAnalysis = {
    AffectedTasks: int
    EstimatedDelay: TimeSpan option
    ResourceImpact: ResourceImpact
    BusinessImpact: BusinessImpact option
}
```

### DecisionQueueItem

```fsharp
type DecisionQueueItem = {
    EscalationInfo: EscalationInfo
    QueuedAt: DateTime
    Priority: Priority
    Status: DecisionStatus
    EstimatedDecisionTime: TimeSpan
    RelatedItems: string list
}

and DecisionStatus =
    | Pending
    | InReview
    | AwaitingAdditionalInfo
    | Escalated
    | Resolved

and Priority =
    | Critical
    | High
    | Medium
    | Low
```

### PODecision

```fsharp
type PODecision = {
    EscalationId: string
    DecisionType: DecisionType
    Reasoning: string
    AdditionalInstructions: string option
    DecidedAt: DateTime
    ExpectedOutcome: string option
}

and DecisionType =
    | Continue of ContinueAction
    | Postpone of PostponeAction
    | Cancel of CancelAction
    | RequestMoreInfo of InfoRequest

and ContinueAction = {
    ModifiedApproach: string option
    AdditionalResources: string list
    NewDeadline: DateTime option
}

and PostponeAction = {
    PostponeUntil: DateTime option
    Reason: string
    AlternativePriority: Priority
}
```

### AlternativeWork

```fsharp
type AlternativeWork = {
    WorkId: string
    Title: string
    Description: string
    EstimatedDuration: TimeSpan
    RequiredSkills: string list
    SuitableAgents: string list
    Priority: Priority
    RelatedToOriginal: bool
    ExpectedValue: WorkValue
}

and WorkValue =
    | HighValue // 元タスクと同等の価値
    | MediumValue // 部分的価値
    | LowValue // 学習・準備作業
    | MaintenanceValue // メンテナンス・改善作業
```

## Error Handling

### Escalation UI Error Types

```fsharp
type EscalationUIError =
    | EscalationNotFound of string
    | DecisionExecutionFailed of string
    | AlternativeWorkNotAvailable of string
    | TeamNotificationFailed of string list
    | HistoryAccessError of string
    | UIRenderingError of string

// エラーハンドリング戦略
let handleEscalationUIError error =
    match error with
    | EscalationNotFound escalationId ->
        // エスカレーション情報の再取得
        retryEscalationRetrieval escalationId
    | DecisionExecutionFailed reason ->
        // 判断実行の手動フォールバック
        requestManualDecisionExecution reason
    | AlternativeWorkNotAvailable reason ->
        // 手動での代替作業提案要求
        requestManualAlternativeWork reason
    | TeamNotificationFailed agents ->
        // 通知失敗エージェントへの再通知
        retryTeamNotification agents
    | HistoryAccessError message ->
        // 履歴アクセスの代替手段提供
        provideAlternativeHistoryAccess message
    | UIRenderingError message ->
        // UI描画の再試行
        retryUIRendering message
```

## UI Design Specifications

### Escalation Alert Display

エスカレーション発生時のアラート表示：

```
┌─ ⚠️ 重要判断が必要です ────────────────────────────────────────┐
│ 致命度: Level 4 (重度)  │ 発生時刻: 14:35:22  │ ID: ESC-001  │
│ 問題: ユーザー認証システムの仕様変更が必要                     │
│ 影響: 3つのタスクに波及、スプリント遅延の可能性               │
├────────────────────────────────────────────────────────────────┤
│ 📊 詳細分析                                                    │
│ • 影響範囲: 認証モジュール全体 (5ファイル、2API)               │
│ • 推定遅延: 2-4時間                                           │
│ • 関連タスク: AUTH-001, AUTH-002, AUTH-003                    │
│ • 影響メンバー: dev2 (ブロック), qa1 (テスト待機)             │
├────────────────────────────────────────────────────────────────┤
│ 💡 推奨対応策                                                  │
│ 1. OAuth2.0への移行 (推奨) - セキュリティ向上、標準準拠       │
│ 2. 現行システム拡張 - 短期対応、技術的負債増加                │
│ 3. 外部認証サービス統合 - 開発工数削減、依存関係増加          │
├────────────────────────────────────────────────────────────────┤
│ 👥 チーム状況                                                  │
│ • dev1: 🟡 設計待機中 (他タスクで継続可能)                    │
│ • dev2: 🔴 ブロック中 (代替作業: UI改善タスク提案)            │
│ • qa1: 🟡 テスト準備中 (代替作業: 探索的テスト継続)           │
├────────────────────────────────────────────────────────────────┤
│ ⚖️ 判断オプション                                              │
│ [継続: OAuth移行] [継続: 現行拡張] [後回し] [詳細確認]         │
└────────────────────────────────────────────────────────────────┘
```

### Decision Queue Display

判断待機キューの表示：

```
┌─ 📋 判断待機キュー ────────────────────────────────────────────┐
│ 待機中: 3件  │ 高優先度: 1件  │ 平均待機時間: 8分            │
├────────────────────────────────────────────────────────────────┤
│ 🔴 ESC-001 │ Lv4 │ 認証システム仕様変更 │ 待機: 12分 │ [詳細] │
│ 🟡 ESC-003 │ Lv3 │ API応答時間改善     │ 待機: 6分  │ [詳細] │
│ 🟡 ESC-005 │ Lv3 │ UI表示バグ修正      │ 待機: 3分  │ [詳細] │
├────────────────────────────────────────────────────────────────┤
│ 📈 統計情報                                                    │
│ • 今日の判断件数: 8件 (平均解決時間: 15分)                    │
│ • 今週の傾向: 認証関連問題が増加傾向                          │
│ • 推奨: 認証システムの根本的見直しを検討                      │
└────────────────────────────────────────────────────────────────┘
```

### Alternative Work Suggestion

代替作業提案の表示：

```
┌─ 🔄 代替作業提案 ──────────────────────────────────────────────┐
│ ブロック中タスク: AUTH-002 (dev2担当)                         │
│ ブロック理由: 認証仕様変更待ち                                 │
├────────────────────────────────────────────────────────────────┤
│ 💼 提案代替作業 (dev2向け)                                     │
│ ┌──────────────────────────────────────────────────────────┐   │
│ │ 🎯 UI-005: ユーザープロフィール画面改善                  │   │
│ │ 推定時間: 45分  │ 価値: 高  │ 関連性: 中                │   │
│ │ 説明: 認証完了後の画面なので、認証仕様に依存しない        │   │
│ └──────────────────────────────────────────────────────────┘   │
│ ┌──────────────────────────────────────────────────────────┐   │
│ │ 🔧 TECH-012: コードリファクタリング                      │   │
│ │ 推定時間: 30分  │ 価値: 中  │ 関連性: 低                │   │
│ │ 説明: 技術的負債削減、認証システムとは独立               │   │
│ └──────────────────────────────────────────────────────────┘   │
├────────────────────────────────────────────────────────────────┤
│ ⚖️ 選択オプション                                              │
│ [UI-005実行] [TECH-012実行] [待機継続] [手動調整]             │
└────────────────────────────────────────────────────────────────┘
```

## Testing Strategy

### Unit Testing

```fsharp
module EscalationUITests =
    
    [<Fact>]
    let ``EscalationUIManager - エスカレーション通知の正常表示`` () =
        // Given
        let mockEscalationManager = createMockEscalationManager()
        let uiManager = EscalationUIManager(mockEscalationManager, mockTaskManager, mockAgentManager)
        let escalation = createTestEscalation(SeverityLevel.Level4)
        
        // When
        uiManager.ShowEscalationAlert(escalation)
        
        // Then
        // UI表示の検証
        Assert.True(isEscalationAlertDisplayed())
    
    [<Fact>]
    let ``DecisionQueueManager - 優先度順ソート`` () =
        // Given
        let queueManager = DecisionQueueManager()
        let items = createMixedPriorityItems()
        
        // When
        let sortedItems = queueManager.SortByPriority(items)
        
        // Then
        Assert.Equal(Priority.Critical, sortedItems.[0].Priority)
        Assert.Equal(Priority.Low, sortedItems.[sortedItems.Length - 1].Priority)
```

### Integration Testing

```fsharp
[<Fact>]
let ``エスカレーション管理UI - エンドツーエンドフロー`` () =
    // Given
    let collaborationFacade = createTestCollaborationFacade()
    let escalationUI = createEscalationUI(collaborationFacade)
    
    // When - 問題発生からPO判断まで
    let problemId = "test-problem-001"
    collaborationFacade.TriggerEscalation(problemId, SeverityLevel.Level4)
    
    let decision = { 
        EscalationId = problemId
        DecisionType = Continue { ModifiedApproach = Some "OAuth2.0移行"; AdditionalResources = []; NewDeadline = None }
        Reasoning = "セキュリティ向上のため"
        AdditionalInstructions = None
        DecidedAt = DateTime.UtcNow
        ExpectedOutcome = Some "認証システムの安全性向上"
    }
    
    escalationUI.ExecuteDecision(decision) |> Async.RunSynchronously
    
    // Then
    let queueStatus = escalationUI.GetDecisionQueue()
    Assert.Empty(queueStatus) // 判断完了により キューから削除
```

## Performance Considerations

### Real-time Notifications

- **通知遅延**: エスカレーション発生から1秒以内に通知
- **UI更新**: 判断実行から3秒以内にチーム状況更新
- **キュー管理**: 100件までの判断待機アイテムを効率的に管理

### Memory Management

- **履歴保持**: 直近30日分のエスカレーション履歴
- **キューサイズ**: 最大50件の同時判断待機
- **UI状態**: 不要な表示状態の自動クリーンアップ

### Scalability

- **並行エスカレーション**: 複数問題の同時処理対応
- **判断履歴**: 効率的な検索・フィルタリング機能
- **通知システム**: 大量通知時のパフォーマンス最適化

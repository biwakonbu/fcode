# fcode リアルタイム協調アーキテクチャ

**目的**: PO中心の自律開発チーム実現のための協調作業基盤設計

## 1. アーキテクチャ概要

### 1.1 システム全体像

```
┌─────────────────────────────────────────────────────────────────────┐
│                     fcode メインプロセス                              │
│                  (F# + Terminal.Gui 1.15.0)                        │
├─────────────────────────────────────────────────────────────────────┤
│                      UI レイヤー                                    │
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐     │
│ │会話ペイン    │ │dev1-3 ペイン │ │qa1-2 ペイン  │ │ux/pm ペイン │     │
│ │PO指示入力   │ │実装・レビュー│ │テスト設計    │ │UI/進捗管理  │     │
│ └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘     │
├─────────────────────────────────────────────────────────────────────┤
│                   協調作業管理レイヤー                                │
│ ┌───────────────────────────────────────────────────────────────────┐ │
│ │           RealtimeCollaborationFacade                           │ │
│ │              統合ファサード・イベント統合                          │ │
│ └───────────────────────────────────────────────────────────────────┘ │
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐     │
│ │AgentState   │ │TaskDependency│ │Progress     │ │Collaboration│     │
│ │Manager      │ │Graph        │ │Aggregator   │ │Coordinator  │     │
│ │・状態追跡    │ │・依存関係    │ │・進捗監視    │ │・競合制御    │     │
│ │・健全性監視  │ │・実行可能性  │ │・トレンド分析│ │・デッドロック│     │
│ └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘     │
├─────────────────────────────────────────────────────────────────────┤
│                    永続化レイヤー                                    │
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐     │
│ │SQLite3      │ │Session      │ │Message      │ │Configuration│     │
│ │タスク管理    │ │Persistence  │ │Persistence  │ │Manager      │     │
│ │・タスク状態  │ │・セッション  │ │・エージェント│ │・設定管理    │     │
│ │・依存関係    │ │・UI状態     │ │・通信履歴    │ │・環境変数    │     │
│ └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘     │
├─────────────────────────────────────────────────────────────────────┤
│                   エージェント実行レイヤー                           │
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐     │
│ │Claude Code  │ │Custom Script│ │SDK統合      │ │外部エージェント│   │
│ │CLI統合      │ │CLI          │ │・TUI内統合  │ │・将来拡張    │     │
│ │・プロセス制御│ │・カスタム処理│ │・ライブラリ連携│ │・他AI統合    │     │
│ └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘     │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.2 データフロー

```
[PO指示] → [会話ペイン] → [TaskInfo生成] → [依存関係解析] → [エージェント自動割り当て]
    ↓
[リソース競合チェック] → [タスク実行許可] → [Claude Code/SDK呼び出し] → [進捗監視]
    ↓
[結果取得] → [UI表示] → [完了通知] → [次タスク実行可能性判定] → [自動継続実行]
```

## 2. コアコンポーネント設計

### 2.1 RealtimeCollaborationFacade

**役割**: 協調作業の統合制御・イベント統合

```fsharp
type RealtimeCollaborationFacade(config: CollaborationConfig) =
    // コアコンポーネント統合
    let agentStateManager = new AgentStateManager(config)
    let taskDependencyGraph = new TaskDependencyGraph(config)
    let progressAggregator = new ProgressAggregator(agentStateManager, taskDependencyGraph, config)
    let collaborationCoordinator = new CollaborationCoordinator(agentStateManager, taskDependencyGraph, config)
    
    // 統合イベントシステム
    let systemEvent = Event<SystemEvent>()
    
    // === 高レベル統合機能 ===
    member _.AutoAssignTask(taskId: string) // エージェント自動割り当て
    member _.ExecuteWorkflow(taskIds: string list) // ワークフロー実行
    member _.ExecuteTaskWithCoordination(taskId: string) // 協調制御付き実行
    member _.PerformSystemHealthCheck() // システム健全性チェック
```

**実装状況**: ✅ 完成 (400行, 包括的機能)

### 2.2 AgentStateManager

**役割**: エージェント状態追跡・健全性監視

```fsharp
type AgentStateManager(config: CollaborationConfig) =
    let states = ConcurrentDictionary<string, AgentState>()
    let stateChangedEvent = Event<AgentState>()
    
    member _.UpdateAgentState(agentId, status, ?progress, ?currentTask, ?workingDir, ?processId)
    member _.GetAgentsByStatus(status: AgentStatus)
    member _.PerformHealthCheck() // 停滞エージェント検出
    member _.GetActiveAgentCount()
    member _.GetAverageProgress()
```

**実装状況**: ✅ 完成 (268行, 型安全・並行安全)

### 2.3 TaskDependencyGraph

**役割**: タスク依存関係管理・実行可能性判定

```fsharp
type TaskDependencyGraph(config: CollaborationConfig) =
    let tasks = ConcurrentDictionary<string, TaskInfo>()
    let dependencies = ConcurrentDictionary<string, TaskDependency>()
    
    member _.AddTask(task: TaskInfo)
    member _.AddDependency(taskId: string, dependsOnTaskId: string)
    member _.GetExecutableTasks() // 依存関係解決済みタスク取得
    member _.CompleteTask(taskId: string) // 完了処理・新規実行可能タスク判定
    member _.DetectCircularDependencies() // 循環依存検出
```

**実装状況**: ✅ 完成 (549行, グラフアルゴリズム実装)

### 2.4 CollaborationCoordinator

**役割**: 競合回避・リソース制御・デッドロック検出

```fsharp
type CollaborationCoordinator(agentStateManager, taskDependencyGraph, config) =
    let resourceLocks = ConcurrentDictionary<string, (string * DateTime)>()
    let operationQueue = ConcurrentQueue<string * string * DateTime>()
    
    member _.RequestTaskExecution(agentId, taskId, requiredResources) // リソース競合チェック
    member _.NotifyTaskCompletion(agentId, taskId, releasedResources) // リソース解放
    member _.DetectDeadlock() // デッドロック検出・解決
    member _.AnalyzeCollaborationEfficiency() // 並列作業効率分析
```

**実装状況**: ✅ 完成 (496行, 高度な協調制御)

### 2.5 ProgressAggregator

**役割**: 進捗監視・トレンド分析・レポート生成

```fsharp
type ProgressAggregator(agentStateManager, taskDependencyGraph, config) =
    member _.GetCurrentSummary() // 現在の進捗サマリー
    member _.AnalyzeProgressTrend() // 進捗トレンド分析
    member _.CheckMilestones(milestones) // マイルストーン達成チェック
    member _.GenerateProgressReport() // 詳細レポート生成
    member _.StartMonitoring(intervalSeconds) // リアルタイム監視開始
```

**実装状況**: ✅ 完成 (408行, 分析機能充実)

## 3. イベント駆動アーキテクチャ

### 3.1 システムイベント統合

```fsharp
type SystemEvent =
    | AgentStateChanged of AgentState
    | TaskChanged of TaskInfo
    | ProgressUpdated of ProgressSummary
    | CollaborationEventOccurred of CollaborationEvent
    | SystemReset

type CollaborationEvent =
    | TaskStarted of agentId: string * taskId: string * resources: string list
    | TaskCompleted of agentId: string * taskId: string * resources: string list
    | SynchronizationRequested of agents: string list * reason: string
    | DeadlockDetected of agents: string list
```

### 3.2 イベントフロー

```
エージェント状態変更 → AgentStateChanged → UI更新 → 進捗表示更新
タスク完了通知 → TaskCompleted → 依存関係再評価 → 新規実行可能タスク判定
デッドロック検出 → DeadlockDetected → 競合解決戦略実行 → 自動復旧
```

## 4. 型安全性・エラーハンドリング

### 4.1 Result型による一貫したエラー処理

```fsharp
type CollaborationError =
    | InvalidInput of string
    | NotFound of string
    | CircularDependency of string list
    | ConcurrencyError of string
    | SystemError of string
    | ConflictDetected of ConflictType list
    | DeadlockDetected of string list
    | ResourceUnavailable of string

// 全API共通のResult型返却
member _.UpdateAgentState(...) : Result<unit, CollaborationError>
member _.ExecuteWorkflow(...) : Async<Result<(string * Result<string, CollaborationError>) list, CollaborationError>>
```

### 4.2 並行安全性

- **ConcurrentDictionary**: 状態管理・タスク管理
- **ロック機構**: 重要な状態変更時の排他制御
- **アトミック操作**: リソース割り当て・解放

## 5. テスト戦略

### 5.1 テストカバレッジ

**総テスト数**: 625行の包括的テストスイート

```fsharp
// Unit Tests - 基本機能テスト
[<Trait("TestCategory", "Unit")>]
[<Fact>]
let ``AgentStateManager - エージェント状態の基本操作テスト`` () = ...

// Integration Tests - 統合機能テスト  
[<Trait("TestCategory", "Integration")>]
[<Fact>]
let ``RealtimeCollaborationFacade - ワークフロー実行統合テスト`` () = ...

// Performance Tests - パフォーマンステスト
[<Trait("TestCategory", "Performance")>]
[<Fact>]
let ``CollaborationCoordinator - 大量タスク並列実行テスト`` () = ...
```

### 5.2 テスト実行戦略

```bash
# CI環境: 基本機能テスト
dotnet test --filter "TestCategory=Unit"

# 開発環境: 全機能テスト
dotnet test tests/fcode.Tests.fsproj

# パフォーマンス検証
dotnet test --filter "TestCategory=Performance"
```

## 6. 設定管理

### 6.1 CollaborationConfig

```fsharp
type CollaborationConfig = {
    // 基本設定
    MaxConcurrentAgents: int
    TaskTimeoutMinutes: int
    StaleAgentThreshold: TimeSpan
    MaxRetryAttempts: int
    
    // SQLite設定 (将来実装)
    DatabasePath: string
    ConnectionPoolSize: int
    WALModeEnabled: bool
    AutoVacuumEnabled: bool
} with
    static member Default = {
        MaxConcurrentAgents = 10
        TaskTimeoutMinutes = 30
        StaleAgentThreshold = TimeSpan.FromMinutes(5.0)
        MaxRetryAttempts = 3
        DatabasePath = "~/.fcode/tasks.db"
        ConnectionPoolSize = 5
        WALModeEnabled = true
        AutoVacuumEnabled = true
    }
```

## 7. パフォーマンス・スケーラビリティ

### 7.1 並列処理最適化

- **非同期処理**: async/await パターンの一貫した使用
- **並行コレクション**: ConcurrentDictionary, ConcurrentQueue
- **リソースプール**: データベース接続プール（将来実装）

### 7.2 メモリ効率

- **IDisposable**: 適切なリソース管理
- **イベント購読解除**: メモリリーク防止
- **状態履歴制限**: 古い状態データの自動削除

## 8. 拡張性・将来対応

### 8.1 マルチエージェント統合準備

```fsharp
// 将来的な他エージェント統合インターフェース
type IAgentExecutor =
    abstract member ExecuteTask: TaskInfo -> Async<Result<AgentResponse, Error>>

type ClaudeCodeExecutor() =
    interface IAgentExecutor with
        member _.ExecuteTask(task) = // Claude Code CLI統合

type CustomScriptExecutor(script: string) =
    interface IAgentExecutor with  
        member _.ExecuteTask(task) = // カスタムスクリプト実行

type SDKExecutor(apiKey: string) =
    interface IAgentExecutor with
        member _.ExecuteTask(task) = // Claude SDK直接統合
```

### 8.2 プラグインアーキテクチャ対応

- **Interface分離**: 各コンポーネントのInterface定義完了
- **依存注入**: 外部実装の差し込み可能
- **設定外部化**: 動的な動作変更対応

## 9. 運用・監視

### 9.1 健全性監視

```fsharp
member _.PerformSystemHealthCheck() =
    // エージェント健全性チェック
    // デッドロック検出  
    // 循環依存検出
    // システム全体健全性評価
```

### 9.2 統計・分析

```fsharp
member _.AnalyzeCollaborationEfficiency() =
    // 並列作業効率分析
    // リソース使用率分析
    // ボトルネック検出
```

## 10. 実装状況・次期ステップ

### 10.1 完成済み機能 (2,526行)

- ✅ **RealtimeCollaborationFacade**: 統合ファサード (400行)
- ✅ **AgentStateManager**: エージェント状態管理 (268行)  
- ✅ **TaskDependencyGraph**: タスク依存関係管理 (549行)
- ✅ **CollaborationCoordinator**: 競合制御・デッドロック検出 (496行)
- ✅ **ProgressAggregator**: 進捗監視・分析 (408行)
- ✅ **CollaborationTypes**: 型定義・エラー型 (163行)
- ✅ **Interface定義**: 拡張可能アーキテクチャ (242行)

### 10.2 次期実装ステップ

**Phase 1**: UI統合
- 会話ペインからのPO指示 → TaskInfo変換
- インタラクティブUI統合
- ペイン間イベント連携

**Phase 2**: SQLite永続化 ([TASK_STORAGE_DESIGN.md](./TASK_STORAGE_DESIGN.md) 参照)
- タスク・依存関係永続化
- エージェント状態履歴
- 進捗・統計データ保存

**Phase 3**: エージェント統合
- Claude Code CLI統合完成
- Claude SDK直接統合
- カスタムエージェント対応

## 11. 技術的特記事項

### 11.1 F#言語活用

- **型安全性**: Option型、Result型による安全なプログラミング
- **パターンマッチ**: 状態遷移・エラーハンドリングの表現力
- **非同期処理**: async computation expressionの活用
- **関数型**: 副作用の制御・テスタビリティ向上

### 11.2 並行プログラミング

- **Actor like パターン**: 各エージェントの独立状態管理
- **イベント駆動**: リアクティブな状態変更伝播
- **競合制御**: デッドロック検出・リソース管理

---

**最終更新**: 2025-07-02  
**実装者**: Claude Code  
**レビュー**: 完了 (批判的レビュー → 戦略的価値確認 → アーキテクチャ文書化)
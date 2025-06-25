# プロセス分離アーキテクチャ設計書

## 概要

fcodeは、Claude Codeの不安定性（JSランタイム特性、メモリリーク、ネットワーク依存）に対処するため、tmuxライクなプロセス分離アーキテクチャを採用する。

## アーキテクチャ詳細

### システム構成

```
┌─────────────────────────────────────────────┐
│ fcode メインプロセス (TUI層)                   │
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ │
│ │会話ペイン     │ │dev1ペイン    │ │dev2ペイン    │ │
│ │(UI表示のみ)   │ │(UI表示のみ)   │ │(UI表示のみ)   │ │
│ └─────────────┘ └─────────────┘ └─────────────┘ │
├─────────────────────────────────────────────┤
│ プロセススーパーバイザー                       │
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ │
│ │セッション管理  │ │健全性監視     │ │IPC管理      │ │
│ │- 起動/停止    │ │- ハートビート  │ │- メッセージ   │ │
│ │- 状態追跡     │ │- リソース監視  │ │- キューイング │ │
│ │- 復旧制御     │ │- 自動再起動   │ │- 同期制御    │ │
│ └─────────────┘ └─────────────┘ └─────────────┘ │
└─┬───────────────────────────────────────────┘
  │ IPC Layer (Named Pipes / Unix Domain Sockets)
  │
  ├─── Claude Code Worker Process (dev1)
  │    ├─ Process Monitor Thread
  │    ├─ Input/Output Handler
  │    └─ Session State Manager
  │
  ├─── Claude Code Worker Process (dev2)
  │    ├─ Process Monitor Thread
  │    ├─ Input/Output Handler
  │    └─ Session State Manager
  │
  └─── [dev3, qa1, qa2, ux, pm] Workers...
```

## 設計コンポーネント

### 1. プロセススーパーバイザー (ProcessSupervisor)

```fsharp
module ProcessSupervisor

type WorkerStatus = 
    | Starting
    | Running
    | Unhealthy
    | Crashed
    | Stopping

type WorkerProcess = {
    PaneId: string
    ProcessId: int option
    Status: WorkerStatus
    LastHeartbeat: DateTime
    RestartCount: int
    SessionId: string
    MemoryUsageMB: float
    CpuUsagePercent: float
}

type SupervisorConfig = {
    HeartbeatIntervalMs: int // 2000ms
    MemoryLimitMB: float     // 512MB per process
    CpuLimitPercent: float   // 50% per process
    MaxRestarts: int         // 5 times per hour
    RestartCooldownMs: int   // 10000ms
    HealthCheckTimeoutMs: int // 5000ms
}
```

#### 主要機能:

1. **プロセス生存監視**
   - 2秒間隔のハートビート確認
   - プロセスID追跡
   - 応答時間測定

2. **リソース監視**
   - メモリ使用量監視 (上限: 512MB/プロセス)
   - CPU使用率監視 (上限: 50%/プロセス)
   - ファイルディスクリプタ数監視

3. **自動復旧制御**
   - 異常検出から3秒以内の再起動
   - 再起動回数制限 (1時間に5回まで)
   - クールダウン期間設定

### 2. セッション管理 (SessionManager)

```fsharp
module SessionManager

type SessionState = {
    SessionId: string
    PaneId: string
    WorkingDirectory: string
    ConversationHistory: Message list
    LastActivity: DateTime
    IsDetached: bool
    AutoSaveState: byte[]
}

type SessionPersistence = {
    SessionsDirectory: string  // ~/.local/share/fcode/sessions/
    AutoSaveIntervalMs: int    // 30000ms (30秒)
    MaxHistorySize: int        // 1000 messages
    CompressionEnabled: bool   // true
}
```

#### tmuxライク機能:

1. **セッション永続化**
   - セッション状態の自動保存 (30秒間隔)
   - 異常終了時の状態復元
   - 会話履歴の圧縮保存

2. **デタッチ/アタッチ**
   - ネットワーク断絶時のセッション保持
   - 再接続時の状態復元
   - 複数クライアントからの同一セッション参照

3. **セッション一覧管理**
   - アクティブセッション表示
   - 孤立セッションの検出・清掃
   - セッション間の切り替え

### 3. プロセス間通信 (IPC)

```fsharp
module IPC

type IPCMessage = 
    | StartSession of PaneId: string * WorkingDir: string
    | StopSession of PaneId: string
    | SendInput of PaneId: string * Input: string
    | ReceiveOutput of PaneId: string * Output: string
    | Heartbeat of PaneId: string * Timestamp: DateTime
    | ProcessCrashed of PaneId: string * ExitCode: int
    | ResourceAlert of PaneId: string * ResourceType: string * Usage: float

type IPCTransport =
    | NamedPipes of PipeName: string
    | UnixDomainSocket of SocketPath: string
    | TCP of Host: string * Port: int
```

#### 通信方式選択:

- **Linux/macOS**: Unix Domain Sockets (高速・軽量)
- **プロセス間メッセージング**: JSON-based protocol
- **非同期通信**: F# Async with timeout handling
- **キューイング**: メッセージロスト防止

### 4. 健全性監視 (HealthMonitor)

```fsharp
module HealthMonitor

type HealthMetrics = {
    ProcessUptime: TimeSpan
    MemoryUsageMB: float
    CpuUsagePercent: float
    ResponseTimeMs: int
    LastActivity: DateTime
    ErrorCount: int
    RestartCount: int
}

type HealthThresholds = {
    MaxMemoryMB: float         // 512MB
    MaxCpuPercent: float       // 50%
    MaxResponseTimeMs: int     // 10000ms
    MaxInactivityMs: int       // 300000ms (5分)
    MaxErrorsPerHour: int      // 10 errors
}
```

#### 監視項目:

1. **プロセス生存監視**
   - プロセス存在確認
   - ゾンビプロセス検出
   - ハングアップ検出

2. **パフォーマンス監視**
   - メモリリーク検出
   - CPU使用率異常検出
   - 応答時間劣化検出

3. **業務論理監視**
   - API応答エラー頻度
   - セッション状態の整合性
   - 入出力データの妥当性

### 5. 予防的メンテナンス (PreventiveMaintenance)

```fsharp
module PreventiveMaintenance

type MaintenanceSchedule = {
    PeriodicRestartIntervalMs: int    // 3600000ms (1時間)
    GarbageCollectionIntervalMs: int  // 300000ms (5分)
    SessionCleanupIntervalMs: int     // 86400000ms (24時間)
    LogRotationIntervalMs: int        // 86400000ms (24時間)
}

type MaintenanceAction =
    | PeriodicRestart of PaneId: string
    | ForceGarbageCollection of PaneId: string
    | SessionCleanup
    | LogRotation
    | MemoryOptimization of PaneId: string
```

#### 予防策:

1. **定期リブート**
   - 1時間間隔での計画的再起動
   - アクティブ作業中の延期機能
   - グレースフル移行処理

2. **メモリ最適化**
   - 5分間隔でのGC強制実行
   - 使用メモリ量のしきい値監視
   - スワップ使用量の最小化

3. **セッション清掃**
   - 古いセッションファイルの削除
   - 孤立したプロセスの終了
   - ログファイルのローテーション

## エラー処理とフォールバック

### 1. エラー分類

```fsharp
type ProcessError =
    | StartupFailure of Reason: string
    | CommunicationFailure of LastKnownState: string
    | ResourceExhaustion of ResourceType: string
    | UnresponsiveProcess of SilentDurationMs: int
    | CorruptedSession of SessionId: string
    | NetworkConnectivityLoss
```

### 2. 復旧戦略

```fsharp
type RecoveryStrategy =
    | ImmediateRestart
    | DelayedRestart of DelayMs: int
    | FallbackToSafeMode
    | ManualIntervention of Reason: string
    | GracefulShutdown

let selectRecoveryStrategy error restartCount =
    match error, restartCount with
    | StartupFailure _, count when count < 3 -> DelayedRestart 5000
    | ResourceExhaustion _, _ -> ImmediateRestart
    | UnresponsiveProcess _, count when count < 5 -> ImmediateRestart
    | _, count when count >= 5 -> ManualIntervention "Max restart limit exceeded"
    | _ -> FallbackToSafeMode
```

### 3. ユーザー通知

- **非侵入的通知**: ステータスバーでの状態表示
- **重要なイベント**: ポップアップダイアログでの確認
- **ログ記録**: 詳細なトラブルシューティング情報

## 実装フェーズ

### Phase 1: 基盤実装 (2週間)
- ProcessSupervisor基本機能
- IPC通信機構
- 基本的なプロセス監視

### Phase 2: 監視・復旧 (2週間)  
- HealthMonitor実装
- 自動復旧機能
- エラー処理強化

### Phase 3: セッション永続化 (2週間)
- SessionManager実装
- tmuxライク機能
- 状態復元機能

### Phase 4: 最適化・運用機能 (1週間)
- PreventiveMaintenance実装
- パフォーマンス最適化
- 運用監視機能

## テスト戦略

### 1. 単体テスト
- 各モジュールの独立動作確認
- エラーケースの網羅的テスト
- パフォーマンス特性の測定

### 2. 統合テスト  
- プロセス間通信の正常性確認
- 異常系シナリオの動作確認
- 長時間運用での安定性確認

### 3. 障害テスト
- プロセスクラッシュ時の復旧確認
- ネットワーク断絶時の動作確認
- リソース枯渇時の挙動確認

この設計により、Claude Codeの不安定性に対する包括的な対策を実現し、ユーザーに安定した開発環境を提供する。
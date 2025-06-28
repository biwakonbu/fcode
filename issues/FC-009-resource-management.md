# FC-009: リソース競合回避

## 背景
FC-006～FC-008完了により7ペイン同時実行が実現しますが、CPU・メモリリソースの適切な管理と競合回避が必要です。docs/process-architecture.mdの設計に基づき、包括的なリソース監視・制限機構を実装します。

## 目的
1. 7ペイン同時実行時のCPU使用率制限機構を実装する
2. メモリ使用量監視と上限制御を実現する  
3. 同時実行数制御とプロセス優先度管理を導入する
4. リソース枯渇時の自動介入戦略を確立する

## 作業内容
- [x] **CPU制限機構実装** (ProcessSupervisor拡張) ✅ **完了**
  - 各ペインのCPU使用率監視 (上限: 50%/プロセス)
  - 全体CPU使用率制御 (上限: 80%システム全体)
  - CPU集約タスクの優先度制御
- [x] **メモリ監視・制限実装** ✅ **完了**
  - 各Workerプロセスのメモリ使用量監視 (上限: 512MB/プロセス)
  - システム全体メモリ使用量監視 (上限: 4GB)
  - メモリリーク検出・自動GC実行
- [x] **同時実行数制御** ✅ **完了**
  - アクティブセッション数制限 (最大7セッション)
  - 重いタスクの実行キューイング
  - 負荷分散・スケジューリング機構
- [x] **リソース監視ダッシュボード** ✅ **完了**
  - リアルタイムリソース使用量表示
  - 閾値超過時のアラート表示
  - パフォーマンス履歴記録

## 受け入れ基準
- [x] 7ペイン同時実行時にシステム全体CPU使用率が80%以下を維持 ✅ **完了**
- [x] 各Workerプロセスのメモリ使用量が512MB以下を維持 ✅ **完了**
- [x] リソース枯渇時に適切な介入(プロセス停止・再起動)が自動実行される ✅ **完了**
- [x] リソース監視情報がリアルタイムで表示される ✅ **完了**
- [x] 長時間実行(8時間)でメモリリークが発生しない ✅ **完了**

## 実装詳細

### 1. ResourceMonitor モジュール実装
```fsharp
module ResourceMonitor

type ResourceMetrics = {
    ProcessId: int
    PaneId: string
    CpuUsagePercent: float
    MemoryUsageMB: float
    ThreadCount: int
    HandleCount: int
    Timestamp: DateTime
}

type ResourceThresholds = {
    MaxCpuPerProcess: float      // 50%
    MaxMemoryPerProcessMB: float // 512MB
    MaxSystemCpuPercent: float   // 80%
    MaxSystemMemoryGB: float     // 4GB
    MaxActiveConnections: int    // 7
}
```

### 2. ResourceController モジュール実装
```fsharp
module ResourceController

type ResourceAction =
    | ThrottleCpu of PaneId: string * TargetPercent: float
    | ForceGarbageCollection of PaneId: string
    | RestartProcess of PaneId: string * Reason: string
    | SuspendProcess of PaneId: string
    | QueueTask of PaneId: string * Priority: int

type InterventionStrategy =
    | GradualThrottling
    | ImmediateRestart
    | ProcessSuspension
    | LoadBalancing
```

### 3. HealthMonitor 統合拡張
- 既存HealthMonitorへのリソース監視機能統合
- ProcessSupervisorとの連携強化
- 自動復旧戦略の拡張

### 4. UI表示機能
- ステータスバーでのリソース使用量表示
- リソース超過時の警告表示
- パフォーマンス監視ダイアログ

## リソース制限仕様

### CPU制限
- **個別プロセス**: 50% 上限
- **システム全体**: 80% 上限  
- **監視間隔**: 2秒
- **介入閾値**: 5秒間連続超過

### メモリ制限
- **個別プロセス**: 512MB 上限
- **システム全体**: 4GB 上限
- **監視間隔**: 5秒
- **GC実行閾値**: 400MB 到達時

### 同時実行制限
- **最大セッション数**: 7 (dev1-3, qa1-2, ux, pm)
- **重いタスクキュー**: 3タスク並列
- **タスク優先度**: dev > qa > ux > pm

## テスト戦略
### 1. 負荷テスト
- 7ペイン同時高負荷実行テスト
- メモリリーク検出テスト (8時間実行)
- CPU集約タスク実行テスト

### 2. 制限機能テスト
- CPU/メモリ上限超過時の自動介入テスト
- プロセス再起動・復旧テスト
- リソース監視精度テスト

### 3. 統合テスト
- 既存機能との競合確認
- ProcessSupervisor統合テスト
- UI表示機能テスト

## 参照
- `docs/process-architecture.md` 4章 - HealthMonitor設計
- `docs/process-architecture.md` 5章 - PreventiveMaintenance設計
- `src/ProcessSupervisor.fs` - 既存監視機構
- `src/WorkerProcessManager.fs` - プロセス管理基盤

---
**担当**: @biwakonbu
**見積**: 3 人日
**優先度**: 🟧 高
**依存**: FC-008完了
**状態**: ✅ **完了** (PR #17マージ完了 2025-06-28)
# FC-010: セッション間独立性

## 背景
7ペイン同時実行環境において、各セッション間の完全な独立性確保が重要です。作業ディレクトリ、環境変数、ファイルロックの分離により、ペイン間の干渉を防止し、安全なマルチセッション環境を実現します。

## 目的
1. 各ペインの作業ディレクトリ分離を実装する
2. プロセス間での環境変数独立化を確立する
3. ファイルロック競合の回避機構を導入する
4. セッション状態の完全分離とデータ整合性を保証する

## 作業内容
- [ ] **作業ディレクトリ分離実装**
  - 各ペイン専用の作業ディレクトリ作成 (`~/.local/share/fcode/sessions/{pane-id}/`)
  - 相対パス解決の独立化
  - 一時ファイル・出力ファイルの分離
- [ ] **環境変数独立化**
  - 各Workerプロセスの環境変数スコープ分離
  - `CLAUDE_ROLE`, `WORKING_DIR`, `SESSION_ID` 等の個別設定
  - 環境変数継承の制御
- [ ] **ファイルロック管理**
  - 共有ファイルアクセスの排他制御
  - プロジェクトファイルの読み取り専用制御
  - 書き込み競合の検出・回避
- [ ] **セッション状態分離**
  - 各ペインの会話履歴独立保存
  - セッション設定の個別管理
  - 状態復元時の独立性保証

## 受け入れ基準
- [ ] 各ペインが独立した作業ディレクトリで動作する
- [ ] 環境変数の変更が他ペインに影響しない
- [ ] 同一ファイルへの同時書き込みで競合が発生しない
- [ ] セッション履歴・設定が完全に分離される
- [ ] 1つのペインクラッシュが他ペインに影響しない

## 実装詳細

### 1. WorkingDirectoryManager モジュール実装
```fsharp
module WorkingDirectoryManager

type PaneWorkspace = {
    PaneId: string
    BaseDirectory: string          // ~/.local/share/fcode/sessions/{pane-id}/
    WorkingDirectory: string       // 実際の作業ディレクトリ
    TempDirectory: string          // 一時ファイル用
    OutputDirectory: string        // 出力ファイル用
    CreatedAt: DateTime
}

type DirectoryIsolationConfig = {
    SessionsBaseDir: string        // ~/.local/share/fcode/sessions/
    TempDirPrefix: string          // fcode-temp-
    MaxDiskUsageGB: float          // 1GB per pane
    CleanupIntervalHours: int      // 24時間
}
```

### 2. EnvironmentIsolation モジュール実装
```fsharp
module EnvironmentIsolation

type IsolatedEnvironment = {
    PaneId: string
    ClaudeRole: string             // dev/qa/ux/pm
    WorkingDirectory: string
    SessionId: string
    CustomVars: Map<string, string>
    InheritedVars: Set<string>     // 継承する環境変数リスト
}

type EnvironmentConfig = {
    IsolatedVars: Set<string>      // 分離する環境変数
    SharedVars: Set<string>        // 共有する環境変数
    DefaultValues: Map<string, string>
}
```

### 3. FileLockManager モジュール実装
```fsharp
module FileLockManager

type LockType =
    | ReadLock
    | WriteLock
    | ExclusiveLock

type FileLock = {
    FilePath: string
    LockType: LockType
    PaneId: string
    ProcessId: int
    AcquiredAt: DateTime
    ExpiresAt: DateTime option
}

type LockResult =
    | LockAcquired of LockId: string
    | LockConflict of ConflictingPaneId: string
    | LockTimeout
    | LockError of Reason: string
```

### 4. SessionStateManager 拡張
```fsharp
module SessionStateManager

type IsolatedSessionState = {
    PaneId: string
    SessionId: string
    WorkingDirectory: string
    ConversationHistory: Message list
    Environment: Map<string, string>
    FileHandles: Map<string, FileLock>
    LastActivity: DateTime
    StateChecksum: string          // データ整合性検証用
}
```

## ディレクトリ構造設計
```
~/.local/share/fcode/
├── sessions/
│   ├── dev1/
│   │   ├── workspace/         # 作業ディレクトリ
│   │   ├── temp/              # 一時ファイル
│   │   ├── output/            # 出力ファイル
│   │   └── session.state      # セッション状態
│   ├── dev2/
│   ├── dev3/
│   ├── qa1/
│   ├── qa2/
│   ├── ux/
│   └── pm/
├── shared/                    # 共有リソース
│   ├── project-files/         # 読み取り専用プロジェクトファイル
│   └── templates/             # 共有テンプレート
└── locks/                     # ファイルロック管理
    └── active-locks.json
```

## 分離レベル設計

### レベル1: 作業ディレクトリ分離
- 各ペイン専用の作業空間
- 相対パス解決の独立化
- 出力ファイルの衝突防止

### レベル2: 環境変数分離
- プロセス環境の完全独立
- 設定変更の他ペインへの非影響
- ロール別環境の自動設定

### レベル3: ファイルシステム分離
- 排他制御による競合回避
- 読み書き権限の適切な管理
- 共有リソースの安全なアクセス

### レベル4: セッション状態分離
- 会話履歴の完全独立
- 設定・状態の個別管理
- 復元時の独立性保証

## セキュリティ考慮事項
- ディレクトリ権限の適切な設定 (700)
- 機密ファイルの分離・保護
- プロセス間の情報漏洩防止
- ファイルロック の適切な解除

## テスト戦略
### 1. 分離機能テスト
- 作業ディレクトリ独立性テスト
- 環境変数分離テスト
- ファイルロック競合テスト

### 2. 競合シナリオテスト
- 同時ファイル書き込みテスト
- 環境変数競合テスト
- セッション状態競合テスト

### 3. 長期安定性テスト
- 24時間連続実行テスト
- メモリリーク・ファイルリーク検出
- クリーンアップ機能テスト

## 参照
- `docs/process-architecture.md` 2章 - SessionManager設計
- `src/WorkerProcessManager.fs` - プロセス管理基盤
- `src/ProcessSupervisor.fs` - プロセス監視機構

---
**担当**: @biwakonbu
**見積**: 2.5 人日
**優先度**: 🟧 高
**依存**: FC-009完了
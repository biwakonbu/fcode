# FC-011: セッション永続化

## 背景
全7ペイン(dev1-3, qa1-2, ux, pm)の同時実行が実現された現在、tmuxライクなセッション永続化機能が必要です。プロセス異常終了や意図的な一時停止からの復旧機能により、ユーザーの作業継続性を保証します。

## 目的
1. セッションのデタッチ/アタッチ機能を実装する (tmuxライク)
2. 会話履歴の自動保存・復元機能を確立する
3. 異常終了時の作業状態復元機能を提供する
4. 作業中断・再開のシームレスなユーザー体験を実現する

## 作業内容
- [ ] **SessionPersistenceManager実装**
  - セッション状態の永続化機構
  - JSON形式での状態保存・読み込み
  - セッションID管理とメタデータ保存
- [ ] **会話履歴永続化実装**
  - 各ペインの会話履歴自動保存 (5分間隔)
  - 履歴データの圧縮・アーカイブ機能
  - 復元時の整合性検証機構
- [ ] **デタッチ/アタッチ機能実装**
  - Ctrl+X D: セッションデタッチ (プロセス維持)
  - fcode --attach [session-id]: セッション復帰
  - 背景実行時の安全なプロセス管理
- [ ] **異常終了復旧機能実装**
  - クラッシュ時の自動状態保存
  - 起動時の復旧セッション検出・提案
  - 部分破損データからの最大限復旧

## 受け入れ基準
- [ ] Ctrl+X Dでデタッチ後、fcode --attachで同一状態に復帰できる
- [ ] 各ペインの会話履歴が完全に保存・復元される
- [ ] 異常終了後の起動時に復旧オプションが提示される
- [ ] セッション永続化が全7ペインで独立して動作する
- [ ] 永続化データのサイズが合理的範囲(1GB以下)に制限される

## 実装設計

### 1. SessionPersistenceManager モジュール
```fsharp
module SessionPersistenceManager

type SessionSnapshot = {
    SessionId: string
    PaneStates: Map<string, PaneState>
    CreatedAt: DateTime
    LastSavedAt: DateTime
    TotalSize: int64
}

type PaneState = {
    PaneId: string
    ConversationHistory: Message list
    WorkingDirectory: string
    Environment: Map<string, string>
    ProcessStatus: ProcessStatus
    LastActivity: DateTime
}

type PersistenceConfig = {
    AutoSaveIntervalMinutes: int  // 5分
    MaxHistoryLength: int         // 1000メッセージ
    MaxSessionAge: TimeSpan       // 7日間
    StorageDirectory: string      // ~/.local/share/fcode/sessions/
}
```

### 2. SessionStore モジュール
```fsharp
module SessionStore

type SessionStore = {
    SaveSession: SessionSnapshot -> Result<unit, string>
    LoadSession: string -> Result<SessionSnapshot, string>
    ListSessions: unit -> SessionMetadata list
    CleanupOldSessions: unit -> Result<int, string>
}

type SessionMetadata = {
    SessionId: string
    PaneCount: int
    LastActivity: DateTime
    SizeBytes: int64
    IsDetached: bool
}
```

### 3. DetachAttachManager モジュール
```fsharp
module DetachAttachManager

type DetachMode =
    | GracefulDetach    // プロセス維持
    | ForceDetach       // 強制終了
    | BackgroundMode    // 背景実行

type AttachResult =
    | AttachSuccess of SessionSnapshot
    | SessionNotFound of SessionId: string
    | AttachConflict of ActivePid: int
    | AttachError of Reason: string
```

## ディレクトリ構造設計
```
~/.local/share/fcode/persistence/
├── sessions/
│   ├── session-20251229-143052/
│   │   ├── metadata.json           # セッション基本情報
│   │   ├── pane-states/            # ペイン別状態
│   │   │   ├── dev1.json
│   │   │   ├── dev2.json
│   │   │   ├── dev3.json
│   │   │   ├── qa1.json
│   │   │   ├── qa2.json
│   │   │   ├── ux.json
│   │   │   └── pm.json
│   │   └── conversation-history/   # 圧縮済み会話履歴
│   │       ├── dev1.history.gz
│   │       ├── dev2.history.gz
│   │       └── ...
│   └── active-session.json         # 現在のアクティブセッション
├── recovery/                       # 異常終了時復旧データ
│   └── emergency-backup.json
└── config/
    └── persistence-config.json
```

## キーバインド設計
- **Ctrl+X D**: セッションデタッチ
- **Ctrl+X Ctrl+R**: セッション復旧メニュー表示
- **Ctrl+X S**: 手動セッション保存
- **Ctrl+X L**: セッション一覧表示

## CLI拡張設計
```bash
# 基本起動 (新規セッション)
fcode

# セッション復帰
fcode --attach session-20251229-143052

# セッション一覧表示
fcode --list-sessions

# 古いセッション削除
fcode --cleanup-sessions

# デーモンモード起動 (デタッチ状態)
fcode --daemon
```

## データ圧縮戦略
- **会話履歴**: gzip圧縮 (予想圧縮率: 70-80%)
- **状態データ**: JSON最適化 (不要フィールド除去)
- **自動アーカイブ**: 7日以上の古いセッション自動削除
- **サイズ制限**: セッション当たり最大500MB

## セキュリティ考慮事項
- セッションファイル権限: 600 (所有者のみ読み書き)
- 機密情報の暗号化: APIキー・トークン類
- 一時ファイルの適切な削除
- プロセス間通信の保護

## テスト戦略
### 1. 基本機能テスト
- セッション保存・復元の正確性テスト
- デタッチ・アタッチの動作テスト
- 異常終了・復旧シナリオテスト

### 2. 負荷・安定性テスト
- 長時間セッション(24時間)の永続化テスト
- 大量会話履歴(10,000メッセージ)の処理テスト
- 複数セッション同時管理テスト

### 3. 統合テスト
- 既存機能との競合確認
- ProcessSupervisor連携テスト
- リソース管理機能との統合テスト

## パフォーマンス最適化
- **差分保存**: 前回から変更があったペインのみ保存
- **遅延書き込み**: バッファリングによる書き込み最適化
- **並列処理**: ペイン状態の並列保存・復元
- **インデックス化**: セッション検索の高速化

## 将来拡張機能
- **クラウド同期**: セッションのクラウドバックアップ
- **チーム共有**: セッションの他メンバー共有機能
- **テンプレート**: よく使うセッション設定のテンプレート化

## 参照
- `docs/process-architecture.md` - プロセス分離設計
- `src/ClaudeCodeProcess.fs` - 既存セッション管理
- `src/ProcessSupervisor.fs` - プロセス監視機構
- tmux session management design patterns

---
**担当**: @biwakonbu  
**見積**: 4 人日  
**優先度**: 🟨 中  
**依存**: FC-007, FC-008完了 ✅  
**開始**: 2025-06-30  
**GitHub Issue**: 準備中  
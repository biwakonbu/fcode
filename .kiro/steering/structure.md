# プロジェクト構造・フォルダ構成

## プロジェクト全体構造

```
fcode/
├── src/                     # メインアプリケーション (F#ソースコード)
├── tests/                   # テストスイート (82テストケース・3,500行超)
├── docs/                    # プロジェクト文書・設計資料
├── .kiro/                   # Kiroステアリング・設定
├── .github/                 # GitHub Actions CI/CD設定
├── .githooks/               # Git pre-commitフック
├── publish/                 # リリースビルド成果物
├── bin/                     # ビルド出力 (Debug)
├── obj/                     # ビルド中間ファイル
├── TestResults/             # テスト実行結果
├── Makefile                 # ビルドシステム統一インターフェース
├── README.md                # プロジェクト概要・使用方法
├── PRD.md                   # プロダクト要件定義
└── TODO.md                  # 開発進捗・実装状況
```

## src/ - メインアプリケーション

### アーキテクチャ層別構成

#### UI・プレゼンテーション層
```
src/
├── Program.fs               # エントリーポイント・UIレイアウト
├── KeyBindings.fs           # Emacsキーバインドシステム
├── UIHelpers.fs             # UI共通ヘルパー関数
├── UIAbstractions.fs        # UI抽象化レイヤー
├── ColorSchemes.fs          # カラーテーマ管理
├── UnifiedActivityView.fs   # 統合アクティビティ表示
├── DecisionTimelineView.fs  # 意思決定タイムライン
├── ProgressDashboard.fs     # 進捗ダッシュボード
├── EscalationNotificationUI.fs # エスカレーション通知UI
└── RealtimeUIIntegration.fs # リアルタイムUI統合
```

#### ビジネスロジック・協調制御層
```
src/
├── TaskAssignmentManager.fs # タスク自動配分システム
├── QualityGateManager.fs    # 品質ゲート制御
├── EscalationUIHandler.fs   # エスカレーション処理
├── FullWorkflowCoordinator.fs # 全体ワークフロー制御
├── VirtualTimeCoordinator.fs # 仮想時間管理
├── AgentMessaging.fs        # エージェント間メッセージング
├── MultiAgentProcessManager.fs # マルチエージェント管理
└── WorkflowOrchestrator.fs  # ワークフロー統制
```

#### Collaboration/ - 協調作業基盤
```
src/Collaboration/
├── CollaborationTypes.fs    # 協調作業型定義
├── RealtimeCollaborationFacade.fs # 統合ファサード (400行)
├── AgentStateManager.fs     # エージェント状態管理
├── TaskDependencyGraph.fs   # タスク依存関係グラフ
├── ProgressAggregator.fs    # 進捗集約・監視
├── CollaborationCoordinator.fs # 協調制御・競合解決
├── EscalationManager.fs     # エスカレーション管理
├── TaskStorageManager.fs    # SQLite3タスク永続化
├── TaskStorageFactory.fs    # ストレージファクトリ
└── [その他協調機能モジュール]
```

#### インフラ・統合層
```
src/
├── ClaudeCodeProcess.fs     # Claude Code CLI統合
├── ClaudeCodeIntegration.fs # Claude Code統合制御
├── ClaudeCodeIOIntegration.fs # I/O統合管理
├── SessionBridge.fs         # セッション橋渡し
├── SessionStateManager.fs   # セッション状態管理
├── PtyNetManager.fs         # PTY統合管理
├── ResourceMonitor.fs       # リソース監視
├── ResourceController.fs    # リソース制御
└── ConfigurationManager.fs  # 設定管理
```

#### セキュリティ・品質保証層
```
src/
├── SecurityUtils.fs         # セキュリティユーティリティ
├── UISecurityManager.fs     # UI セキュリティ管理
├── InputValidation.fs       # 入力検証
├── JsonSanitizer.fs         # JSON サニタイゼーション
├── Logger.fs                # ログシステム
├── FCodeError.fs            # エラー型定義
└── SimpleMemoryMonitor.fs   # メモリ監視
```

## tests/ - テストスイート

### テスト分類・構成
```
tests/
├── Program.fs               # テストエントリーポイント
├── TestHelpers.fs           # テスト共通ヘルパー
├── MockUI.fs                # UIモック・テスト基盤
├── CITestHelper.fs          # CI環境テスト支援
│
├── *Tests.fs                # ユニットテスト (各モジュール対応)
├── *IntegrationTests.fs     # 統合テスト
├── *EndToEndTests.fs        # E2Eテスト (エージェント別)
├── FC*Tests.fs              # フィーチャー別テスト
└── *PerformanceTests.fs     # パフォーマンステスト
```

### 主要テストカテゴリ
- **ユニットテスト**: 各モジュールの単体機能検証
- **統合テスト**: コンポーネント間連携検証
- **E2Eテスト**: エージェント別エンドツーエンド検証
- **パフォーマンステスト**: 性能・負荷検証
- **セキュリティテスト**: セキュリティ機能検証
- **CI互換性テスト**: CI環境での安定性検証

## docs/ - プロジェクト文書

### 設計・アーキテクチャ文書
```
docs/
├── GRAND_DESIGN.md          # 全体設計・技術アーキテクチャ
├── COLLABORATION_ARCHITECTURE.md # 協調作業アーキテクチャ
├── TASK_STORAGE_DESIGN.md   # タスクストレージ設計 (SQLite3)
├── STORAGE_INTEGRATION_SUMMARY.md # ストレージ統合概要
├── 3TABLE_DESIGN_SUMMARY.md # 3テーブル設計概要
└── process-architecture.md  # プロセスアーキテクチャ
```

### 利用者向け文書
```
docs/
├── USER_MANUAL.md           # 利用者向けマニュアル
├── VALUE_PROPOSITION.md     # 価値提案・ユースケース
└── ui_layout.md             # UI レイアウト設計
```

### 技術知識・ナレッジ
```
docs/knowledge/
├── code-quality-recovery-process.md # コード品質回復プロセス
├── error-handling-unification.md    # エラーハンドリング統一
└── fsharp-refactoring-pitfalls.md   # F#リファクタリング注意点
```

## 設定・メタファイル

### プロジェクト設定
```
├── src/fcode.fsproj         # メインプロジェクトファイル
├── tests/fcode.Tests.fsproj # テストプロジェクトファイル
├── .editorconfig            # エディタ設定統一
├── .fsharplint.json         # FSharpLint設定
└── .fantomasrc              # Fantomas フォーマット設定
```

### Git・CI/CD設定
```
├── .gitignore               # Git除外設定
├── .githooks/               # pre-commit フック
├── .github/workflows/       # GitHub Actions CI/CD
└── .kiro/                   # Kiro ステアリング設定
```

## 命名規則・パターン

### ファイル命名
- **モジュール**: PascalCase (例: `TaskAssignmentManager.fs`)
- **テスト**: モジュール名 + `Tests.fs` (例: `TaskAssignmentManagerTests.fs`)
- **統合テスト**: モジュール名 + `IntegrationTests.fs`
- **E2Eテスト**: 機能名 + `EndToEndTests.fs`

### フォルダ構成原則
- **機能別分離**: 関連機能をフォルダでグループ化 (`Collaboration/`)
- **層別分離**: アーキテクチャ層に応じた配置
- **テスト対応**: `src/` 構造に対応した `tests/` 構造
- **文書整理**: 用途別・対象者別の `docs/` 分類

### 依存関係原則
- **上位層→下位層**: UI層 → ビジネス層 → インフラ層
- **抽象化依存**: 具象ではなく抽象に依存
- **循環依存回避**: モジュール間の循環参照を避ける
- **テスト独立性**: テストは本体コードに依存、逆は禁止

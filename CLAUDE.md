# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

Claude Code統合TUIエディタの開発プロジェクト。F#/.NET8 + Terminal.Guiを使用してクロスプラットフォームなTUIアプリケーションを構築している。

## 技術スタック

- **言語**: F# (.NET 8)
- **UIフレームワーク**: Terminal.Gui 1.5.0
- **対象プラットフォーム**: Linux, macOS (Windows対象外)
- **配布**: .NET Single File Publish (将来的にパッケージマネージャ対応予定)

## プロジェクト構造

```
src/                         # メインアプリケーション
├── Program.fs               # エントリーポイント・UIレイアウト定義 (225行)
├── ColorSchemes.fs          # ロール別カラースキーム定義 (27行)
├── KeyBindings.fs           # Emacsキーバインドシステム (246行)
├── ClaudeCodeProcess.fs     # Claude Codeプロセス管理・セッション制御 (190行)
├── ProcessSupervisor.fs     # プロセス分離・監視・自動復旧システム (422行)
├── Logger.fs                # 包括的ログシステム (71行)
├── RealtimeCollaboration.fs # リアルタイム協調機能統合ファサード (217行)
├── Collaboration/           # リアルタイム協調機能アーキテクチャ（2,526行）
│   ├── CollaborationTypes.fs        # 型定義・エラー型 (163行)
│   ├── IAgentStateManager.fs        # エージェント状態管理インターフェース (46行)
│   ├── ITaskDependencyGraph.fs      # タスク依存関係管理インターフェース (58行)
│   ├── IProgressAggregator.fs       # 進捗監視インターフェース (75行)
│   ├── ICollaborationCoordinator.fs # 協調制御インターフェース (63行)
│   ├── AgentStateManager.fs         # エージェント状態管理実装 (268行)
│   ├── TaskDependencyGraph.fs       # タスク依存関係管理実装 (549行)
│   ├── ProgressAggregator.fs        # 進捗監視・分析実装 (408行)
│   ├── CollaborationCoordinator.fs  # 競合制御・デッドロック検出実装 (496行)
│   └── RealtimeCollaborationFacade.fs # 統合ファサード・イベント統合 (400行)
├── fcode.fsproj             # F#プロジェクトファイル
├── bin/                     # ビルド出力
└── obj/                     # ビルド中間ファイル

tests/                       # 包括的テストスイート（151テスト）
├── KeyBindingsTests.fs      # キーバインドシステムテスト
├── ColorSchemesTests.fs     # カラースキーム機能テスト
├── WorkerProcessManagerDynamicWaitTests.fs  # 動的待機機能テスト
├── IPCChannelTests.fs       # 背圧制御・並行性テスト
├── UIHelpersTests.fs        # UI階層・境界条件テスト
├── EndToEndIntegrationTests.fs  # 統合・E2Eテスト
├── ResourceManagementTests.fs    # リソース管理・リーク検出テスト
├── RealtimeCollaborationTests.fs # リアルタイム協調機能包括的テスト (625行)
├── fcode.Tests.fsproj       # テストプロジェクトファイル
└── Program.fs               # テストエントリーポイント

docs/                        # プロジェクト文書・アーキテクチャ設計
├── TODO.md                  # 詳細開発タスク管理
├── PRD.md                   # プロダクト要件定義
├── ui_layout.md             # UIレイアウト設計詳細
├── COLLABORATION_ARCHITECTURE.md # リアルタイム協調アーキテクチャ設計 (383行)
└── TASK_STORAGE_DESIGN.md   # SQLite3タスクストレージ設計 (783行)
```

## 開発環境セットアップ

### Git フック自動設定済み
**Pre-commit** (コミット時):
- Fantomas: F#コードフォーマット自動チェック・修正
- ビルドチェック: コンパイルエラー検出

**Pre-push** (プッシュ時):
- Unitテスト実行: CI環境と同等のテストチェック
- プッシュ前の品質保証

手動実行：`./scripts/format-and-lint.sh`

```bash
# ビルド
dotnet build src/fcode.fsproj

# 実行
dotnet run --project src/fcode.fsproj

# テスト実行
dotnet test tests/fcode.Tests.fsproj

# Unitテストのみ実行（CI環境と同様）
dotnet test tests/fcode.Tests.fsproj --filter "TestCategory=Unit"

# パフォーマンステスト実行（手動実行用）
dotnet test tests/fcode.Tests.fsproj --filter "TestCategory=Performance"

# 統合テスト実行
dotnet test tests/fcode.Tests.fsproj --filter "TestCategory=Integration"

# カバレッジレポート付きテスト
dotnet test tests/fcode.Tests.fsproj --collect:"XPlat Code Coverage"

# コード品質チェック（フォーマット＋リント）
./scripts/format-and-lint.sh

# 単一ファイルパブリッシュ
dotnet publish src/fcode.fsproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

## アーキテクチャ設計

### UIレイアウト構成
- **会話ペイン**: 左端固定（60列幅）、フラット表示でリアルタイム会話一覧
- **マルチペイン**: 右側エリアに開発者・QA・UX・PM用ペインを配置
- **レイアウト比率**: 上段40%(dev1-3) / 中段40%(qa1-2,ux) / 下段20%(PMタイムライン)

### 実装済み機能（100%完了）
- **UI基盤完全実装**: 8ペインレイアウト（会話、dev1-3、qa1-2、ux、PMタイムライン）
- **UI改善完了**: 会話ペイン60列幅、二重枠解消、統一カラースキーム
- **Emacsキーバインドシステム**: マルチキーシーケンス完全対応（246行）
- **レスポンシブレイアウト**: 上段40%/中段40%/下段20%比率
- **インタラクティブヘルプシステム**: Ctrl+X Hでの操作ガイド
- **包括的ログシステム**: 4段階ログレベル、カテゴリ別出力（71行）
- **プロセス分離アーキテクチャ基盤**: ProcessSupervisor完全実装（422行）
- **UIHelpers根本修正**: Terminal.Gui 1.15.0対応、リフレクション安全化（2025-06-29）
- **包括的テストスイート**: 151テストケース、5カテゴリ（Unit/Integration/Performance/Stability）
- **リアルタイム協調機能基盤**: 完全実装（2,526行、包括的アーキテクチャ）
- **SQLite3タスクストレージ**: TaskStorageManager実装完了（477行）

### 開発中機能（80%完了）
- **Claude Code CLI統合**: プロセス起動基盤完成、TextView初期化に課題
- **SessionManager**: 複数セッション管理基盤実装済み（190行）
- **I/O統合**: 標準入出力キャプチャ・UI表示機能（実装中）

### 緊急課題（画面表示最優先） - 根本修正完了（2025-06-29）
- **TextView初期化タイミング問題**: Terminal.Gui 1.15.0対応・リフレクション安全化で解決
- **Claude Code出力表示**: リアルタイムI/O統合とTUI表示機能（次段階）
- **基本動作確認**: dev1ペインでの完全なClaude対話実現（次段階）

### 後回し機能（動作確認完了後）
- tmuxライクなセッション永続化機能
- 健全性監視・自動復旧システム
- ペイン間コンテキスト共有機能
- マルチエージェント協調ワークフロー

## 重要な実装方針

- Terminal.GuiのFrameViewを活用したペイン分割
- 会話ペインはボーダーレス（フラット表示）
- Claude Code CLIとの外部プロセス連携が前提
- 認証・API管理はClaude Code CLI側に委任

## 外部依存

- Claude Code CLI（必須）: ローカルインストール済み前提
- 設定ファイル: `~/.config/claude-tui/config.toml`（予定）

## 現在のプロジェクト状態（2025-07-02）

### 最新の実装状況（2025-07-02 更新）
- **総実装ライン数**: 3,706行 (src/), 2,402行 (tests/)
- **テストカバレッジ**: 240/240テスト 100%パス（SQLite3統合テスト含む）
- **アーキテクチャ基盤**: UI、キーバインド、ログ、プロセス分離、リアルタイム協調すべて完成
- **根本修正完了**: TextView初期化問題解決（Terminal.Gui 1.15.0対応）
- **SQLite3 TaskStorageManager**: Repository Pattern完全実装・統合テスト成功（2025-07-02）
- **品質改善完了**: 0エラー・0警告でのビルド成功、型安全性向上・統合テスト安定化

### 開発フェーズ再編成: 動作確認最優先
**新しい開発方針**: セッション維持・堅牢性は後回し、まず画面表示を実現

1. **Phase 1 (最優先)**: Claude Code画面表示の実現
   - TextView初期化タイミング問題解決
   - I/O統合実装（標準入出力キャプチャ・TUI表示）
   - dev1ペインでの基本動作確認

2. **Phase 2 (安定化)**: 複数ペイン展開
   - 全ペイン対応とセッション管理安定化

3. **Phase 3 (高度機能)**: ペイン間連携・AI協調機能

### 技術的準備状況
- ✅ UI基盤（Terminal.Gui 1.15.0）完全実装・安定動作
- ✅ F#/.NET8プロセス管理基盤（SessionManager実装済み）
- ✅ プロセス分離アーキテクチャ基盤（ProcessSupervisor 422行）
- ✅ 包括的テストインフラ・ログシステム完成
- ✅ TextView初期化問題根本解決（2025-06-29）
- ✅ 包括的テスト強化（82テスト、実装上の不安要素すべて対応）

### 直近の完了作業（2025-06-29）
- **TextView初期化根本修正** - Terminal.Gui 1.15.0対応・リフレクション安全化 - commit 815e963
- **包括的テスト強化** - 39テスト追加（WorkerProcessManager/IPCChannel/UIHelpers/E2E/リソース管理） - commit a6ff40e
- **CI/CDパイプライン修正** - テストカテゴリ分離・タイムアウト対策 - commit 4cd9317


## 開発時注意事項

- **重複回避**: github issue や PR を立てる時は重複が無いか確認してから対応して
- **PR管理**: PR は issue の実装を対応した場合関連付けておいて

## コーディング規約

### F# スタイルガイド

#### `new` キーワードの使用方針
プロジェクトでは F# Compiler の推奨に従い、IDisposableオブジェクトのリソース管理を明示的に表現する方針を採用：

**IDisposableオブジェクト** - `new` キーワード必須:
```fsharp
let frameView = new FrameView("test")
let textView = new TextView()
let supervisor = new ProcessSupervisor(config)
```

**通常のオブジェクト** - `new` キーワード省略:
```fsharp
let handler = EmacsKeyHandler(panes, sessionManager)
let manager = SessionManager()
```

**理由**: IDisposableオブジェクトではリソース所有権を明確にし、メモリリークを防止するため

#### FSharpLint設定

プロジェクトルートの`.fsharplint.json`で標準的な品質ルールを設定:

- **redundantNewKeyword**: 無効化（IDisposableオブジェクトのnew保持）
- **functionLength**: 関数長制限（最大80行）
- **cyclomaticComplexity**: 複雑度制限（最大15）
- **unusedValue**: 未使用変数検出
- **unusedOpenStatement**: 未使用openステートメント検出
- **命名規約**: 全ルール有効（capitals、parameters等）
- **suggestions**: コード改善提案
- **その他**: 標準品質ルール全般

#### テスト環境設定

CI環境ではTerminal.Gui初期化をスキップ:
```fsharp
let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))
if not isCI then Application.Init()
```

### テスト実行方針

#### テストカテゴリ分類
- **Unit**: 基本的な単体テスト（CI環境で常時実行）
- **Integration**: 統合テスト（手動実行、E2Eテスト含む）
- **Performance**: パフォーマンステスト（手動実行、開発時のみ）
- **Stability**: 長時間稼働・安定性テスト（手動実行）

#### CI環境での実行
CIでは安定性重視でUnitテストのみ実行：
```bash
dotnet test --filter "TestCategory=Unit" --blame-hang-timeout 60000ms
```

#### 手動実行時の使い分け
```bash
# 全テスト実行（開発時）
dotnet test tests/fcode.Tests.fsproj

# パフォーマンス検証時
dotnet test --filter "TestCategory=Performance"

# 統合テスト実行時
dotnet test --filter "TestCategory=Integration"
```

### CI/CD テストアーキテクチャ

#### Terminal.Gui CI環境対応パターン

**背景**: Terminal.GuiはCI環境（ヘッドレス環境）で初期化時にハングする問題があり、業界標準的な対応として**UI依存性分離アーキテクチャ**を採用。

**実装パターン**:

```fsharp
// 1. テスト可能インターフェース定義
type ITestableView =
    abstract member SetColorScheme: ColorScheme -> unit

// 2. CI環境自動判定
let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

// 3. 環境別オブジェクト生成
let createTestableFrameView (title: string) =
    if isCI then
        // CI環境: 軽量モックオブジェクト
        new TestFrameView(title) :> ITestableView
    else
        // 開発環境: 実際のTerminal.Gui
        let frameView = new FrameView(title)
        frameView :> ITestableView

// 4. UI非依存ロジック分離
let getSchemeByRole (role: string) =
    match role with
    | "dev" -> ColorScheme.dev
    | "qa" -> ColorScheme.qa
    | _ -> ColorScheme.Default

let applySchemeByRoleTestable (view: ITestableView) (role: string) =
    let scheme = getSchemeByRole role
    view.SetColorScheme(scheme)
```

**テスト実行**:

```bash
# CI環境（自動判定）
CI=true dotnet test tests/fcode.Tests.fsproj
# 結果: Passed: 11, Failed: 0, Duration: 11ms

# 開発環境
dotnet test tests/fcode.Tests.fsproj  
# 結果: 実際のTerminal.Gui使用
```

**利点**:
- ✅ **高速**: CI環境でTUI初期化回避（11ms実行）
- ✅ **安定**: UI依存性分離でテスト信頼性向上
- ✅ **実用的**: ビジネスロジックの実質的検証
- ✅ **保守性**: ncurses系OSSコミュニティ標準パターン

**他OSSプロジェクトでの採用例**:
- **ncursesアプリケーション**: ロジック/UI分離が推奨パターン
- **Rich (Python)**: `TTY_COMPATIBLE=1`環境変数での強制対応
- **Hecate**: 仮想ターミナルエミュレータによる統合テスト

#### 関連設定ファイル
- `.fsharplint.json`: FSharpLint標準形式での品質ルール設定
- CI/CDパイプライン: F# Compiler + FSharpLint品質チェック
- `.github/workflows/ci.yml`: Linux/macOS自動テスト実行
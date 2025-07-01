# fcode グランドデザイン

**目的**: Claude Code統合TUIエディタの全体設計と実装方針の確定

## 1. システム全体像

### 1.1 アーキテクチャ概要

```
┌─────────────────────────────────────────────────────────────┐
│                    fcode メインプロセス                      │
│                 (F# + Terminal.Gui 1.15.0)                │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ │
│ │UI管理       │ │キーバインド │ │セッション管理│ │設定管理     │ │
│ │・レイアウト │ │・Emacs風    │ │・永続化     │ │・JSON       │ │
│ │・9ペイン    │ │・マルチキー │ │・復旧       │ │・環境変数   │ │
│ └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘ │
├─────────────────────────────────────────────────────────────┤
│                 プロセス監視・管理層                        │
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ │
│ │ProcessSupervisor│ │HealthCheck │ │AutoRecovery │ │IPC Manager  │ │
│ │・プロセス起動   │ │・ハートビート│ │・自動再起動  │ │・Named Pipes│ │
│ │・状態監視      │ │・応答性監視  │ │・状態復元   │ │・メッセージ │ │
│ └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘ │
└─────────────────┬───────────────────────────────────────────┘
                  │ プロセス間通信 (IPC)
    ┌─────────────┼─────────────┬─────────────┬─────────────┐
    │             │             │             │             │
┌───▼───┐     ┌───▼───┐     ┌───▼───┐     ┌───▼───┐     ┌───▼───┐
│Claude │     │Claude │     │Claude │     │Claude │     │Claude │
│Worker1│     │Worker2│     │Worker3│     │Worker4│     │Worker5│
│(conv) │     │(dev1) │     │(dev2) │     │(dev3) │     │(qa1)  │
└───────┘     └───────┘     └───────┘     └───────┘     └───────┘
┌───────┐     ┌───────┐     ┌───────┐     ┌───────┐
│Claude │     │Claude │     │Claude │     │Claude │
│Worker6│     │Worker7│     │Worker8│     │Worker9│
│(qa2)  │     │(ux)   │     │(pm)   │     │(pdm)  │
└───────┘     └───────┘     └───────┘     └───────┘
```

### 1.2 技術スタック確定

| 層 | 技術 | 理由 |
|---|---|---|
| **UI層** | Terminal.Gui 1.15.0 | クロスプラットフォーム、安定性、豊富な機能 |
| **言語** | F# (.NET 8) | 関数型、型安全性、非同期処理、パターンマッチ |
| **プロセス管理** | System.Diagnostics.Process | .NET標準、信頼性 |
| **IPC** | Named Pipes (Windows) / Unix Domain Sockets (Linux/macOS) | 高性能、信頼性 |
| **設定管理** | System.Text.Json | .NET標準、パフォーマンス |
| **ログ** | 自作軽量ログシステム | 依存関係最小化 |

## 2. UIレイアウト設計

### 2.1 画面分割戦略

```
┌──────┬───────────┬───────────┬───────────┐ 160x40推奨
│ 会話 │  dev1     │   dev2    │  dev3     │ ↑
│ 60列 │  シニア   │  エンジニア │  エンジニア │ │ 16行
│ 固定 │  批判的   │  並列作業  │  並列作業  │ │ (40%)
│      ├───────────┼───────────┼───────────┤ ↓
│      │  qa1      │   qa2     │  ux       │ ↑
│      │  テスト   │  ヒューリ  │  ユーザー  │ │ 16行
│      │  リード   │  スティック │  中心設計  │ │ (40%)
│      ├───────────┴───────────┴───────────┤ ↓
│      │         PM / PdM 統合管理          │ ↑ 8行
│      │    進捗管理 / 要件・市場分析        │ │ (20%)
└──────┴───────────────────────────────────┘ ↓
```

### 2.2 レスポンシブ対応

| 画面サイズ | レイアウト | 備考 |
|---|---|---|
| **160x40以上** | 標準9ペイン | 最適表示 |
| **120x30-159x39** | 縮小版（PM/PdM統合） | 実用範囲 |
| **120x30未満** | 警告表示 | 使用不推奨 |

### 2.3 カラースキーム

```fsharp
// Terminal.Gui ColorScheme定義
let colorSchemes = 
    [|
        ("conversation", Color.Blue, Color.White)      // 会話: 青地/白文字
        ("dev", Color.DarkGreen, Color.Gray)           // 開発: 緑系
        ("qa", Color.DarkYellow, Color.Black)          // QA: 黄系  
        ("ux", Color.DarkCyan, Color.White)            // UX: シアン系
        ("pm", Color.DarkMagenta, Color.White)         // 管理: マゼンタ系
    |]
```

## 3. プロセス分離アーキテクチャ

### 3.1 設計原則

1. **完全分離**: メインTUIプロセスとClaude Codeインスタンスの独立性
2. **フォルトトレラント**: 個別プロセス異常が全体に波及しない
3. **自動復旧**: プロセス監視と自動再起動
4. **セッション永続性**: tmuxライクなデタッチ/アタッチ機能

### 3.2 プロセス管理戦略

```fsharp
// ProcessSupervisor.fs - 核心実装
type WorkerProcess = {
    PaneId: string
    Role: string  
    Process: Process option
    LastHeartbeat: DateTime
    Status: ProcessStatus
    RestartCount: int
}

type ProcessStatus = 
    | Starting
    | Running  
    | Unhealthy
    | Crashed
    | Stopped

// 監視間隔
let HealthCheckInterval = TimeSpan.FromSeconds(2.0)
let MaxRestartAttempts = 3
let RestartCooldown = TimeSpan.FromSeconds(5.0)
```

### 3.3 IPC通信設計

```fsharp
// メッセージ型定義
type IPCMessage = 
    | StartClaude of PaneId: string
    | StopClaude of PaneId: string  
    | SendPrompt of PaneId: string * Prompt: string
    | ReceiveResponse of PaneId: string * Response: string
    | Heartbeat of PaneId: string
    | StatusUpdate of PaneId: string * Status: ProcessStatus

// 通信チャネル
type IPCChannel = {
    PipeServer: NamedPipeServerStream
    MessageQueue: ConcurrentQueue<IPCMessage>
    IsConnected: bool
}
```

## 4. Claude Code統合戦略

### 4.1 統合方針の重要な制約

**重要**: Claude Codeは独自のモデル設定・認証システムを持つため、外部からの直接制御は限定的。以下の方針で統合する：

1. **プロセス起動**: `claude code` コマンドの外部プロセスとして起動
2. **入出力キャプチャ**: 標準入出力をパイプで取得
3. **システムプロンプト**: 会話開始時に初期メッセージとして送信
4. **設定委譲**: 認証・API設定はClaude Code側で管理

### 4.2 実装方法

```fsharp
// ClaudeCodeProcess.fs
type ClaudeCodeInstance = {
    PaneId: string
    Process: Process
    StdinWriter: StreamWriter
    StdoutReader: StreamReader
    SystemPrompt: string option
    IsInitialized: bool
}

let startClaudeCode (paneConfig: PaneConfig) =
    let startInfo = ProcessStartInfo()
    startInfo.FileName <- "claude"
    startInfo.Arguments <- "code"
    startInfo.UseShellExecute <- false
    startInfo.RedirectStandardInput <- true
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    
    let process = Process.Start(startInfo)
    
    // システムプロンプトの初期送信
    match paneConfig.SystemPrompt with
    | Some prompt -> 
        process.StandardInput.WriteLine(prompt)
        process.StandardInput.Flush()
    | None -> ()
    
    { PaneId = paneConfig.PaneId
      Process = process
      StdinWriter = process.StandardInput
      StdoutReader = process.StandardOutput
      SystemPrompt = paneConfig.SystemPrompt
      IsInitialized = true }
```

### 4.3 システムプロンプト配信戦略

Claude Codeへの制約を考慮し、以下の段階的アプローチを採用：

1. **Phase 1**: 会話開始時の初期メッセージとして送信
2. **Phase 2**: 定期的なリマインダーメッセージ
3. **Future**: Claude Codeが外部プロンプト設定をサポートした場合の対応準備

## 5. データフロー設計

### 5.1 メッセージフロー

```
User Input → TUI → KeyBinding → Action → ProcessSupervisor → Claude Worker
    ↓
TUI ← Display ← MessageQueue ← IPC ← Output Processing ← Claude Worker
```

### 5.2 セッション永続化

```fsharp
// セッションデータ構造
type SessionData = {
    SessionId: string
    CreatedAt: DateTime
    UpdatedAt: DateTime
    PaneStates: Map<string, PaneState>
    ConversationHistory: ConversationMessage[]
}

type PaneState = {
    PaneId: string
    Role: string
    IsActive: bool
    LastMessage: string option
    MessageHistory: string[]
    ScrollPosition: int
}

// 保存場所: ~/.config/claude-tui/sessions/
```

## 6. パフォーマンス・スケーラビリティ

### 6.1 リソース使用量目標

| 項目 | 目標値 | 監視方法 |
|---|---|---|
| **メインプロセスメモリ** | < 100MB | Process.WorkingSet64 |
| **Claude Worker合計** | < 2GB | プロセス監視 |
| **UI応答性** | < 100ms | フレームレート監視 |
| **IPC遅延** | < 10ms | メッセージタイムスタンプ |

### 6.2 最適化戦略

1. **遅延初期化**: 使用されるペインのみClaudeプロセス起動
2. **メモリプール**: メッセージオブジェクトの再利用
3. **非同期処理**: UI更新とIPC通信の分離
4. **ガベージコレクション**: 定期的なメモリクリーンアップ

## 7. エラーハンドリング・信頼性

### 7.1 障害分離設計

```fsharp
// 障害レベル定義
type FailureLevel = 
    | PaneLevel      // 単一ペインの障害
    | WorkerLevel    // Claude Workerプロセスの障害  
    | IPCLevel       // 通信障害
    | SystemLevel    // システム全体の障害

// 復旧戦略
let recoveryStrategy = function
    | PaneLevel -> RestartPane
    | WorkerLevel -> RestartWorkerWithBackoff  
    | IPCLevel -> ReinitializeIPC
    | SystemLevel -> GracefulShutdown
```

### 7.2 データ保護

1. **自動保存**: 30秒間隔でセッション状態保存
2. **バックアップ**: 過去5セッションの履歴保持
3. **整合性チェック**: 起動時の設定ファイル検証
4. **安全な終了**: Ctrl+Cでのグレースフル終了

## 8. 開発・テスト戦略

### 8.1 テスト分類

| テテストタイプ | 内容 | 実行環境 |
|---|---|---|
| **Unit** | 純粋関数・ロジック | CI + Local |
| **Integration** | プロセス間通信・Claude統合 | Local |
| **E2E** | UI操作・ユーザーシナリオ | Manual |
| **Performance** | 負荷・メモリリーク | Manual |
| **Stability** | 長時間稼働 | Manual |

### 8.2 品質ゲート

```yaml
# CI/CD品質基準
criteria:
  unit_test_coverage: "> 80%"
  build_success: "required"
  code_format: "Fantomas適用"
  linting: "FSharpLint基準準拠"
  
# リリース基準  
release_criteria:
  e2e_test_pass: "required"
  performance_benchmark: "メモリ使用量 < 2GB"
  stability_test: "24時間連続稼働"
```

## 9. 段階的実装計画

### Phase 1: 基盤構築 (完了)
- ✅ UI基盤（Terminal.Gui）
- ✅ 9ペインレイアウト
- ✅ Emacsキーバインド
- ✅ 設定管理システム
- ✅ プロセス監視基盤

### Phase 2: Claude統合 (次段階)
- 🔄 Claude Codeプロセス起動・停止
- 🔄 標準入出力キャプチャ
- 🔄 基本的な会話機能
- 🔄 システムプロンプト配信

### Phase 3: 協調機能
- ⏳ ペイン間メッセージ共有
- ⏳ セッション永続化
- ⏳ 自動復旧システム

### Phase 4: 高度機能
- ⏳ AIチーム協調ワークフロー
- ⏳ 高度なエラーハンドリング
- ⏳ パフォーマンス最適化

## 10. 技術リスク・制約

### 10.1 Claude Code依存リスク

| リスク | 影響度 | 軽減策 |
|---|---|---|
| **API仕様変更** | 高 | プロセス分離で影響局所化 |
| **認証方式変更** | 中 | Claude Code側の設定に委譲 |
| **パフォーマンス劣化** | 中 | 監視・自動再起動で対応 |
| **ライセンス変更** | 低 | 代替統合方法の調査 |

### 10.2 技術制約

1. **Terminal.Gui制約**: マウス操作サポート限定
2. **プラットフォーム制約**: Windows非対応
3. **メモリ制約**: 複数Claudeプロセスによる使用量増加
4. **ネットワーク制約**: Claude API依存

## 11. 成功指標・KPI

### 11.1 技術指標

- **安定性**: MTBF > 8時間
- **パフォーマンス**: UI応答時間 < 100ms
- **リソース効率**: システム全体メモリ使用量 < 2GB
- **可用性**: 自動復旧率 > 95%

### 11.2 ユーザビリティ指標

- **学習コストの低さ**: 基本操作習得 < 30分
- **作業効率向上**: 従来比20%以上の生産性向上
- **エラー率の低さ**: 操作ミス < 5%

---

## 結論

本グランドデザインは、Claude Code統合TUIエディタの実現可能性を重視し、段階的な実装を前提とした現実的な設計です。特にClaude Codeの制約を受け入れつつ、プロセス分離アーキテクチャにより信頼性と拡張性を確保しています。

次のステップとして、Phase 2のClaude統合機能の詳細設計と実装に着手します。
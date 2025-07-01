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

### 2.1 PO中心設計の画面分割戦略

**設計原則**: Product Ownerの使用体験を最優先した情報階層設計

```
┌─────────┬───────────────────────────────┐ 160x40推奨
│         │              メイン作業エリア           │
│  会話   │ ┌───────────────────────────────────────┐ │ ↑
│  60列   │ │        統合進捗ダッシュボード         │ │ │ 12行
│  固定   │ │  ⏱️ 18vh/72vh │ 🎯 機能A実装 │ ⚠️ 0件 │ │ │ (30%)
│         │ └───────────────────────────────────────┘ │ ↓
│  ・指示 ├─────────────────────────────────────────┤ ↑
│  ・判断 │ dev1 │ dev2 │ dev3 │ qa1  │ qa2  │ ux   │ │ │ 20行
│  ・承認 │シニア│並列A │並列B │テスト│探索的│ユーザ│ │ │ (50%)
│         │ 🟢   │ 🟡   │ 🟢   │ 🟡   │ 🟢   │ 🟢   │ │ ↓
│  POの   ├─────────────────────────────────────────┤ ↑
│  メイン │         PM統合管理・重要判断エリア        │ │ │ 8行
│  操作   │ 📊 ベロシティ │ ⚠️ 重要判断待ち │ 📋 Next │ │ │ (20%)
│  領域   │    track4:85% │   致命度Lv3   │ Sprint │ │ ↓
└─────────┴───────────────────────────────┘
```

### 2.2 会話ペイン詳細設計

**POのメイン操作領域**: チャット形式での指示・判断・チーム連携

```
┌─ 会話ペイン ─────────────────────────────────────────────────┐
│ 15:42 [PO] ECサイトのカート機能を改善したい      │
│ 15:42 [PdM] 市場分析開始、競合調査実施中...      │
│ 15:43 [UX] ユーザー行動分析、離脱ポイント特定中  │
│ 15:43 [PM] プロジェクト全体計画、作業分解中      │
│                                                │
│ 🔔 STANDUP MTG #3 (18vh)                      │
│ 15:45 [dev1] 送料API実装60%完了、懸念あり       │
│ 15:45 [dev2] UI実装80%、dev1レビュー待ち       │
│ 15:45 [dev3] 状態管理実装中、明日統合予定       │
│ 15:46 [qa1] テスト戦略策定完了、実行準備中      │
│ 15:46 [qa2] 異常系テスト中、IE11問題発見        │
│ 15:46 [ux] ワイヤーフレーム更新、KPI設計中      │
│ 15:47 [pdm] 品質評価実施中、改善点3件特定       │
│ 15:47 [pm] 進捗良好、統合準備開始指示           │
│                                                │
│ ⚠️ 重要判断 - PM                               │
│ 15:48 [pm] IE11対応の優先度決定が必要           │
│ → 致命度Lv2、影響範囲限定的                     │
│                                                │
│ 15:48 [PO] IE11は優先度低で対応。Chrome,       │
│           Safari, Edge対応を優先してください    │
│                                                │
│ 15:49 [pm] 承知しました。qa2に方針伝達します    │
│ 15:49 [qa2] 了解、Chrome優先でテスト継続        │
│                                                │
│ ↓ 自動スクロール                               │
├────────────────────────────────────────────────┤ ↑
│ > カート離脱率の目標値を45%に設定してください   │ │ 3行
│                                          [送信]│ │ 入力
└────────────────────────────────────────────────┘ ↓
```

**会話ペイン機能**:
- **表示エリア**: 上部37行、時刻・発言者・メッセージ表示
- **入力エリア**: 下部3行、`> ` プロンプト + 56列入力 + `[送信]`
- **特別表示**: スタンドアップMTG、重要判断、システム通知
- **自動スクロール**: 新着メッセージ時に最下部へ移動

### 2.3 統合ダッシュボード設計: 大画面高密度情報表示

**POの意思決定支援情報** - 上部30%領域、FHD対応拡張表示

**FHD大画面版ダッシュボード (180列×18行)**:
```
┌────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ ⏱️ 仮想時間進行: ████████████░░░░░░░░ 18vh/72vh (25%) │ 📅 Next Standup: 4分12秒後 (22vh) │ ⚡ Auto RMTG: 54分08秒後 │ 🕐 現実時間: 14:32:18    │
│ 🎯 現在スプリント: カート機能改善 - 送料表示最適化    │ 📊 統合進捗: 65% (目標60%超過)      │ 🔄 連携状況: 良好        │ ⚠️ 重要アラート: 0件    │
│                                                      │                                   │                         │                        │
│ 📋 ベロシティ詳細管理                                │ 📦 成果物・品質ゲート状況              │ 👥 チーム詳細ステータス  │ 📈 効率・リスク分析     │
│ ├─ 4Sprint平均: 85SP (±12SP変動)                   │ ├─ 統合PR: 準備中(80%完了)            │ ├─ dev1🟢: レビュー中   │ ├─ 作業効率: 105%      │
│ ├─ 今回目標: 78SP                                  │ ├─ 品質ゲート: 通過予定               │ ├─ dev2🟡: 実装中      │ ├─ ベロシティ予想: 110%│
│ ├─ 現在完了: 51SP (65%)                            │ ├─ テストカバレッジ: 85%              │ ├─ dev3🟢: 統合準備    │ ├─ 依存関係: クリア    │
│ ├─ 残予定: 27SP                                    │ ├─ コード品質: A (静的解析通過)       │ ├─ qa1🟡: 戦略策定中   │ ├─ リスク: 低(Lv1-2)   │
│ └─ 達成予想: 84SP (目標+8%)                        │ └─ パフォーマンス: 1.2秒 (目標2秒内)   │ ├─ qa2🟢: 探索テスト中 │ └─ ブロッカー: 0件     │
│                                                      │                                   │ ├─ ux🟢: KPI設計完了   │                        │
│ 🎯 KPI・目標達成状況                                │ 🔄 チーム連携・コミュニケーション        │ ├─ pm🟢: 管理良好      │ 🚀 次アクション        │
│ ├─ 離脱率改善: 68%→45%目標 (予想47%達成)           │ ├─ 前回MTG課題: 3件→0件解決            │ └─ pdm🟢: 品質評価中   │ ├─ 22vh: Standup#4    │
│ ├─ 満足度向上: 3.2→4.0目標 (予想4.2達成)           │ ├─ Cross-team依存: 2件→0件            │                         │ ├─ 36vh: 統合PR作成   │
│ ├─ タスク完了率: 65%→80%目標 (予想85%達成)         │ ├─ 知識共有セッション: 完了            │ 📊 定量分析・メトリクス │ ├─ 54vh: 品質チェック │
│ └─ 応答時間: 現在1.2秒 (目標1.5秒内達成)           │ └─ コードレビュー効率: 95%             │ ├─ Commit頻度: 1.2/h   │ └─ 72vh: RMTG開始     │
└────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

**大画面対応ダッシュボード機能**:
- **4分割レイアウト**: ベロシティ管理 | 成果物・品質 | チーム状況 | 効率・リスク分析
- **詳細メトリクス**: SP管理、品質ゲート、KPI進捗、チーム効率の数値表示
- **リアルタイム更新**: 1秒間隔での状況更新、進捗バー・ステータス反映
- **予測分析**: ベロシティ予想、目標達成率、リスク評価の表示
- **次アクション**: タイムライン表示で次の重要なマイルストーンを明示

### 2.4 重要判断時のUI表示

**PMの重要判断フロー発生時**

```
┌─ ⚠️ 重要判断が必要です ────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ 致命度: Level 4 (重度)                                                                     │
│ 問題: ユーザー認証システムの仕様変更が必要                                                    │
│ 影響: 3つのタスクに波及、スプリント遅延の可能性                                            │
│                                                                                            │
│ [継続] [後回し] [中止] [詳細確認]                                                           │
└────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

### 2.5 レスポンシブ対応: 大画面最適化戦略

**現代ディスプレイ対応表**:

| 画面サイズ | 文字数 | レイアウト | 最適化内容 | 備考 |
|---|---|---|---|---|
| **FHD+ (4K含む)** | 240x60+ | 大画面フル活用 | 高密度情報表示、詳細メトリクス、拡張ダッシュボード | 主要ターゲット |
| **FHD標準** | 200x50-239x59 | 大画面活用 | 標準密度情報、基本メトリクス、標準ダッシュボード | 推奨サイズ |
| **HD** | 160x40-199x49 | 標準レイアウト | PO体験最適化、コンパクト表示 | 互換性維持 |
| **小画面** | 120x30-159x39 | 縮小版 | ダッシュボード簡略化、必須情報のみ | 実用範囲 |
| **最小** | 120x30未満 | 警告表示 | 使用不推奨メッセージ | 対応外 |

**大画面活用方針**:
```fsharp
// 動的レイアウト適応システム
let adaptToScreenSize (width: int) (height: int) =
    match (width, height) with
    | (w, h) when w >= 240 && h >= 60 ->
        { LayoutType = HighDensity
          ConversationWidth = w / 4        // 25%割り当て
          DashboardHeight = h * 30 / 100   // 30%詳細情報
          PaneColumns = 8                  // 8分割専門ペイン
          InfoDensity = Maximum
          ExtendedFeatures = true }
    | (w, h) when w >= 200 && h >= 50 ->
        { LayoutType = StandardDensity
          ConversationWidth = 60           // 固定60列
          DashboardHeight = h * 30 / 100
          PaneColumns = 6                  // 6分割
          InfoDensity = Standard
          ExtendedFeatures = true }
    | (w, h) when w >= 160 && h >= 40 ->
        { LayoutType = Compact
          ConversationWidth = 50
          DashboardHeight = h * 25 / 100
          PaneColumns = 6
          InfoDensity = Minimal
          ExtendedFeatures = false }
    | _ ->
        { LayoutType = Unsupported
          ShowWarning = "画面サイズが小さすぎます。160x40以上を推奨" }
```

### 2.6 大画面最適化カラースキーム

**長時間使用・高密度情報表示に最適化した配色**

```fsharp
// 大画面・長時間使用対応カラースキーム
let largeScreenColorSchemes = 
    [|
        // メインペイン（眼精疲労軽減重視）
        ("conversation", Color.DarkBlue, Color.Gray90)    // 会話: 濃青地/薄灰文字（長時間読書最適化）
        ("dashboard", Color.Black, Color.Gray80)          // ダッシュボード: 黒地/薄灰文字（高コントラスト）
        
        // 専門ペイン（役割別識別性向上）
        ("dev", Color.DarkGreen, Color.LightGray)         // 開発: 深緑地/薄灰文字（集中力重視）
        ("qa", Color.DarkRed, Color.LightGray)            // QA: 深赤地/薄灰文字（注意喚起）
        ("ux", Color.DarkCyan, Color.LightGray)           // UX: 深シアン地/薄灰文字（創造性）
        ("pm", Color.DarkMagenta, Color.LightYellow)      // PM: 深マゼンタ地/薄黄文字（管理重視）
        ("pdm", Color.DarkOrange, Color.LightGray)        // PdM: 深橙地/薄灰文字（品質重視）
        
        // ステータス・アラート（視認性最優先）
        ("alert_critical", Color.Red, Color.White)        // 重要アラート: 赤地/白文字
        ("alert_warning", Color.Orange, Color.Black)      // 警告: 橙地/黒文字
        ("success", Color.DarkGreen, Color.LightGreen)    // 成功: 深緑地/薄緑文字
        ("progress_good", Color.Green, Color.White)       // 良好: 🟢
        ("progress_caution", Color.Yellow, Color.Black)   // 注意: 🟡
        ("progress_error", Color.Red, Color.White)        // エラー: 🔴
        
        // 大画面専用拡張カラー
        ("metrics_high", Color.Blue, Color.White)         // 高密度メトリクス表示
        ("timeline", Color.Purple, Color.LightGray)       // タイムライン表示
        ("prediction", Color.Teal, Color.White)           // 予測・分析表示
        ("collaboration", Color.Lime, Color.Black)        // チーム連携表示
    |]

// 大画面使用時の視覚的配慮
let largeScreenVisualConfig = {
    // 文字サイズの動的調整
    BaseFontSize = 12
    LargeFontSize = 14      // FHD+での文字サイズ向上
    HeaderFontSize = 16     // ヘッダー・重要情報
    
    // コントラスト比の最適化
    MinContrastRatio = 4.5  // WCAG AA準拠
    PreferredRatio = 7.0    // 長時間使用最適化
    
    // 大画面特有の配慮
    EyeStrainReduction = true   // 青色光軽減
    HighDensityMode = true      // 高密度情報表示モード
    AdaptiveBrightness = true   // 環境光対応明度調整
}
```

### 2.7 POインタラクションフロー

**ユーザー（PO）の典型操作パターン**

1. **起動時**: 会話ペインにフォーカス、ビジョン説明入力
2. **指示後**: 統合ダッシュボードで全体監視モード
3. **問題発生**: アラート → 詳細確認 → 判断入力
4. **スタンドアップ**: 監視モード（参加不要、会話ペインで状況確認）
5. **RMTG**: 会話ペインで最終判断・承認入力

**キーバインド最適化**:
- **Tab**: ペイン間移動（POは主に会話ペイン中心）
- **Enter**: 会話ペインでメッセージ送信
- **Ctrl+L**: UI更新（ダッシュボードリフレッシュ）
- **Ctrl+X H**: ヘルプ表示（PO操作ガイド）

### 2.8 スタンドアップMTG時のUI変化

**6分毎のスタンドアップMTG進行中の表示**

```
┌─ 🔔 STANDUP MTG #3 進行中 (18vh) ──────────────────────────────────────────────────────────────────────────────────────────┐
│ ├─ dev1: 報告完了 ✅                                                                                   │
│ ├─ dev2: 報告中... 🟡                                                                                   │
│ ├─ dev3: 待機中 ⏳                                                                                    │
│ ├─ qa1: 待機中 ⏳                                                                                     │
│ ├─ qa2: 待機中 ⏳                                                                                     │
│ ├─ ux: 待機中 ⏳                                                                                      │
│ ├─ pdm: 待機中 ⏳                                                                                     │
│ └─ pm: 待機中 ⏳                                                                                      │
│                                                                                                    │
│ 予定時間: 2-3分 │ 経過時間: 1分1秒 │ 次回: 24vh (6分後)                                        │
└────────────────────────────────────────────────────────────────────────────────────────────────────┘
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
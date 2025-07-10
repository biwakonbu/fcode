# PR #92 Review TODO List

## 概要
PR #92 "FC-022 専門エージェント統合: 基本実装完了" のCodeRabbitレビュー指摘事項対応管理

## 対応状況

### 🔴 High Priority - 即座に対応必要

#### 1. Processインスタンスでのnewキーワード不足
- **場所**: `src/DevOpsIntegration.fs`
- **行番号**: 87, 120, 147
- **問題**: IDisposableなProcessクラスのインスタンス化で`new`キーワードが不足
- **修正内容**:
  ```fsharp
  // 修正前
  use proc = Process.Start(psi)
  
  // 修正後
  use proc = new Process.Start(psi)
  ```
- **ステータス**: ❌ 未対応

#### 2. ResourceManagementTests.fsのコンパイルエラー
- **場所**: `tests/ResourceManagementTests.fs`
- **行番号**: 322
- **問題**: 未定義の変数`sessionManager`使用によるコンパイルエラー
- **修正内容**: sessionManagerの定義を追加、またはセッション管理コードを一時的に無効化
- **ステータス**: ❌ 未対応

#### 3. DevOpsIntegration.fsの時間計算修正
- **場所**: `src/DevOpsIntegration.fs`
- **行番号**: 100
- **問題**: `TotalProcessorTime`の使用が不正確（CPU時間と実際の経過時間は異なる）
- **修正内容**: Stopwatchを使用した実行時間測定に変更
- **ステータス**: ❌ 未対応

#### 4. 可変DictionaryをMapに変更
- **場所**: `src/DevOpsIntegration.fs`
- **行番号**: 283
- **問題**: F#では不変データ構造が推奨されるが、mutableなDictionaryを使用
- **修正内容**: `Dictionary<DevFlowStage, bool>`を`Map<DevFlowStage, bool>`に変更
- **ステータス**: ❌ 未対応

### 🟡 Medium Priority - 改善推奨

#### 5. Docker形式文字列の定数化
- **場所**: `src/DevOpsIntegration.fs`
- **行番号**: 147
- **問題**: 複雑な形式文字列のハードコーディング
- **修正内容**: 形式文字列を定数として抽出
- **ステータス**: ❌ 未対応

#### 6. SessionManagerのリソース管理改善
- **場所**: `tests/ResourceManagementTests.fs`
- **行番号**: 321-356
- **問題**: SessionManagerインスタンスの適切な破棄とsessionStopSuccess変数の未使用
- **修正内容**: testResourcesへの追加とsessionStopSuccess変数の使用
- **ステータス**: ❌ 未対応

#### 7. スレッド増加許容値の見直し
- **場所**: `tests/ResourceManagementTests.fs`
- **行番号**: 289
- **問題**: 許容値を単純に30に引き上げることは根本的解決策ではない
- **修正内容**: テストの並列実行数を明示的に制御
- **ステータス**: ❌ 未対応

#### 8. 長時間稼働安定性テストの実装完了
- **場所**: `tests/ResourceManagementTests.fs`
- **行番号**: 178-227
- **問題**: 実際のWorkerManager操作がコメントアウトされている
- **修正内容**: モック/スタブ実装、または`[<Ignore>]`属性を使用
- **ステータス**: ❌ 未対応

### 🟢 Low Priority - 将来対応

#### 9. テストカテゴリーの追加検討
- **場所**: `tests/AIModelProviderTests.fs`, `tests/SpecialistAgentManagerTests.fs`
- **問題**: StabilityカテゴリやPerformanceカテゴリの追加を検討
- **修正内容**: エッジケース、異常系、並行実行時の安定性テスト追加
- **ステータス**: ❌ 未対応

#### 10. TODOコメントの実装計画承認
- **場所**: `src/DevOpsIntegration.fs`
- **行番号**: 175
- **問題**: YAMLライブラリへの移行を検討するTODOコメント
- **修正内容**: 将来の改善計画として適切
- **ステータス**: ✅ 承認済み

## 対応優先順位

1. **即座対応**: Processのnewキーワード追加（3箇所）
2. **即座対応**: ResourceManagementTests.fsのコンパイルエラー修正
3. **即座対応**: DevOpsIntegration.fsの時間計算修正
4. **即座対応**: Dictionary→Map変更
5. **改善推奨**: 残りのMedium Priority項目
6. **将来対応**: Low Priority項目

## 作業メモ

- PR #92は13のActionableコメントを受けている
- 主要な問題はF#コーディング規約（newキーワード）とリソース管理
- テストの安定性向上が重要な課題
- 全体的なコード品質は高く、アーキテクチャ設計は適切

## 次回アクション

1. High Priority項目から順番に対応
2. 各修正後にテスト実行で動作確認
3. 全修正完了後にPRを更新
4. CodeRabbitの再レビュー要求
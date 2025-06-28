# F# FSAC Auto-Fix ツール テスト・継続改善ガイド

## 概要

FSAC自動修正ツールの品質向上と継続的改善のためのテスト戦略とプロセスを定義します。

## テストスイート構成

### ファイル構成
```
tools/
├── fsac-auto-fix.fsx          # メインツール
├── fsac-auto-fix-tests.fsx    # テストスイート
└── test-cases/                # テストケース用サンプルファイル（今後追加予定）
```

### テストカテゴリ

#### 1. Conservative レベルテスト
最も安全な修正のみをテスト
- 型キャスト関数: `float(x)` → `float x`
- 静的メソッド呼び出し: `Type.Method(arg)` → `Type.Method arg`
- IDisposableコンストラクタ保護

#### 2. Standard レベルテスト
- Conservative レベル + 安全な関数呼び出し
- インスタンスメソッド呼び出し
- マッチ式内での関数呼び出し

#### 3. 問題ケーステスト
実際に発見された問題の回帰テスト
- 複雑な式での優先順位
- メソッドチェーン
- ネストした関数呼び出し

#### 4. エッジケーステスト
境界条件やエラーケース
- 空の括弧
- 複数引数
- 文字列・コメント内の括弧

## 継続的改善プロセス

### Step 1: 問題発見時のテストケース追加

新しい問題が発見されたら、以下の手順でテストケースを追加：

```fsharp
{
    Name = "問題の簡潔な説明"
    Level = Conservative/Standard/Aggressive
    Input = "修正前のコード"
    Expected = "期待される修正後のコード"
    Description = "FSAC診断コード: 詳細な説明"
    ShouldChange = true/false  // 修正が期待されるか
}
```

#### テストケース追加例
```fsharp
// 新しい問題が発見された場合
let newProblemTestCases = [
    {
        Name = "Array method precedence"
        Level = Standard
        Input = "let length = Directory.GetFiles(dir).Length"
        Expected = "let length = (Directory.GetFiles dir).Length"
        Description = "FSAC0004: 配列メソッド呼び出しでの優先順位問題"
        ShouldChange = true
    }
]
```

### Step 2: テスト実行とツール修正

```bash
# テスト実行
dotnet fsi fsac-auto-fix-tests.fsx

# 失敗したテストケースを確認
❌ FAIL Array method precedence
      Expected: let length = (Directory.GetFiles dir).Length
      Actual:   let length = Directory.GetFiles dir.Length
      Desc:     FSAC0004: 配列メソッド呼び出しでの優先順位問題
```

### Step 3: ツールの修正

`fsac-auto-fix.fsx`でパターンマッチングを改良:

```fsharp
// 修正例: より適切な正規表現パターンの追加
let improvedPattern = {
    Code = "FSAC0004"
    Name = "Safe method call with precedence"
    Pattern = Regex(@"(\w+\.\w+)\\(([^)]+)\\)\\.(\w+)", RegexOptions.Compiled)
    Replacement = fun (content: string) ->
        let pattern = Regex(@"(\w+\.\w+)\\(([^)]+)\\)\\.(\w+)")
        pattern.Replace(content, "($1 $2).$3")
}
```

### Step 4: 回帰テスト実行

```bash
# 全テスト再実行
dotnet fsi fsac-auto-fix-tests.fsx

# 成功確認
✅ PASS Array method precedence
📊 Results: 25 passed, 0 failed
```

### Step 5: 本番適用前チェック

```bash
# 実際のコードベースでドライラン
dotnet fsi fsac-auto-fix.fsx -- --dir src --dry-run --level conservative

# 問題なければ適用
dotnet fsi fsac-auto-fix.fsx -- --dir src --level conservative
```

## テスト実行方法

### 基本実行
```bash
# 全テスト実行
dotnet fsi fsac-auto-fix-tests.fsx
```

### 継続的統合（CI）での実行
```bash
# CI環境での自動テスト
cd tools
dotnet fsi fsac-auto-fix-tests.fsx
if [ $? -ne 0 ]; then
    echo "Auto-fix tool tests failed"
    exit 1
fi
```

## テストデータ管理

### テストケース追加ガイドライン

1. **明確な命名**: テストケース名は問題を端的に表現
2. **FSAC診断コード**: Descriptionに該当するFSACコードを記載
3. **期待値の明確化**: Expectedフィールドに正確な期待結果
4. **レベル適切性**: 修正の安全性に応じたFixLevel設定

### 問題分類

#### FSAC0004関連（括弧除去）
- 関数呼び出し
- メソッド呼び出し
- 型キャスト
- 演算子優先順位

#### FSAC0002関連（冗長な修飾子）
- System.String → String
- System.Int32 → Int32
- 名前空間の簡略化

#### FSAC0001関連（未使用open文）
- 未参照の名前空間
- 重複するopen文

## 品質保証チェックリスト

### 新機能追加時
- [ ] 対応するテストケース追加
- [ ] 既存テストの回帰確認
- [ ] 複数レベルでの動作確認
- [ ] エッジケースの考慮

### リリース前
- [ ] 全テストケース成功
- [ ] 実際のコードベースでのドライラン確認
- [ ] ビルド・テスト成功の確認
- [ ] コードレビュー実施

## 今後の拡張予定

### テスト自動化
- GitHub Actions統合
- プルリクエスト時の自動テスト実行
- カバレッジレポート生成

### テストケース拡充
- より多様なFSAC診断コード対応
- 複雑なF#構文パターン
- パフォーマンステスト

### ツール統合
- IDE拡張機能でのテスト実行
- リアルタイム品質チェック
- カスタムルール設定

## まとめ

このテスト・改善プロセスにより：

1. **品質向上**: 継続的なテストによる信頼性確保
2. **迅速な問題解決**: 発見した問題の即座な修正とテスト化
3. **回帰防止**: 過去の問題の再発防止
4. **チーム協力**: 明確なプロセスによる効率的な開発

継続的にテストケースを追加し、ツールの精度を向上させることで、より安全で効果的なコード品質改善を実現します。
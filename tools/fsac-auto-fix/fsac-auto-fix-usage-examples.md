# F# FSAC Auto-Fix ツール 使用例・継続改善ガイド

## 概要

FSAC自動修正ツールの実用的な使用例と、問題発見時の継続的改善プロセスを説明します。

## 基本的な使用例

### 1. 診断コード確認
```bash
# サポートされているFSAC診断コードとその例を確認
dotnet fsi fsac-auto-fix.fsx -- --diagnostics
```

**出力例:**
```
🔍 FSAC Diagnostic Codes Supported:
==================================================
📋 FSAC0004: Unnecessary parentheses
   関数呼び出し時の不要な括弧を削除
   Examples:
     • float(x) → float x
     • func(arg) → func arg
     • Type.Method(arg) → Type.Method arg

📋 FSAC0002: Redundant qualifier
   冗長な修飾子を削除
   Examples:
     • System.String.Empty → String.Empty
     • System.Int32.Parse → Int32.Parse

📋 FSAC0001: Unused open statement
   未使用のopen文を削除
   Examples:
     • open System.Unused // 未使用の場合削除
```

### 2. 特定診断コードの修正
```bash
# FSAC0004（不要な括弧）のみを修正
dotnet fsi fsac-auto-fix.fsx -- --dir src --fix FSAC0004 --dry-run

# 確認後、実際に適用
dotnet fsi fsac-auto-fix.fsx -- --dir src --fix FSAC0004
```

### 3. レベル別修正
```bash
# Conservative: 最も安全な修正のみ
dotnet fsi fsac-auto-fix.fsx -- --dir src --level conservative

# Standard: 標準的な修正（デフォルト）
dotnet fsi fsac-auto-fix.fsx -- --dir src

# Aggressive: すべての修正を適用
dotnet fsi fsac-auto-fix.fsx -- --dir src --level aggressive
```

## 実際のワークフロー例

### シナリオ1: 新機能開発後のコード品質改善

```bash
# 1. まず診断コードを確認
dotnet fsi fsac-auto-fix.fsx -- --diagnostics

# 2. ドライランで影響を確認
dotnet fsi fsac-auto-fix.fsx -- --dir src --level conservative --dry-run

# 3. 安全な修正から開始
dotnet fsi fsac-auto-fix.fsx -- --dir src --level conservative

# 4. ビルド・テスト確認
dotnet build src/fcode.fsproj
dotnet test tests/fcode.Tests.fsproj

# 5. 問題なければ標準レベルに移行
dotnet fsi fsac-auto-fix.fsx -- --dir src --level standard
```

### シナリオ2: 特定の問題に集中した修正

```bash
# FSAC0004の問題が多い場合、まずそれに集中
dotnet fsi fsac-auto-fix.fsx -- --dir src --fix FSAC0004

# その後、他の診断コードに対応
dotnet fsi fsac-auto-fix.fsx -- --dir src --fix FSAC0002
```

## 問題発見時の継続改善プロセス

### Step 1: 問題の特定と記録

ツール使用中に予期しない結果や問題が発生した場合：

#### 例: 誤った修正が発生
```bash
# 問題のあるコード例
let result = Directory.GetFiles(path).Length  # 修正前
let result = Directory.GetFiles path.Length   # 誤った修正（括弧が必要）
```

### Step 2: テストケースの追加

`tools/fsac-auto-fix-tests.fsx`に新しいテストケースを追加：

```fsharp
// 新しい問題ケースを追加
let newProblemTestCases = [
    {
        Name = "Array method precedence issue"
        Level = Standard
        Input = "let length = Directory.GetFiles(dir).Length"
        Expected = "let length = (Directory.GetFiles dir).Length"
        Description = "FSAC0004: 配列メソッド呼び出しでの優先順位問題"
        ShouldChange = true
    }
]

// 既存のproblemTestCasesリストに追加
let problemTestCases = [
    // 既存のテストケース...
] @ newProblemTestCases
```

### Step 3: テスト実行と確認

```bash
# テストスイート実行
dotnet fsi fsac-auto-fix-tests.fsx

# 失敗を確認
❌ FAIL Array method precedence issue
      Expected: let length = (Directory.GetFiles dir).Length
      Actual:   let length = Directory.GetFiles dir.Length
      Desc:     FSAC0004: 配列メソッド呼び出しでの優先順位問題
```

### Step 4: ツールの修正

`tools/fsac-auto-fix.fsx`でパターンマッチングを改良：

```fsharp
// より安全なパターン追加例
{
    Code = "FSAC0004"
    Name = "Safe method call with property access"
    Pattern = Regex(@"(\w+\.\w+)\(([^)]+)\)\.(\w+)", RegexOptions.Compiled)
    Examples = ["Directory.GetFiles(dir).Length → (Directory.GetFiles dir).Length"]
    Replacement = fun (content: string) ->
        let pattern = Regex(@"(\w+\.\w+)\(([^)]+)\)\.(\w+)")
        pattern.Replace(content, "($1 $2).$3")
}
```

### Step 5: 再テストと確認

```bash
# テスト再実行
dotnet fsi fsac-auto-fix-tests.fsx

# 成功確認
✅ PASS Array method precedence issue
📊 Results: 26 passed, 0 failed
```

### Step 6: 実コードでの検証

```bash
# 実際のコードベースで確認
dotnet fsi fsac-auto-fix.fsx -- --dir src --fix FSAC0004 --dry-run

# 修正内容確認後、適用
dotnet fsi fsac-auto-fix.fsx -- --dir src --fix FSAC0004
```

## トラブルシューティング

### よくある問題と対処法

#### 1. IDisposableコンストラクタの誤修正
```bash
# 問題: newキーワードが削除される
let frame = FrameView title  # 誤った修正

# 解決: IDisposableTypesリストに型を追加
# tools/fsac-auto-fix.fsx の iDisposableTypes に追加
```

#### 2. 複雑な式での優先順位問題
```bash
# 問題例の検出時
let result = func(arg1 + arg2).Property

# テストケース追加
{
    Name = "Complex expression with property access"
    Input = "let result = func(arg1 + arg2).Property"
    Expected = "let result = (func (arg1 + arg2)).Property"
    # ...
}
```

#### 3. 文字列・コメント内の誤解析
```bash
# 問題: 文字列内の括弧が修正される
let text = "func(arg)"  # これは修正されるべきではない

# テストケース追加で確認
{
    Name = "String literal protection"
    Input = "let text = \"func(arg)\""
    Expected = "let text = \"func(arg)\""
    ShouldChange = false
}
```

## 品質保証プロセス

### 修正前チェックリスト
- [ ] ドライランで影響範囲確認
- [ ] テストスイート実行（全テスト成功）
- [ ] 特定診断コードでの段階的適用
- [ ] ビルド・テスト成功確認

### 修正後検証
- [ ] 期待された修正が適用されている
- [ ] 副作用のない修正のみ
- [ ] IDisposableコンストラクタ保護
- [ ] 式の優先順位保持

### 継続的改善メトリクス

#### 成功指標
- テストケース成功率: 95%以上
- 誤修正報告: 月1件以下
- 修正適用時間: 10分以内
- コード品質向上: FSAC警告50%削減

#### 改善トラッキング
```bash
# 修正前後の警告数比較
echo "Before: $(dotnet build 2>&1 | grep -c FSAC)"
# ツール適用
echo "After: $(dotnet build 2>&1 | grep -c FSAC)"
```

## まとめ

このツールと継続改善プロセスにより：

1. **迅速な品質向上**: 自動化によるコード品質改善
2. **安全な修正**: 段階的アプローチと包括的テスト
3. **継続的学習**: 問題発見時の即座な改善
4. **チーム協力**: 明確なプロセスによる効率的な開発

定期的にテストケースを追加し、ツールの精度を向上させることで、より安全で効果的なコード品質改善を実現します。
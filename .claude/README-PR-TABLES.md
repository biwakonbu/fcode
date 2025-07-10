# PR Description Markdown Table Fix Guide

## 🚨 問題

`/next-task` コマンドでPR descriptionのMarkdownテーブルが正しく表示されない問題：

### 問題のある表示例
```
< /dev/null |  指標 | 修正前 | 修正後 |
|------|--------|--------|
```

## ✅ 解決方法

### 1. 手動修正（即座の対応）

PR作成後に手動でPR説明文を編集：

```bash
# PR説明文を編集
gh pr edit <PR番号> --body-file <修正ファイル>
```

### 2. テンプレート使用（推奨）

作成したテンプレートを使用：

```bash
# PRテーブル生成スクリプト実行
/home/biwakonbu/github/fcode/.claude/scripts/generate-pr-table.sh

# テンプレートを使用してPR作成
gh pr create --title "タイトル" --body-file .claude/templates/pr-description-template.md
```

### 3. 正しいMarkdownテーブル書式

```markdown
| 指標 | 修正前 | 修正後 |
|------|--------|--------|
| **テスト実行時間** | 2分タイムアウト | 36秒完了 ✅ |
| **テスト成功率** | 不安定 | 464/464 (100%) ✅ |
```

**重要**: パイプ記号 `|` の前後に必ずスペースを入れる

### 4. 代替HTMLテーブル

Markdownが複雑な場合はHTMLテーブルを使用：

```html
<table>
<tr><th>指標</th><th>修正前</th><th>修正後</th></tr>
<tr><td><strong>テスト実行時間</strong></td><td>2分タイムアウト</td><td>36秒完了 ✅</td></tr>
</table>
```

## 🔧 避けるべき文字列

PR descriptionで以下の文字列を避ける：

- `< /dev/null` (リダイレクト記号)
- `<<<` `>>>` (マージマーカー)
- `` `command` `` `$()` (コマンド置換)
- パイプ記号前後のスペース不統一

## 📝 修正済みの例

FC-027のPR #93は修正済み：
- ❌ 修正前: `< /dev/null |  指標 | 修正前 | 修正後 |`
- ✅ 修正後: `| 指標 | 修正前 | 修正後 |`

## 🚀 将来の改善

1. **スクリプト自動化**: PR作成時に自動でテーブル修正
2. **テンプレート拡張**: 各種修正効果テーブルテンプレート
3. **検証機能**: Markdown構文チェック機能追加

## 📚 関連ファイル

- `.claude/scripts/generate-pr-table.sh`: テーブル生成スクリプト
- `.claude/templates/pr-description-template.md`: PRテンプレート
- `README-PR-TABLES.md`: このガイド（本ファイル）
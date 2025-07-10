# PR Description Template for FC Tasks

## Markdown Table Format for Modification Effects

### 📊 修正効果テーブル (Standard Format)

```markdown
| 指標 | 修正前 | 修正後 | 改善率 |
|------|--------|--------|--------|
| **テスト実行時間** | 2分タイムアウト | 36秒完了 | 70%短縮 |
| **テスト成功率** | 不安定 | 464/464 (100%) | 100%安定 |
| **JSON解析エラー** | 頻発 | 0件 | 完全解決 |
| **Terminal.Guiハング** | CI環境で発生 | 完全回避 | 100%解決 |
| **CI成功率** | 失敗 | 100%安定実行 | 完全安定化 |
```

### 📊 修正効果テーブル (Alternative Format with HTML)

```html
<table>
<tr>
<th>指標</th>
<th>修正前</th>
<th>修正後</th>
<th>改善率</th>
</tr>
<tr>
<td><strong>テスト実行時間</strong></td>
<td>2分タイムアウト</td>
<td>36秒完了</td>
<td>70%短縮</td>
</tr>
<tr>
<td><strong>テスト成功率</strong></td>
<td>不安定</td>
<td>464/464 (100%)</td>
<td>100%安定</td>
</tr>
<tr>
<td><strong>JSON解析エラー</strong></td>
<td>頻発</td>
<td>0件</td>
<td>完全解決</td>
</tr>
<tr>
<td><strong>Terminal.Guiハング</strong></td>
<td>CI環境で発生</td>
<td>完全回避</td>
<td>100%解決</td>
</tr>
<tr>
<td><strong>CI成功率</strong></td>
<td>失敗</td>
<td>100%安定実行</td>
<td>完全安定化</td>
</tr>
</table>
```

### 💡 GitHub PR Description Best Practices

1. **避けるべき文字列**:
   - `< /dev/null` (リダイレクト記号)
   - `<<<` `>>>` (マージマーカー)
   - `$()` `` `command` `` (コマンド置換)
   - `|` の前後スペース統一 (Markdownテーブル)

2. **推奨される記述方法**:
   - パイプ記号 `|` の前後にスペースを必ず挿入
   - セル内容で `|` を使う場合は `&#124;` にエスケープ
   - 長い文字列は改行して可読性を向上
   - HTMLテーブルタグをMarkdownの代替として利用

3. **テーブルフォーマット最適化**:
   ```markdown
   | Left-aligned | Center-aligned | Right-aligned |
   |:-------------|:--------------:|--------------:|
   | 左寄せ        |     中央       |        右寄せ |
   ```

### 🔧 next-task コマンド改善提案

PRのdescription生成時に以下の修正を適用:

1. **制御文字除去**: リダイレクト記号 `< /dev/null` を除去
2. **Markdownテーブル最適化**: パイプ記号の正規化
3. **エスケープ処理**: 特殊文字の適切なエスケープ
4. **HTMLテーブル代替**: 複雑な表はHTMLテーブルで生成

## Example Usage

```bash
# PR作成時のテンプレート使用例
gh pr create --title "FC-XXX: Title" --body-file .claude/templates/pr-body.md
```
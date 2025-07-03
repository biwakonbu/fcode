# Handle PR Reviews

CodeRabbitレビュー指摘事項の自動対応とレポート作成コマンド

引数: `$ARGUMENTS` - 対象PR番号（省略時は現在のブランチから自動検出）

## 実行内容

1. **PRレビュー状況確認**
   - PR番号を$ARGUMENTSから取得（未指定時は現在ブランチから自動検出）
   - 対象PRのレビューコメントを検索
   - CodeRabbit/coderabbitaiからの指摘事項を特定
   - 未対応項目の分類と優先度判定

2. **自動修正実行**
   - **Markdownリンティング修正**: MD031/MD032/MD047対応
   - **MessageBox API一貫性**: エラーケースでErrorQuery使用統一
   - **ユーザビリティ向上**: エラーメッセージにログ確認案内追加
   - **デバッグ情報強化**: UIメッセージにログファイルパス表示

3. **品質保証**
   - F#コードのビルド確認（0エラー・0警告）
   - Fantomasによる自動フォーマット適用
   - pre-commitフックによる品質チェック

4. **自動コミット・プッシュ**
   - 修正内容の自動ステージング
   - 統一フォーマットでのコミットメッセージ生成
   - リモートブランチへの自動プッシュ

5. **CodeRabbitレポート**
   - `@coderabbitai` への完了報告コメント自動投稿
   - 対応項目・品質保証結果・コミット情報の詳細レポート

## 使用方法

現在のブランチのPRに対して実行:
```
/handle-pr-reviews
```

特定のPR番号を指定して実行:
```
/handle-pr-reviews 53
```

実装時の$ARGUMENTS使用例:
```bash
# PR番号の取得
PR_NUMBER=${$ARGUMENTS:-}
if [ -z "$PR_NUMBER" ]; then
    # 現在のブランチから自動検出
    CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD)
    PR_NUMBER=$(gh pr list --head "$CURRENT_BRANCH" --json number --jq '.[0].number')
fi
```

## 対応可能な修正パターン

### F#コード修正
- MessageBox API一貫性改善（Query → ErrorQuery統一）
- エラーメッセージのユーザビリティ向上
- ログファイルパス情報の追加
- Result型エラーハンドリングの最適化

### Markdownドキュメント修正
- MD031: 見出し周りの空行追加
- MD032: リスト前後の空行追加  
- MD047: ファイル末尾改行追加
- リンティングルール全般への準拠

## 品質基準

- **t_wada基準準拠**: failwith除去、関数分離、型安全性
- **Railway Oriented Programming**: Result型統一エラーハンドリング
- **F# Best Practices**: IDisposableオブジェクトnew明示等
- **CI/CDパイプライン**: pre-commit/pre-pushフック完全通過

## 出力例

```
🤖 CodeRabbitレビュー自動対応完了報告

### ✅ 自動対応完了項目
- Markdownリンティング修正: MD031/MD032/MD047 準拠
- MessageBox API一貫性: エラーケースでErrorQuery使用統一
- ユーザビリティ向上: エラーメッセージにログ確認案内追加
- デバッグ支援強化: UIメッセージにログファイルパス表示

### 📋 品質保証
- ✅ ビルド成功: 0エラー、0警告
- ✅ 自動修正: 対象ファイル 3 件
- ✅ コミット: d99d3fa
- ✅ プッシュ完了: feature/fc-014-p3-1-sqlite3-task-storage
```

## 注意事項

- 現在のブランチにPRが存在する必要があります
- GitHub CLIが設定済みである必要があります
- CodeRabbitからのレビューコメントが存在しない場合は自動終了します
- 複雑な修正は手動対応が必要な場合があります
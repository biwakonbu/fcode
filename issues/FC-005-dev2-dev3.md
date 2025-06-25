# FC-005: dev2/dev3 ペイン対応

## 背景
Phase 1 では dev1 ペインのみが動作確認済みです。多ペイン化の第一歩として、同一ロール (開発者) 用の dev2, dev3 ペインを拡張実装します。

## 目的
1. dev2, dev3 ペインで Claude Code が独立して動作し、同時対話が可能になる。
2. プロセス分離 (FC-003) と組み合わせ、各ペインが独立 Worker を保持する。

## 作業内容
- [ ] UI レイアウト: TextView を dev2, dev3 ペインに配置し、キーバインド (Ctrl+X 2 / 3) を追加。
- [ ] SessionManager: `createSession "dev2"`, "dev3" に対応するロジックを追加。
- [ ] Worker 起動: 各ペインで独立した Worker プロセスを起動し、環境変数 `CLAUDE_ROLE=dev` を付与。
- [ ] 回帰テスト: dev1, dev2, dev3 が同時起動し、それぞれのチャットが混線しないことを確認 (統合テスト追加)。

## 受け入れ基準
- [ ] dev1, dev2, dev3 で同時に入力→応答サイクルが完了する。
- [ ] CPU/メモリ使用量が dev1 単体時 +40% 以内に収まる。

## 参照
- `docs/ui_layout.md`
- `docs/pty-architecture.md` 6章

---
**担当**: @
**見積**: 1 人日
**優先度**: 🟨 低 

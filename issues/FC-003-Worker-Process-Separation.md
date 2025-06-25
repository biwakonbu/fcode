# FC-003: Worker Process 分離実装

## 背景
`docs/pty-architecture.md` 6章, 7章 で提案されている tmux ライクなプロセス分離により、UI 障害がセッションに影響しない堅牢構成を目指します。Phase 1 では同一プロセス内で動作していましたが、多ペイン化に先立ち Worker 分離を実現します。

## 目的
1. 各ペイン (dev1, dev2, …) ごとに独立した Claude Code Worker プロセスを起動し、クラッシュ分離を実現する。
2. `ProcessSupervisor` を親プロセスとして、Worker の生存監視・自動再起動を行う。
3. 既存 UI との I/O パイプを IPC (UDS) 経由に置き換え、標準入出力をリダイレクトする。

## 作業内容
- [ ] `SessionServer` (親) / `WorkerProcess` (子) の新しいプロセスツリーを設計。
- [ ] `ProcessSupervisor` に子プロセス起動 API (`spawnWorker : SessionId -> unit`) を実装。
- [ ] 標準入力: UI → IPC → Worker, 標準出力/標準エラー: Worker → IPC → UI へのストリームブリッジを追加。
- [ ] クラッシュ検出後、指数バックオフで再起動するロジックを `ProcessSupervisor` に統合。
- [ ] PoC 実行後、既存の単一プロセスコード (ClaudeCodeProcess.fs) をリファクタリングし、SessionBridge + PtyManager への責務分離を開始。

## 受け入れ基準
- [ ] Worker プロセスがクラッシュしても UI が落ちず、3 秒以内に自動再起動されること。
- [ ] dev1 ペインで従来と同等の対話が可能であること (回帰テスト通過)。
- [ ] ログに Worker PID, 再起動回数, クラッシュ原因が記録されていること。

## 参照
- `docs/pty-architecture.md` 6.1, 7.1, 14.2

---
**担当**: @
**見積**: 2 人日
**優先度**: 🟧 中 

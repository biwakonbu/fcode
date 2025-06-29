# FC-004: プロセス間通信の安定化

## 背景
FC-002 で確立した IPC レイヤを実際の fcode ランタイムで利用する際、バックプレッシャ、メッセージロスト、UI スレッド負荷など運用上の課題を解消する必要があります。

## 目的
1. 非同期ディスパッチ (MainLoop.Invoke) による UI スレッド安全な更新を保証する。
2. Channel ベースのバックプレッシャ制御でメッセージロストをゼロにする。
3. 高頻度更新時も CPU 使用率が 25% 未満に収まるよう最適化する。

## 作業内容
- [ ] `SessionBridge` に受信キュー (`Channel<RenderDiff>`) を実装し、UI スレッドに 16 ms 周期でバッチ適用。
- [ ] `RenderBuffer` の Dirty-flag 二層化を導入し、差分計算コストを最小化。
- [ ] バックプレッシャ (チャネル長) が閾値を超えた場合、古いメッセージをドロップするかフロー制御を行うポリシーを実装。
- [ ] `OpenTelemetry` でメトリクス (QueueLength, ApplyLatency) を計測し、ベンチマークを取得。

## 受け入れ基準
- [ ] 1000 行/秒の出力ストリームで CPU 使用率 < 25%。
- [ ] メッセージロスト 0。キュー遅延が 100 ms を超えない。(p99)
- [ ] ベンチ結果が `docs/perf/ipc-stability.md` に記録されている。

## 参照
- `docs/pty-architecture.md` 7.4, 9.2

---
**担当**: @
**見積**: 2 人日
**優先度**: 🟧 中 

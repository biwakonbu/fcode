# キーイベント欠落問題 : 再発防止設計

## 1. 背景
Terminal.Gui を用いた現行 UI では、デバッグ時に `TextView` を生成せず `FrameView` だけの状態で起動したところ、**キーイベントがまったく取得できない** という障害が発生した。本ドキュメントでは、その根本原因を整理し、今後同様の問題を発生させないための **最小構成と実装ガイドライン** をまとめる。

## 2. 原因整理
1. Terminal.Gui は **`CanFocus = true` の View が少なくとも 1 つ存在し、かつフォーカスが当たっている** ことを前提にキーイベントをディスパッチする。
2. デバッグのため `TextView` 生成を一時的に無効化した結果、画面内にフォーカス可能な View が 0 となった。
3. その状態で `Application.Run()` を開始すると、フォーカス対象が無いため **すべてのキーイベントが破棄** される。

## 3. 理想構成
| No | 対策 | 実装ポイント |
|---|---|---|
| 1 | **フォーカス可能 View を常時 1 つ以上配置** | 各 Pane には ReadOnly の `TextView` を置くか、置かない場合は `FrameView.CanFocus <- true` を設定する |
| 2 | **起動直後に確実にフォーカスを与える** | `focusablePanes.[0].SetFocus()` を呼び、初期フォーカスを会話ペインに固定する |
| 3 | **キー処理の一元化** | `KeyRouter` (既存 `EmacsKeyHandler`) を復活させ、`top.add_KeyDown` で集中処理。ダミーの `KeyCatcherView` を `Application.Top` に追加して Unhandled キーも捕捉する |
| 4 | **デバッグモードの安全装置** | `#if DEBUG_MINIMAL` フラグで UI を簡略化する際でも、Pane に `CanFocus <- true` を付与し、`Application.SetFocus(Application.Top)` を呼んでフォーカスゼロ状態を防止する |
| 5 | **EventLoop との協調** | 独自 `OptimizedEventLoop` が UI スレッドを占有しないよう `AddIdle` 内での処理量を抑制し、UI 操作前に `View.HasFocus` を確認する |

## 4. 実装サンプル
```fsharp
// Pane 生成時の代表例
let makePane title =
    let fv = new FrameView(title)
    fv.Border.Effect3D <- false
    fv.CanFocus <- true  // フォーカス可能にする
    // TextView を追加する場合は以下
    let tv = new TextView()
    tv.ReadOnly <- true
    tv.Text <- ""
    fv.Add(tv)
    fv
```

```fsharp
// アプリ起動時のフォーカス確定
focusablePanes.[0].SetFocus()  // 会話ペインを初期フォーカス
```

## 5. デバッグ最小構成ガイド
```fsharp
#if DEBUG_MINIMAL
// TextView を生成しないミニマル UI でもフォーカスを確保
pane.CanFocus <- true
Application.SetFocus(Application.Top)
#endif
```

## 6. 運用上のチェックリスト
- [ ] `Application.Top.Subviews` に `CanFocus = true` の View が存在するか。
- [ ] 起動シーケンスで `SetFocus` を呼び出しているか。
- [ ] デバッグビルドで UI を削った際にフォーカスゼロになっていないか。
- [ ] EventLoop で UI スレッドをブロックしていないか。

## 7. まとめ
* **フォーカスを絶やさない** ─ これが Terminal.Gui でキーイベントを確実に取得するための最重要ポイント。
* UI を簡略化／レイアウトを変更する際は、必ず「フォーカス可能 View の存在」と「初期フォーカス設定」をチェックすること。
* キーイベント処理は 1 箇所に集約し、Unhandled ケースを拾うダミー View を配置することでデバッグを容易にする。

> このドキュメントは `docs/key-event-focus.md` として配置され、今後の UI 実装・デバッグ時のリファレンスとする。 

module FCode.KeyBindings

open Terminal.Gui
open System
open FCode.Logger
open FCode.ClaudeCodeProcess
open FCode.FCodeError

// キーバインドアクション定義
type KeyAction =
    | Exit
    | NextPane
    | PreviousPane
    | ToggleConversation
    | FocusPane of int
    | Refresh
    | ShowHelp
    | StartClaudeCode
    | StopClaudeCode
    | Cancel
    | DetachSession
    | SaveSession
    | ShowSessionList
    | RecoveryMenu

// キーシーケンス状態管理
type KeySequenceState =
    { PendingKey: Key option
      Timestamp: DateTime }

// キーバインド設定 (Emacs準拠)
let emacsKeyBindings =
    [
      // 終了コマンド (Ctrl+X Ctrl+C)
      ([ Key.CtrlMask ||| Key.X; Key.CtrlMask ||| Key.C ], Exit)

      // 緊急終了 (Esc)
      ([ Key.Esc ], Exit)

      // キャンセル (Ctrl+G) - Emacs風入力キャンセル
      ([ Key.CtrlMask ||| Key.G ], Cancel)

      // ペイン移動 (Ctrl+X O = Other window)
      ([ Key.CtrlMask ||| Key.X; Key.O ], NextPane)

      // 直前ペイン (Ctrl+X Ctrl+O)
      ([ Key.CtrlMask ||| Key.X; Key.CtrlMask ||| Key.O ], PreviousPane)

      // 会話ペイントグル (Ctrl+X V = View toggle)
      ([ Key.CtrlMask ||| Key.X; Key.V ], ToggleConversation)

      // リフレッシュ (Ctrl+L)
      ([ Key.CtrlMask ||| Key.L ], Refresh)

      // ヘルプ表示 (Ctrl+X H)
      ([ Key.CtrlMask ||| Key.X; Key.H ], ShowHelp)

      // Claude Code制御 (Ctrl+X C / Ctrl+X K)
      ([ Key.CtrlMask ||| Key.X; Key.CtrlMask ||| Key.S ], StartClaudeCode)
      ([ Key.CtrlMask ||| Key.X; Key.K ], StopClaudeCode)

      // セッション永続化機能 (Ctrl+X D, Ctrl+X S, Ctrl+X L, Ctrl+X Ctrl+R)
      ([ Key.CtrlMask ||| Key.X; Key.D ], DetachSession)
      ([ Key.CtrlMask ||| Key.X; Key.S ], SaveSession)
      ([ Key.CtrlMask ||| Key.X; Key.L ], ShowSessionList)
      ([ Key.CtrlMask ||| Key.X; Key.CtrlMask ||| Key.R ], RecoveryMenu)

      // 数字キーによるダイレクト移動 (Ctrl+X 0-7)
      ([ Key.CtrlMask ||| Key.X; Key.D0 ], FocusPane 0)
      ([ Key.CtrlMask ||| Key.X; Key.D1 ], FocusPane 1)
      ([ Key.CtrlMask ||| Key.X; Key.D2 ], FocusPane 2)
      ([ Key.CtrlMask ||| Key.X; Key.D3 ], FocusPane 3)
      ([ Key.CtrlMask ||| Key.X; Key.D4 ], FocusPane 4)
      ([ Key.CtrlMask ||| Key.X; Key.D5 ], FocusPane 5)
      ([ Key.CtrlMask ||| Key.X; Key.D6 ], FocusPane 6)
      ([ Key.CtrlMask ||| Key.X; Key.D7 ], FocusPane 7) ]

// ヘルプダイアログ表示
let showHelpDialog () =
    let helpText =
        """
Emacs風キーバインド:

基本操作:
  Ctrl+X Ctrl+C  : 終了
  Ctrl+G         : キャンセル/キーシーケンスリセット
  Ctrl+L         : 画面リフレッシュ
  Ctrl+X H       : このヘルプを表示

ペイン操作:
  Ctrl+X O       : 次のペインに移動
  Ctrl+X Ctrl+O  : 前のペインに移動
  Ctrl+X V       : 会話ペイン表示切替

Claude Code制御:
  Ctrl+X Ctrl+S  : 現在ペインでClaude Code起動
  Ctrl+X K       : 現在ペインのClaude Code終了

セッション永続化:
  Ctrl+X D       : セッションデタッチ (背景実行)
  Ctrl+X S       : 手動セッション保存
  Ctrl+X L       : セッション一覧表示
  Ctrl+X Ctrl+R  : セッション復旧メニュー

ダイレクト移動:
  Ctrl+X 0-7     : 指定ペインに直接移動
    0: 会話, 1: dev1, 2: dev2, 3: dev3
    4: qa1,  5: qa2,  6: ux,   7: PM

Escキーでこのダイアログを閉じます
"""

    let helpDialog = MessageBox.Query(60, 20, "キーバインドヘルプ", helpText, "閉じる")
    ()

// キーシーケンス管理クラス
type EmacsKeyHandler(focusablePanes: FrameView[], sessionMgr: FCode.ClaudeCodeProcess.SessionManager) =
    let mutable keySequenceState =
        { PendingKey = None
          Timestamp = DateTime.MinValue }

    let sequenceTimeout = TimeSpan.FromSeconds(2.0) // 2秒でタイムアウト
    let mutable currentPaneIndex = 0

    // キーシーケンスのタイムアウトチェック
    let isSequenceExpired () =
        DateTime.Now - keySequenceState.Timestamp > sequenceTimeout

    // キーシーケンスリセット
    let resetSequence () =
        keySequenceState <-
            { PendingKey = None
              Timestamp = DateTime.MinValue }

    // アクション実行
    let executeAction action =
        match action with
        | Exit ->
            // 全てのClaude Codeセッションをクリーンアップしてから終了
            try
                sessionMgr.CleanupAllSessions()
                logInfo "KeyBindings" "All sessions cleaned up successfully" |> ignore
            with ex ->
                logException "KeyBindings" "Error during session cleanup" ex |> ignore

            Application.RequestStop()
        | NextPane ->
            currentPaneIndex <- (currentPaneIndex + 1) % focusablePanes.Length
            focusablePanes.[currentPaneIndex].SetFocus()
        | PreviousPane ->
            currentPaneIndex <- (currentPaneIndex - 1 + focusablePanes.Length) % focusablePanes.Length
            focusablePanes.[currentPaneIndex].SetFocus()
        | ToggleConversation ->
            let convoPane = focusablePanes.[0] // 会話ペインは最初
            convoPane.Visible <- not convoPane.Visible
        | FocusPane index when index < focusablePanes.Length ->
            currentPaneIndex <- index
            focusablePanes.[index].SetFocus()
        | FocusPane _ -> () // 無効なインデックスは無視
        | Refresh -> Application.Refresh()
        | ShowHelp -> showHelpDialog ()
        | StartClaudeCode ->
            logInfo "KeyBindings" $"StartClaudeCode action triggered - currentPaneIndex: {currentPaneIndex}"

            let paneId =
                match currentPaneIndex with
                | 1 -> "dev1"
                | 2 -> "dev2"
                | 3 -> "dev3"
                | 4 -> "qa1"
                | 5 -> "qa2"
                | 6 -> "ux"
                | 7 -> "pm"
                | _ -> "unknown"

            logDebug "KeyBindings" $"Mapped paneIndex {currentPaneIndex} to paneId: {paneId}"

            if paneId <> "unknown" && currentPaneIndex > 0 then
                let currentPane = focusablePanes.[currentPaneIndex]

                let textViews =
                    currentPane.Subviews
                    |> Seq.choose (function
                        | :? TextView as tv -> Some tv
                        | _ -> None)
                    |> Seq.toList

                match textViews with
                | textView :: _ ->
                    let workingDir = System.Environment.CurrentDirectory

                    let success = sessionMgr.StartSession(paneId, workingDir, textView)

                    if success then
                        MessageBox.Query(50, 10, "Claude Code", $"{paneId}ペインでClaude Codeを再起動しました", "OK")
                        |> ignore
                    else
                        MessageBox.ErrorQuery(50, 10, "Error", "Claude Code起動に失敗しました。詳細はログを確認してください。", "OK")
                        |> ignore
                | [] ->
                    MessageBox.Query(50, 10, "Claude Code", "このペインはClaude Code対応していません", "OK")
                    |> ignore
            else
                MessageBox.Query(50, 10, "Claude Code", "会話ペインではClaude Codeは起動できません", "OK")
                |> ignore
        | StopClaudeCode ->
            let paneId =
                match currentPaneIndex with
                | 1 -> "dev1"
                | 2 -> "dev2"
                | 3 -> "dev3"
                | 4 -> "qa1"
                | 5 -> "qa2"
                | 6 -> "ux"
                | 7 -> "pm"
                | _ -> "unknown"

            if paneId <> "unknown" && currentPaneIndex > 0 then
                let success = sessionMgr.StopSession(paneId)

                if success then
                    MessageBox.Query(50, 10, "Claude Code", $"{paneId}ペインのClaude Codeを終了しました", "OK")
                    |> ignore
                else
                    MessageBox.ErrorQuery(50, 10, "Error", "アクティブなセッションがありません", "OK") |> ignore
            else
                MessageBox.Query(50, 10, "Claude Code", "会話ペインではClaude Code操作はできません", "OK")
                |> ignore
        | Cancel ->
            // Emacs風キャンセル処理 - 進行中のキーシーケンスをリセット
            logDebug "KeyBindings" "Ctrl+G pressed - canceling current operation"
            resetSequence ()
            Application.Refresh()
        | DetachSession ->
            // セッションデタッチ機能 (未実装)
            MessageBox.Query(50, 10, "セッション永続化", "セッションデタッチ機能は実装中です", "OK") |> ignore
        | SaveSession ->
            // 手動セッション保存 (未実装)
            MessageBox.Query(50, 10, "セッション永続化", "セッション保存機能は実装中です", "OK") |> ignore
        | ShowSessionList ->
            // セッション一覧表示 (未実装)
            MessageBox.Query(50, 10, "セッション永続化", "セッション一覧機能は実装中です", "OK") |> ignore
        | RecoveryMenu ->
            // セッション復旧メニュー (未実装)
            MessageBox.Query(50, 10, "セッション永続化", "セッション復旧機能は実装中です", "OK") |> ignore

    // マルチキーシーケンス検索
    let findMultiKeyBinding (firstKey: Key) (secondKey: Key) =
        emacsKeyBindings
        |> List.tryFind (fun (keySeq, _) ->
            match keySeq with
            | [ k1; k2 ] -> k1 = firstKey && k2 = secondKey
            | _ -> false)
        |> Option.map snd

    // シングルキー検索
    let findSingleKeyBinding (key: Key) =
        emacsKeyBindings
        |> List.tryFind (fun (keySeq, _) ->
            match keySeq with
            | [ k ] -> k = key
            | _ -> false)
        |> Option.map snd

    // メインキーハンドラ
    member _.HandleKey(keyEvent: KeyEvent) =
        let currentKey = keyEvent.Key

        // デバッグログ追加
        logDebug "KeyBindings" $"Key pressed: {currentKey}, PendingKey: {keySequenceState.PendingKey}"

        // タイムアウトチェック
        if keySequenceState.PendingKey.IsSome && isSequenceExpired () then
            logDebug "KeyBindings" "Key sequence timed out, resetting"
            resetSequence ()

        match keySequenceState.PendingKey with
        | None ->
            // 最初のキーまたはシングルキーコマンド
            match findSingleKeyBinding currentKey with
            | Some action ->
                executeAction action
                true // handled
            | None ->
                // マルチキーシーケンスの開始可能性をチェック
                let isPotentialMultiKey =
                    emacsKeyBindings
                    |> List.exists (fun (keySeq, _) ->
                        match keySeq with
                        | firstKey :: _ -> firstKey = currentKey
                        | [] -> false)

                if isPotentialMultiKey then
                    logDebug "KeyBindings" $"Starting multi-key sequence with: {currentKey}"

                    keySequenceState <-
                        { PendingKey = Some currentKey
                          Timestamp = DateTime.Now }

                    true // handled (待機中)
                else
                    false // not handled
        | Some pendingKey ->
            // 2番目のキー処理
            logDebug "KeyBindings" $"Processing second key: {currentKey} after {pendingKey}"

            match findMultiKeyBinding pendingKey currentKey with
            | Some action ->
                logDebug "KeyBindings" $"Found multi-key action: {action}"
                executeAction action
                resetSequence ()
                true // handled
            | None ->
                logDebug "KeyBindings" $"No multi-key binding found for {pendingKey} + {currentKey}"
                resetSequence ()
                false // not handled

    // 現在のペインインデックス取得
    member _.CurrentPaneIndex = currentPaneIndex

    // ペインインデックス設定
    member _.SetCurrentPaneIndex(index: int) =
        if index >= 0 && index < focusablePanes.Length then
            currentPaneIndex <- index

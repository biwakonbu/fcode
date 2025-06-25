module TuiPoC.KeyBindings

open Terminal.Gui
open System

// キーバインドアクション定義
type KeyAction =
    | Exit
    | NextPane
    | PreviousPane
    | ToggleConversation
    | FocusPane of int
    | Refresh
    | ShowHelp

// キーシーケンス状態管理
type KeySequenceState = {
    PendingKey: Key option
    Timestamp: DateTime
}

// キーバインド設定 (Emacs準拠)
let emacsKeyBindings = [
    // 終了コマンド (Ctrl+X Ctrl+C)
    ([Key.CtrlMask ||| Key.X; Key.CtrlMask ||| Key.C], Exit)
    
    // ペイン移動 (Ctrl+X O = Other window)
    ([Key.CtrlMask ||| Key.X; Key.O], NextPane)
    
    // 直前ペイン (Ctrl+X Ctrl+O)
    ([Key.CtrlMask ||| Key.X; Key.CtrlMask ||| Key.O], PreviousPane)
    
    // 会話ペイントグル (Ctrl+X C)
    ([Key.CtrlMask ||| Key.X; Key.C], ToggleConversation)
    
    // リフレッシュ (Ctrl+L)
    ([Key.CtrlMask ||| Key.L], Refresh)
    
    // ヘルプ表示 (Ctrl+X H)
    ([Key.CtrlMask ||| Key.X; Key.H], ShowHelp)
    
    // 数字キーによるダイレクト移動 (Ctrl+X 0-7)
    ([Key.CtrlMask ||| Key.X; Key.D0], FocusPane 0)
    ([Key.CtrlMask ||| Key.X; Key.D1], FocusPane 1)
    ([Key.CtrlMask ||| Key.X; Key.D2], FocusPane 2)
    ([Key.CtrlMask ||| Key.X; Key.D3], FocusPane 3)
    ([Key.CtrlMask ||| Key.X; Key.D4], FocusPane 4)
    ([Key.CtrlMask ||| Key.X; Key.D5], FocusPane 5)
    ([Key.CtrlMask ||| Key.X; Key.D6], FocusPane 6)
    ([Key.CtrlMask ||| Key.X; Key.D7], FocusPane 7)
]

// ヘルプダイアログ表示
let showHelpDialog() =
    let helpText = """
Emacs風キーバインド:

基本操作:
  Ctrl+X Ctrl+C  : 終了
  Ctrl+L         : 画面リフレッシュ
  Ctrl+X H       : このヘルプを表示

ペイン操作:
  Ctrl+X O       : 次のペインに移動
  Ctrl+X Ctrl+O  : 前のペインに移動
  Ctrl+X C       : 会話ペイン表示切替

ダイレクト移動:
  Ctrl+X 0-7     : 指定ペインに直接移動
    0: 会話, 1: dev1, 2: dev2, 3: dev3
    4: qa1,  5: qa2,  6: ux,   7: PM

Escキーでこのダイアログを閉じます
"""
    let helpDialog = MessageBox.Query(60, 20, "キーバインドヘルプ", helpText, "閉じる")
    ()

// キーシーケンス管理クラス
type EmacsKeyHandler(focusablePanes: FrameView[]) =
    let mutable keySequenceState = { PendingKey = None; Timestamp = DateTime.MinValue }
    let sequenceTimeout = TimeSpan.FromSeconds(2.0) // 2秒でタイムアウト
    let mutable currentPaneIndex = 0
    
    // キーシーケンスのタイムアウトチェック
    let isSequenceExpired() =
        DateTime.Now - keySequenceState.Timestamp > sequenceTimeout
    
    // キーシーケンスリセット
    let resetSequence() =
        keySequenceState <- { PendingKey = None; Timestamp = DateTime.MinValue }
    
    // アクション実行
    let executeAction action =
        match action with
        | Exit ->
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
        | Refresh ->
            Application.Refresh()
        | ShowHelp ->
            showHelpDialog()
    
    // マルチキーシーケンス検索
    let findMultiKeyBinding (firstKey: Key) (secondKey: Key) =
        emacsKeyBindings
        |> List.tryFind (fun (keySeq, _) ->
            match keySeq with
            | [k1; k2] -> k1 = firstKey && k2 = secondKey
            | _ -> false)
        |> Option.map snd
    
    // シングルキー検索
    let findSingleKeyBinding (key: Key) =
        emacsKeyBindings
        |> List.tryFind (fun (keySeq, _) ->
            match keySeq with
            | [k] -> k = key
            | _ -> false)
        |> Option.map snd
    
    // メインキーハンドラ
    member _.HandleKey(keyEvent: KeyEvent) =
        let currentKey = keyEvent.Key
        
        // タイムアウトチェック
        if keySequenceState.PendingKey.IsSome && isSequenceExpired() then
            resetSequence()
        
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
                    keySequenceState <- { PendingKey = Some currentKey; Timestamp = DateTime.Now }
                    true // handled (待機中)
                else
                    false // not handled
        | Some pendingKey ->
            // 2番目のキー処理
            match findMultiKeyBinding pendingKey currentKey with
            | Some action ->
                executeAction action
                resetSequence()
                true // handled
            | None ->
                resetSequence()
                false // not handled
    
    // 現在のペインインデックス取得
    member _.CurrentPaneIndex = currentPaneIndex
    
    // ペインインデックス設定
    member _.SetCurrentPaneIndex(index: int) =
        if index >= 0 && index < focusablePanes.Length then
            currentPaneIndex <- index
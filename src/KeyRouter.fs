namespace FCode

open Terminal.Gui
open FCode.Logger

/// キー入力をfcodeホットキーとClaude透過キーに振り分けるルーター
type KeyRouter(sessionBridge: SessionBridge) =

    /// キー入力をルーティングし、透過キーかfcodeホットキーかを判定
    /// Returns: true = Claude透過キー、false = fcodeホットキー
    member this.RouteKey(keyEvent: KeyEvent) : bool =
        match keyEvent.Key with
        // fcodeホットキー（内部処理）
        | k when k = (Key.CtrlMask ||| Key.X) ->
            logDebug "KeyRouter" "fcode内部キー: Ctrl+X"
            false
        | k when k = (Key.CtrlMask ||| Key.L) ->
            logDebug "KeyRouter" "fcode内部キー: Ctrl+L"
            false
        | k when k = (Key.CtrlMask ||| Key.H) ->
            logDebug "KeyRouter" "fcode内部キー: Ctrl+H"
            false
        | k when k = (Key.CtrlMask ||| Key.O) ->
            logDebug "KeyRouter" "fcode内部キー: Ctrl+O"
            false

        // Claude透過キー（PTY送信）
        | _ ->
            let success = this.SendToPty(keyEvent)

            if success then
                logDebug "KeyRouter" $"Claude透過キー送信成功: {this.GetKeyDescription(keyEvent)}"
            else
                logWarning "KeyRouter" $"Claude透過キー送信失敗: {this.GetKeyDescription(keyEvent)}"

            true

    /// PTYへキー入力を送信
    member private this.SendToPty(keyEvent: KeyEvent) : bool =
        try
            let keySequence = this.ConvertToEscapeSequence(keyEvent)
            sessionBridge.SendInput(keySequence)
        with ex ->
            logError "KeyRouter" $"PTY送信例外: {ex.Message}"
            false

    /// KeyEventをエスケープシーケンスに変換
    member private this.ConvertToEscapeSequence(keyEvent: KeyEvent) : string =
        match keyEvent.Key with
        // 基本制御文字
        | Key.Enter -> "\n"
        | Key.Tab -> "\t"
        | Key.Backspace -> "\b"
        | Key.Delete -> "\u001b[3~"
        | Key.Esc -> "\u001b"

        // カーソルキー
        | Key.CursorUp -> "\u001b[A"
        | Key.CursorDown -> "\u001b[B"
        | Key.CursorLeft -> "\u001b[D"
        | Key.CursorRight -> "\u001b[C"

        // ファンクションキー
        | Key.F1 -> "\u001bOP"
        | Key.F2 -> "\u001bOQ"
        | Key.F3 -> "\u001bOR"
        | Key.F4 -> "\u001bOS"
        | Key.F5 -> "\u001b[15~"
        | Key.F6 -> "\u001b[17~"
        | Key.F7 -> "\u001b[18~"
        | Key.F8 -> "\u001b[19~"
        | Key.F9 -> "\u001b[20~"
        | Key.F10 -> "\u001b[21~"
        | Key.F11 -> "\u001b[23~"
        | Key.F12 -> "\u001b[24~"

        // ページ移動
        | Key.PageUp -> "\u001b[5~"
        | Key.PageDown -> "\u001b[6~"
        | Key.Home -> "\u001b[H"
        | Key.End -> "\u001b[F"

        // Ctrl+文字キー
        | k when k = (Key.CtrlMask ||| Key.A) -> "\u0001" // Ctrl+A
        | k when k = (Key.CtrlMask ||| Key.B) -> "\u0002" // Ctrl+B
        | k when k = (Key.CtrlMask ||| Key.C) -> "\u0003" // Ctrl+C
        | k when k = (Key.CtrlMask ||| Key.D) -> "\u0004" // Ctrl+D
        | k when k = (Key.CtrlMask ||| Key.E) -> "\u0005" // Ctrl+E
        | k when k = (Key.CtrlMask ||| Key.F) -> "\u0006" // Ctrl+F
        | k when k = (Key.CtrlMask ||| Key.G) -> "\u0007" // Ctrl+G
        | k when k = (Key.CtrlMask ||| Key.I) -> "\u0009" // Ctrl+I (Tab)
        | k when k = (Key.CtrlMask ||| Key.J) -> "\u000A" // Ctrl+J
        | k when k = (Key.CtrlMask ||| Key.K) -> "\u000B" // Ctrl+K
        | k when k = (Key.CtrlMask ||| Key.M) -> "\u000D" // Ctrl+M (Enter)
        | k when k = (Key.CtrlMask ||| Key.N) -> "\u000E" // Ctrl+N
        | k when k = (Key.CtrlMask ||| Key.P) -> "\u0010" // Ctrl+P
        | k when k = (Key.CtrlMask ||| Key.Q) -> "\u0011" // Ctrl+Q
        | k when k = (Key.CtrlMask ||| Key.R) -> "\u0012" // Ctrl+R
        | k when k = (Key.CtrlMask ||| Key.S) -> "\u0013" // Ctrl+S
        | k when k = (Key.CtrlMask ||| Key.T) -> "\u0014" // Ctrl+T
        | k when k = (Key.CtrlMask ||| Key.U) -> "\u0015" // Ctrl+U
        | k when k = (Key.CtrlMask ||| Key.V) -> "\u0016" // Ctrl+V
        | k when k = (Key.CtrlMask ||| Key.W) -> "\u0017" // Ctrl+W
        | k when k = (Key.CtrlMask ||| Key.Y) -> "\u0019" // Ctrl+Y
        | k when k = (Key.CtrlMask ||| Key.Z) -> "\u001A" // Ctrl+Z

        // 通常文字キー
        | _ ->
            // KeyValueから文字を取得
            let keyValue = int keyEvent.KeyValue

            if keyValue >= 32 && keyValue <= 126 then
                // 印刷可能ASCII文字
                string (char keyValue)
            else
                // その他のキーは空文字（無視）
                ""

    /// デバッグ用キー説明を取得
    member private this.GetKeyDescription(keyEvent: KeyEvent) : string =
        $"Key={keyEvent.Key}, KeyValue={keyEvent.KeyValue}"

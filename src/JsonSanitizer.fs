namespace fcode

open System
open System.Text.Json
open System.Text.RegularExpressions

/// JSON解析における制御文字・エスケープシーケンス除去専用モジュール
/// Terminal.GuiのANSI制御コード・制御文字がJSON解析を破綻させる問題の根本解決
module JsonSanitizer =

    /// 制御文字・エスケープシーケンス・ANSI制御コード完全除去パターン
    let private sanitizePatterns =
        [|
           // 基本制御文字（NULL, タブ以外の制御文字）
           @"[\x00-\x08\x0E-\x1F\x7F]"

           // ANSI CSI (Control Sequence Introducer) - カラー・カーソル制御
           @"\u001b\[[0-9;]*[mK]" // カラーコード m, クリア K
           @"\u001b\[[0-9;]*[HfABCDsuJnp]" // カーソル制御
           @"\u001b\[[\d;]*[HfABCDsu]" // カーソル移動・制御

           // ANSI DCS (Device Control String) - モード設定
           @"\u001b\[\?[0-9;]*[hl]" // モード設定/解除
           @"\u001b\[\?[\d;]*[hl]" // プライベートモード

           // Terminal.Gui特有の制御シーケンス
           @"\[\?\d+[hl]" // モード切り替え
           @"\][\d;]*[a-zA-Z]" // OSC (Operating System Command)
           @"\u001b\][\d;]*[^\u0007]*\u0007" // OSCシーケンス完全

           // その他のTerminal制御
           @"\u001b[DEHMNOIZ]" // 単一制御文字
           @"\u001b\([012AB]" // 文字セット選択
           @"\u001b\*[012AB]" // 文字セット指定

           // Cursor制御（Terminal.Guiで頻出）
           @"\u001b\[[\d;]*[qr]" // カーソル形状・復元
           @"\u001b\[!p" // リセット

           // Mouse制御（Terminal.Gui）
           @"\u001b\[\?1003[hl]" // マウストラッキング
           @"\u001b\[\?1015[hl]" // マウス制御
           @"\u001b\[\?1006[hl]" |] // マウス制御拡張

    /// 入力文字列から制御文字・エスケープシーケンスを完全除去
    let sanitizeForJson (input: string) : string =
        if String.IsNullOrEmpty(input) then
            ""
        else
            // 複数パターンを順次適用してエスケープシーケンス完全除去
            let sanitized =
                sanitizePatterns
                |> Array.fold (fun acc pattern -> Regex.Replace(acc, pattern, "", RegexOptions.Compiled)) (input.Trim())

            // 連続する空白・改行を正規化
            let normalized = Regex.Replace(sanitized, @"\s+", " ", RegexOptions.Compiled)
            normalized.Trim()

    /// JSON解析安全実行（ジェネリック版）
    let tryParseJson<'T> (input: string) : Result<'T, string> =
        try
            let sanitized = sanitizeForJson input

            if String.IsNullOrWhiteSpace(sanitized) then
                Error "Empty input after sanitization"
            elif sanitized.Length < 2 then
                Error "Input too short to be valid JSON"
            else
                // JSON解析オプション設定
                let options = JsonSerializerOptions()
                options.PropertyNameCaseInsensitive <- true
                options.ReadCommentHandling <- JsonCommentHandling.Skip
                options.AllowTrailingCommas <- true

                let result = JsonSerializer.Deserialize<'T>(sanitized, options)
                Ok result
        with
        | :? JsonException as ex -> Error $"JSON parse failed: {ex.Message}"
        | ex -> Error $"Unexpected error: {ex.Message}"

    /// プレーンテキスト用の安全なサニタイズ（JSON以外）
    let sanitizeForPlainText (input: string) : string =
        if String.IsNullOrEmpty(input) then
            ""
        else
            // より軽量なサニタイズ（基本制御文字のみ除去）
            let basicPattern = @"[\x00-\x08\x0E-\x1F\x7F]|\u001b\[[0-9;]*[mK]"
            let sanitized = Regex.Replace(input.Trim(), basicPattern, "", RegexOptions.Compiled)
            sanitized.Trim()

    /// JSON解析可能性チェック（事前検証）
    let isValidJsonCandidate (input: string) : bool =
        let sanitized = sanitizeForJson input

        if String.IsNullOrWhiteSpace(sanitized) then
            false
        else
            // 基本的なJSON構造チェック
            let trimmed = sanitized.Trim()

            (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            || (trimmed.StartsWith("[") && trimmed.EndsWith("]"))

    /// JSON解析ログ付き安全実行
    let tryParseJsonWithLogging<'T> (input: string) (logFunc: string -> unit) : Result<'T, string> =
        let originalLength = if isNull input then 0 else input.Length
        let sanitized = sanitizeForJson input
        let sanitizedLength = sanitized.Length

        if originalLength <> sanitizedLength then
            logFunc $"JsonSanitizer: Removed {originalLength - sanitizedLength} control characters"

        tryParseJson<'T> sanitized

namespace fcode

open System
open System.Text.Json
open System.Text.RegularExpressions

/// JSON解析における制御文字・エスケープシーケンス除去専用モジュール
/// Terminal.GuiのANSI制御コード・制御文字がJSON解析を破綻させる問題の根本解決
module JsonSanitizer =

    /// 制御文字・エスケープシーケンス・ANSI制御コード完全除去パターン（強化版）
    let private sanitizePatterns =
        [|
           // 最も包括的なESCシーケンス除去（優先適用）
           @"\u001b\[[?!>]*[0-9;]*[A-Za-z@]", " " // 包括的CSI
           @"\u001b\][^\u0007\u001b]*[\u0007\u001b\\]", " " // OSC完全版
           @"\u001b[NOPQRSTUVWXYZ\[\\\]^_`abcdefghijklmnopqrstuvwxyz{|}~]", " " // ESC + 制御文字

           // ANSI カラー・制御シーケンス
           @"\u001b\[[0-9;]*[mK]", " " // カラー・クリア
           @"\u001b\[\?[0-9;]*[hl]", " " // プライベートモード設定
           @"\u001b\[[0-9;]*[ABCDHJ]", " " // カーソル制御
           @"\u001b\[[0-9]+[;,][0-9]*[HfGr]", " " // 位置制御

           // Terminal.Gui特有制御シーケンス（強化）
           @"\u001b\[\?[0-9]+[hl]", " " // モード設定
           @"\u001b\[\d+[;,]\d*[a-zA-Z]", " " // パラメータ付き制御
           @"\u001b\][0-9;]*[^\u0007]*", " " // OSC不完全版
           @"\][0-9]+[a-zA-Z]", " " // 残存OSC断片

           // 基本制御文字・null文字・非印刷文字
           @"[\x00-\x08\x0B-\x0C\x0E-\x1F\x7F]", " " // 制御文字（TAB・LF保持）
           @"[\uFEFF]", " " // BOM文字

           // JSON特有の問題文字（日本語文字は保持）
           @"[^\x09\x0A\x0D\x20-\x7E\u00A0-\uFFFF]", " " // ASCII+Unicode印刷可能文字以外
           @"\\x[0-9a-fA-F]{2}", " " |] // 16進エスケープ残存

    /// 入力文字列から制御文字・エスケープシーケンスを完全除去（強化版）
    let sanitizeForJson (input: string) : string =
        if String.IsNullOrEmpty(input) then
            ""
        else
            try
                // 段階的サニタイズで確実に除去
                let step1 = input.Trim()

                // Step 1: 最も危険なエスケープシーケンス優先除去
                let step2 =
                    sanitizePatterns
                    |> Array.fold
                        (fun acc (pattern, replacement) ->
                            try
                                Regex.Replace(
                                    acc,
                                    pattern,
                                    replacement,
                                    RegexOptions.Compiled ||| RegexOptions.Multiline
                                )
                            with
                            | :? ArgumentException -> acc // 無効な正規表現はスキップ
                            | _ -> acc)
                        step1

                // Step 2: 残存制御文字の徹底除去
                let step3 =
                    step2.ToCharArray()
                    |> Array.map (fun c ->
                        let code = int c
                        // 安全なASCII+Unicode文字保持（日本語対応）
                        if (code >= 32 && code <= 126) || code = 9 || code = 10 || code = 13 || code >= 160 then
                            c
                        else
                            ' ')
                    |> System.String

                // Step 3: 連続空白・改行正規化
                let step4 = Regex.Replace(step3, @"\s+", " ", RegexOptions.Compiled)

                // Step 4: 前後の空白・改行除去
                let result = step4.Trim()

                // Step 5: 最終的な制御文字・非ASCII文字の安全確認
                let finalResult =
                    if result.Length > 0 then
                        // JSONに危険な文字が含まれていないか最終チェック
                        result.ToCharArray()
                        |> Array.filter (fun c ->
                            let code = int c

                            (code >= 32 && code <= 126)
                            || code = 9
                            || code = 10
                            || code = 13
                            || (code >= 160 && code <= 65535))
                        |> System.String
                    else
                        ""

                // 結果検証: エスケープシーケンス残存チェック
                if finalResult.Length > 0 && not (finalResult.Contains("\u001b")) then
                    finalResult
                else
                    // エスケープシーケンスが残存している場合は空文字を返す
                    ""
            with ex ->
                // サニタイズ失敗時は空文字で安全側に
                ""

    /// JSON解析安全実行（ジェネリック版）
    let tryParseJson<'T> (input: string) : Result<'T, string> =
        try
            if String.IsNullOrWhiteSpace(input) then
                Error "Empty input"
            else
                let sanitized = sanitizeForJson input

                if String.IsNullOrWhiteSpace(sanitized) then
                    Error "Empty input after sanitization"
                elif sanitized.Length < 2 then
                    Error "Input too short to be valid JSON"
                else
                    // 追加的な制御文字除去（JSON特化）
                    let finalSanitized =
                        sanitized
                        |> fun s ->
                            Regex.Replace(s, @"[^\x20-\x7E\x09\x0A\x0D\u00A0-\uFFFF]", "", RegexOptions.Compiled)
                        |> fun s -> s.Trim()

                    if String.IsNullOrWhiteSpace(finalSanitized) then
                        Error "No valid content after deep sanitization"
                    else
                        // JSON解析オプション設定
                        let options = JsonSerializerOptions()
                        options.PropertyNameCaseInsensitive <- true
                        options.ReadCommentHandling <- JsonCommentHandling.Skip
                        options.AllowTrailingCommas <- true

                        let result = JsonSerializer.Deserialize<'T>(finalSanitized, options)
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
            let basicPattern = @"[\x00-\x08\x0E-\x1F\x7F]|\u001b\[[0-9;]*[a-zA-Z]"

            let sanitized =
                Regex.Replace(input.Trim(), basicPattern, " ", RegexOptions.Compiled)
            // 連続する空白を単一空白に正規化
            let normalized = Regex.Replace(sanitized, @"\s+", " ", RegexOptions.Compiled)
            normalized.Trim()

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

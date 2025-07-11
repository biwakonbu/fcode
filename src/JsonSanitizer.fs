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
           // ANSI エスケープシーケンス全般 - 最優先除去
           @"\u001b\[[0-9;]*[mK]", " " // カラー・クリア
           @"\u001b\[[0-9;]*[A-Za-z]", " " // 汎用CSI
           @"\u001b\[\?[0-9;]*[hl]", " " // プライベートモード
           @"\u001b\[[\d;]*[a-zA-Z]", " " // 全般CSI

           // Terminal.Gui特有制御シーケンス
           @"\[[0-9;]*[a-zA-Z]", " " // ブラケット制御
           @"\u001b\][\d;]*[^\u0007]*\u0007", " " // OSC シーケンス
           @"\][0-9]*[a-zA-Z]", " " // OSC簡易版

           // より包括的なESCシーケンス
           @"\u001b[A-Za-z]", " " // 単一制御文字
           @"\u001b\([012AB]", " " // 文字セット
           @"\u001b\*[012AB]", " " // 文字セット指定

           // 基本制御文字（最後に適用）
           @"[\x00-\x08\x0E-\x1F\x7F]", " " |]

    /// 入力文字列から制御文字・エスケープシーケンスを完全除去
    let sanitizeForJson (input: string) : string =
        if String.IsNullOrEmpty(input) then
            ""
        else
            // 複数パターンを順次適用してエスケープシーケンス完全除去
            let sanitized =
                sanitizePatterns
                |> Array.fold
                    (fun acc (pattern, replacement) -> Regex.Replace(acc, pattern, replacement, RegexOptions.Compiled))
                    (input.Trim())

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

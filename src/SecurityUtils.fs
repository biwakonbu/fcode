namespace FCode

open System
open System.IO
open System.Text.RegularExpressions
open System.Security.Cryptography

/// セキュリティユーティリティモジュール
module SecurityUtils =

    /// セキュリティ検証結果の型
    type SecurityResult<'T> =
        | SecurityOk of 'T
        | SecurityError of string

    /// 機密情報として扱う環境変数のパターン（完全一致）
    let private sensitiveEnvExactPatterns =
        [ "API_KEY"
          "APIKEY"
          "SECRET"
          "PASSWORD"
          "TOKEN"
          "PRIVATE"
          "CREDENTIAL"
          "JWT"
          "DATABASE_URL" ]

    /// 機密情報として扱う環境変数の接頭辞パターン
    let private sensitiveEnvPrefixPatterns =
        [ "API_KEY_"; "SECRET_"; "PASSWORD_"; "TOKEN_"; "PRIVATE_"; "CREDENTIAL_" ]

    /// 機密情報として扱う環境変数の接尾辞パターン
    let private sensitiveEnvSuffixPatterns =
        [ "_API_KEY"
          "_SECRET"
          "_PASSWORD"
          "_TOKEN"
          "_PRIVATE"
          "_CREDENTIAL"
          "_AUTH" ]

    /// 危険な環境変数として扱うパターン
    let private dangerousEnvPatterns =
        [ Regex(@"^LD_PRELOAD$", RegexOptions.IgnoreCase)
          Regex(@"^LD_LIBRARY_PATH$", RegexOptions.IgnoreCase)
          Regex(@"^SHELL$", RegexOptions.IgnoreCase)
          Regex(@"^PATH$", RegexOptions.IgnoreCase) ]

    /// 危険なコマンドを含むかチェックするパターン
    let private dangerousCommandPatterns =
        [ Regex(@"rm\s+-rf", RegexOptions.IgnoreCase)
          Regex(@"chmod\s+777", RegexOptions.IgnoreCase)
          Regex(@"sudo\s+", RegexOptions.IgnoreCase)
          Regex(@"su\s+", RegexOptions.IgnoreCase)
          Regex(@"wget\s+", RegexOptions.IgnoreCase)
          Regex(@"curl\s+", RegexOptions.IgnoreCase)
          Regex(@"nc\s+", RegexOptions.IgnoreCase)
          Regex(@"netcat\s+", RegexOptions.IgnoreCase) ]

    /// 無効なファイル名文字のパターン
    let private invalidFileNameChars =
        [ '<'
          '>'
          ':'
          '"'
          '|'
          '?'
          '*'
          '/'
          '\\'
          '\x00'
          '\x01'
          '\x02'
          '\x03'
          '\x04'
          '\x05'
          '\x06'
          '\x07'
          '\x08'
          '\x09'
          '\x0A'
          '\x0B'
          '\x0C'
          '\x0D'
          '\x0E'
          '\x0F'
          '\x10'
          '\x11'
          '\x12'
          '\x13'
          '\x14'
          '\x15'
          '\x16'
          '\x17'
          '\x18'
          '\x19'
          '\x1A'
          '\x1B'
          '\x1C'
          '\x1D'
          '\x1E'
          '\x1F' ]

    /// パス区切り文字
    let private pathSeparators = [ '/'; '\\' ]

    /// セッションIDを安全にサニタイズ
    let sanitizeSessionId (sessionId: string) : Result<string, string> =
        if String.IsNullOrEmpty(sessionId) then
            Error "セッションIDが空です"
        else
            // 長さ制限（最大64文字）
            let truncatedId =
                if sessionId.Length > 64 then
                    sessionId.[..63]
                else
                    sessionId

            // 危険な文字を除去
            let safeChars =
                truncatedId.ToCharArray()
                |> Array.filter (fun c ->
                    // 英数字、ハイフン、アンダースコアのみ許可
                    Char.IsLetterOrDigit(c) || c = '-' || c = '_')
                |> Array.take (min 64 (Array.length (truncatedId.ToCharArray())))

            let result =
                if safeChars.Length = 0 then
                    // 完全に無効な場合はハッシュ値を使用
                    use sha = SHA256.Create()
                    let hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sessionId))
                    Convert.ToHexString(hashBytes).[..15] // 16文字のハッシュ
                else
                    String(safeChars)

            Ok result

    /// ペインIDを安全にサニタイズ
    let sanitizePaneId (paneId: string) : Result<string, string> =
        if String.IsNullOrEmpty(paneId) then
            Error "ペインIDが空です"
        else
            // 長さ制限（最大32文字）
            let truncatedId = if paneId.Length > 32 then paneId.[..31] else paneId

            // 危険な文字を除去・置換
            let safeChars =
                truncatedId.ToCharArray()
                |> Array.map (fun c ->
                    if Char.IsLetterOrDigit(c) || c = '-' || c = '_' then c
                    elif c = ' ' then '_'
                    else 'X') // 危険な文字は'X'に置換
                |> Array.take (min 32 (Array.length (truncatedId.ToCharArray())))

            Ok(String(safeChars))

    /// ファイルパスの安全性を検証
    let validateFilePath (basePath: string) (targetPath: string) =
        try
            let normalizedBase = Path.GetFullPath(basePath)
            let normalizedTarget = Path.GetFullPath(targetPath)

            if normalizedTarget.StartsWith(normalizedBase) then
                Ok normalizedTarget
            else
                Error $"パスインジェクション攻撃を検出: {targetPath} が基準ディレクトリ {basePath} の外を指しています"
        with ex ->
            Error $"パス検証エラー: {ex.Message}"

    /// 機密情報を含む環境変数をフィルタリング
    let filterSensitiveEnvironment (environment: Map<string, string>) : Map<string, string> =
        environment
        |> Map.filter (fun key value ->
            let upperKey = key.ToUpper()

            // 完全一致チェック
            let hasExactMatch =
                sensitiveEnvExactPatterns |> List.exists (fun pattern -> upperKey = pattern)

            // 接頭辞チェック
            let hasPrefixMatch =
                sensitiveEnvPrefixPatterns
                |> List.exists (fun pattern -> upperKey.StartsWith(pattern))

            // 接尾辞チェック
            let hasSuffixMatch =
                sensitiveEnvSuffixPatterns
                |> List.exists (fun pattern -> upperKey.EndsWith(pattern))

            // 機密情報パターンにマッチしないもののみ保持
            not (hasExactMatch || hasPrefixMatch || hasSuffixMatch))

    /// 危険な環境変数をフィルタリング
    let filterDangerousEnvironment (environment: Map<string, string>) : Map<string, string> =
        environment
        |> Map.filter (fun key value ->
            let isDangerous =
                dangerousEnvPatterns |> List.exists (fun pattern -> pattern.IsMatch(key))

            if isDangerous then
                // SHELLの場合は値も検証
                if
                    key.ToUpper() = "SHELL"
                    && dangerousCommandPatterns |> List.exists (fun pattern -> pattern.IsMatch(value))
                then
                    // Security Warning: 危険なSHELL環境変数を除去（ログ出力は上位レイヤーで実施）
                    false
                else if key.ToUpper() = "SHELL" then
                    true // 安全なSHELLは保持
                else
                    // Security Warning: 危険な環境変数を除去（ログ出力は上位レイヤーで実施）
                    false
            else
                true)

    /// 会話履歴から機密情報をフィルタリング（最適化版） - sanitizeLogMessageの後に定義
    /// ファイル名の安全性を検証
    let validateFileName (fileName: string) : Result<string, string> =
        if String.IsNullOrEmpty(fileName) then
            Error "ファイル名が空です"
        elif fileName.Length > 255 then
            Error $"ファイル名が長すぎます: {fileName.Length}文字（最大255文字）"
        elif fileName.Contains("..") then
            Error "ファイル名にディレクトリトラバーサル文字列(..)が含まれています"
        elif invalidFileNameChars |> List.exists (fileName.Contains) then
            let invalidChars =
                invalidFileNameChars |> List.filter (fileName.Contains) |> List.map string

            let invalidCharsStr = String.Join(", ", invalidChars)
            Error $"ファイル名に無効な文字が含まれています: {invalidCharsStr}"
        elif pathSeparators |> List.exists (fileName.Contains) then
            Error "ファイル名にパス区切り文字が含まれています"
        else
            Ok fileName

    /// ディレクトリの安全性を検証
    let validateDirectory (baseDir: string) (targetDir: string) : Result<string, string> =
        try
            let normalizedBase = Path.GetFullPath(baseDir)
            let normalizedTarget = Path.GetFullPath(targetDir)

            // 基準ディレクトリ内かチェック
            if not (normalizedTarget.StartsWith(normalizedBase)) then
                Error $"ディレクトリトラバーサル攻撃を検出: {targetDir}"
            // シンボリックリンクでないかチェック
            elif
                File.Exists(normalizedTarget)
                && File.GetAttributes(normalizedTarget).HasFlag(FileAttributes.ReparsePoint)
            then
                Error $"シンボリックリンクは許可されていません: {targetDir}"
            else
                Ok normalizedTarget
        with ex ->
            Error $"ディレクトリ検証エラー: {ex.Message}"

    /// データサイズの制限チェック
    let validateDataSize (data: byte[]) (maxSizeMB: int) : Result<unit, string> =
        let sizeBytes = int64 data.Length
        let maxSizeBytes = int64 maxSizeMB * 1024L * 1024L

        if sizeBytes > maxSizeBytes then
            Error $"データサイズが制限を超えています: {sizeBytes / 1024L / 1024L}MB > {maxSizeMB}MB"
        else
            Ok()

    /// 会話履歴の長さ制限チェック
    let validateConversationLength (conversation: string list) (maxLength: int) : Result<string list, string> =
        if conversation.Length > maxLength then
            // Security Warning: 会話履歴が制限を超えているため切り詰めます（ログ出力は上位レイヤーで実施）
            Ok(conversation |> List.take maxLength)
        else
            Ok conversation

    /// 環境変数の完全なセキュリティフィルタリング
    let sanitizeEnvironment (environment: Map<string, string>) : Map<string, string> =
        environment |> filterSensitiveEnvironment |> filterDangerousEnvironment

    /// セッション全体のセキュリティ検証
    let validateSessionSecurity
        (sessionId: string)
        (paneStates: Map<string, 'T>)
        (basePath: string)
        : Result<string * Map<string, 'T>, string> =
        try
            // セッションIDのサニタイズ
            match sanitizeSessionId sessionId with
            | Error msg -> Error msg
            | Ok safeSessionId ->
                // ペインIDのサニタイズ
                let paneResults =
                    paneStates
                    |> Map.toList
                    |> List.map (fun (paneId, state) ->
                        match sanitizePaneId paneId with
                        | Ok safePaneId -> Ok(safePaneId, state)
                        | Error msg -> Error msg)

                // すべてのペインIDサニタイズが成功したかチェック
                let errors =
                    paneResults
                    |> List.choose (function
                        | Error e -> Some e
                        | Ok _ -> None)

                if not errors.IsEmpty then
                    let errorMessage = String.Join("; ", errors)
                    Error $"ペインIDサニタイズエラー: {errorMessage}"
                else
                    let safePaneStates =
                        paneResults
                        |> List.choose (function
                            | Ok result -> Some result
                            | Error _ -> None)
                        |> Map.ofList

                    // セッションディレクトリの安全性検証
                    let sessionDir = Path.Combine(basePath, "sessions", safeSessionId)

                    match validateDirectory basePath sessionDir with
                    | Ok _ -> Ok(safeSessionId, safePaneStates)
                    | Error msg -> Error msg

        with ex ->
            Error $"セッションセキュリティ検証エラー: {ex.Message}"

    /// 事前コンパイル済み正規表現パターン（パフォーマンス最適化）
    let private compiledPatterns =
        lazy
            (let patterns =
                dict
                    [ "openai_api_key",
                      Regex(@"sk-[a-zA-Z0-9\-_]{20,}", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
                      "github_token",
                      Regex(@"gh[pousr]_[a-zA-Z0-9]{36}", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
                      "aws_access_key", Regex(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
                      "jwt_token",
                      Regex(
                          @"eyJ[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+",
                          RegexOptions.Compiled ||| RegexOptions.IgnoreCase
                      )
                      "password_field",
                      Regex(@"password[:\s=]+[^\s]+", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
                      "connection_string",
                      Regex(
                          @"(Server|Host|Data Source)\s*=\s*[^;]+;[^;]*Password\s*=\s*[^;]+;",
                          RegexOptions.Compiled ||| RegexOptions.IgnoreCase
                      )
                      "mongodb_url",
                      Regex(@"mongodb://[^@]+:[^@]+@[^/]+/[^\s]+", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
                      "postgresql_url",
                      Regex(@"postgresql://[^@]+:[^@]+@[^/]+/[^\s]+", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
                      "redis_url",
                      Regex(@"redis://[^@]+:[^@]+@[^/]+", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
                      "unix_home_path", Regex(@"/home/[^/\s]+", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
                      "windows_user_path",
                      Regex(@"C:\\Users\\[^\\]+", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
                      "stack_trace",
                      Regex(
                          @"at [^\r\n]+\.[^\r\n]+\([^\r\n]*\)[^\r\n]*",
                          RegexOptions.Compiled ||| RegexOptions.IgnoreCase
                      ) ]

             patterns)

    /// ログメッセージの機密情報除去
    let sanitizeLogMessage (message: string) : string =
        if String.IsNullOrEmpty(message) then
            message
        else
            let patterns = compiledPatterns.Value
            let mutable sanitized = message

            // OpenAI API Keys
            sanitized <- patterns.["openai_api_key"].Replace(sanitized, "[API_KEY]")

            // GitHub Tokens
            sanitized <- patterns.["github_token"].Replace(sanitized, "[GITHUB_TOKEN]")

            // AWS Access Keys
            sanitized <- patterns.["aws_access_key"].Replace(sanitized, "[AWS_ACCESS_KEY]")

            // JWT Tokens
            sanitized <- patterns.["jwt_token"].Replace(sanitized, "[JWT_TOKEN]")

            // Password fields
            sanitized <- patterns.["password_field"].Replace(sanitized, "password=[REDACTED]")

            // Database connection strings
            sanitized <- patterns.["connection_string"].Replace(sanitized, "Database=[REDACTED];")
            sanitized <- patterns.["mongodb_url"].Replace(sanitized, "mongodb://[REDACTED]")
            sanitized <- patterns.["postgresql_url"].Replace(sanitized, "postgresql://[REDACTED]")
            sanitized <- patterns.["redis_url"].Replace(sanitized, "redis://[REDACTED]")

            // Home directory paths (Unix and Windows)
            sanitized <- patterns.["unix_home_path"].Replace(sanitized, "/home/[USER]")
            sanitized <- patterns.["windows_user_path"].Replace(sanitized, "C:\\Users\\[USER]")

            // Stack trace information
            sanitized <- patterns.["stack_trace"].Replace(sanitized, "at [STACK_TRACE]")

            // 機密性の高い環境変数値を除去（環境変数の形式のみに限定）
            sensitiveEnvExactPatterns
            |> List.iter (fun pattern ->
                let envPattern = Regex($@"\b{pattern}[:\s=]+[^\s]+", RegexOptions.IgnoreCase)
                sanitized <- envPattern.Replace(sanitized, $"{pattern}=[REDACTED]"))

            sanitized

    /// 会話履歴から機密情報をフィルタリング（最適化版）
    let filterSensitiveConversation (conversation: string list) : string list =
        conversation |> List.map sanitizeLogMessage

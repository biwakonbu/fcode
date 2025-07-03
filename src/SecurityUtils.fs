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

    /// 機密情報として扱う環境変数のパターン
    let private sensitiveEnvPatterns =
        [ Regex(@".*API_?KEY.*", RegexOptions.IgnoreCase)
          Regex(@".*SECRET.*", RegexOptions.IgnoreCase)
          Regex(@".*PASSWORD.*", RegexOptions.IgnoreCase)
          Regex(@".*TOKEN.*", RegexOptions.IgnoreCase)
          Regex(@".*PRIVATE.*", RegexOptions.IgnoreCase)
          Regex(@".*CREDENTIAL.*", RegexOptions.IgnoreCase)
          Regex(@".*AUTH.*", RegexOptions.IgnoreCase)
          Regex(@"JWT.*", RegexOptions.IgnoreCase)
          Regex(@".*DATABASE_URL.*", RegexOptions.IgnoreCase)
          Regex(@".*CONNECTION.*", RegexOptions.IgnoreCase) ]

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
            // 機密情報パターンにマッチしないもののみ保持
            not (sensitiveEnvPatterns |> List.exists (fun pattern -> pattern.IsMatch(key))))

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
                    Logger.logWarning "Security" $"危険なSHELL環境変数を除去: {key}={value}"
                    false
                else if key.ToUpper() = "SHELL" then
                    true // 安全なSHELLは保持
                else
                    Logger.logWarning "Security" $"危険な環境変数を除去: {key}"
                    false
            else
                true)

    /// 会話履歴から機密情報をフィルタリング
    let filterSensitiveConversation (conversation: string list) : string list =
        conversation
        |> List.map (fun message ->
            let mutable filteredMessage = message

            // API KEYパターンを検出・置換
            let apiKeyPattern = Regex(@"sk-[a-zA-Z0-9\-_]{20,}", RegexOptions.IgnoreCase)
            filteredMessage <- apiKeyPattern.Replace(filteredMessage, "[API_KEY_REDACTED]")

            // トークンパターンを検出・置換
            let tokenPattern = Regex(@"[a-zA-Z0-9]{32,}", RegexOptions.IgnoreCase)
            filteredMessage <- tokenPattern.Replace(filteredMessage, "[TOKEN_REDACTED]")

            // パスワードらしき文字列を検出・置換
            let passwordPattern = Regex(@"password[:\s=]+[^\s]+", RegexOptions.IgnoreCase)
            filteredMessage <- passwordPattern.Replace(filteredMessage, "password=[REDACTED]")

            filteredMessage)

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
            Logger.logWarning "Security" $"会話履歴が制限を超えているため切り詰めます: {conversation.Length} > {maxLength}"
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

    /// ログメッセージの機密情報除去
    let sanitizeLogMessage (message: string) : string =
        let mutable sanitized = message

        // API KEYを除去
        let apiKeyPattern = Regex(@"sk-[a-zA-Z0-9\-_]{10,}", RegexOptions.IgnoreCase)
        sanitized <- apiKeyPattern.Replace(sanitized, "[API_KEY]")

        // パスワードらしき情報を除去
        let passwordPattern = Regex(@"password[:\s=]+[^\s]+", RegexOptions.IgnoreCase)
        sanitized <- passwordPattern.Replace(sanitized, "password=[REDACTED]")

        // 長いトークンらしき文字列を除去
        let tokenPattern = Regex(@"[a-zA-Z0-9]{32,}", RegexOptions.IgnoreCase)
        sanitized <- tokenPattern.Replace(sanitized, "[TOKEN]")

        sanitized

namespace FCode

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open System.IO.Compression
open FCode.SecurityUtils
open fcode

/// セッション永続化機能を提供するモジュール
module SessionPersistenceManager =

    /// 個別ペインの状態を表現する型
    type PaneState =
        { PaneId: string
          ConversationHistory: string list
          WorkingDirectory: string
          Environment: Map<string, string>
          ProcessStatus: string
          LastActivity: DateTime
          MessageCount: int
          SizeBytes: int64 }

    /// セッション全体のスナップショットを表現する型
    type SessionSnapshot =
        { SessionId: string
          PaneStates: Map<string, PaneState>
          CreatedAt: DateTime
          LastSavedAt: DateTime
          TotalSize: int64
          Version: string }

    /// セッションのメタデータ情報
    type SessionMetadata =
        { SessionId: string
          PaneCount: int
          LastActivity: DateTime
          SizeBytes: int64
          IsDetached: bool
          CreatedAt: DateTime }

    /// 永続化設定
    type PersistenceConfig =
        { AutoSaveIntervalMinutes: int
          MaxHistoryLength: int
          MaxSessionAge: TimeSpan
          StorageDirectory: string
          CompressionEnabled: bool
          MaxSessionSizeMB: int }

    /// デフォルト設定
    let defaultConfig =
        { AutoSaveIntervalMinutes = 5
          MaxHistoryLength = 1000
          MaxSessionAge = TimeSpan.FromDays(7.0)
          StorageDirectory =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "fcode",
                "persistence"
            )
          CompressionEnabled = true
          MaxSessionSizeMB = 500 }

    /// セッション保存・読み込み操作の結果
    type PersistenceResult<'T> =
        | Success of 'T
        | Error of string

    /// ストレージディレクトリの初期化
    let initializeStorage (config: PersistenceConfig) =
        try
            let sessionsDir = Path.Combine(config.StorageDirectory, "sessions")
            let recoveryDir = Path.Combine(config.StorageDirectory, "recovery")
            let configDir = Path.Combine(config.StorageDirectory, "config")

            Directory.CreateDirectory(sessionsDir) |> ignore
            Directory.CreateDirectory(recoveryDir) |> ignore
            Directory.CreateDirectory(configDir) |> ignore

            // ディレクトリ権限設定 (Unix系のみ)
            FCode.UnixPermissions.UnixPermissionHelper.setSessionDirectoryPermissions (sessionsDir)
            |> ignore

            Success()
        with ex ->
            Error $"ストレージ初期化失敗: {ex.Message}"

    /// セッションIDの生成
    let generateSessionId () =
        DateTime.Now.ToString("yyyyMMdd-HHmmss")
        + "-"
        + Guid.NewGuid().ToString("N")[..7]

    /// JSON設定でのシリアライゼーション
    let private jsonOptions = JsonSerializerOptions(WriteIndented = true)

    /// 会話履歴の圧縮
    let compressHistory (history: string list) : byte[] =
        let historyJson = JsonSerializer.Serialize(history, jsonOptions)
        use inputStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(historyJson))
        use outputStream = new MemoryStream()
        use gzipStream = new GZipStream(outputStream, CompressionLevel.SmallestSize)
        inputStream.CopyTo(gzipStream)
        gzipStream.Close()
        outputStream.ToArray()

    /// 会話履歴の展開
    let decompressHistory (compressedData: byte[]) : string list =
        try
            use inputStream = new MemoryStream(compressedData)
            use gzipStream = new GZipStream(inputStream, CompressionMode.Decompress)
            use outputStream = new MemoryStream()
            gzipStream.CopyTo(outputStream)
            let json = System.Text.Encoding.UTF8.GetString(outputStream.ToArray())

            // FC-034: 強化されたフォールバック解析
            match JsonSanitizer.tryParseJsonWithFallback<string list> json (Logger.logDebug "SessionPersistence") with
            | Result.Ok(result, method) ->
                if method <> "strict" then
                    Logger.logInfo "SessionPersistence" $"会話履歴解析成功（{method}方式）"

                result
            | Result.Error errorMsg ->
                Logger.logError "SessionPersistence" $"会話履歴JSON解析失敗（全フォールバック方式）: {errorMsg}"
                []
        with ex ->
            Logger.logError "SessionPersistence" $"会話履歴展開失敗: {ex.Message}"
            []

    /// ペイン状態の保存（セキュリティ強化版）
    let savePaneState (config: PersistenceConfig) (sessionId: string) (paneState: PaneState) =
        try
            // セキュリティ検証: IDのサニタイズ
            match sanitizeSessionId sessionId with
            | Result.Error msg -> Error $"セッションIDサニタイズエラー: {msg}"
            | Result.Ok safeSessionId ->
                match sanitizePaneId paneState.PaneId with
                | Result.Error msg -> Error $"ペインIDサニタイズエラー: {msg}"
                | Result.Ok safePaneId ->

                    let sessionDir = Path.Combine(config.StorageDirectory, "sessions", safeSessionId)

                    // パスインジェクション防止: ベースディレクトリ外へのアクセスを拒否
                    let normalizedSessionDir = Path.GetFullPath(sessionDir)
                    let normalizedBaseDir = Path.GetFullPath(config.StorageDirectory)

                    if not (normalizedSessionDir.StartsWith(normalizedBaseDir)) then
                        Error "セキュリティ検証失敗: パスインジェクション攻撃を検出"
                    else
                        Directory.CreateDirectory(sessionDir) |> ignore

                        let stateDir = Path.Combine(sessionDir, "pane-states")
                        Directory.CreateDirectory(stateDir) |> ignore

                        let historyDir = Path.Combine(sessionDir, "conversation-history")
                        Directory.CreateDirectory(historyDir) |> ignore

                        // 環境変数から機密情報を除去
                        let sanitizedEnvironment = sanitizeEnvironment paneState.Environment

                        // 会話履歴から機密情報を除去
                        let sanitizedHistory = filterSensitiveConversation paneState.ConversationHistory

                        // ペイン状態の保存 (会話履歴除く、機密情報除去済み)
                        let stateWithoutHistory =
                            { paneState with
                                PaneId = safePaneId
                                ConversationHistory = []
                                Environment = sanitizedEnvironment }

                        let stateJson = JsonSerializer.Serialize(stateWithoutHistory, jsonOptions)
                        let stateFile = Path.Combine(stateDir, $"{safePaneId}.json")
                        File.WriteAllText(stateFile, stateJson)

                        // 会話履歴の圧縮保存（機密情報除去済み）
                        if config.CompressionEnabled && sanitizedHistory.Length > 0 then
                            let compressedHistory = compressHistory sanitizedHistory
                            let historyFile = Path.Combine(historyDir, $"{safePaneId}.history.gz")
                            File.WriteAllBytes(historyFile, compressedHistory)

                        Success()
        with ex ->
            let sanitizedMessage = sanitizeLogMessage ex.Message
            Logger.logError "SessionPersistence" $"ペイン状態保存失敗: {sanitizedMessage}"

            match sanitizePaneId paneState.PaneId with
            | Result.Ok safePaneId -> Error $"ペイン状態保存失敗 ({safePaneId}): {sanitizedMessage}"
            | Result.Error _ -> Error $"ペイン状態保存失敗: {sanitizedMessage}"

    /// ペイン状態の読み込み
    let loadPaneState (config: PersistenceConfig) (sessionId: string) (paneId: string) =
        try
            let sessionDir = Path.Combine(config.StorageDirectory, "sessions", sessionId)
            let stateFile = Path.Combine(sessionDir, "pane-states", $"{paneId}.json")

            let historyFile =
                Path.Combine(sessionDir, "conversation-history", $"{paneId}.history.gz")

            if not (File.Exists(stateFile)) then
                Error $"ペイン状態ファイルが見つかりません: {paneId}"
            else
                let stateJson = File.ReadAllText(stateFile)

                // JSON内容の事前検証
                if String.IsNullOrWhiteSpace(stateJson) then
                    raise (ArgumentException("State file is empty or invalid"))

                // JsonSanitizerによる制御文字・エスケープシーケンス完全除去
                let cleanStateJson = JsonSanitizer.sanitizeForJson stateJson

                // JSON構造抽出（埋め込まれたJSONを検出・抽出）
                let extractedJson = JsonSanitizer.extractJsonContent stateJson

                // JsonSanitizerによる事前検証
                if not (JsonSanitizer.isValidJsonCandidate stateJson) then
                    raise (JsonException("Invalid JSON format in state file after sanitization"))

                // JsonSanitizerによる安全なJSON解析（抽出されたJSONを使用）
                let paneStateResult = JsonSanitizer.tryParseJson<PaneState> extractedJson

                let basePaneState =
                    match paneStateResult with
                    | Result.Ok state -> state
                    | Result.Error errorMsg -> raise (JsonException($"JSON parse failed: {errorMsg}"))

                // 会話履歴の読み込み（関数型アプローチ）
                let finalPaneState =
                    if File.Exists(historyFile) then
                        let compressedData = File.ReadAllBytes(historyFile)
                        let history = decompressHistory compressedData

                        { basePaneState with
                            ConversationHistory = history }
                    else
                        basePaneState

                Success finalPaneState
        with ex ->
            Error $"ペイン状態読み込み失敗 ({paneId}): {ex.Message}"

    /// セッションスナップショットの保存
    let saveSession (config: PersistenceConfig) (snapshot: SessionSnapshot) =
        try
            Logger.logInfo "SessionPersistence" $"セッション保存開始: {snapshot.SessionId}"

            let sessionDir =
                Path.Combine(config.StorageDirectory, "sessions", snapshot.SessionId)

            Directory.CreateDirectory(sessionDir) |> ignore

            // メタデータの保存
            let metadata =
                { SessionId = snapshot.SessionId
                  PaneCount = snapshot.PaneStates.Count
                  LastActivity = snapshot.LastSavedAt
                  SizeBytes = snapshot.TotalSize
                  IsDetached = false // 現在はアタッチ状態固定。将来DetachAttachManagerと連携予定
                  CreatedAt = snapshot.CreatedAt }

            let metadataJson = JsonSerializer.Serialize(metadata, jsonOptions)
            let metadataFile = Path.Combine(sessionDir, "metadata.json")
            File.WriteAllText(metadataFile, metadataJson)

            // 各ペイン状態の保存
            let saveResults =
                snapshot.PaneStates
                |> Map.toList
                |> List.map (fun (_, paneState) -> savePaneState config snapshot.SessionId paneState)

            let errors =
                saveResults
                |> List.choose (function
                    | Error e -> Some e
                    | Success _ -> None)

            if errors.IsEmpty then
                Logger.logInfo "SessionPersistence" $"セッション保存完了: {snapshot.SessionId}"
                Success()
            else
                let errorMsg = String.Join("; ", errors)
                Logger.logError "SessionPersistence" $"セッション保存部分失敗: {errorMsg}"
                Error errorMsg
        with ex ->
            Logger.logError "SessionPersistence" $"セッション保存失敗: {ex.Message}"
            Error $"セッション保存失敗: {ex.Message}"

    /// セッションスナップショットの読み込み
    let loadSession (config: PersistenceConfig) (sessionId: string) =
        try
            Logger.logInfo "SessionPersistence" $"セッション読み込み開始: {sessionId}"

            let sessionDir = Path.Combine(config.StorageDirectory, "sessions", sessionId)
            let metadataFile = Path.Combine(sessionDir, "metadata.json")

            if not (File.Exists(metadataFile)) then
                Error $"セッションが見つかりません: {sessionId}"
            else
                let metadataJson = File.ReadAllText(metadataFile)

                let metadata =
                    match JsonSanitizer.tryParseJson<SessionMetadata> (metadataJson) with
                    | Result.Ok result -> result
                    | Result.Error errorMsg ->
                        Logger.logError "SessionPersistence" $"セッションメタデータ解析失敗: {errorMsg}"
                        failwith $"セッションメタデータ解析失敗: {errorMsg}"

                // 各ペインの状態データディレクトリを確認
                let stateDir = Path.Combine(sessionDir, "pane-states")

                if not (Directory.Exists(stateDir)) then
                    Error $"ペイン状態ディレクトリが見つかりません: {sessionId}"
                else
                    let paneFiles = Directory.GetFiles(stateDir, "*.json")
                    let paneIds = paneFiles |> Array.map (fun f -> Path.GetFileNameWithoutExtension(f))

                    // 各ペイン状態の読み込み
                    let paneResults =
                        paneIds
                        |> Array.map (fun paneId -> (paneId, loadPaneState config sessionId paneId))
                        |> Array.toList

                    let errors =
                        paneResults
                        |> List.choose (fun (id, result) ->
                            match result with
                            | Error e -> Some $"{id}: {e}"
                            | Success _ -> None)

                    let paneStates =
                        paneResults
                        |> List.choose (fun (id, result) ->
                            match result with
                            | Success state -> Some(id, state)
                            | Error _ -> None)
                        |> Map.ofList

                    if paneStates.IsEmpty then
                        let errorMsg = String.Join("; ", errors)
                        Error $"有効なペイン状態が見つかりません: {errorMsg}"
                    else
                        let snapshot =
                            { SessionId = sessionId
                              PaneStates = paneStates
                              CreatedAt = metadata.CreatedAt
                              LastSavedAt = metadata.LastActivity
                              TotalSize = metadata.SizeBytes
                              Version = "1.0" }

                        Logger.logInfo "SessionPersistence" $"セッション読み込み完了: {sessionId} ({paneStates.Count}ペイン)"

                        if not errors.IsEmpty then
                            let errorMsg = String.Join("; ", errors)
                            Logger.logWarning "SessionPersistence" $"部分的な読み込みエラー: {errorMsg}"

                        Success snapshot
        with ex ->
            Logger.logError "SessionPersistence" $"セッション読み込み失敗: {ex.Message}"
            Error $"セッション読み込み失敗: {ex.Message}"

    /// 利用可能なセッション一覧の取得
    let listSessions (config: PersistenceConfig) =
        try
            let sessionsDir = Path.Combine(config.StorageDirectory, "sessions")

            if not (Directory.Exists(sessionsDir)) then
                []
            else
                Directory.GetDirectories(sessionsDir)
                |> Array.choose (fun sessionDir ->
                    try
                        let sessionId = Path.GetFileName(sessionDir)
                        let metadataFile = Path.Combine(sessionDir, "metadata.json")

                        if File.Exists(metadataFile) then
                            let metadataJson = File.ReadAllText(metadataFile)

                            match JsonSanitizer.tryParseJson<SessionMetadata> (metadataJson) with
                            | Result.Ok metadata -> Some metadata
                            | Result.Error errorMsg ->
                                Logger.logError "SessionPersistence" $"セッションメタデータ解析失敗 ({sessionId}): {errorMsg}"
                                None
                        else
                            None
                    with ex ->
                        Logger.logWarning "SessionPersistence" $"セッションメタデータ読み込み失敗: {ex.Message}"
                        None)
                |> Array.sortByDescending (fun m -> m.LastActivity)
                |> Array.toList
        with ex ->
            Logger.logError "SessionPersistence" $"セッション一覧取得失敗: {ex.Message}"
            []

    /// 古いセッションのクリーンアップ
    let cleanupOldSessions (config: PersistenceConfig) =
        try
            let cutoffTime = DateTime.Now - config.MaxSessionAge
            let sessions = listSessions config
            let oldSessions = sessions |> List.filter (fun s -> s.LastActivity < cutoffTime)

            // 関数型アプローチでセッション削除
            let deleteResults =
                oldSessions
                |> List.map (fun session ->
                    try
                        let sessionDir =
                            Path.Combine(config.StorageDirectory, "sessions", session.SessionId)

                        if Directory.Exists(sessionDir) then
                            Directory.Delete(sessionDir, true)
                            Logger.logInfo "SessionPersistence" $"古いセッションを削除: {session.SessionId}"
                            true // 削除成功
                        else
                            false // ディレクトリが存在しない
                    with ex ->
                        Logger.logWarning "SessionPersistence" $"セッション削除失敗 ({session.SessionId}): {ex.Message}"
                        false // 削除失敗
                )

            let deletedCount = deleteResults |> List.filter id |> List.length
            Success deletedCount
        with ex ->
            Logger.logError "SessionPersistence" $"セッションクリーンアップ失敗: {ex.Message}"
            Error $"セッションクリーンアップ失敗: {ex.Message}"

    /// アクティブセッションの設定
    let setActiveSession (config: PersistenceConfig) (sessionId: string) =
        try
            let activeSessionFile = Path.Combine(config.StorageDirectory, "active-session.json")

            let activeSessionData =
                {| SessionId = sessionId
                   SetAt = DateTime.Now |}

            let json = JsonSerializer.Serialize(activeSessionData, jsonOptions)
            File.WriteAllText(activeSessionFile, json)
            Success()
        with ex ->
            Error $"アクティブセッション設定失敗: {ex.Message}"

    /// アクティブセッションの取得
    let getActiveSession (config: PersistenceConfig) =
        try
            let activeSessionFile = Path.Combine(config.StorageDirectory, "active-session.json")

            if File.Exists(activeSessionFile) then
                let json = File.ReadAllText(activeSessionFile)

                match JsonSanitizer.tryParseJson<{| SessionId: string; SetAt: DateTime |}> (json) with
                | Result.Ok data -> Success(Some data.SessionId)
                | Result.Error errorMsg ->
                    Logger.logError "SessionPersistence" $"アクティブセッション解析失敗: {errorMsg}"
                    Success None
            else
                Success None
        with ex ->
            Logger.logWarning "SessionPersistence" $"アクティブセッション取得失敗: {ex.Message}"
            Success None

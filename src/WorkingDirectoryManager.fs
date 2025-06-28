namespace FCode

open System
open System.IO
open System.Text.Json

/// 各ペイン用の独立した作業ディレクトリ管理モジュール
module WorkingDirectoryManager =

    // 型定義
    type PaneWorkspace =
        { PaneId: string
          BaseDirectory: string // ~/.local/share/fcode/sessions/{pane-id}/
          WorkingDirectory: string // 実際の作業ディレクトリ
          TempDirectory: string // 一時ファイル用
          OutputDirectory: string // 出力ファイル用
          CreatedAt: DateTime }

    type DirectoryIsolationConfig =
        { SessionsBaseDir: string // ~/.local/share/fcode/sessions/
          TempDirPrefix: string // fcode-temp-
          MaxDiskUsageGB: float // 1GB per pane
          CleanupIntervalHours: int } // 24時間

    type DiskUsageInfo =
        { PaneId: string
          UsageMB: float
          LastChecked: DateTime
          ExceedsLimit: bool }

    // デフォルト設定
    let defaultConfig =
        { SessionsBaseDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fcode", "sessions")
          TempDirPrefix = "fcode-temp-"
          MaxDiskUsageGB = 1.0
          CleanupIntervalHours = 24 }

    // ディレクトリ作成とアクセス権設定
    let private createSecureDirectory (path: string) =
        try
            if not (Directory.Exists(path)) then
                Directory.CreateDirectory(path) |> ignore

                // Linuxでディレクトリ権限を700に設定
                if Environment.OSVersion.Platform = PlatformID.Unix then
                    let chmodPath = "chmod"
                    let chmodArgs = sprintf "700 \"%s\"" path
                    let chmod = System.Diagnostics.Process.Start(chmodPath, chmodArgs)
                    chmod.WaitForExit()

            Logger.logInfo "WorkingDirectoryManager" $"セキュアディレクトリ作成完了: {path}"
            Ok()
        with ex ->
            Logger.logError "WorkingDirectoryManager" $"ディレクトリ作成失敗: {path}" ex
            Error $"ディレクトリ作成エラー: {ex.Message}"

    // ペイン専用ワークスペース作成
    let createPaneWorkspace (config: DirectoryIsolationConfig) (paneId: string) =
        try
            let baseDir = Path.Combine(config.SessionsBaseDir, paneId)
            let workingDir = Path.Combine(baseDir, "workspace")
            let tempDir = Path.Combine(baseDir, "temp")
            let outputDir = Path.Combine(baseDir, "output")

            // 各ディレクトリを作成
            let createResults =
                [ createSecureDirectory baseDir
                  createSecureDirectory workingDir
                  createSecureDirectory tempDir
                  createSecureDirectory outputDir ]

            // すべてのディレクトリ作成が成功したかチェック
            let allSuccess =
                createResults
                |> List.forall (function
                    | Ok _ -> true
                    | Error _ -> false)

            if allSuccess then
                let workspace =
                    { PaneId = paneId
                      BaseDirectory = baseDir
                      WorkingDirectory = workingDir
                      TempDirectory = tempDir
                      OutputDirectory = outputDir
                      CreatedAt = DateTime.UtcNow }

                Logger.logInfo "WorkingDirectoryManager" $"ペインワークスペース作成完了: {paneId}"
                Ok workspace
            else
                let errors =
                    createResults
                    |> List.choose (function
                        | Error e -> Some e
                        | Ok _ -> None)

                let errorMessage = String.Join(", ", errors)
                Error $"ワークスペース作成失敗: {errorMessage}"

        with ex ->
            Logger.logError "WorkingDirectoryManager" $"ワークスペース作成例外: {paneId}" ex
            Error $"ワークスペース作成例外: {ex.Message}"

    // ディスク使用量計算
    let private calculateDirectorySize (path: string) =
        try
            if Directory.Exists(path) then
                let files = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                let totalBytes = files |> Array.sumBy (fun file -> FileInfo(file).Length)
                float totalBytes / (1024.0 * 1024.0) // MB単位
            else
                0.0
        with ex ->
            Logger.logError "WorkingDirectoryManager" $"ディスク使用量計算エラー: {path}" ex
            0.0

    // ペインのディスク使用量チェック
    let checkDiskUsage (config: DirectoryIsolationConfig) (workspace: PaneWorkspace) =
        try
            let sizeMB = calculateDirectorySize workspace.BaseDirectory
            let exceedsLimit = sizeMB > (config.MaxDiskUsageGB * 1024.0)

            let usageInfo =
                { PaneId = workspace.PaneId
                  UsageMB = sizeMB
                  LastChecked = DateTime.UtcNow
                  ExceedsLimit = exceedsLimit }

            if exceedsLimit then
                Logger.logWarn "WorkingDirectoryManager" $"ディスク使用量上限超過: {workspace.PaneId} - {sizeMB:F2}MB"
            else
                Logger.logDebug "WorkingDirectoryManager" $"ディスク使用量正常: {workspace.PaneId} - {sizeMB:F2}MB"

            Ok usageInfo
        with ex ->
            Logger.logError "WorkingDirectoryManager" $"ディスク使用量チェックエラー: {workspace.PaneId}" ex
            Error $"ディスク使用量チェックエラー: {ex.Message}"

    // 古いファイル・ディレクトリのクリーンアップ
    let cleanupOldFiles (config: DirectoryIsolationConfig) (workspace: PaneWorkspace) =
        try
            let cutoffTime = DateTime.UtcNow.AddHours(-float config.CleanupIntervalHours)
            let mutable deletedCount = 0
            let mutable freedSpaceMB = 0.0

            // 一時ディレクトリのクリーンアップ
            if Directory.Exists(workspace.TempDirectory) then
                let tempFiles =
                    Directory.GetFiles(workspace.TempDirectory, "*", SearchOption.AllDirectories)

                for filePath in tempFiles do
                    let fileInfo = FileInfo(filePath)

                    if fileInfo.LastWriteTimeUtc < cutoffTime then
                        let sizeMB = float fileInfo.Length / (1024.0 * 1024.0)
                        File.Delete(filePath)
                        deletedCount <- deletedCount + 1
                        freedSpaceMB <- freedSpaceMB + sizeMB

            // 空ディレクトリの削除
            let emptyDirs =
                Directory.GetDirectories(workspace.TempDirectory, "*", SearchOption.AllDirectories)
                |> Array.filter (fun dir ->
                    Directory.GetFiles(dir).Length = 0 && Directory.GetDirectories(dir).Length = 0)

            for dir in emptyDirs do
                Directory.Delete(dir)
                deletedCount <- deletedCount + 1

            Logger.logInfo
                "WorkingDirectoryManager"
                $"クリーンアップ完了: {workspace.PaneId} - {deletedCount}個削除, {freedSpaceMB:F2}MB解放"

            Ok(deletedCount, freedSpaceMB)

        with ex ->
            Logger.logError "WorkingDirectoryManager" $"クリーンアップエラー: {workspace.PaneId}" ex
            Error $"クリーンアップエラー: {ex.Message}"

    // ワークスペース情報の保存
    let saveWorkspaceInfo (workspace: PaneWorkspace) =
        try
            let stateFile = Path.Combine(workspace.BaseDirectory, "workspace.json")

            let json =
                JsonSerializer.Serialize(workspace, JsonSerializerOptions(WriteIndented = true))

            File.WriteAllText(stateFile, json)

            Logger.logDebug "WorkingDirectoryManager" $"ワークスペース情報保存完了: {workspace.PaneId}"
            Ok()
        with ex ->
            Logger.logError "WorkingDirectoryManager" $"ワークスペース情報保存エラー: {workspace.PaneId}" ex
            Error $"保存エラー: {ex.Message}"

    // ワークスペース情報の読み込み
    let loadWorkspaceInfo (paneId: string) (config: DirectoryIsolationConfig) =
        try
            let baseDir = Path.Combine(config.SessionsBaseDir, paneId)
            let stateFile = Path.Combine(baseDir, "workspace.json")

            if File.Exists(stateFile) then
                let json = File.ReadAllText(stateFile)
                let workspace = JsonSerializer.Deserialize<PaneWorkspace>(json)
                Logger.logDebug "WorkingDirectoryManager" $"ワークスペース情報読み込み完了: {paneId}"
                Ok(Some workspace)
            else
                Logger.logDebug "WorkingDirectoryManager" $"ワークスペース情報ファイル未存在: {paneId}"
                Ok None
        with ex ->
            Logger.logError "WorkingDirectoryManager" $"ワークスペース情報読み込みエラー: {paneId}" ex
            Error $"読み込みエラー: {ex.Message}"

    // ペインワークスペースの初期化または復元
    let initializeOrRestoreWorkspace (config: DirectoryIsolationConfig) (paneId: string) =
        try
            match loadWorkspaceInfo paneId config with
            | Ok(Some existingWorkspace) ->
                // 既存ワークスペースが存在する場合はディレクトリの存在を確認
                if
                    Directory.Exists(existingWorkspace.BaseDirectory)
                    && Directory.Exists(existingWorkspace.WorkingDirectory)
                then
                    Logger.logInfo "WorkingDirectoryManager" $"既存ワークスペース復元: {paneId}"
                    Ok existingWorkspace
                else
                    // ディレクトリが存在しない場合は再作成
                    Logger.logWarn "WorkingDirectoryManager" $"ワークスペースディレクトリ消失、再作成: {paneId}"
                    createPaneWorkspace config paneId
            | Ok None ->
                // 新規ワークスペース作成
                Logger.logInfo "WorkingDirectoryManager" $"新規ワークスペース作成: {paneId}"
                createPaneWorkspace config paneId
            | Error e -> Error e
        with ex ->
            Logger.logError "WorkingDirectoryManager" $"ワークスペース初期化エラー: {paneId}" ex
            Error $"初期化エラー: {ex.Message}"

    // 全ペインのワークスペース状況取得
    let getAllWorkspaceStatus (config: DirectoryIsolationConfig) =
        try
            let paneIds = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "pm" ]
            let statusList = ResizeArray<PaneWorkspace * DiskUsageInfo>()

            for paneId in paneIds do
                match initializeOrRestoreWorkspace config paneId with
                | Ok workspace ->
                    match checkDiskUsage config workspace with
                    | Ok usageInfo -> statusList.Add((workspace, usageInfo))
                    | Error e -> Logger.logWarn "WorkingDirectoryManager" $"ペイン{paneId}の使用量チェック失敗: {e}"
                | Error e -> Logger.logWarn "WorkingDirectoryManager" $"ペイン{paneId}のワークスペース取得失敗: {e}"

            Logger.logInfo "WorkingDirectoryManager" $"全ワークスペース状況取得完了: {statusList.Count}個"
            Ok(statusList.ToArray())
        with ex ->
            Logger.logError "WorkingDirectoryManager" $"全ワークスペース状況取得エラー" ex
            Error $"状況取得エラー: {ex.Message}"

    // ワークスペースの完全削除
    let deleteWorkspace (paneId: string) (config: DirectoryIsolationConfig) =
        try
            let baseDir = Path.Combine(config.SessionsBaseDir, paneId)

            if Directory.Exists(baseDir) then
                Directory.Delete(baseDir, true)
                Logger.logInfo "WorkingDirectoryManager" $"ワークスペース削除完了: {paneId}"
                Ok()
            else
                Logger.logWarn "WorkingDirectoryManager" $"削除対象ワークスペース未存在: {paneId}"
                Ok()
        with ex ->
            Logger.logError "WorkingDirectoryManager" $"ワークスペース削除エラー: {paneId}" ex
            Error $"削除エラー: {ex.Message}"

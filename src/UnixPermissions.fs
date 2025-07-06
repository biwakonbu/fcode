module FCode.UnixPermissions

open System
open System.IO
open System.Runtime.InteropServices
open FCode.Logger

/// Unix権限設定ユーティリティ
module UnixPermissionHelper =

    [<DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)>]
    extern int chmod(string path, uint32 mode)

    [<DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)>]
    extern int access(string path, int mode)

    /// ファイル権限モード定数
    let S_IRUSR = 0o400u // owner read
    let S_IWUSR = 0o200u // owner write
    let S_IXUSR = 0o100u // owner execute
    let S_IRGRP = 0o040u // group read
    let S_IWGRP = 0o020u // group write
    let S_IXGRP = 0o010u // group execute
    let S_IROTH = 0o004u // other read
    let S_IWOTH = 0o002u // other write
    let S_IXOTH = 0o001u // other execute

    /// アクセスモード定数
    let F_OK = 0 // existence
    let R_OK = 4 // read permission
    let W_OK = 2 // write permission
    let X_OK = 1 // execute permission

    /// ディレクトリに安全な権限を設定 (755: owner全権限、group/other読み取り・実行のみ)
    let setSecureDirectoryPermissions (path: string) =
        try
            if String.IsNullOrEmpty(path) then
                logError "UnixPermissions" "pathがnullまたは空です"
                false
            elif Environment.OSVersion.Platform = PlatformID.Unix then
                let mode =
                    S_IRUSR ||| S_IWUSR ||| S_IXUSR ||| S_IRGRP ||| S_IXGRP ||| S_IROTH ||| S_IXOTH

                let result = chmod (path, mode)

                if result = 0 then
                    logInfo "UnixPermissions" (sprintf "ディレクトリ権限設定成功: %s (755)" path)
                    true
                else
                    logWarning
                        "UnixPermissions"
                        (sprintf "ディレクトリ権限設定失敗: %s (errno: %d)" path (Marshal.GetLastWin32Error()))

                    false
            else
                logInfo "UnixPermissions" "非Unix環境: 権限設定をスキップ"
                true
        with ex ->
            logError "UnixPermissions" (sprintf "ディレクトリ権限設定エラー: %s - %s" path ex.Message)
            false

    /// ファイルに安全な権限を設定 (600: owner読み書きのみ、group/otherアクセス不可)
    let setSecureFilePermissions (path: string) =
        try
            if String.IsNullOrEmpty(path) then
                logError "UnixPermissions" "pathがnullまたは空です"
                false
            elif Environment.OSVersion.Platform = PlatformID.Unix then
                let mode = S_IRUSR ||| S_IWUSR
                let result = chmod (path, mode)

                if result = 0 then
                    logInfo "UnixPermissions" (sprintf "ファイル権限設定成功: %s (600)" path)
                    true
                else
                    logWarning
                        "UnixPermissions"
                        (sprintf "ファイル権限設定失敗: %s (errno: %d)" path (Marshal.GetLastWin32Error()))

                    false
            else
                logInfo "UnixPermissions" "非Unix環境: 権限設定をスキップ"
                true
        with ex ->
            logError "UnixPermissions" (sprintf "ファイル権限設定エラー: %s - %s" path ex.Message)
            false

    /// パスの読み書き権限をチェック
    let checkReadWriteAccess (path: string) =
        try
            if String.IsNullOrEmpty(path) then
                logError "UnixPermissions" "pathがnullまたは空です"
                false
            elif Environment.OSVersion.Platform = PlatformID.Unix then
                let result = access (path, R_OK ||| W_OK)
                result = 0
            else
                // Windows環境では.NETの標準機能を使用
                try
                    File.GetAttributes(path) |> ignore
                    true
                with
                | :? UnauthorizedAccessException -> false
                | :? FileNotFoundException -> false
                | :? DirectoryNotFoundException -> false
        with ex ->
            logError "UnixPermissions" (sprintf "アクセス権限チェックエラー: %s - %s" path ex.Message)
            false

    /// セッションディレクトリ用の安全な権限設定
    let setSessionDirectoryPermissions (sessionDir: string) =
        try
            if not (Directory.Exists(sessionDir)) then
                Directory.CreateDirectory(sessionDir) |> ignore

            let success = setSecureDirectoryPermissions (sessionDir)

            if success then
                logInfo "UnixPermissions" (sprintf "セッションディレクトリ権限設定完了: %s" sessionDir)
            else
                logWarning "UnixPermissions" (sprintf "セッションディレクトリ権限設定部分失敗: %s" sessionDir)

            success
        with ex ->
            logError "UnixPermissions" (sprintf "セッションディレクトリ権限設定エラー: %s - %s" sessionDir ex.Message)
            false

    /// セッションファイル用の安全な権限設定
    let setSessionFilePermissions (filePath: string) =
        try
            if File.Exists(filePath) then
                let success = setSecureFilePermissions (filePath)

                if success then
                    logInfo "UnixPermissions" (sprintf "セッションファイル権限設定完了: %s" filePath)
                else
                    logWarning "UnixPermissions" (sprintf "セッションファイル権限設定部分失敗: %s" filePath)

                success
            else
                logWarning "UnixPermissions" (sprintf "セッションファイルが存在しません: %s" filePath)
                false
        with ex ->
            logError "UnixPermissions" (sprintf "セッションファイル権限設定エラー: %s - %s" filePath ex.Message)
            false

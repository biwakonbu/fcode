namespace FCode

open System
open System.IO
open System.Collections.Concurrent
open System.Threading
open System.Text.Json

/// ファイルロック競合回避機構モジュール
module FileLockManager =

    // 型定義
    type LockType =
        | ReadLock
        | WriteLock
        | ExclusiveLock

    type FileLock =
        { LockId: string
          FilePath: string
          LockType: LockType
          PaneId: string
          ProcessId: int
          AcquiredAt: DateTime
          ExpiresAt: DateTime option
          LastHeartbeat: DateTime }

    // シリアライゼーション用の型（JSON対応）
    type SerializableFileLock =
        { LockId: string
          FilePath: string
          LockType: string
          PaneId: string
          ProcessId: int
          AcquiredAt: DateTime
          ExpiresAt: DateTime option
          LastHeartbeat: DateTime }

    type LockResult =
        | LockAcquired of LockId: string
        | LockConflict of ConflictingPaneId: string * ConflictingLockType: LockType
        | LockTimeout
        | LockError of Reason: string

    type LockManagerConfig =
        { LocksDirectory: string // ~/.local/share/fcode/locks/
          DefaultTimeoutSeconds: int // 30秒
          HeartbeatIntervalSeconds: int // 5秒
          MaxLockAgeHours: int // 24時間
          CleanupIntervalMinutes: int } // 10分

    type LockConflictInfo =
        { RequestedFile: string
          RequestedType: string
          RequestingPane: string
          ConflictingLocks: FileLock array
          ConflictReason: string }

    // スレッドセーフなロック管理
    type private LockRegistry() =
        let locks = ConcurrentDictionary<string, FileLock>()
        let fileToLocks = ConcurrentDictionary<string, Set<string>>()

        member _.AddLock(lock: FileLock) =
            locks.[lock.LockId] <- lock

            fileToLocks.AddOrUpdate(
                lock.FilePath,
                Set.singleton lock.LockId,
                fun _ existing -> existing.Add(lock.LockId)
            )
            |> ignore

        member _.RemoveLock(lockId: string) =
            match locks.TryRemove(lockId) with
            | true, lock ->
                fileToLocks.AddOrUpdate(lock.FilePath, Set.empty<string>, fun _ existing -> existing.Remove(lockId))
                |> ignore

                Some lock
            | false, _ -> None

        member _.GetLock(lockId: string) =
            match locks.TryGetValue(lockId) with
            | true, lock -> Some lock
            | false, _ -> None

        member _.GetLocksForFile(filePath: string) =
            match fileToLocks.TryGetValue(filePath) with
            | true, lockIds ->
                lockIds
                |> Set.toArray
                |> Array.choose (fun id ->
                    match locks.TryGetValue(id) with
                    | true, lock -> Some lock
                    | false, _ -> None)
            | false, _ -> Array.empty

        member _.GetAllLocks() = locks.Values |> Seq.toArray

        member _.UpdateHeartbeat(lockId: string) =
            match locks.TryGetValue(lockId) with
            | true, lock ->
                let updatedLock =
                    { lock with
                        LastHeartbeat = DateTime.UtcNow }

                locks.[lockId] <- updatedLock
                true
            | false, _ -> false

    // グローバルロックレジストリ
    let private lockRegistry = LockRegistry()

    // LockType変換関数
    let private lockTypeToString (lockType: LockType) =
        match lockType with
        | ReadLock -> "ReadLock"
        | WriteLock -> "WriteLock"
        | ExclusiveLock -> "ExclusiveLock"

    let private stringToLockType (str: string) =
        match str with
        | "ReadLock" -> ReadLock
        | "WriteLock" -> WriteLock
        | "ExclusiveLock" -> ExclusiveLock
        | _ -> failwith $"不正なLockType: {str}"

    // FileLock <-> SerializableFileLock 変換関数
    let private toSerializable (lock: FileLock) : SerializableFileLock =
        { LockId = lock.LockId
          FilePath = lock.FilePath
          LockType = lockTypeToString lock.LockType
          PaneId = lock.PaneId
          ProcessId = lock.ProcessId
          AcquiredAt = lock.AcquiredAt
          ExpiresAt = lock.ExpiresAt
          LastHeartbeat = lock.LastHeartbeat }

    let private fromSerializable (serializable: SerializableFileLock) : FileLock =
        { LockId = serializable.LockId
          FilePath = serializable.FilePath
          LockType = stringToLockType serializable.LockType
          PaneId = serializable.PaneId
          ProcessId = serializable.ProcessId
          AcquiredAt = serializable.AcquiredAt
          ExpiresAt = serializable.ExpiresAt
          LastHeartbeat = serializable.LastHeartbeat }

    // JsonSerializerOptions
    let private jsonOptions = JsonSerializerOptions(WriteIndented = true)

    // デフォルト設定
    let defaultConfig =
        { LocksDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fcode", "locks")
          DefaultTimeoutSeconds = 30
          HeartbeatIntervalSeconds = 5
          MaxLockAgeHours = 24
          CleanupIntervalMinutes = 10 }

    // ロックID生成
    let private generateLockId (paneId: string) (filePath: string) =
        let timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss")
        let hash = Math.Abs(filePath.GetHashCode()).ToString("X8")
        $"lock-{paneId}-{hash}-{timestamp}"

    // ロック情報ファイル保存（内部関数）
    let private saveLockToFile (config: LockManagerConfig) (lock: FileLock) =
        try
            if not (Directory.Exists(config.LocksDirectory)) then
                Directory.CreateDirectory(config.LocksDirectory) |> ignore

            let lockFile = Path.Combine(config.LocksDirectory, $"{lock.LockId}.json")

            let serializableLock = toSerializable lock
            let json = JsonSerializer.Serialize(serializableLock, jsonOptions)

            File.WriteAllText(lockFile, json)

            Ok()
        with ex ->
            Logger.logException "FileLockManager" $"ロックファイル保存エラー: {lock.LockId}" ex
            Error $"保存エラー: {ex.Message}"

    // ロックファイル削除（内部関数）
    let private deleteLockFile (config: LockManagerConfig) (lockId: string) =
        try
            let lockFile = Path.Combine(config.LocksDirectory, $"{lockId}.json")

            if File.Exists(lockFile) then
                File.Delete(lockFile)

            Ok()
        with ex ->
            Logger.logException "FileLockManager" $"ロックファイル削除エラー: {lockId}" ex
            Error $"削除エラー: {ex.Message}"

    // ロック競合チェック
    let private checkLockConflict (filePath: string) (requestType: LockType) (requestingPane: string) =
        let existingLocks = lockRegistry.GetLocksForFile(filePath)
        let conflicts = ResizeArray<FileLock>()

        for existingLock in existingLocks do
            // 期限切れロックは除外
            match existingLock.ExpiresAt with
            | Some expires when expires < DateTime.UtcNow -> ()
            | _ ->
                let hasConflict =
                    match requestType, existingLock.LockType with
                    | ReadLock, ReadLock -> false // Read-Read: 競合なし
                    | ReadLock, WriteLock -> true // Read-Write: 競合
                    | ReadLock, ExclusiveLock -> true // Read-Exclusive: 競合
                    | WriteLock, ReadLock -> true // Write-Read: 競合
                    | WriteLock, WriteLock -> true // Write-Write: 競合
                    | WriteLock, ExclusiveLock -> true // Write-Exclusive: 競合
                    | ExclusiveLock, _ -> true // Exclusive-Any: 競合

                if hasConflict && existingLock.PaneId <> requestingPane then
                    conflicts.Add(existingLock)

        conflicts.ToArray()

    // ファイルロック取得
    let acquireFileLock
        (config: LockManagerConfig)
        (filePath: string)
        (lockType: LockType)
        (paneId: string)
        (timeoutSeconds: int option)
        =
        try
            let absolutePath = Path.GetFullPath(filePath)
            let processId = System.Diagnostics.Process.GetCurrentProcess().Id
            let timeout = timeoutSeconds |> Option.defaultValue config.DefaultTimeoutSeconds
            let lockId = generateLockId paneId absolutePath

            // 競合チェック
            let conflicts = checkLockConflict absolutePath lockType paneId

            if conflicts.Length > 0 then
                let conflictInfo =
                    { RequestedFile = absolutePath
                      RequestedType = lockTypeToString lockType
                      RequestingPane = paneId
                      ConflictingLocks = conflicts
                      ConflictReason = $"{conflicts.Length}個の競合ロックが存在" }

                Logger.logWarning "FileLockManager" $"ロック競合検出: {paneId} -> {absolutePath} ({lockTypeToString lockType})"
                LockConflict(conflicts.[0].PaneId, conflicts.[0].LockType)
            else
                let lock: FileLock =
                    { LockId = lockId
                      FilePath = absolutePath
                      LockType = lockType
                      PaneId = paneId
                      ProcessId = processId
                      AcquiredAt = DateTime.UtcNow
                      ExpiresAt = Some(DateTime.UtcNow.AddSeconds(float timeout))
                      LastHeartbeat = DateTime.UtcNow }

                lockRegistry.AddLock(lock)

                // ロック情報をファイルに保存
                saveLockToFile config lock |> ignore

                Logger.logInfo
                    "FileLockManager"
                    $"ロック取得成功: {paneId} -> {absolutePath} ({lockTypeToString lockType}) [ID: {lockId}]"

                LockAcquired lockId

        with ex ->
            Logger.logException "FileLockManager" $"ロック取得エラー: {paneId} -> {filePath}" ex
            LockError $"ロック取得例外: {ex.Message}"

    // ファイルロック解放
    let releaseFileLock (config: LockManagerConfig) (lockId: string) =
        try
            match lockRegistry.RemoveLock(lockId) with
            | Some lock ->
                // ロックファイル削除
                deleteLockFile config lockId |> ignore

                Logger.logInfo "FileLockManager" $"ロック解放成功: {lock.PaneId} -> {lock.FilePath} [ID: {lockId}]"
                Ok()
            | None ->
                Logger.logWarning "FileLockManager" $"解放対象ロック未存在: {lockId}"
                Error $"ロック未存在: {lockId}"
        with ex ->
            Logger.logException "FileLockManager" $"ロック解放エラー: {lockId}" ex
            Error $"解放例外: {ex.Message}"

    // ハートビート更新
    let updateLockHeartbeat (lockId: string) =
        try
            if lockRegistry.UpdateHeartbeat(lockId) then
                Logger.logDebug "FileLockManager" $"ハートビート更新: {lockId}"
                Ok()
            else
                Logger.logWarning "FileLockManager" $"ハートビート更新失敗 - ロック未存在: {lockId}"
                Error $"ロック未存在: {lockId}"
        with ex ->
            Logger.logException "FileLockManager" $"ハートビート更新エラー: {lockId}" ex
            Error $"ハートビート例外: {ex.Message}"

    // ロック期間延長
    let extendLockDuration (lockId: string) (additionalSeconds: int) =
        try
            match lockRegistry.GetLock(lockId) with
            | Some lock ->
                let newExpiry =
                    match lock.ExpiresAt with
                    | Some currentExpiry -> Some(currentExpiry.AddSeconds(float additionalSeconds))
                    | None -> Some(DateTime.UtcNow.AddSeconds(float additionalSeconds))

                let extendedLock = { lock with ExpiresAt = newExpiry }
                lockRegistry.AddLock(extendedLock)

                Logger.logInfo "FileLockManager" $"ロック期間延長: {lockId} (+{additionalSeconds}秒)"
                Ok()
            | None ->
                Logger.logWarning "FileLockManager" $"延長対象ロック未存在: {lockId}"
                Error $"ロック未存在: {lockId}"
        with ex ->
            Logger.logException "FileLockManager" $"ロック延長エラー: {lockId}" ex
            Error $"延長例外: {ex.Message}"

    // 期限切れロックのクリーンアップ
    let cleanupExpiredLocks (config: LockManagerConfig) =
        try
            let allLocks = lockRegistry.GetAllLocks()
            let now = DateTime.UtcNow
            let mutable cleanedCount = 0

            for lock in allLocks do
                let shouldCleanup =
                    match lock.ExpiresAt with
                    | Some expires when expires < now -> true
                    | None when (now - lock.LastHeartbeat).TotalHours > float config.MaxLockAgeHours -> true
                    | _ -> false

                if shouldCleanup then
                    lockRegistry.RemoveLock(lock.LockId) |> ignore
                    deleteLockFile config lock.LockId |> ignore
                    cleanedCount <- cleanedCount + 1
                    Logger.logDebug "FileLockManager" $"期限切れロック削除: {lock.LockId} ({lock.PaneId})"

            Logger.logInfo "FileLockManager" $"期限切れロッククリーンアップ完了: {cleanedCount}個削除"
            Ok cleanedCount
        with ex ->
            Logger.logException "FileLockManager" $"クリーンアップエラー" ex
            Error $"クリーンアップ例外: {ex.Message}"

    // ペイン別ロック一覧取得
    let getLocksForPane (paneId: string) =
        try
            let allLocks = lockRegistry.GetAllLocks()
            let paneLocks = allLocks |> Array.filter (fun lock -> lock.PaneId = paneId)

            Logger.logDebug "FileLockManager" $"ペインロック一覧取得: {paneId} - {paneLocks.Length}個"
            Ok paneLocks
        with ex ->
            Logger.logException "FileLockManager" $"ペインロック取得エラー: {paneId}" ex
            Error $"取得例外: {ex.Message}"

    // ファイル別ロック一覧取得
    let getLocksForFile (filePath: string) =
        try
            let absolutePath = Path.GetFullPath(filePath)
            let fileLocks = lockRegistry.GetLocksForFile(absolutePath)

            Logger.logDebug "FileLockManager" $"ファイルロック一覧取得: {absolutePath} - {fileLocks.Length}個"
            Ok fileLocks
        with ex ->
            Logger.logException "FileLockManager" $"ファイルロック取得エラー: {filePath}" ex
            Error $"取得例外: {ex.Message}"

    // ロック統計情報取得
    let getLockStatistics () =
        try
            let allLocks = lockRegistry.GetAllLocks()
            let now = DateTime.UtcNow

            let stats =
                {| TotalLocks = allLocks.Length
                   ReadLocks = allLocks |> Array.filter (fun l -> l.LockType = ReadLock) |> Array.length
                   WriteLocks = allLocks |> Array.filter (fun l -> l.LockType = WriteLock) |> Array.length
                   ExclusiveLocks = allLocks |> Array.filter (fun l -> l.LockType = ExclusiveLock) |> Array.length
                   ExpiredLocks =
                    allLocks
                    |> Array.filter (fun l ->
                        match l.ExpiresAt with
                        | Some expires -> expires < now
                        | None -> false)
                    |> Array.length
                   PaneBreakdown =
                    allLocks
                    |> Array.groupBy (fun l -> l.PaneId)
                    |> Array.map (fun (pane, locks) -> (pane, locks.Length))
                    |> Map.ofArray |}

            Logger.logDebug "FileLockManager" $"ロック統計取得完了: {stats.TotalLocks}個のロック"
            Ok stats
        with ex ->
            Logger.logException "FileLockManager" $"統計取得エラー" ex
            Error $"統計例外: {ex.Message}"

    // 永続化されたロック復元
    let restorePersistedLocks (config: LockManagerConfig) =
        try
            if not (Directory.Exists(config.LocksDirectory)) then
                Logger.logInfo "FileLockManager" "ロックディレクトリ未存在、復元スキップ"
                Ok 0
            else
                let lockFiles = Directory.GetFiles(config.LocksDirectory, "*.json")
                let mutable restoredCount = 0

                for lockFile in lockFiles do
                    try
                        let json = File.ReadAllText(lockFile)

                        let serializableLock =
                            JsonSerializer.Deserialize<SerializableFileLock>(json, jsonOptions)

                        let lock = fromSerializable serializableLock

                        // 期限切れでない場合のみ復元
                        let isValid =
                            match lock.ExpiresAt with
                            | Some expires -> expires > DateTime.UtcNow
                            | None -> (DateTime.UtcNow - lock.LastHeartbeat).TotalHours < float config.MaxLockAgeHours

                        if isValid then
                            lockRegistry.AddLock(lock)
                            restoredCount <- restoredCount + 1
                        else
                            File.Delete(lockFile)
                    with ex ->
                        Logger.logWarning "FileLockManager" $"ロックファイル復元失敗: {lockFile} - {ex.Message}"
                        File.Delete(lockFile) // 破損ファイル削除

                Logger.logInfo "FileLockManager" $"永続ロック復元完了: {restoredCount}個復元"
                Ok restoredCount
        with ex ->
            Logger.logException "FileLockManager" $"ロック復元エラー" ex
            Error $"復元例外: {ex.Message}"

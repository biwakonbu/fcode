namespace FCode.SessionManagement

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open System.Threading
open System.Collections.Concurrent
open System.Diagnostics
open FCode.SessionPersistenceManager
open FCode.DetachAttachManager
open FCode

/// バックグラウンドタスク管理機能
module BackgroundTaskManager =

    /// バックグラウンドタスクの状態
    type BackgroundTaskStatus =
        | Pending
        | Running
        | Paused
        | Completed
        | Failed
        | Cancelled

    /// バックグラウンドタスクの優先度
    type TaskPriority =
        | Low = 1
        | Normal = 2
        | High = 3
        | Critical = 4

    /// バックグラウンドタスクの定義
    type BackgroundTask =
        { TaskId: string
          SessionId: string
          PaneId: string
          TaskType: string
          Description: string
          Status: BackgroundTaskStatus
          Priority: TaskPriority
          CreatedAt: DateTime
          StartedAt: DateTime option
          CompletedAt: DateTime option
          LastHeartbeat: DateTime option
          Progress: float
          ProcessId: int option
          Command: string
          Arguments: string list
          WorkingDirectory: string
          Environment: Map<string, string>
          MaxRuntime: TimeSpan option
          RetryCount: int
          MaxRetries: int
          DependsOn: string list
          Outputs: string list
          Errors: string list }

    /// タスク実行結果
    type TaskExecutionResult =
        | Success of Outputs: string list
        | Failure of ErrorMessage: string * Outputs: string list
        | Timeout of PartialOutputs: string list
        | Cancelled

    /// バックグラウンドタスク管理設定
    type BackgroundTaskConfig =
        { MaxConcurrentTasks: int
          DefaultMaxRuntime: TimeSpan
          HeartbeatInterval: TimeSpan
          TaskCleanupInterval: TimeSpan
          MaxTaskHistory: int
          StorageDirectory: string }

    /// デフォルト設定
    let defaultBackgroundTaskConfig =
        { MaxConcurrentTasks = 10
          DefaultMaxRuntime = TimeSpan.FromHours(2.0)
          HeartbeatInterval = TimeSpan.FromSeconds(30.0)
          TaskCleanupInterval = TimeSpan.FromMinutes(5.0)
          MaxTaskHistory = 1000
          StorageDirectory =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "fcode",
                "background-tasks"
            ) }

    /// 共通のJSON設定
    let private jsonOptions = JsonSerializerOptions(WriteIndented = true)

    /// アクティブタスクの管理
    let private activeTasks = ConcurrentDictionary<string, BackgroundTask>()

    let private taskCancellations =
        ConcurrentDictionary<string, CancellationTokenSource>()

    let mutable private taskSemaphore =
        new SemaphoreSlim(defaultBackgroundTaskConfig.MaxConcurrentTasks)

    /// タスクIDの生成
    let generateTaskId (sessionId: string) (paneId: string) =
        let timestamp = DateTime.Now.ToString("yyyyMMddHHmmss")
        let guidPart = Guid.NewGuid().ToString("N").Substring(0, 8)
        $"{sessionId}-{paneId}-{timestamp}-{guidPart}"

    /// タスクストレージディレクトリの初期化
    let initializeTaskStorage (config: BackgroundTaskConfig) =
        try
            Directory.CreateDirectory(config.StorageDirectory) |> ignore

            Directory.CreateDirectory(Path.Combine(config.StorageDirectory, "active"))
            |> ignore

            Directory.CreateDirectory(Path.Combine(config.StorageDirectory, "completed"))
            |> ignore

            Directory.CreateDirectory(Path.Combine(config.StorageDirectory, "failed"))
            |> ignore

            Logger.logInfo "BackgroundTaskManager" "タスクストレージ初期化完了"
            true
        with ex ->
            Logger.logError "BackgroundTaskManager" $"タスクストレージ初期化失敗: {ex.Message}"
            false

    /// タスクの永続化
    let persistTask (config: BackgroundTaskConfig) (task: BackgroundTask) =
        async {
            try
                let subDir =
                    match task.Status with
                    | Running
                    | Pending
                    | Paused -> "active"
                    | Completed -> "completed"
                    | Failed
                    | BackgroundTaskStatus.Cancelled -> "failed"

                let taskFile = Path.Combine(config.StorageDirectory, subDir, $"{task.TaskId}.json")

                let json = JsonSerializer.Serialize(task, jsonOptions)

                File.WriteAllText(taskFile, json)

                Logger.logDebug "BackgroundTaskManager" $"タスク永続化完了: {task.TaskId}"
                return true

            with ex ->
                Logger.logError "BackgroundTaskManager" $"タスク永続化失敗 ({task.TaskId}): {ex.Message}"
                return false
        }

    /// タスクの読み込み
    let loadTask (config: BackgroundTaskConfig) (taskId: string) =
        async {
            try
                let taskFiles =
                    [ Path.Combine(config.StorageDirectory, "active", $"{taskId}.json")
                      Path.Combine(config.StorageDirectory, "completed", $"{taskId}.json")
                      Path.Combine(config.StorageDirectory, "failed", $"{taskId}.json") ]

                let existingFile = taskFiles |> List.tryFind File.Exists

                match existingFile with
                | Some file ->
                    let json = File.ReadAllText(file)
                    let task = JsonSerializer.Deserialize<BackgroundTask>(json)
                    return Some task
                | None -> return None

            with ex ->
                Logger.logError "BackgroundTaskManager" $"タスク読み込み失敗 ({taskId}): {ex.Message}"
                return None
        }

    /// 全アクティブタスクの読み込み
    let loadActiveTasks (config: BackgroundTaskConfig) =
        async {
            try
                let activeDir = Path.Combine(config.StorageDirectory, "active")

                if Directory.Exists(activeDir) then
                    let taskFiles = Directory.GetFiles(activeDir, "*.json")

                    let! tasks =
                        taskFiles
                        |> Array.map (fun file ->
                            async {
                                try
                                    let json = File.ReadAllText(file)
                                    let task = JsonSerializer.Deserialize<BackgroundTask>(json)
                                    return Some task
                                with ex ->
                                    Logger.logWarning "BackgroundTaskManager" $"タスクファイル読み込み失敗: {file} - {ex.Message}"
                                    return None
                            })
                        |> Async.Parallel

                    let validTasks = tasks |> Array.choose id |> Array.toList

                    // アクティブタスクキャッシュに復元
                    for task in validTasks do
                        activeTasks.TryAdd(task.TaskId, task) |> ignore

                    Logger.logInfo "BackgroundTaskManager" $"アクティブタスク読み込み完了: {validTasks.Length}件"
                    return validTasks
                else
                    return []

            with ex ->
                Logger.logError "BackgroundTaskManager" $"アクティブタスク読み込み失敗: {ex.Message}"
                return []
        }

    /// タスクの作成
    let createTask
        (sessionId: string)
        (paneId: string)
        (taskType: string)
        (description: string)
        (command: string)
        (arguments: string list)
        (workingDir: string)
        (env: Map<string, string>)
        (priority: TaskPriority)
        =
        let taskId = generateTaskId sessionId paneId

        { TaskId = taskId
          SessionId = sessionId
          PaneId = paneId
          TaskType = taskType
          Description = description
          Status = Pending
          Priority = priority
          CreatedAt = DateTime.Now
          StartedAt = None
          CompletedAt = None
          LastHeartbeat = None
          Progress = 0.0
          ProcessId = None
          Command = command
          Arguments = arguments
          WorkingDirectory = workingDir
          Environment = env
          MaxRuntime = None
          RetryCount = 0
          MaxRetries = 3
          DependsOn = []
          Outputs = []
          Errors = [] }

    /// プロセス設定の初期化
    let private createProcessStartInfo (task: BackgroundTask) =
        let startInfo = ProcessStartInfo()
        startInfo.FileName <- task.Command
        startInfo.Arguments <- String.Join(" ", task.Arguments)
        startInfo.WorkingDirectory <- task.WorkingDirectory
        startInfo.UseShellExecute <- false
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        startInfo.CreateNoWindow <- true

        for kvp in task.Environment do
            startInfo.Environment.[kvp.Key] <- kvp.Value

        startInfo

    /// プロセス出力ハンドラーの設定
    let private setupOutputHandlers
        (proc: Process)
        (task: BackgroundTask)
        (outputs: ResizeArray<string>)
        (errors: ResizeArray<string>)
        =
        proc.OutputDataReceived.Add(fun args ->
            if not (String.IsNullOrEmpty(args.Data)) then
                outputs.Add(args.Data)
                Logger.logDebug "BackgroundTaskManager" $"タスク出力 ({task.TaskId}): {args.Data}")

        proc.ErrorDataReceived.Add(fun args ->
            if not (String.IsNullOrEmpty(args.Data)) then
                errors.Add(args.Data)
                Logger.logWarning "BackgroundTaskManager" $"タスクエラー出力 ({task.TaskId}): {args.Data}")

    /// プロセス実行結果の処理
    let private processExecutionResult
        (waitResult: TaskExecutionResult)
        (runningTask: BackgroundTask)
        (errors: ResizeArray<string>)
        =
        match waitResult with
        | TaskExecutionResult.Success outputs ->
            { runningTask with
                Status = Completed
                CompletedAt = Some DateTime.Now
                Progress = 1.0
                Outputs = outputs
                Errors = errors.ToArray() |> Array.toList }
        | TaskExecutionResult.Failure(errorMsg, outputs) ->
            { runningTask with
                Status = Failed
                CompletedAt = Some DateTime.Now
                Outputs = outputs
                Errors = errorMsg :: (errors.ToArray() |> Array.toList) }
        | _ ->
            { runningTask with
                Status = Failed
                CompletedAt = Some DateTime.Now }

    /// プロセス実行とモニタリング
    let private executeProcess (config: BackgroundTaskConfig) (task: BackgroundTask) (startedTask: BackgroundTask) =
        async {
            let cancellationSource = new CancellationTokenSource()
            let maxRuntime = defaultArg task.MaxRuntime config.DefaultMaxRuntime
            cancellationSource.CancelAfter(maxRuntime)
            taskCancellations.TryAdd(task.TaskId, cancellationSource) |> ignore

            use proc = new Process()
            proc.StartInfo <- createProcessStartInfo task
            let outputs = ResizeArray<string>()
            let errors = ResizeArray<string>()

            setupOutputHandlers proc task outputs errors

            if proc.Start() then
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()

                let runningTask =
                    { startedTask with
                        ProcessId = Some proc.Id }

                activeTasks.TryUpdate(task.TaskId, runningTask, startedTask) |> ignore
                let! _ = persistTask config runningTask

                let! waitResult =
                    async {
                        try
                            proc.WaitForExit()
                            return TaskExecutionResult.Success(outputs.ToArray() |> Array.toList)
                        with ex ->
                            return TaskExecutionResult.Failure(ex.Message, outputs.ToArray() |> Array.toList)
                    }

                let finalTask = processExecutionResult waitResult runningTask errors
                activeTasks.TryUpdate(task.TaskId, finalTask, runningTask) |> ignore
                let! _ = persistTask config finalTask
                taskCancellations.TryRemove(task.TaskId) |> ignore
                Logger.logInfo "BackgroundTaskManager" $"タスク実行完了: {task.TaskId} - {finalTask.Status}"
                return waitResult
            else
                return TaskExecutionResult.Failure("プロセス開始失敗", [])
        }

    /// タスクの実行
    let executeTask (config: BackgroundTaskConfig) (task: BackgroundTask) =
        async {
            try
                Logger.logInfo "BackgroundTaskManager" $"タスク実行開始: {task.TaskId} - {task.Description}"

                let startedTask =
                    { task with
                        Status = Running
                        StartedAt = Some DateTime.Now }

                activeTasks.TryUpdate(task.TaskId, startedTask, task) |> ignore
                let! _ = persistTask config startedTask
                return! executeProcess config task startedTask

            with ex ->
                Logger.logError "BackgroundTaskManager" $"タスク実行エラー ({task.TaskId}): {ex.Message}"

                let errorTask =
                    { task with
                        Status = Failed
                        CompletedAt = Some DateTime.Now
                        Errors = [ ex.Message ] }

                activeTasks.TryUpdate(task.TaskId, errorTask, task) |> ignore
                let! _ = persistTask config errorTask
                return TaskExecutionResult.Failure(ex.Message, [])
        }

    /// タスクのスケジューリング
    let scheduleTask (config: BackgroundTaskConfig) (task: BackgroundTask) =
        async {
            try
                // セマフォを使用して同時実行数を制限
                do! taskSemaphore.WaitAsync() |> Async.AwaitTask

                // アクティブタスクに追加
                activeTasks.TryAdd(task.TaskId, task) |> ignore
                let! _ = persistTask config task

                // 依存関係のチェック
                let dependenciesResolved =
                    task.DependsOn
                    |> List.forall (fun depId ->
                        match activeTasks.TryGetValue(depId) with
                        | true, depTask -> depTask.Status = Completed
                        | false, _ -> true) // 依存タスクが見つからない場合は解決済みとみなす

                if dependenciesResolved then
                    // バックグラウンドでタスクを実行
                    Async.Start(
                        async {
                            try
                                let! result = executeTask config task
                                Logger.logDebug "BackgroundTaskManager" $"バックグラウンドタスク完了: {task.TaskId}"
                            finally
                                taskSemaphore.Release() |> ignore
                        }
                    )

                    Logger.logInfo "BackgroundTaskManager" $"タスクスケジューリング完了: {task.TaskId}"
                    return true
                else
                    taskSemaphore.Release() |> ignore
                    Logger.logWarning "BackgroundTaskManager" $"タスク依存関係未解決: {task.TaskId}"
                    return false

            with ex ->
                taskSemaphore.Release() |> ignore
                Logger.logError "BackgroundTaskManager" $"タスクスケジューリング失敗 ({task.TaskId}): {ex.Message}"
                return false
        }

    /// タスクのキャンセル
    let cancelTask (taskId: string) =
        async {
            try
                match taskCancellations.TryGetValue(taskId) with
                | true, cancellationSource ->
                    cancellationSource.Cancel()
                    taskCancellations.TryRemove(taskId) |> ignore

                    match activeTasks.TryGetValue(taskId) with
                    | true, task ->
                        let cancelledTask =
                            { task with
                                Status = BackgroundTaskStatus.Cancelled
                                CompletedAt = Some DateTime.Now }

                        activeTasks.TryUpdate(taskId, cancelledTask, task) |> ignore
                        Logger.logInfo "BackgroundTaskManager" $"タスクキャンセル完了: {taskId}"
                        return true
                    | false, _ ->
                        Logger.logWarning "BackgroundTaskManager" $"キャンセル対象タスクが見つかりません: {taskId}"
                        return false
                | false, _ ->
                    Logger.logWarning "BackgroundTaskManager" $"キャンセル対象タスクが見つかりません: {taskId}"
                    return false

            with ex ->
                Logger.logError "BackgroundTaskManager" $"タスクキャンセル失敗 ({taskId}): {ex.Message}"
                return false
        }

    /// アクティブタスク一覧の取得
    let getActiveTasks (sessionId: string option) =
        let allTasks = activeTasks.Values |> Seq.toList

        match sessionId with
        | Some sid -> allTasks |> List.filter (fun t -> t.SessionId = sid)
        | None -> allTasks

    /// タスクの状態更新
    let updateTaskProgress (taskId: string) (progress: float) (status: BackgroundTaskStatus option) =
        async {
            try
                match activeTasks.TryGetValue(taskId) with
                | true, task ->
                    let updatedStatus = defaultArg status task.Status

                    let updatedTask =
                        { task with
                            Progress = progress
                            Status = updatedStatus
                            LastHeartbeat = Some DateTime.Now }

                    activeTasks.TryUpdate(taskId, updatedTask, task) |> ignore
                    Logger.logDebug "BackgroundTaskManager" $"タスク進捗更新: {taskId} - {progress * 100.0}%%"
                    return true
                | false, _ ->
                    Logger.logWarning "BackgroundTaskManager" $"進捗更新対象タスクが見つかりません: {taskId}"
                    return false

            with ex ->
                Logger.logError "BackgroundTaskManager" $"タスク進捗更新失敗 ({taskId}): {ex.Message}"
                return false
        }

    /// 完了したタスクのクリーンアップ
    let cleanupCompletedTasks (config: BackgroundTaskConfig) =
        async {
            try
                let completedTasks =
                    activeTasks.Values
                    |> Seq.filter (fun t ->
                        t.Status = Completed
                        || t.Status = Failed
                        || t.Status = BackgroundTaskStatus.Cancelled)
                    |> Seq.toList

                let mutable cleanedCount = 0

                for task in completedTasks do
                    match activeTasks.TryRemove(task.TaskId) with
                    | true, _ ->
                        cleanedCount <- cleanedCount + 1
                        Logger.logDebug "BackgroundTaskManager" $"完了タスククリーンアップ: {task.TaskId}"
                    | false, _ -> ()

                Logger.logInfo "BackgroundTaskManager" $"完了タスククリーンアップ完了: {cleanedCount}件"
                return cleanedCount

            with ex ->
                Logger.logError "BackgroundTaskManager" $"完了タスククリーンアップ失敗: {ex.Message}"
                return 0
        }

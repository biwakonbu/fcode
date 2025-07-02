module FCode.Collaboration.TaskDependencyGraph

open System
open System.Collections.Concurrent
open System.Collections.Generic
open FCode.Logger
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.ITaskDependencyGraph

/// タスク依存関係管理実装
type TaskDependencyGraph(config: CollaborationConfig) =
    let tasks = ConcurrentDictionary<string, TaskInfo>()
    let dependencies = ConcurrentDictionary<string, TaskDependency>()
    let taskChangedEvent = Event<TaskInfo>()
    let lockObj = obj ()
    let mutable disposed = false

    /// 入力検証
    let validateTaskId taskId =
        if String.IsNullOrWhiteSpace(taskId) then
            Result.Error(InvalidInput "TaskId cannot be null or empty")
        else
            Result.Ok taskId

    /// 循環依存検出（深度優先探索）
    let detectCircularDependency fromTask toTask =
        let rec dfs visited current path =
            if current = toTask then
                // 目標に到達した場合、循環が確認された
                Some(List.rev (current :: path))
            elif Set.contains current visited then
                // 既に訪問済みの場合、この経路では循環なし
                None
            else
                let newVisited = Set.add current visited
                let newPath = current :: path

                match dependencies.TryGetValue(current) with
                | true, dep ->
                    // 各依存関係を再帰的に探索
                    dep.DependsOn |> List.tryPick (fun depTask -> dfs newVisited depTask newPath)
                | false, _ -> None

        dfs Set.empty fromTask []

    /// タスク変更イベント
    [<CLIEvent>]
    member _.TaskChanged = taskChangedEvent.Publish

    /// タスクを追加
    member _.AddTask(task: TaskInfo) =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "TaskDependencyGraph has been disposed")
                else
                    match validateTaskId task.TaskId with
                    | Result.Error e -> Result.Error e
                    | Result.Ok validId ->
                        let updatedTask =
                            { task with
                                TaskId = validId
                                UpdatedAt = DateTime.UtcNow }

                        tasks.AddOrUpdate(validId, updatedTask, fun _ _ -> updatedTask) |> ignore

                        // 依存関係エントリを初期化
                        if not (dependencies.ContainsKey(validId)) then
                            dependencies.[validId] <-
                                { TaskId = validId
                                  DependsOn = []
                                  Dependents = [] }

                        taskChangedEvent.Trigger(updatedTask)

                        logInfo "TaskDependencyGraph"
                        <| sprintf "Task added: %s (%s)" validId updatedTask.Title

                        Result.Ok())
        with ex ->
            logError "TaskDependencyGraph" <| sprintf "Error adding task: %s" ex.Message
            Result.Error(SystemError ex.Message)

    /// タスクを取得
    member _.GetTask(taskId: string) =
        try
            match validateTaskId taskId with
            | Result.Error e -> Result.Error e
            | Result.Ok validId ->
                match tasks.TryGetValue(validId) with
                | true, task -> Result.Ok(Some task)
                | false, _ -> Result.Ok None
        with ex ->
            logError "TaskDependencyGraph" <| sprintf "Error getting task: %s" ex.Message
            Result.Error(SystemError ex.Message)

    /// 全タスクを取得
    member _.GetAllTasks() =
        try
            if disposed then
                Result.Error(SystemError "TaskDependencyGraph has been disposed")
            else
                let tasksList = tasks.Values |> Seq.toList
                Result.Ok tasksList
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error getting all tasks: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 依存関係を追加（循環依存検出含む）
    member _.AddDependency(taskId: string, dependsOnTaskId: string) =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "TaskDependencyGraph has been disposed")
                else
                    match validateTaskId taskId, validateTaskId dependsOnTaskId with
                    | Result.Error e, _
                    | _, Result.Error e -> Result.Error e
                    | Result.Ok validTaskId, Result.Ok validDependsOnId ->
                        if validTaskId = validDependsOnId then
                            Result.Error(CircularDependency [ validTaskId ])
                        else
                            // 循環依存チェック：validDependsOnIdからvalidTaskIdに到達できるかチェック
                            match detectCircularDependency validDependsOnId validTaskId with
                            | Some cycle ->
                                let cycleStr = String.Join(" -> ", cycle)

                                logError "TaskDependencyGraph"
                                <| sprintf "Circular dependency detected: %s" cycleStr

                                Result.Error(CircularDependency cycle)
                            | None ->
                                // 依存関係を追加
                                let updateDependency taskId f =
                                    match dependencies.TryGetValue(taskId) with
                                    | true, dep -> dependencies.[taskId] <- f dep
                                    | false, _ ->
                                        dependencies.[taskId] <-
                                            f
                                                { TaskId = taskId
                                                  DependsOn = []
                                                  Dependents = [] }

                                updateDependency validTaskId (fun dep ->
                                    { dep with
                                        DependsOn = validDependsOnId :: dep.DependsOn |> List.distinct })

                                updateDependency validDependsOnId (fun dep ->
                                    { dep with
                                        Dependents = validTaskId :: dep.Dependents |> List.distinct })

                                logInfo "TaskDependencyGraph"
                                <| sprintf "Dependency added: %s depends on %s" validTaskId validDependsOnId

                                Result.Ok())
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error adding dependency: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 依存関係を削除
    member _.RemoveDependency(taskId: string, dependsOnTaskId: string) =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "TaskDependencyGraph has been disposed")
                else
                    match validateTaskId taskId, validateTaskId dependsOnTaskId with
                    | Result.Error e, _
                    | _, Result.Error e -> Result.Error e
                    | Result.Ok validTaskId, Result.Ok validDependsOnId ->
                        let updateDependency taskId f =
                            match dependencies.TryGetValue(taskId) with
                            | true, dep -> dependencies.[taskId] <- f dep
                            | false, _ -> ()

                        updateDependency validTaskId (fun dep ->
                            { dep with
                                DependsOn = dep.DependsOn |> List.filter ((<>) validDependsOnId) })

                        updateDependency validDependsOnId (fun dep ->
                            { dep with
                                Dependents = dep.Dependents |> List.filter ((<>) validTaskId) })

                        logInfo "TaskDependencyGraph"
                        <| sprintf "Dependency removed: %s no longer depends on %s" validTaskId validDependsOnId

                        Result.Ok())
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error removing dependency: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// タスクの依存関係を取得
    member _.GetDependencies(taskId: string) =
        try
            match validateTaskId taskId with
            | Result.Error e -> Result.Error e
            | Result.Ok validId ->
                match dependencies.TryGetValue(validId) with
                | true, dep -> Result.Ok(Some dep)
                | false, _ -> Result.Ok None
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error getting dependencies: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 実行可能タスク一覧を取得（依存関係が解決済み）
    member _.GetExecutableTasks() =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "TaskDependencyGraph has been disposed")
                else
                    // 安全なスナップショット取得で並行変更の影響を避ける
                    let tasksSnapshot = tasks.Values |> Seq.toList

                    let executableTasks =
                        tasksSnapshot
                        |> List.filter (fun task ->
                            task.Status = Pending
                            && match dependencies.TryGetValue(task.TaskId) with
                               | true, dep ->
                                   dep.DependsOn
                                   |> List.forall (fun depTaskId ->
                                       match tasks.TryGetValue(depTaskId) with
                                       | true, depTask -> depTask.Status = Completed
                                       | false, _ -> false)
                               | false, _ -> true)

                    Result.Ok executableTasks)
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error getting executable tasks: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// ブロックされているタスク一覧を取得
    member _.GetBlockedTasks() =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "TaskDependencyGraph has been disposed")
                else
                    // 安全なスナップショット取得
                    let tasksSnapshot = tasks.Values |> Seq.toList

                    let blockedTasks =
                        tasksSnapshot
                        |> Seq.filter (fun task ->
                            task.Status = Pending
                            && match dependencies.TryGetValue(task.TaskId) with
                               | true, dep ->
                                   dep.DependsOn
                                   |> List.exists (fun depTaskId ->
                                       match tasks.TryGetValue(depTaskId) with
                                       | true, depTask -> depTask.Status <> Completed
                                       | false, _ -> true)
                               | false, _ -> false)
                        |> Seq.map (fun task ->
                            let blockers =
                                match dependencies.TryGetValue(task.TaskId) with
                                | true, dep ->
                                    dep.DependsOn
                                    |> List.filter (fun depTaskId ->
                                        match tasks.TryGetValue(depTaskId) with
                                        | true, depTask -> depTask.Status <> Completed
                                        | false, _ -> true)
                                | false, _ -> []

                            (task, blockers))
                        |> Seq.toList

                    Result.Ok blockedTasks)
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error getting blocked tasks: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// タスク完了時の処理
    member this.CompleteTask(taskId: string) =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "TaskDependencyGraph has been disposed")
                else
                    match validateTaskId taskId with
                    | Result.Error e -> Result.Error e
                    | Result.Ok validId ->
                        match tasks.TryGetValue(validId) with
                        | true, task ->
                            let completedTask =
                                { task with
                                    Status = Completed
                                    UpdatedAt = DateTime.UtcNow }

                            tasks.[validId] <- completedTask
                            taskChangedEvent.Trigger(completedTask)
                            logInfo "TaskDependencyGraph" <| sprintf "Task completed: %s" validId

                            // 新しく実行可能になったタスクを取得
                            match this.GetExecutableTasks() with
                            | Result.Ok newlyExecutable ->
                                newlyExecutable
                                |> List.iter (fun task ->
                                    logInfo "TaskDependencyGraph" <| sprintf "Task now executable: %s" task.TaskId)

                                Result.Ok newlyExecutable
                            | Result.Error e -> Result.Error e
                        | false, _ -> Result.Error(NotFound(sprintf "Task not found: %s" validId)))
        with ex ->
            logError "TaskDependencyGraph" <| sprintf "Error completing task: %s" ex.Message
            Result.Error(SystemError ex.Message)

    /// タスク状態更新
    member _.UpdateTaskStatus(taskId: string, status: TaskStatus) =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "TaskDependencyGraph has been disposed")
                else
                    match validateTaskId taskId with
                    | Result.Error e -> Result.Error e
                    | Result.Ok validId ->
                        match tasks.TryGetValue(validId) with
                        | true, task ->
                            let updatedTask =
                                { task with
                                    Status = status
                                    UpdatedAt = DateTime.UtcNow }

                            tasks.[validId] <- updatedTask
                            taskChangedEvent.Trigger(updatedTask)

                            logInfo "TaskDependencyGraph"
                            <| sprintf "Task status updated: %s -> %A" validId status

                            Result.Ok()
                        | false, _ -> Result.Error(NotFound(sprintf "Task not found: %s" validId)))
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error updating task status: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 循環依存検出
    member _.DetectCircularDependencies() =
        try
            if disposed then
                Result.Error(SystemError "TaskDependencyGraph has been disposed")
            else
                let cycles = ResizeArray<string list>()
                let visited = HashSet<string>()
                let recursionStack = HashSet<string>()

                let rec detectCycles taskId path =
                    if recursionStack.Contains(taskId) then
                        // 循環発見
                        let cycleStart = path |> List.findIndex ((=) taskId)
                        let cycle = path |> List.skip cycleStart
                        cycles.Add(taskId :: cycle)
                        true
                    elif visited.Contains(taskId) then
                        false
                    else
                        visited.Add(taskId) |> ignore
                        recursionStack.Add(taskId) |> ignore

                        let hasCycle =
                            match dependencies.TryGetValue(taskId) with
                            | true, dep ->
                                dep.DependsOn |> List.exists (fun depId -> detectCycles depId (taskId :: path))
                            | false, _ -> false

                        recursionStack.Remove(taskId) |> ignore
                        hasCycle

                tasks.Keys |> Seq.iter (fun taskId -> detectCycles taskId [] |> ignore)
                Result.Ok(cycles |> Seq.toList)
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error detecting circular dependencies: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 重要パス分析（最長実行時間パス）
    member _.GetCriticalPath() =
        try
            if disposed then
                Result.Error(SystemError "TaskDependencyGraph has been disposed")
            else
                let memo = Dictionary<string, TimeSpan * string list>()

                let rec calculatePath taskId =
                    match memo.TryGetValue(taskId) with
                    | true, cached -> cached
                    | false, _ ->
                        match tasks.TryGetValue(taskId), dependencies.TryGetValue(taskId) with
                        | (true, task), (true, dep) ->
                            let duration = task.EstimatedDuration |> Option.defaultValue TimeSpan.Zero

                            if dep.DependsOn.IsEmpty then
                                let result = (duration, [ taskId ])
                                memo.[taskId] <- result
                                result
                            else
                                let (maxDepDuration, maxDepPath) =
                                    dep.DependsOn |> List.map calculatePath |> List.maxBy fst

                                let totalDuration = maxDepDuration + duration
                                let totalPath = taskId :: maxDepPath
                                let result = (totalDuration, totalPath)
                                memo.[taskId] <- result
                                result
                        | _ ->
                            let result = (TimeSpan.Zero, [])
                            memo.[taskId] <- result
                            result

                let allTaskIds = tasks.Keys |> Seq.toList

                if allTaskIds.IsEmpty then
                    Result.Ok(TimeSpan.Zero, [])
                else
                    let criticalPath = allTaskIds |> List.map calculatePath |> List.maxBy fst
                    Result.Ok criticalPath
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error calculating critical path: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 依存関係グラフの可視化用データ取得
    member _.GetGraphData() =
        try
            if disposed then
                Result.Error(SystemError "TaskDependencyGraph has been disposed")
            else
                let taskData =
                    tasks.Values
                    |> Seq.map (fun task ->
                        (task.TaskId, task.Status, task.AssignedAgent |> Option.defaultValue "Unassigned"))
                    |> Seq.toList

                let edgeData =
                    dependencies.Values
                    |> Seq.collect (fun dep -> dep.DependsOn |> List.map (fun depTaskId -> (dep.TaskId, depTaskId)))
                    |> Seq.toList

                Result.Ok(taskData, edgeData)
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error getting graph data: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// 統計情報取得
    member this.GetStatistics() =
        try
            if disposed then
                Result.Error(SystemError "TaskDependencyGraph has been disposed")
            else
                let allTasks = tasks.Values |> Seq.toList
                let completedTasks = allTasks |> List.filter (fun t -> t.Status = Completed)

                let blockedTaskCount =
                    match this.GetBlockedTasks() with
                    | Result.Ok blocked -> blocked.Length
                    | Result.Error _ -> 0

                let executableTaskCount =
                    match this.GetExecutableTasks() with
                    | Result.Ok executable -> executable.Length
                    | Result.Error _ -> 0

                let stats =
                    { TotalTasks = allTasks.Length
                      CompletedTasks = completedTasks.Length
                      BlockedTasks = blockedTaskCount
                      ExecutableTasks = executableTaskCount
                      CompletionRate =
                        if allTasks.IsEmpty then
                            0.0
                        else
                            float completedTasks.Length / float allTasks.Length * 100.0 }

                Result.Ok stats
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error getting statistics: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// システムリセット
    member _.Reset() =
        try
            lock lockObj (fun () ->
                if disposed then
                    Result.Error(SystemError "TaskDependencyGraph has been disposed")
                else
                    tasks.Clear()
                    dependencies.Clear()
                    logInfo "TaskDependencyGraph" "System state reset"
                    Result.Ok())
        with ex ->
            logError "TaskDependencyGraph"
            <| sprintf "Error resetting system: %s" ex.Message

            Result.Error(SystemError ex.Message)

    /// リソース解放
    member _.Dispose() =
        if not disposed then
            disposed <- true
            tasks.Clear()
            dependencies.Clear()
            logInfo "TaskDependencyGraph" "TaskDependencyGraph disposed"

    interface ITaskDependencyGraph with
        member this.AddTask(task) = this.AddTask(task)
        member this.GetTask(taskId) = this.GetTask(taskId)
        member this.GetAllTasks() = this.GetAllTasks()

        member this.AddDependency(taskId, dependsOnTaskId) =
            this.AddDependency(taskId, dependsOnTaskId)

        member this.RemoveDependency(taskId, dependsOnTaskId) =
            this.RemoveDependency(taskId, dependsOnTaskId)

        member this.GetDependencies(taskId) = this.GetDependencies(taskId)
        member this.GetExecutableTasks() = this.GetExecutableTasks()
        member this.GetBlockedTasks() = this.GetBlockedTasks()
        member this.CompleteTask(taskId) = this.CompleteTask(taskId)
        member this.UpdateTaskStatus(taskId, status) = this.UpdateTaskStatus(taskId, status)
        member this.DetectCircularDependencies() = this.DetectCircularDependencies()
        member this.GetCriticalPath() = this.GetCriticalPath()
        member this.GetGraphData() = this.GetGraphData()
        member this.GetStatistics() = this.GetStatistics()
        member this.TaskChanged = this.TaskChanged
        member this.Reset() = this.Reset()

    interface IDisposable with
        member this.Dispose() = this.Dispose()

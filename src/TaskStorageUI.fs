module FCode.TaskStorageUI

open System
open System.Text
open Terminal.Gui
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.TaskStorageManager
open FCode.Logger

/// タスクストレージ情報表示UI
type TaskStorageDisplay(storageManager: TaskStorageManager) =
    
    let mutable taskListView: TextView option = None
    let mutable taskStatsView: TextView option = None
    let mutable taskDetailView: TextView option = None
    let lockObj = obj()
    let mutable disposed = false

    /// タスクリスト表示用テキストビューを設定
    member this.SetTaskListView(view: TextView) =
        taskListView <- Some view
        this.UpdateTaskListDisplay()

    /// タスク統計表示用テキストビューを設定
    member this.SetTaskStatsView(view: TextView) =
        taskStatsView <- Some view
        this.UpdateTaskStatsDisplay()

    /// タスク詳細表示用テキストビューを設定
    member this.SetTaskDetailView(view: TextView) =
        taskDetailView <- Some view
        this.UpdateTaskDetailDisplay()

    /// タスクリスト表示更新
    member this.UpdateTaskListDisplay() =
        lock lockObj (fun () ->
            if not disposed then
                match taskListView with
                | Some view ->
                    async {
                        let! tasksResult = storageManager.GetExecutableTasks()
                        match tasksResult with
                        | Result.Ok tasks ->
                            let displayText = this.BuildTaskListText(tasks)
                            Application.MainLoop.Invoke(fun () ->
                                view.Text <- displayText
                            )
                        | Result.Error error ->
                            let errorText = $"📋 タスク一覧取得エラー: {error}"
                            Application.MainLoop.Invoke(fun () ->
                                view.Text <- errorText
                            )
                            logError "TaskStorageUI" $"タスク一覧取得失敗: {error}"
                    }
                    |> Async.Start
                | None -> ())

    /// タスク統計表示更新
    member this.UpdateTaskStatsDisplay() =
        lock lockObj (fun () ->
            if not disposed then
                match taskStatsView with
                | Some view ->
                    async {
                        let! tasksResult = storageManager.GetExecutableTasks()
                        match tasksResult with
                        | Result.Ok tasks ->
                            let statsText = this.BuildTaskStatsText(tasks)
                            Application.MainLoop.Invoke(fun () ->
                                view.Text <- statsText
                            )
                        | Result.Error error ->
                            let errorText = $"📊 タスク統計取得エラー: {error}"
                            Application.MainLoop.Invoke(fun () ->
                                view.Text <- errorText
                            )
                            logError "TaskStorageUI" $"タスク統計取得失敗: {error}"
                    }
                    |> Async.Start
                | None -> ())

    /// タスク詳細表示更新
    member this.UpdateTaskDetailDisplay() =
        lock lockObj (fun () ->
            if not disposed then
                match taskDetailView with
                | Some view ->
                    async {
                        let! recentTasksResult = storageManager.GetExecutableTasks()
                        match recentTasksResult with
                        | Result.Ok allTasks ->
                            let recentTasks = allTasks |> List.sortByDescending (_.UpdatedAt) |> List.take (min 5 allTasks.Length)
                            let detailText = this.BuildTaskDetailText(recentTasks)
                            Application.MainLoop.Invoke(fun () ->
                                view.Text <- detailText
                            )
                        | Result.Error error ->
                            let errorText = $"🔍 最近のタスク詳細取得エラー: {error}"
                            Application.MainLoop.Invoke(fun () ->
                                view.Text <- errorText
                            )
                            logError "TaskStorageUI" $"タスク詳細取得失敗: {error}"
                    }
                    |> Async.Start
                | None -> ())

    /// タスクリストテキスト構築
    member private _.BuildTaskListText(tasks: TaskInfo list) =
        let text = StringBuilder()
        text.AppendLine("📋 タスクストレージ一覧") |> ignore
        text.AppendLine("========================") |> ignore
        text.AppendLine() |> ignore

        if tasks.IsEmpty then
            text.AppendLine("  タスクがありません") |> ignore
        else
            for task in tasks |> List.take (min 20 tasks.Length) do
                let statusIcon = 
                    match task.Status with
                    | TaskStatus.Pending -> "⏳"
                    | TaskStatus.InProgress -> "🔄"
                    | TaskStatus.Completed -> "✅"
                    | TaskStatus.Failed -> "❌"
                    | TaskStatus.Cancelled -> "🚫"

                let priorityIcon =
                    match task.Priority with
                    | TaskPriority.Low -> "🔵"
                    | TaskPriority.Medium -> "🟡"
                    | TaskPriority.High -> "🔴"
                    | TaskPriority.Critical -> "🚨"

                text.AppendLine($"  {statusIcon} {priorityIcon} {task.Title}") |> ignore
                text.AppendLine($"    ID: {task.TaskId}") |> ignore
                
                match task.AssignedAgent with
                | Some agent -> text.AppendLine($"    エージェント: {agent}") |> ignore
                | None -> text.AppendLine("    エージェント: 未割り当て") |> ignore
                
                text.AppendLine() |> ignore

            if tasks.Length > 20 then
                text.AppendLine($"  ... および他 {tasks.Length - 20} 件") |> ignore

        text.ToString()

    /// タスク統計テキスト構築
    member private _.BuildTaskStatsText(tasks: TaskInfo list) =
        let text = StringBuilder()
        text.AppendLine("📊 タスクストレージ統計") |> ignore
        text.AppendLine("========================") |> ignore
        text.AppendLine() |> ignore

        let totalTasks = tasks.Length
        let pendingTasks = tasks |> List.filter (fun t -> t.Status = TaskStatus.Pending) |> List.length
        let inProgressTasks = tasks |> List.filter (fun t -> t.Status = TaskStatus.InProgress) |> List.length
        let completedTasks = tasks |> List.filter (fun t -> t.Status = TaskStatus.Completed) |> List.length
        let cancelledTasks = tasks |> List.filter (fun t -> t.Status = TaskStatus.Cancelled) |> List.length

        text.AppendLine($"  📈 総タスク数: {totalTasks}") |> ignore
        text.AppendLine() |> ignore
        text.AppendLine("  ステータス別:") |> ignore
        text.AppendLine($"    ⏳ 待機中: {pendingTasks}") |> ignore
        text.AppendLine($"    🔄 進行中: {inProgressTasks}") |> ignore
        text.AppendLine($"    ✅ 完了: {completedTasks}") |> ignore
        text.AppendLine($"    🚫 キャンセル: {cancelledTasks}") |> ignore
        text.AppendLine() |> ignore

        // 優先度別統計
        let highPriorityTasks = tasks |> List.filter (fun t -> t.Priority = TaskPriority.High || t.Priority = TaskPriority.Critical) |> List.length
        let mediumPriorityTasks = tasks |> List.filter (fun t -> t.Priority = TaskPriority.Medium) |> List.length
        let lowPriorityTasks = tasks |> List.filter (fun t -> t.Priority = TaskPriority.Low) |> List.length

        text.AppendLine("  優先度別:") |> ignore
        text.AppendLine($"    🔴 高/緊急: {highPriorityTasks}") |> ignore
        text.AppendLine($"    🟡 中: {mediumPriorityTasks}") |> ignore
        text.AppendLine($"    🔵 低: {lowPriorityTasks}") |> ignore
        text.AppendLine() |> ignore

        // エージェント別統計
        let agentGroups = 
            tasks 
            |> List.choose (fun t -> t.AssignedAgent)
            |> List.groupBy id
            |> List.map (fun (agent, tasks) -> (agent, tasks.Length))
            |> List.sortByDescending snd

        if not agentGroups.IsEmpty then
            text.AppendLine("  エージェント別:") |> ignore
            for (agent, count) in agentGroups |> List.take (min 5 agentGroups.Length) do
                text.AppendLine($"    👤 {agent}: {count}") |> ignore

        text.ToString()

    /// タスク詳細テキスト構築
    member private _.BuildTaskDetailText(recentTasks: TaskInfo list) =
        let text = StringBuilder()
        text.AppendLine("🔍 最近のタスク詳細") |> ignore
        text.AppendLine("========================") |> ignore
        text.AppendLine() |> ignore

        if recentTasks.IsEmpty then
            text.AppendLine("  最近のタスクがありません") |> ignore
        else
            for task in recentTasks do
                text.AppendLine($"📝 {task.Title}") |> ignore
                text.AppendLine($"   ID: {task.TaskId}") |> ignore
                text.AppendLine($"   説明: {task.Description}") |> ignore
                text.AppendLine($"   ステータス: {task.Status}") |> ignore
                text.AppendLine($"   優先度: {task.Priority}") |> ignore
                
                match task.AssignedAgent with
                | Some agent -> text.AppendLine($"   担当: {agent}") |> ignore
                | None -> text.AppendLine("   担当: 未割り当て") |> ignore
                
                match task.EstimatedDuration with
                | Some duration -> text.AppendLine($"   見積時間: {duration.TotalMinutes:F0}分") |> ignore
                | None -> text.AppendLine("   見積時間: 未設定") |> ignore
                
                let createdAtText = task.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                let updatedAtText = task.UpdatedAt.ToString("yyyy-MM-dd HH:mm")
                text.AppendLine($"   作成日時: {createdAtText}") |> ignore
                text.AppendLine($"   更新日時: {updatedAtText}") |> ignore
                text.AppendLine() |> ignore

        text.ToString()

    /// 表示更新イベントハンドラー
    member this.HandleTaskUpdatedEvent() =
        this.UpdateTaskListDisplay()
        this.UpdateTaskStatsDisplay()
        this.UpdateTaskDetailDisplay()
        logInfo "TaskStorageUI" "タスクストレージ表示更新完了"

    /// UI統合: 定期更新開始
    member this.StartPeriodicUpdate() =
        let timer = new System.Timers.Timer(30000.0) // 30秒間隔
        timer.Elapsed.Add(fun _ ->
            if not disposed then
                this.HandleTaskUpdatedEvent()
        )
        timer.Start()
        logInfo "TaskStorageUI" "タスクストレージ定期更新開始（30秒間隔）"

    /// リソース解放
    member this.Dispose() =
        lock lockObj (fun () ->
            disposed <- true
            taskListView <- None
            taskStatsView <- None
            taskDetailView <- None
        )

    interface IDisposable with
        member this.Dispose() = this.Dispose()
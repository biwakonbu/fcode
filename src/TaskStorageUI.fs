module FCode.TaskStorageUI

open System
open System.Text
open Terminal.Gui
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.TaskStorageManager
open FCode.Configuration
open FCode.Logger

/// タスクストレージ情報表示UI
type TaskStorageDisplay(storageManager: TaskStorageManager) =

    let mutable taskListView: TextView option = None
    let mutable taskStatsView: TextView option = None
    let mutable taskDetailView: TextView option = None
    let lockObj = obj ()
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
                            Application.MainLoop.Invoke(fun () -> view.Text <- displayText)
                        | Result.Error error ->
                            let errorText = sprintf "📋 タスク一覧取得エラー: %O" error
                            Application.MainLoop.Invoke(fun () -> view.Text <- errorText)
                            logError "TaskStorageUI" (sprintf "タスク一覧取得失敗: %O" error)
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
                            Application.MainLoop.Invoke(fun () -> view.Text <- statsText)
                        | Result.Error error ->
                            let errorText = sprintf "📊 タスク統計取得エラー: %O" error
                            Application.MainLoop.Invoke(fun () -> view.Text <- errorText)
                            logError "TaskStorageUI" (sprintf "タスク統計取得失敗: %O" error)
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
                            let recentTasks =
                                allTasks
                                |> List.sortByDescending (_.UpdatedAt)
                                |> List.take (min 5 allTasks.Length)

                            let detailText = this.BuildTaskDetailText(recentTasks)
                            Application.MainLoop.Invoke(fun () -> view.Text <- detailText)
                        | Result.Error error ->
                            let errorText = sprintf "🔍 最近のタスク詳細取得エラー: %O" error
                            Application.MainLoop.Invoke(fun () -> view.Text <- errorText)
                            logError "TaskStorageUI" (sprintf "タスク詳細取得失敗: %O" error)
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
                    | _ -> "❓" // 未知の優先度値への対応

                text.AppendLine(sprintf "  %s %s %s" statusIcon priorityIcon task.Title)
                |> ignore

                text.AppendLine(sprintf "    ID: %s" task.TaskId) |> ignore

                match task.AssignedAgent with
                | Some agent -> text.AppendLine(sprintf "    エージェント: %s" agent) |> ignore
                | None -> text.AppendLine("    エージェント: 未割り当て") |> ignore

                text.AppendLine() |> ignore

            if tasks.Length > 20 then
                text.AppendLine(sprintf "  ... および他 %d 件" (tasks.Length - 20)) |> ignore

        text.ToString()

    /// タスク統計テキスト構築
    member private _.BuildTaskStatsText(tasks: TaskInfo list) =
        let text = StringBuilder()
        text.AppendLine("📊 タスクストレージ統計") |> ignore
        text.AppendLine("========================") |> ignore
        text.AppendLine() |> ignore

        let totalTasks = tasks.Length

        let pendingTasks =
            tasks |> List.filter (fun t -> t.Status = TaskStatus.Pending) |> List.length

        let inProgressTasks =
            tasks |> List.filter (fun t -> t.Status = TaskStatus.InProgress) |> List.length

        let completedTasks =
            tasks |> List.filter (fun t -> t.Status = TaskStatus.Completed) |> List.length

        let cancelledTasks =
            tasks |> List.filter (fun t -> t.Status = TaskStatus.Cancelled) |> List.length

        text.AppendLine(sprintf "  📈 総タスク数: %d" totalTasks) |> ignore
        text.AppendLine() |> ignore
        text.AppendLine("  ステータス別:") |> ignore
        text.AppendLine(sprintf "    ⏳ 待機中: %d" pendingTasks) |> ignore
        text.AppendLine(sprintf "    🔄 進行中: %d" inProgressTasks) |> ignore
        text.AppendLine(sprintf "    ✅ 完了: %d" completedTasks) |> ignore
        text.AppendLine(sprintf "    🚫 キャンセル: %d" cancelledTasks) |> ignore
        text.AppendLine() |> ignore

        // 優先度別統計
        let highPriorityTasks =
            tasks
            |> List.filter (fun t -> t.Priority = TaskPriority.High || t.Priority = TaskPriority.Critical)
            |> List.length

        let mediumPriorityTasks =
            tasks |> List.filter (fun t -> t.Priority = TaskPriority.Medium) |> List.length

        let lowPriorityTasks =
            tasks |> List.filter (fun t -> t.Priority = TaskPriority.Low) |> List.length

        text.AppendLine("  優先度別:") |> ignore
        text.AppendLine(sprintf "    🔴 高/緊急: %d" highPriorityTasks) |> ignore
        text.AppendLine(sprintf "    🟡 中: %d" mediumPriorityTasks) |> ignore
        text.AppendLine(sprintf "    🔵 低: %d" lowPriorityTasks) |> ignore
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
                text.AppendLine(sprintf "    👤 %s: %d" agent count) |> ignore

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
                text.AppendLine(sprintf "📝 %s" task.Title) |> ignore
                text.AppendLine(sprintf "   ID: %s" task.TaskId) |> ignore
                text.AppendLine(sprintf "   説明: %s" task.Description) |> ignore
                text.AppendLine(sprintf "   ステータス: %O" task.Status) |> ignore
                text.AppendLine(sprintf "   優先度: %O" task.Priority) |> ignore

                match task.AssignedAgent with
                | Some agent -> text.AppendLine(sprintf "   担当: %s" agent) |> ignore
                | None -> text.AppendLine("   担当: 未割り当て") |> ignore

                match task.EstimatedDuration with
                | Some duration -> text.AppendLine(sprintf "   見積時間: %.0f分" duration.TotalMinutes) |> ignore
                | None -> text.AppendLine("   見積時間: 未設定") |> ignore

                let createdAtText = task.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                let updatedAtText = task.UpdatedAt.ToString("yyyy-MM-dd HH:mm")
                text.AppendLine(sprintf "   作成日時: %s" createdAtText) |> ignore
                text.AppendLine(sprintf "   更新日時: %s" updatedAtText) |> ignore
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
        let intervalMs =
            float FCode.Configuration.DefaultConfig.uiConfig.TaskStorageUpdateIntervalMs

        let timer = new System.Timers.Timer(intervalMs)

        timer.Elapsed.Add(fun _ ->
            if not disposed then
                this.HandleTaskUpdatedEvent())

        timer.Start()
        logInfo "TaskStorageUI" $"タスクストレージ定期更新開始（{intervalMs / 1000.0}秒間隔）"

    /// リソース解放
    member this.Dispose() =
        lock lockObj (fun () ->
            disposed <- true
            taskListView <- None
            taskStatsView <- None
            taskDetailView <- None)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

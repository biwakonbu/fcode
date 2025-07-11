namespace FCode

open System
open System.Threading
open System.Collections.Concurrent
open FCode.Logger
// TaskAssignmentManagerの型参照のみ削除（実際には不要）

/// エージェント作業状況の詳細情報
type AgentWorkStatus =
    | Idle of lastActivity: DateTime
    | Working of taskTitle: string * startTime: DateTime * progress: float
    | Completed of taskTitle: string * completionTime: DateTime * result: string
    | Error of taskTitle: string * errorMessage: string * errorTime: DateTime
    | Reviewing of reviewTarget: string * reviewer: string * startTime: DateTime

/// エージェント作業表示用の統合情報
type AgentWorkInfo =
    { AgentId: string
      CurrentStatus: AgentWorkStatus
      TaskHistory: (DateTime * string * AgentWorkStatus) list
      LastUpdate: DateTime
      ProgressPercentage: float
      EstimatedCompletion: DateTime option }

/// エージェント作業状況管理・リアルタイム表示マネージャー
type AgentWorkDisplayManager() =

    let agentWorkInfos = ConcurrentDictionary<string, AgentWorkInfo>()
    let mutable displayUpdateHandlers = []
    let statusUpdateLock = obj ()

    /// 表示更新ハンドラーを登録
    member this.RegisterDisplayUpdateHandler(handler: string -> AgentWorkInfo -> unit) =
        lock statusUpdateLock (fun () -> displayUpdateHandlers <- handler :: displayUpdateHandlers)

    /// エージェントを初期化（アイドル状態で開始）
    member this.InitializeAgent(agentId: string) =
        let initialInfo =
            { AgentId = agentId
              CurrentStatus = Idle DateTime.Now
              TaskHistory = []
              LastUpdate = DateTime.Now
              ProgressPercentage = 0.0
              EstimatedCompletion = None }

        agentWorkInfos.AddOrUpdate(agentId, initialInfo, fun _ _ -> initialInfo)
        |> ignore

        logInfo "AgentWorkDisplay" (sprintf "Initialized agent: %s" agentId)

        // 表示更新通知
        this.NotifyDisplayUpdate(agentId, initialInfo)

    /// エージェント作業開始を記録
    member this.StartTask(agentId: string, taskTitle: string, estimatedDuration: TimeSpan) =
        let startTime = DateTime.Now
        let estimatedCompletion = startTime.Add(estimatedDuration)

        let updateFunc (existing: AgentWorkInfo) =
            let newStatus = Working(taskTitle, startTime, 0.0)
            let historyEntry = (startTime, sprintf "Task started: %s" taskTitle, newStatus)

            { existing with
                CurrentStatus = newStatus
                TaskHistory = historyEntry :: existing.TaskHistory |> List.truncate 10 // 最新10件保持
                LastUpdate = startTime
                ProgressPercentage = 0.0
                EstimatedCompletion = Some estimatedCompletion }

        match agentWorkInfos.TryGetValue(agentId) with
        | true, existing ->
            let updated = updateFunc existing
            agentWorkInfos.TryUpdate(agentId, updated, existing) |> ignore
            logInfo "AgentWorkDisplay" (sprintf "Agent %s started task: %s" agentId taskTitle)
            this.NotifyDisplayUpdate(agentId, updated)
        | false, _ ->
            logWarning "AgentWorkDisplay" (sprintf "Agent %s not initialized, initializing with task start" agentId)
            this.InitializeAgent(agentId)
            this.StartTask(agentId, taskTitle, estimatedDuration)

    /// 作業進捗を更新
    member this.UpdateProgress(agentId: string, progress: float, statusNote: string) =
        let updateTime = DateTime.Now

        let updateFunc (existing: AgentWorkInfo) =
            let newStatus =
                match existing.CurrentStatus with
                | Working(taskTitle, startTime, _) -> Working(taskTitle, startTime, progress)
                | other -> other // 作業中でない場合は変更しない

            let historyEntry = (updateTime, statusNote, newStatus)

            { existing with
                CurrentStatus = newStatus
                TaskHistory = historyEntry :: existing.TaskHistory |> List.truncate 10
                LastUpdate = updateTime
                ProgressPercentage = progress }

        match agentWorkInfos.TryGetValue(agentId) with
        | true, existing ->
            let updated = updateFunc existing
            agentWorkInfos.TryUpdate(agentId, updated, existing) |> ignore
            logInfo "AgentWorkDisplay" (sprintf "Agent %s progress updated: %.1f%% - %s" agentId progress statusNote)
            this.NotifyDisplayUpdate(agentId, updated)
        | false, _ -> logWarning "AgentWorkDisplay" (sprintf "Agent %s not found for progress update" agentId)

    /// タスク完了を記録
    member this.CompleteTask(agentId: string, result: string) =
        let completionTime = DateTime.Now

        let updateFunc (existing: AgentWorkInfo) =
            let newStatus =
                match existing.CurrentStatus with
                | Working(taskTitle, _, _) -> Completed(taskTitle, completionTime, result)
                | other -> other

            let historyEntry = (completionTime, sprintf "Task completed: %s" result, newStatus)

            { existing with
                CurrentStatus = newStatus
                TaskHistory = historyEntry :: existing.TaskHistory |> List.truncate 10
                LastUpdate = completionTime
                ProgressPercentage = 100.0
                EstimatedCompletion = None }

        match agentWorkInfos.TryGetValue(agentId) with
        | true, existing ->
            let updated = updateFunc existing
            agentWorkInfos.TryUpdate(agentId, updated, existing) |> ignore
            logInfo "AgentWorkDisplay" (sprintf "Agent %s completed task: %s" agentId result)
            this.NotifyDisplayUpdate(agentId, updated)
        | false, _ -> logWarning "AgentWorkDisplay" (sprintf "Agent %s not found for task completion" agentId)

    /// エラー発生を記録
    member this.ReportError(agentId: string, errorMessage: string) =
        let errorTime = DateTime.Now

        let updateFunc (existing: AgentWorkInfo) =
            let newStatus =
                match existing.CurrentStatus with
                | Working(taskTitle, _, _) -> Error(taskTitle, errorMessage, errorTime)
                | other -> Error("Unknown Task", errorMessage, errorTime)

            let historyEntry = (errorTime, sprintf "Error occurred: %s" errorMessage, newStatus)

            { existing with
                CurrentStatus = newStatus
                TaskHistory = historyEntry :: existing.TaskHistory |> List.truncate 10
                LastUpdate = errorTime
                EstimatedCompletion = None }

        match agentWorkInfos.TryGetValue(agentId) with
        | true, existing ->
            let updated = updateFunc existing
            agentWorkInfos.TryUpdate(agentId, updated, existing) |> ignore
            logError "AgentWorkDisplay" (sprintf "Agent %s reported error: %s" agentId errorMessage)
            this.NotifyDisplayUpdate(agentId, updated)
        | false, _ -> logWarning "AgentWorkDisplay" (sprintf "Agent %s not found for error report" agentId)

    /// レビュー開始を記録
    member this.StartReview(agentId: string, reviewTarget: string, reviewer: string) =
        let startTime = DateTime.Now
        let newStatus = Reviewing(reviewTarget, reviewer, startTime)

        let updateFunc (existing: AgentWorkInfo) =
            let historyEntry =
                (startTime, sprintf "Review started: %s by %s" reviewTarget reviewer, newStatus)

            { existing with
                CurrentStatus = newStatus
                TaskHistory = historyEntry :: existing.TaskHistory |> List.truncate 10
                LastUpdate = startTime
                ProgressPercentage = 0.0 }

        match agentWorkInfos.TryGetValue(agentId) with
        | true, existing ->
            let updated = updateFunc existing
            agentWorkInfos.TryUpdate(agentId, updated, existing) |> ignore
            logInfo "AgentWorkDisplay" (sprintf "Agent %s started review: %s" agentId reviewTarget)
            this.NotifyDisplayUpdate(agentId, updated)
        | false, _ -> logWarning "AgentWorkDisplay" (sprintf "Agent %s not found for review start" agentId)

    /// エージェント作業情報を取得
    member this.GetAgentWorkInfo(agentId: string) : AgentWorkInfo option =
        match agentWorkInfos.TryGetValue(agentId) with
        | true, info -> Some info
        | false, _ -> None

    /// 全エージェントの作業情報を取得
    member this.GetAllAgentWorkInfos() : (string * AgentWorkInfo) list =
        agentWorkInfos |> Seq.map (fun kvp -> kvp.Key, kvp.Value) |> Seq.toList

    /// 表示更新通知を全ハンドラーに送信
    member private this.NotifyDisplayUpdate(agentId: string, workInfo: AgentWorkInfo) =
        lock statusUpdateLock (fun () ->
            for handler in displayUpdateHandlers do
                try
                    handler agentId workInfo
                with ex ->
                    logError "AgentWorkDisplay" (sprintf "Display update handler error for %s: %s" agentId ex.Message))

    /// 作業状況の表示用文字列を生成
    member this.FormatWorkStatus(workInfo: AgentWorkInfo) : string =
        let statusText =
            match workInfo.CurrentStatus with
            | Idle lastActivity -> sprintf "🟢 アイドル状態 (最終活動: %s)" (lastActivity.ToString("HH:mm:ss"))
            | Working(taskTitle, startTime, progress) ->
                let elapsed = DateTime.Now - startTime

                let eta =
                    workInfo.EstimatedCompletion
                    |> Option.map (fun eta -> eta.ToString("HH:mm:ss"))
                    |> Option.defaultValue "未定"

                sprintf "🔵 作業中: %s\n   進捗: %.1f%% | 経過時間: %.1f分 | 完了予定: %s" taskTitle progress elapsed.TotalMinutes eta
            | Completed(taskTitle, completionTime, result) ->
                sprintf "✅ 完了: %s\n   結果: %s (%s)" taskTitle result (completionTime.ToString("HH:mm:ss"))
            | Error(taskTitle, errorMessage, errorTime) ->
                sprintf "❌ エラー: %s\n   エラー内容: %s (%s)" taskTitle errorMessage (errorTime.ToString("HH:mm:ss"))
            | Reviewing(reviewTarget, reviewer, startTime) ->
                let elapsed = DateTime.Now - startTime
                sprintf "🔍 レビュー中: %s\n   レビュアー: %s | 経過時間: %.1f分" reviewTarget reviewer elapsed.TotalMinutes

        let recentHistory =
            workInfo.TaskHistory
            |> List.truncate 3
            |> List.map (fun (time, note, _) -> sprintf "  %s %s" (time.ToString("HH:mm:ss")) note)
            |> String.concat "\n"

        let historySection =
            if recentHistory.Length > 0 then
                sprintf "\n\n📋 最近の活動:\n%s" recentHistory
            else
                ""

        sprintf
            "🤖 %s\n%s%s\n\n最終更新: %s"
            workInfo.AgentId
            statusText
            historySection
            (workInfo.LastUpdate.ToString("HH:mm:ss"))

/// グローバルなAgentWorkDisplayManagerインスタンス
module AgentWorkDisplayGlobal =
    let private globalManager = lazy (new AgentWorkDisplayManager())

    /// グローバルマネージャーインスタンスを取得
    let GetManager () = globalManager.Value

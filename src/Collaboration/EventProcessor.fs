module FCode.Collaboration.EventProcessor

open System
open System.Collections.Concurrent
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IEventProcessor
open FCode.Collaboration.ITimeCalculationManager
open FCode.Collaboration.IMeetingScheduler
open FCode.Logger

/// イベントプロセッサー実装
type EventProcessor
    (timeCalculationManager: ITimeCalculationManager, meetingScheduler: IMeetingScheduler, config: VirtualTimeConfig) =

    let pendingEvents = ConcurrentDictionary<string, TimeEvent list>()
    let eventHistory = ConcurrentDictionary<string, (DateTime * TimeEvent) list>()
    let processedEventCount = ref 0

    /// イベント履歴に追加
    member private this.AddToHistory(sprintId: string, event: TimeEvent) =
        let timestamp = DateTime.UtcNow

        match eventHistory.TryGetValue(sprintId) with
        | true, history -> eventHistory.[sprintId] <- (timestamp, event) :: history
        | false, _ -> eventHistory.[sprintId] <- [ (timestamp, event) ]

    /// タイムイベント登録
    member this.RegisterTimeEvent(sprintId: string, event: TimeEvent) =
        async {
            try
                match pendingEvents.TryGetValue(sprintId) with
                | true, events ->
                    let updatedEvents = event :: events
                    pendingEvents.[sprintId] <- updatedEvents
                    this.AddToHistory(sprintId, event)
                    logInfo "EventProcessor" <| sprintf "イベント登録: %s (%A)" sprintId event
                    return Result.Ok()
                | false, _ ->
                    pendingEvents.[sprintId] <- [ event ]
                    this.AddToHistory(sprintId, event)
                    logInfo "EventProcessor" <| sprintf "初回イベント登録: %s (%A)" sprintId event
                    return Result.Ok()
            with ex ->
                logError "EventProcessor" <| sprintf "イベント登録例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 保留中イベント取得
    member this.GetPendingEvents(sprintId: string) =
        async {
            try
                match pendingEvents.TryGetValue(sprintId) with
                | true, events ->
                    logInfo "EventProcessor" <| sprintf "保留中イベント取得: %s (%d件)" sprintId events.Length
                    return Result.Ok events
                | false, _ ->
                    logInfo "EventProcessor" <| sprintf "保留中イベントなし: %s" sprintId
                    return Result.Ok []
            with ex ->
                logError "EventProcessor" <| sprintf "保留中イベント取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// イベント発火処理
    member this.ProcessEvents(sprintId: string) =
        async {
            try
                let! currentTimeResult = timeCalculationManager.GetCurrentVirtualTime(sprintId)

                match currentTimeResult with
                | Result.Ok currentTime ->
                    let! eventsResult = this.GetPendingEvents(sprintId)

                    match eventsResult with
                    | Result.Ok events ->
                        let processedEvents = []
                        let currentVH = timeCalculationManager.ToVirtualHours(currentTime)

                        // イベント処理ロジック
                        for event in events do
                            match event with
                            | StandupScheduled(scheduledTime, participants) ->
                                let scheduledVH = timeCalculationManager.ToVirtualHours(scheduledTime)

                                if currentVH >= scheduledVH then
                                    let! _ = this.ExecuteStandupEvent(sprintId, participants)
                                    ()

                            | ReviewMeetingTriggered scheduledTime ->
                                let scheduledVH = timeCalculationManager.ToVirtualHours(scheduledTime)

                                if currentVH >= scheduledVH then
                                    let! _ = this.ExecuteReviewEvent(sprintId)
                                    ()

                            | TaskDeadlineApproaching(taskId, deadline) ->
                                let deadlineVH = timeCalculationManager.ToVirtualHours(deadline)
                                let warningThreshold = deadlineVH - 6 // 6vh前に警告

                                if currentVH >= warningThreshold then
                                    let! _ = this.ExecuteDeadlineEvent(sprintId, taskId)
                                    ()

                            | SprintCompleted _ ->
                                let! _ = this.ExecuteReviewEvent(sprintId)
                                ()

                            | EmergencyStop reason ->
                                let! _ = this.ExecuteEmergencyStopEvent(sprintId, reason)
                                ()

                        System.Threading.Interlocked.Add(processedEventCount, events.Length) |> ignore
                        logInfo "EventProcessor" <| sprintf "イベント処理: %s (%d件)" sprintId events.Length

                        return Result.Ok processedEvents
                    | Result.Error error -> return Result.Error error
                | Result.Error error -> return Result.Error error
            with ex ->
                logError "EventProcessor" <| sprintf "イベント処理例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 特定イベント削除
    member this.RemoveEvent(sprintId: string, eventPattern: TimeEvent) =
        async {
            try
                match pendingEvents.TryGetValue(sprintId) with
                | true, events ->
                    let filteredEvents =
                        events
                        |> List.filter (fun event ->
                            match (event, eventPattern) with
                            | (StandupScheduled _, StandupScheduled _) -> false
                            | (ReviewMeetingTriggered _, ReviewMeetingTriggered _) -> false
                            | (TaskDeadlineApproaching(id1, _), TaskDeadlineApproaching(id2, _)) -> id1 <> id2
                            | (SprintCompleted _, SprintCompleted _) -> false
                            | (EmergencyStop _, EmergencyStop _) -> false
                            | _ -> true)

                    let removed = events.Length <> filteredEvents.Length

                    if removed then
                        pendingEvents.[sprintId] <- filteredEvents
                        logInfo "EventProcessor" <| sprintf "イベント削除: %s" sprintId

                    return Result.Ok removed
                | false, _ -> return Result.Ok false
            with ex ->
                logError "EventProcessor" <| sprintf "イベント削除例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// イベント種別フィルタ
    member this.FilterEventsByType(sprintId: string, eventType: string) =
        async {
            try
                let! eventsResult = this.GetPendingEvents(sprintId)

                match eventsResult with
                | Result.Ok events ->
                    let filteredEvents =
                        events
                        |> List.filter (fun event ->
                            match event with
                            | StandupScheduled _ when eventType = "Standup" -> true
                            | ReviewMeetingTriggered _ when eventType = "Review" -> true
                            | TaskDeadlineApproaching _ when eventType = "Deadline" -> true
                            | SprintCompleted _ when eventType = "Sprint" -> true
                            | EmergencyStop _ when eventType = "Emergency" -> true
                            | _ -> false)

                    return Result.Ok filteredEvents
                | Result.Error error -> return Result.Error error
            with ex ->
                logError "EventProcessor" <| sprintf "イベント種別フィルタ例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 時刻範囲フィルタ
    member this.FilterEventsByTimeRange(sprintId: string, startTime: VirtualTimeUnit, endTime: VirtualTimeUnit) =
        async {
            try
                let! eventsResult = this.GetPendingEvents(sprintId)

                match eventsResult with
                | Result.Ok events ->
                    let startVH = timeCalculationManager.ToVirtualHours(startTime)
                    let endVH = timeCalculationManager.ToVirtualHours(endTime)

                    let filteredEvents =
                        events
                        |> List.filter (fun event ->
                            let eventTime =
                                match event with
                                | StandupScheduled(time, _) -> timeCalculationManager.ToVirtualHours(time)
                                | ReviewMeetingTriggered time -> timeCalculationManager.ToVirtualHours(time)
                                | TaskDeadlineApproaching(_, time) -> timeCalculationManager.ToVirtualHours(time)
                                | SprintCompleted time -> timeCalculationManager.ToVirtualHours(time)
                                | EmergencyStop _ -> 0 // 緊急停止は時刻無関係

                            eventTime >= startVH && eventTime <= endVH)

                    return Result.Ok filteredEvents
                | Result.Error error -> return Result.Error error
            with ex ->
                logError "EventProcessor" <| sprintf "時刻範囲フィルタ例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 重複イベントチェック
    member this.CheckDuplicateEvents(sprintId: string) =
        async {
            try
                let! eventsResult = this.GetPendingEvents(sprintId)

                match eventsResult with
                | Result.Ok events ->
                    let duplicates =
                        events
                        |> List.groupBy id
                        |> List.filter (fun (_, group) -> group.Length > 1)
                        |> List.map fst

                    if not duplicates.IsEmpty then
                        logWarning "EventProcessor"
                        <| sprintf "重複イベント検出: %s (%d件)" sprintId duplicates.Length

                    return Result.Ok duplicates
                | Result.Error error -> return Result.Error error
            with ex ->
                logError "EventProcessor" <| sprintf "重複イベントチェック例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スタンドアップイベント実行
    member this.ExecuteStandupEvent(sprintId: string, participants: string list) =
        async {
            try
                let! scheduleResult = meetingScheduler.ScheduleNextStandup(sprintId, participants)

                match scheduleResult with
                | Result.Ok meeting ->
                    logInfo "EventProcessor" <| sprintf "スタンドアップイベント実行: %s" meeting.MeetingId
                    return Result.Ok()
                | Result.Error error ->
                    logError "EventProcessor" <| sprintf "スタンドアップイベント実行失敗: %A" error
                    return Result.Error error
            with ex ->
                logError "EventProcessor" <| sprintf "スタンドアップイベント実行例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// レビューイベント実行
    member this.ExecuteReviewEvent(sprintId: string) =
        async {
            try
                let! reviewResult = meetingScheduler.TriggerReviewMeeting(sprintId)

                match reviewResult with
                | Result.Ok meeting ->
                    logInfo "EventProcessor" <| sprintf "レビューイベント実行: %s" meeting.MeetingId
                    return Result.Ok()
                | Result.Error error ->
                    logError "EventProcessor" <| sprintf "レビューイベント実行失敗: %A" error
                    return Result.Error error
            with ex ->
                logError "EventProcessor" <| sprintf "レビューイベント実行例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 期限アプローチイベント実行
    member this.ExecuteDeadlineEvent(sprintId: string, taskId: string) =
        async {
            try
                logWarning "EventProcessor"
                <| sprintf "タスク期限アプローチ: Sprint=%s Task=%s" sprintId taskId
                // 実際の期限通知処理をここに実装
                return Result.Ok()
            with ex ->
                logError "EventProcessor" <| sprintf "期限アプローチイベント実行例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 緊急停止イベント実行
    member this.ExecuteEmergencyStopEvent(sprintId: string, reason: string) =
        async {
            try
                logError "EventProcessor"
                <| sprintf "緊急停止実行: Sprint=%s Reason=%s" sprintId reason
                // 実際の緊急停止処理をここに実装
                return Result.Ok()
            with ex ->
                logError "EventProcessor" <| sprintf "緊急停止イベント実行例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// イベント統計取得
    member this.GetEventStatistics(sprintId: string) =
        async {
            try
                let! eventsResult = this.GetPendingEvents(sprintId)

                match eventsResult with
                | Result.Ok events ->
                    let standupCount =
                        events
                        |> List.filter (function
                            | StandupScheduled _ -> true
                            | _ -> false)
                        |> List.length

                    let reviewCount =
                        events
                        |> List.filter (function
                            | ReviewMeetingTriggered _ -> true
                            | _ -> false)
                        |> List.length

                    let deadlineCount =
                        events
                        |> List.filter (function
                            | TaskDeadlineApproaching _ -> true
                            | _ -> false)
                        |> List.length

                    let emergencyCount =
                        events
                        |> List.filter (function
                            | EmergencyStop _ -> true
                            | _ -> false)
                        |> List.length

                    let statistics =
                        [ ("PendingEvents", events.Length)
                          ("StandupEvents", standupCount)
                          ("ReviewEvents", reviewCount)
                          ("DeadlineEvents", deadlineCount)
                          ("EmergencyEvents", emergencyCount)
                          ("ProcessedTotal", !processedEventCount) ]

                    return Result.Ok statistics
                | Result.Error error -> return Result.Error error
            with ex ->
                logError "EventProcessor" <| sprintf "イベント統計取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// イベント履歴取得
    member this.GetEventHistory(sprintId: string) =
        async {
            try
                match eventHistory.TryGetValue(sprintId) with
                | true, history ->
                    let sortedHistory = history |> List.sortByDescending fst
                    logInfo "EventProcessor" <| sprintf "イベント履歴取得: %s (%d件)" sprintId history.Length
                    return Result.Ok sortedHistory
                | false, _ -> return Result.Ok []
            with ex ->
                logError "EventProcessor" <| sprintf "イベント履歴取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// イベント健全性チェック
    member this.PerformEventHealthCheck() =
        async {
            try
                let totalPendingEvents = pendingEvents.Values |> Seq.sumBy List.length
                let totalHistoryEvents = eventHistory.Values |> Seq.sumBy List.length
                let processedCount = !processedEventCount

                let isHealthy =
                    totalPendingEvents < 10000
                    && // 大量イベントチェック
                    totalHistoryEvents < 50000
                    && // 履歴制限チェック
                    processedCount >= 0 // 基本整合性

                let message =
                    if isHealthy then
                        sprintf "イベント健全性OK: 保留中=%d 履歴=%d 処理済み=%d" totalPendingEvents totalHistoryEvents processedCount
                    else
                        sprintf "イベント健全性NG: 異常検出 保留中=%d 履歴=%d" totalPendingEvents totalHistoryEvents

                logInfo "EventProcessor" message
                return Result.Ok(isHealthy, message)

            with ex ->
                logError "EventProcessor" <| sprintf "イベント健全性チェック例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    interface IEventProcessor with
        member this.RegisterTimeEvent(sprintId, event) = this.RegisterTimeEvent(sprintId, event)
        member this.GetPendingEvents(sprintId) = this.GetPendingEvents(sprintId)
        member this.ProcessEvents(sprintId) = this.ProcessEvents(sprintId)

        member this.RemoveEvent(sprintId, eventPattern) =
            this.RemoveEvent(sprintId, eventPattern)

        member this.FilterEventsByType(sprintId, eventType) =
            this.FilterEventsByType(sprintId, eventType)

        member this.FilterEventsByTimeRange(sprintId, startTime, endTime) =
            this.FilterEventsByTimeRange(sprintId, startTime, endTime)

        member this.CheckDuplicateEvents(sprintId) = this.CheckDuplicateEvents(sprintId)

        member this.ExecuteStandupEvent(sprintId, participants) =
            this.ExecuteStandupEvent(sprintId, participants)

        member this.ExecuteReviewEvent(sprintId) = this.ExecuteReviewEvent(sprintId)

        member this.ExecuteDeadlineEvent(sprintId, taskId) =
            this.ExecuteDeadlineEvent(sprintId, taskId)

        member this.ExecuteEmergencyStopEvent(sprintId, reason) =
            this.ExecuteEmergencyStopEvent(sprintId, reason)

        member this.GetEventStatistics(sprintId) = this.GetEventStatistics(sprintId)
        member this.GetEventHistory(sprintId) = this.GetEventHistory(sprintId)
        member this.PerformEventHealthCheck() = this.PerformEventHealthCheck()

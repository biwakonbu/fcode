module FCode.VirtualTimeCoordinator

open System
open System.Collections.Concurrent
open System.Timers
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IVirtualTimeManager
open FCode.Collaboration.ITimeCalculationManager
open FCode.Collaboration.IMeetingScheduler
open FCode.Collaboration.IEventProcessor
open FCode.Logger

/// VirtualTimeCoordinator: 統合制御・ファサード実装
type VirtualTimeCoordinator
    (
        timeCalculationManager: ITimeCalculationManager,
        meetingScheduler: IMeetingScheduler,
        eventProcessor: IEventProcessor,
        config: VirtualTimeConfig
    ) =

    let sprintTimers = ConcurrentDictionary<string, Timer>()

    /// タイマーイベントハンドラー
    member private this.OnTimerElapsed(sprintId: string) =
        async {
            try
                logInfo "VirtualTimeCoordinator" <| sprintf "タイマーイベント発生: Sprint=%s" sprintId

                // 仮想時間コンテキスト更新
                let! updateResult = timeCalculationManager.UpdateVirtualTimeContext(sprintId)

                match updateResult with
                | Result.Ok updatedContext ->
                    logInfo "VirtualTimeCoordinator"
                    <| sprintf "仮想時間更新: %A" updatedContext.CurrentVirtualTime

                    // イベント処理
                    let! eventResult = eventProcessor.ProcessEvents(sprintId)

                    match eventResult with
                    | Result.Ok events ->
                        if not (List.isEmpty events) then
                            logInfo "VirtualTimeCoordinator" <| sprintf "イベント処理完了: %d件" events.Length
                    | Result.Error error -> logError "VirtualTimeCoordinator" <| sprintf "イベント処理エラー: %A" error

                | Result.Error error -> logError "VirtualTimeCoordinator" <| sprintf "時間更新エラー: %A" error

            with ex ->
                logError "VirtualTimeCoordinator" <| sprintf "タイマーイベント例外: %s" ex.Message
        }
        |> (fun computation ->
            try
                Async.Start(computation)
            with ex ->
                logError "VirtualTimeCoordinator" <| sprintf "タイマー非同期例外: %s" ex.Message)

    /// スプリント開始
    member this.StartSprint(sprintId: string) =
        async {
            try
                logInfo "VirtualTimeCoordinator" <| sprintf "スプリント開始: %s" sprintId

                // スプリントコンテキスト作成
                let context = timeCalculationManager.CreateSprintContext(sprintId, config)

                // タイマー設定（1分間隔）
                let timer = new Timer(float config.VirtualHourDurationMs)
                timer.Elapsed.Add(fun _ -> this.OnTimerElapsed(sprintId))
                timer.AutoReset <- true
                timer.Start()
                sprintTimers.[sprintId] <- timer

                // 初回スタンドアップスケジュール
                let! standupResult = meetingScheduler.ScheduleNextStandup(sprintId, [])

                match standupResult with
                | Result.Ok _ -> logInfo "VirtualTimeCoordinator" <| sprintf "スプリント開始完了: %s" sprintId
                | Result.Error error -> logWarning "VirtualTimeCoordinator" <| sprintf "初回スタンドアップスケジュール失敗: %A" error

                return Result.Ok context

            with ex ->
                logError "VirtualTimeCoordinator" <| sprintf "スプリント開始例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スプリント停止
    member this.StopSprint(sprintId: string) =
        async {
            try
                logInfo "VirtualTimeCoordinator" <| sprintf "スプリント停止: %s" sprintId

                // タイマー停止
                match sprintTimers.TryRemove(sprintId) with
                | true, timer ->
                    timer.Stop()
                    timer.Dispose()
                    logInfo "VirtualTimeCoordinator" <| sprintf "スプリント停止完了: %s" sprintId
                    return Result.Ok()
                | false, _ ->
                    logWarning "VirtualTimeCoordinator" <| sprintf "スプリント未発見: %s" sprintId
                    return Result.Error(NotFound $"スプリント {sprintId} が見つかりません")

            with ex ->
                logError "VirtualTimeCoordinator" <| sprintf "スプリント停止例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// アクティブスプリント一覧
    member this.GetActiveSprints() =
        async {
            try
                let contexts = [] // 暫定的に空リストを返す、後でインターフェースに追加
                logInfo "VirtualTimeCoordinator" <| sprintf "アクティブスプリント一覧: %d件" contexts.Length
                return Result.Ok contexts
            with ex ->
                logError "VirtualTimeCoordinator" <| sprintf "アクティブスプリント取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スプリント統計取得
    member this.GetSprintStatistics(sprintId: string) =
        async {
            try
                // 時間統計
                let! progressResult = timeCalculationManager.CalculateSprintProgress(sprintId)
                let! remainingResult = timeCalculationManager.CalculateRemainingTime(sprintId)

                // MTG統計
                let! meetingStatsResult = meetingScheduler.GetMeetingStatistics(sprintId)

                // イベント統計
                let! eventStatsResult = eventProcessor.GetEventStatistics(sprintId)

                match (progressResult, remainingResult, meetingStatsResult, eventStatsResult) with
                | (Result.Ok progress, Result.Ok remaining, Result.Ok meetingStats, Result.Ok eventStats) ->
                    let statistics =
                        [ StringMetric("SprintId", sprintId)
                          FloatMetric("Progress", progress)
                          TimeSpanMetric("RemainingTime", remaining)
                          StringMetric("LastUpdate", DateTime.UtcNow.ToString()) ]
                        @ (meetingStats
                           |> List.map (fun (name, count) -> IntMetric($"Meeting_{name}", count)))
                        @ (eventStats |> List.map (fun (name, count) -> IntMetric($"Event_{name}", count)))

                    logInfo "VirtualTimeCoordinator" <| sprintf "スプリント統計取得: %s" sprintId
                    return Result.Ok statistics

                | (Result.Error progressError, _, _, _) ->
                    logError "VirtualTimeCoordinator" <| sprintf "進捗統計取得失敗: %A" progressError
                    return Result.Error(SystemError $"進捗統計取得失敗: {progressError}")
                | (_, Result.Error remainingError, _, _) ->
                    logError "VirtualTimeCoordinator" <| sprintf "残り時間統計取得失敗: %A" remainingError
                    return Result.Error(SystemError $"残り時間統計取得失敗: {remainingError}")
                | (_, _, Result.Error meetingError, _) ->
                    logError "VirtualTimeCoordinator" <| sprintf "MTG統計取得失敗: %A" meetingError
                    return Result.Error(SystemError $"MTG統計取得失敗: {meetingError}")
                | (_, _, _, Result.Error eventError) ->
                    logError "VirtualTimeCoordinator" <| sprintf "イベント統計取得失敗: %A" eventError
                    return Result.Error(SystemError $"イベント統計取得失敗: {eventError}")

            with ex ->
                logError "VirtualTimeCoordinator" <| sprintf "スプリント統計取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// システム健全性チェック
    member this.PerformHealthCheck() =
        async {
            try
                // 各コンポーネントの健全性チェック
                let! meetingHealthResult = meetingScheduler.PerformHealthCheck()
                let! eventHealthResult = eventProcessor.PerformEventHealthCheck()

                match (meetingHealthResult, eventHealthResult) with
                | (Result.Ok(meetingOk, meetingMsg), Result.Ok(eventOk, eventMsg)) ->
                    let activeTimersCount = sprintTimers.Count
                    let timerHealthy = activeTimersCount < 100 // タイマー制限

                    let overallHealthy = meetingOk && eventOk && timerHealthy

                    let message =
                        if overallHealthy then
                            sprintf
                                "VirtualTimeCoordinator健全性OK: タイマー=%d件, MTG=%s, Event=%s"
                                activeTimersCount
                                meetingMsg
                                eventMsg
                        else
                            sprintf
                                "VirtualTimeCoordinator健全性NG: タイマー=%d件, MTG=%s, Event=%s"
                                activeTimersCount
                                meetingMsg
                                eventMsg

                    logInfo "VirtualTimeCoordinator" message
                    return Result.Ok(overallHealthy, message)

                | (Result.Error meetingError, _) ->
                    let message = $"MTG健全性チェック失敗: {meetingError}"
                    logError "VirtualTimeCoordinator" message
                    return Result.Ok(false, message)
                | (_, Result.Error eventError) ->
                    let message = $"イベント健全性チェック失敗: {eventError}"
                    logError "VirtualTimeCoordinator" message
                    return Result.Ok(false, message)
                | _ ->
                    let message = "健全性チェック予期しない失敗"
                    logError "VirtualTimeCoordinator" message
                    return Result.Ok(false, message)

            with ex ->
                logError "VirtualTimeCoordinator" <| sprintf "健全性チェック例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// リソース解放
    member this.Dispose() =
        try
            // 全タイマー停止（例外安全）
            let timersToDispose = sprintTimers.Values |> Seq.toList

            for timer in timersToDispose do
                try
                    timer.Stop()
                    timer.Dispose()
                with ex ->
                    logError "VirtualTimeCoordinator" <| sprintf "タイマー解放エラー: %s" ex.Message

            sprintTimers.Clear()
            logInfo "VirtualTimeCoordinator" "VirtualTimeCoordinator disposed successfully"
        with ex ->
            logError "VirtualTimeCoordinator" <| sprintf "Dispose例外: %s" ex.Message

    interface IVirtualTimeManager with
        member this.StartSprint(sprintId) = this.StartSprint(sprintId)
        member this.StopSprint(sprintId) = this.StopSprint(sprintId)

        member this.GetCurrentVirtualTime(sprintId) =
            timeCalculationManager.GetCurrentVirtualTime(sprintId)

        member this.CalculateVirtualTime(realElapsed) =
            timeCalculationManager.CalculateVirtualTime(realElapsed)

        member this.CalculateRealDuration(virtualTime) =
            timeCalculationManager.CalculateRealDuration(virtualTime)

        member this.ScheduleNextStandup(sprintId, participants) =
            meetingScheduler.ScheduleNextStandup(sprintId, participants)

        member this.ExecuteStandup(meetingId, progressReports) =
            meetingScheduler.ExecuteStandup(meetingId, progressReports)

        member this.GetStandupHistory(sprintId) =
            meetingScheduler.GetStandupHistory(sprintId)

        member this.TriggerReviewMeeting(sprintId) =
            meetingScheduler.TriggerReviewMeeting(sprintId)

        member this.AssessCompletion(sprintId, taskIds) =
            meetingScheduler.AssessCompletion(sprintId, taskIds)

        member this.DecideContinuation(sprintId, assessment) =
            meetingScheduler.DecideContinuation(sprintId, assessment)

        member this.RegisterTimeEvent(sprintId, event) =
            eventProcessor.RegisterTimeEvent(sprintId, event)

        member this.GetPendingEvents(sprintId) =
            eventProcessor.GetPendingEvents(sprintId)

        member this.ProcessEvents(sprintId) = eventProcessor.ProcessEvents(sprintId)
        member this.GetActiveSprints() = this.GetActiveSprints()
        member this.GetSprintStatistics(sprintId) = this.GetSprintStatistics(sprintId)
        member this.PerformHealthCheck() = this.PerformHealthCheck()

    interface IDisposable with
        member this.Dispose() = this.Dispose()

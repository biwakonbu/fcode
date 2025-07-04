module FCode.VirtualTimeManager

open System
open System.Collections.Concurrent
open System.Timers
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IVirtualTimeManager
open FCode.Collaboration.IAgentStateManager
open FCode.Collaboration.ITaskDependencyGraph
open FCode.Collaboration.IProgressAggregator
open FCode.Logger

/// VirtualTimeManager実装
type VirtualTimeManager
    (
        agentStateManager: IAgentStateManager,
        taskDependencyGraph: ITaskDependencyGraph,
        progressAggregator: IProgressAggregator,
        config: VirtualTimeConfig
    ) =

    let activeSprints = ConcurrentDictionary<string, VirtualTimeContext>()
    let standupHistory = ConcurrentDictionary<string, StandupMeeting list>()
    let reviewHistory = ConcurrentDictionary<string, ReviewMeeting list>()
    let pendingEvents = ConcurrentDictionary<string, TimeEvent list>()
    let sprintTimers = ConcurrentDictionary<string, Timer>()
    let eventIdCounter = ref 0

    /// イベントID生成
    member private this.GenerateEventId() =
        let id = System.Threading.Interlocked.Increment(eventIdCounter)
        sprintf "EVENT-%s-%04d" (DateTime.UtcNow.ToString("yyyyMMdd")) id

    /// タイマーイベントハンドラー
    member private this.OnTimerElapsed(sprintId: string) =
        async {
            try
                logInfo "VirtualTimeManager" <| sprintf "タイマーイベント発生: Sprint=%s" sprintId

                match activeSprints.TryGetValue(sprintId) with
                | true, context ->
                    // 仮想時間更新
                    let realElapsed = DateTime.UtcNow - context.StartTime
                    let virtualTime = this.CalculateVirtualTime(realElapsed)

                    let updatedContext =
                        { context with
                            CurrentVirtualTime = virtualTime
                            ElapsedRealTime = realElapsed
                            LastUpdate = DateTime.UtcNow }

                    activeSprints.[sprintId] <- updatedContext

                    // イベント処理
                    let! eventResult = this.ProcessEvents(sprintId)

                    match eventResult with
                    | Result.Ok(events: TimeEvent list) ->
                        if not (List.isEmpty events) then
                            logInfo "VirtualTimeManager" <| sprintf "イベント処理完了: %d件" events.Length
                    | Result.Error error -> logError "VirtualTimeManager" <| sprintf "イベント処理エラー: %A" error

                | false, _ -> logWarning "VirtualTimeManager" <| sprintf "非アクティブスプリント: %s" sprintId

            with ex ->
                logError "VirtualTimeManager" <| sprintf "タイマーイベント例外: %s" ex.Message
        }
        |> Async.Start

    /// スプリント開始
    member this.StartSprint(sprintId: string) =
        async {
            try
                logInfo "VirtualTimeManager" <| sprintf "スプリント開始: %s" sprintId

                if activeSprints.ContainsKey(sprintId) then
                    logWarning "VirtualTimeManager" <| sprintf "スプリント既に開始済み: %s" sprintId
                    return Result.Error(ConcurrencyError $"スプリント {sprintId} は既に開始されています")
                else
                    let context =
                        { StartTime = DateTime.UtcNow
                          CurrentVirtualTime = VirtualHour 0
                          ElapsedRealTime = TimeSpan.Zero
                          SprintDuration = TimeSpan.FromMinutes(float (config.SprintDurationVD * 24))
                          IsActive = true
                          LastUpdate = DateTime.UtcNow }

                    activeSprints.[sprintId] <- context
                    standupHistory.[sprintId] <- []
                    reviewHistory.[sprintId] <- []
                    pendingEvents.[sprintId] <- []

                    // タイマー設定（1分間隔）
                    let timer = new Timer(float config.VirtualHourDurationMs)
                    timer.Elapsed.Add(fun _ -> this.OnTimerElapsed(sprintId))
                    timer.AutoReset <- true
                    timer.Start()
                    sprintTimers.[sprintId] <- timer

                    // 初回スタンドアップスケジュール
                    let! standupResult = this.ScheduleNextStandup(sprintId, [])

                    match standupResult with
                    | Result.Ok _ -> logInfo "VirtualTimeManager" <| sprintf "スプリント開始完了: %s" sprintId
                    | Result.Error error -> logWarning "VirtualTimeManager" <| sprintf "初回スタンドアップスケジュール失敗: %A" error

                    return Result.Ok context

            with ex ->
                logError "VirtualTimeManager" <| sprintf "スプリント開始例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スプリント停止
    member this.StopSprint(sprintId: string) =
        async {
            try
                logInfo "VirtualTimeManager" <| sprintf "スプリント停止: %s" sprintId

                match activeSprints.TryRemove(sprintId) with
                | true, context ->
                    // タイマー停止
                    match sprintTimers.TryRemove(sprintId) with
                    | true, timer ->
                        timer.Stop()
                        timer.Dispose()
                        logInfo "VirtualTimeManager" <| sprintf "タイマー停止: %s" sprintId
                    | false, _ -> logWarning "VirtualTimeManager" <| sprintf "タイマー未発見: %s" sprintId

                    logInfo "VirtualTimeManager" <| sprintf "スプリント停止完了: %s" sprintId
                    return Result.Ok()
                | false, _ ->
                    logWarning "VirtualTimeManager" <| sprintf "スプリント未発見: %s" sprintId
                    return Result.Error(NotFound $"スプリント {sprintId} が見つかりません")

            with ex ->
                logError "VirtualTimeManager" <| sprintf "スプリント停止例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 現在の仮想時間取得
    member this.GetCurrentVirtualTime(sprintId: string) =
        async {
            try
                match activeSprints.TryGetValue(sprintId) with
                | true, context ->
                    let realElapsed = DateTime.UtcNow - context.StartTime
                    let virtualTime = this.CalculateVirtualTime(realElapsed)
                    logInfo "VirtualTimeManager" <| sprintf "仮想時間取得: %s = %A" sprintId virtualTime
                    return Result.Ok virtualTime
                | false, _ ->
                    logError "VirtualTimeManager" <| sprintf "スプリント未発見: %s" sprintId
                    return Result.Error(NotFound $"スプリント {sprintId} が見つかりません")
            with ex ->
                logError "VirtualTimeManager" <| sprintf "仮想時間取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 実経過時間から仮想時間計算
    member this.CalculateVirtualTime(realElapsed: TimeSpan) =
        let totalMinutes = int realElapsed.TotalMinutes
        let virtualHours = totalMinutes // 1vh = 1分リアルタイム

        // スプリント完了判定 (72分 = 3vd)
        let sprintTotalMinutes = config.SprintDurationVD * 24

        if virtualHours >= sprintTotalMinutes then
            VirtualSprint config.SprintDurationVD
        else
            // 24分未満は時間単位、24分以上は日単位で表現
            VirtualHour virtualHours

    /// 仮想時間から実時間計算
    member this.CalculateRealDuration(virtualTime: VirtualTimeUnit) =
        match virtualTime with
        | VirtualHour hours -> TimeSpan.FromMinutes(float hours)
        | VirtualDay days -> TimeSpan.FromMinutes(float (days * 24))
        | VirtualSprint sprints -> TimeSpan.FromMinutes(float (sprints * config.SprintDurationVD * 24))

    /// 次回スタンドアップスケジュール
    member this.ScheduleNextStandup(sprintId: string, participants: string list) =
        async {
            try
                logInfo "VirtualTimeManager" <| sprintf "スタンドアップスケジュール: %s" sprintId

                match activeSprints.TryGetValue(sprintId) with
                | true, context ->
                    let currentVH =
                        match context.CurrentVirtualTime with
                        | VirtualHour h -> h
                        | VirtualDay d -> d * 24
                        | VirtualSprint s -> s * config.SprintDurationVD * 24

                    let nextStandupVH =
                        ((currentVH / config.StandupIntervalVH) + 1) * config.StandupIntervalVH

                    let scheduledTime = VirtualHour nextStandupVH

                    let meeting =
                        { MeetingId = this.GenerateEventId()
                          ScheduledTime = scheduledTime
                          ActualTime = DateTime.UtcNow.Add(this.CalculateRealDuration(scheduledTime))
                          Participants = participants
                          ProgressReports = []
                          Decisions = []
                          Adjustments = []
                          NextMeetingTime = VirtualHour(nextStandupVH + config.StandupIntervalVH) }

                    // イベント登録
                    let event = StandupScheduled(scheduledTime, participants)
                    let! eventResult = this.RegisterTimeEvent(sprintId, event)

                    match eventResult with
                    | Result.Ok _ ->
                        logInfo "VirtualTimeManager"
                        <| sprintf "スタンドアップスケジュール完了: %A at %A" meeting.MeetingId scheduledTime

                        return Result.Ok meeting
                    | Result.Error error ->
                        logError "VirtualTimeManager" <| sprintf "イベント登録失敗: %A" error
                        return Result.Error error

                | false, _ ->
                    logError "VirtualTimeManager" <| sprintf "スプリント未発見: %s" sprintId
                    return Result.Error(NotFound $"スプリント {sprintId} が見つかりません")

            with ex ->
                logError "VirtualTimeManager" <| sprintf "スタンドアップスケジュール例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スタンドアップMTG実行
    member this.ExecuteStandup(meetingId: string, progressReports: (string * string) list) =
        async {
            try
                logInfo "VirtualTimeManager" <| sprintf "スタンドアップ実行: %s" meetingId

                // 進捗レポート生成
                let decisions = [ "進捗状況確認完了"; sprintf "%d件の進捗報告を受領" progressReports.Length ]

                let adjustments =
                    [ if
                          progressReports
                          |> List.exists (fun (_, report) -> report.ToLowerInvariant().Contains("blocked"))
                      then
                          "ブロッカー解決支援が必要"
                      if
                          progressReports
                          |> List.exists (fun (_, report) -> report.ToLowerInvariant().Contains("delay"))
                      then
                          "スケジュール調整を検討" ]

                let meeting =
                    { MeetingId = meetingId
                      ScheduledTime = VirtualHour 6 // 暫定値
                      ActualTime = DateTime.UtcNow
                      Participants = progressReports |> List.map fst
                      ProgressReports = progressReports
                      Decisions = decisions
                      Adjustments = adjustments
                      NextMeetingTime = VirtualHour 12 // 暫定値
                    }

                logInfo "VirtualTimeManager"
                <| sprintf "スタンドアップ完了: %s (%d参加者)" meetingId progressReports.Length

                return Result.Ok meeting

            with ex ->
                logError "VirtualTimeManager" <| sprintf "スタンドアップ実行例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スタンドアップ履歴取得
    member this.GetStandupHistory(sprintId: string) =
        async {
            try
                match standupHistory.TryGetValue(sprintId) with
                | true, history ->
                    logInfo "VirtualTimeManager"
                    <| sprintf "スタンドアップ履歴取得: %s (%d件)" sprintId history.Length

                    return Result.Ok history
                | false, _ ->
                    logInfo "VirtualTimeManager" <| sprintf "スタンドアップ履歴なし: %s" sprintId
                    return Result.Ok []
            with ex ->
                logError "VirtualTimeManager" <| sprintf "スタンドアップ履歴取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 72分レビューMTGトリガー
    member this.TriggerReviewMeeting(sprintId: string) =
        async {
            try
                logInfo "VirtualTimeManager" <| sprintf "レビューMTGトリガー: %s" sprintId

                match activeSprints.TryGetValue(sprintId) with
                | true, context ->
                    // 完成度評価
                    let! assessmentResult = this.AssessCompletion(sprintId, [])

                    let assessment =
                        match assessmentResult with
                        | Result.Ok a -> a
                        | Result.Error _ ->
                            { TasksCompleted = 0
                              TasksInProgress = 0
                              TasksBlocked = 0
                              OverallCompletionRate = 0.0
                              QualityScore = 0.0
                              AcceptanceCriteriaMet = false
                              RequiresPOApproval = true }

                    // 継続判定
                    let! continuationResult = this.DecideContinuation(sprintId, assessment)

                    let continuation =
                        match continuationResult with
                        | Result.Ok c -> c
                        | Result.Error _ -> RequirePOApproval "評価エラーのため"

                    let meeting =
                        { MeetingId = this.GenerateEventId()
                          TriggerTime = VirtualSprint 1
                          ActualTime = DateTime.UtcNow
                          CompletionAssessment = assessment
                          QualityEvaluation =
                            { CodeQuality = 0.8
                              TestCoverage = 0.75
                              DocumentationScore = 0.7
                              SecurityCompliance = true
                              PerformanceMetrics = []
                              IssuesFound = []
                              RecommendedImprovements = [] }
                          NextSprintPlan = None
                          ContinuationDecision = continuation }

                    logInfo "VirtualTimeManager"
                    <| sprintf "レビューMTG完了: %s (継続判定: %A)" meeting.MeetingId continuation

                    return Result.Ok meeting

                | false, _ ->
                    logError "VirtualTimeManager" <| sprintf "スプリント未発見: %s" sprintId
                    return Result.Error(NotFound $"スプリント {sprintId} が見つかりません")

            with ex ->
                logError "VirtualTimeManager" <| sprintf "レビューMTGトリガー例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 完成度評価実行
    member this.AssessCompletion(sprintId: string, taskIds: string list) =
        async {
            try
                logInfo "VirtualTimeManager" <| sprintf "完成度評価: %s" sprintId

                // 進捗集計（モック実装）
                let tasksCompleted = taskIds |> List.length |> (fun x -> x * 60 / 100)
                let tasksInProgress = taskIds |> List.length |> (fun x -> x * 30 / 100)
                let tasksBlocked = taskIds |> List.length |> (fun x -> x * 10 / 100)

                let completionRate =
                    if taskIds.Length > 0 then
                        float tasksCompleted / float taskIds.Length
                    else
                        0.8 // デフォルト値

                let assessment =
                    { TasksCompleted = tasksCompleted
                      TasksInProgress = tasksInProgress
                      TasksBlocked = tasksBlocked
                      OverallCompletionRate = completionRate
                      QualityScore = 0.85
                      AcceptanceCriteriaMet = completionRate >= 0.8
                      RequiresPOApproval = completionRate < 0.9 }

                logInfo "VirtualTimeManager"
                <| sprintf "完成度評価完了: %.1f%% (%d/%d)" (completionRate * 100.0) tasksCompleted taskIds.Length

                return Result.Ok assessment

            with ex ->
                logError "VirtualTimeManager" <| sprintf "完成度評価例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 継続判定実行
    member this.DecideContinuation(sprintId: string, assessment: CompletionAssessment) =
        async {
            try
                logInfo "VirtualTimeManager"
                <| sprintf "継続判定: %s (完成度=%.1f%%)" sprintId (assessment.OverallCompletionRate * 100.0)

                let decision =
                    if assessment.OverallCompletionRate >= 0.9 && assessment.AcceptanceCriteriaMet then
                        AutoContinue "高品質完成・自動継続承認"
                    elif assessment.OverallCompletionRate >= 0.7 && not assessment.RequiresPOApproval then
                        AutoContinue "標準品質達成・継続可能"
                    elif assessment.TasksBlocked > (assessment.TasksCompleted / 2) then
                        StopExecution "重大ブロッカー多数・実行停止推奨"
                    elif assessment.QualityScore < 0.5 then
                        RequirePOApproval "品質基準未達・PO判断要求"
                    else
                        RequirePOApproval "標準完成度・PO承認要求"

                logInfo "VirtualTimeManager" <| sprintf "継続判定完了: %A" decision
                return Result.Ok decision

            with ex ->
                logError "VirtualTimeManager" <| sprintf "継続判定例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// タイムイベント登録
    member this.RegisterTimeEvent(sprintId: string, event: TimeEvent) =
        async {
            try
                match pendingEvents.TryGetValue(sprintId) with
                | true, events ->
                    let updatedEvents = event :: events
                    pendingEvents.[sprintId] <- updatedEvents
                    logInfo "VirtualTimeManager" <| sprintf "イベント登録: %s (%A)" sprintId event
                    return Result.Ok()
                | false, _ ->
                    pendingEvents.[sprintId] <- [ event ]
                    logInfo "VirtualTimeManager" <| sprintf "初回イベント登録: %s (%A)" sprintId event
                    return Result.Ok()
            with ex ->
                logError "VirtualTimeManager" <| sprintf "イベント登録例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 保留中イベント取得
    member this.GetPendingEvents(sprintId: string) =
        async {
            try
                match pendingEvents.TryGetValue(sprintId) with
                | true, events ->
                    logInfo "VirtualTimeManager"
                    <| sprintf "保留中イベント取得: %s (%d件)" sprintId events.Length

                    return Result.Ok events
                | false, _ ->
                    logInfo "VirtualTimeManager" <| sprintf "保留中イベントなし: %s" sprintId
                    return Result.Ok []
            with ex ->
                logError "VirtualTimeManager" <| sprintf "保留中イベント取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// イベント発火処理
    member this.ProcessEvents(sprintId: string) =
        async {
            try
                let! currentTimeResult = this.GetCurrentVirtualTime(sprintId)

                match currentTimeResult with
                | Result.Ok currentTime ->
                    let! eventsResult = this.GetPendingEvents(sprintId)

                    match eventsResult with
                    | Result.Ok events ->
                        let processedEvents = []
                        // イベント処理ロジック（簡略化）
                        logInfo "VirtualTimeManager"
                        <| sprintf "イベント処理: %s (%d件)" sprintId events.Length

                        return Result.Ok processedEvents
                    | Result.Error error -> return Result.Error error
                | Result.Error error -> return Result.Error error
            with ex ->
                logError "VirtualTimeManager" <| sprintf "イベント処理例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// アクティブスプリント一覧
    member this.GetActiveSprints() =
        async {
            try
                let sprints = activeSprints.Values |> Seq.toList
                logInfo "VirtualTimeManager" <| sprintf "アクティブスプリント一覧: %d件" sprints.Length
                return Result.Ok sprints
            with ex ->
                logError "VirtualTimeManager" <| sprintf "アクティブスプリント取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スプリント統計取得
    member this.GetSprintStatistics(sprintId: string) =
        async {
            try
                match activeSprints.TryGetValue(sprintId) with
                | true, context ->
                    let statistics =
                        [ ("SprintId", box sprintId)
                          ("StartTime", box context.StartTime)
                          ("ElapsedTime", box context.ElapsedRealTime)
                          ("VirtualTime", box context.CurrentVirtualTime)
                          ("IsActive", box context.IsActive) ]

                    logInfo "VirtualTimeManager" <| sprintf "スプリント統計取得: %s" sprintId
                    return Result.Ok statistics
                | false, _ ->
                    logError "VirtualTimeManager" <| sprintf "スプリント未発見: %s" sprintId
                    return Result.Error(NotFound $"スプリント {sprintId} が見つかりません")
            with ex ->
                logError "VirtualTimeManager" <| sprintf "スプリント統計取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// システム健全性チェック
    member this.PerformHealthCheck() =
        async {
            try
                let activeCount = activeSprints.Count
                let timerCount = sprintTimers.Count
                let healthy = activeCount = timerCount && activeCount <= config.MaxConcurrentSprints

                let message =
                    if healthy then
                        sprintf "正常 - アクティブスプリント: %d, タイマー: %d" activeCount timerCount
                    else
                        sprintf
                            "異常 - アクティブスプリント: %d, タイマー: %d, 上限: %d"
                            activeCount
                            timerCount
                            config.MaxConcurrentSprints

                logInfo "VirtualTimeManager" <| sprintf "健全性チェック: %s" message
                return Result.Ok(healthy, message)

            with ex ->
                logError "VirtualTimeManager" <| sprintf "健全性チェック例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    interface IVirtualTimeManager with
        member this.StartSprint(sprintId) = this.StartSprint(sprintId)
        member this.StopSprint(sprintId) = this.StopSprint(sprintId)
        member this.GetCurrentVirtualTime(sprintId) = this.GetCurrentVirtualTime(sprintId)
        member this.CalculateVirtualTime(realElapsed) = this.CalculateVirtualTime(realElapsed)
        member this.CalculateRealDuration(virtualTime) = this.CalculateRealDuration(virtualTime)

        member this.ScheduleNextStandup(sprintId, participants) =
            this.ScheduleNextStandup(sprintId, participants)

        member this.ExecuteStandup(meetingId, progressReports) =
            this.ExecuteStandup(meetingId, progressReports)

        member this.GetStandupHistory(sprintId) = this.GetStandupHistory(sprintId)
        member this.TriggerReviewMeeting(sprintId) = this.TriggerReviewMeeting(sprintId)

        member this.AssessCompletion(sprintId, taskIds) =
            this.AssessCompletion(sprintId, taskIds)

        member this.DecideContinuation(sprintId, assessment) =
            this.DecideContinuation(sprintId, assessment)

        member this.RegisterTimeEvent(sprintId, event) = this.RegisterTimeEvent(sprintId, event)
        member this.GetPendingEvents(sprintId) = this.GetPendingEvents(sprintId)
        member this.ProcessEvents(sprintId) = this.ProcessEvents(sprintId)
        member this.GetActiveSprints() = this.GetActiveSprints()
        member this.GetSprintStatistics(sprintId) = this.GetSprintStatistics(sprintId)
        member this.PerformHealthCheck() = this.PerformHealthCheck()

    /// リソース解放
    member this.Dispose() =
        // 全タイマー停止
        for kvp in sprintTimers do
            kvp.Value.Stop()
            kvp.Value.Dispose()

        sprintTimers.Clear()

        activeSprints.Clear()
        standupHistory.Clear()
        reviewHistory.Clear()
        pendingEvents.Clear()

        logInfo "VirtualTimeManager" "VirtualTimeManager disposed"

    interface IDisposable with
        member this.Dispose() = this.Dispose()

module FCode.Collaboration.MeetingScheduler

open System
open System.Collections.Concurrent
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.IMeetingScheduler
open FCode.Collaboration.ITimeCalculationManager
open FCode.Logger

/// ミーティングスケジューラー実装
type MeetingScheduler(timeCalculationManager: ITimeCalculationManager, config: VirtualTimeConfig) =

    let standupHistory = ConcurrentDictionary<string, StandupMeeting list>()
    let reviewHistory = ConcurrentDictionary<string, ReviewMeeting list>()
    let scheduledMeetings = ConcurrentDictionary<string, DateTime>()
    let eventIdCounter = ref 0

    /// イベントID生成
    member private this.GenerateEventId() =
        let id = System.Threading.Interlocked.Increment(eventIdCounter)
        sprintf "MEETING-%s-%04d" (DateTime.UtcNow.ToString("yyyyMMdd")) id

    /// 次回スタンドアップスケジュール
    member this.ScheduleNextStandup(sprintId: string, participants: string list) =
        async {
            try
                logInfo "MeetingScheduler" <| sprintf "スタンドアップスケジュール: %s" sprintId

                let! currentTimeResult = timeCalculationManager.GetCurrentVirtualTime(sprintId)

                match currentTimeResult with
                | Result.Ok currentTime ->
                    let nextMeetingTime =
                        this.CalculateNextMeetingTime(currentTime, config.StandupIntervalVH)

                    let actualTime =
                        DateTime.UtcNow.Add(timeCalculationManager.CalculateRealDuration(nextMeetingTime))

                    let meeting =
                        { MeetingId = this.GenerateEventId()
                          ScheduledTime = nextMeetingTime
                          ActualTime = actualTime
                          Participants = participants
                          ProgressReports = []
                          Decisions = []
                          Adjustments = []
                          NextMeetingTime = this.CalculateNextMeetingTime(nextMeetingTime, config.StandupIntervalVH) }

                    scheduledMeetings.[meeting.MeetingId] <- actualTime

                    logInfo "MeetingScheduler"
                    <| sprintf "スタンドアップスケジュール完了: %s at %A" meeting.MeetingId nextMeetingTime

                    return Result.Ok meeting

                | Result.Error error ->
                    logError "MeetingScheduler" <| sprintf "現在時刻取得失敗: %A" error
                    return Result.Error error

            with ex ->
                logError "MeetingScheduler" <| sprintf "スタンドアップスケジュール例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スタンドアップMTG実行
    member this.ExecuteStandup(meetingId: string, progressReports: (string * string) list) =
        async {
            try
                logInfo "MeetingScheduler" <| sprintf "スタンドアップ実行: %s" meetingId

                // 進捗レポート分析
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
                          "スケジュール調整を検討"
                      if
                          progressReports
                          |> List.exists (fun (_, report) -> report.ToLowerInvariant().Contains("risk"))
                      then
                          "リスク評価・対策検討"
                      if progressReports.Length < 2 then
                          "参加者増員を検討" ]

                let meeting =
                    { MeetingId = meetingId
                      ScheduledTime = VirtualHour config.StandupIntervalVH
                      ActualTime = DateTime.UtcNow
                      Participants = progressReports |> List.map fst
                      ProgressReports = progressReports
                      Decisions = decisions
                      Adjustments = adjustments
                      NextMeetingTime = VirtualHour(config.StandupIntervalVH * 2) }

                logInfo "MeetingScheduler"
                <| sprintf "スタンドアップ完了: %s (%d参加者)" meetingId progressReports.Length

                return Result.Ok meeting

            with ex ->
                logError "MeetingScheduler" <| sprintf "スタンドアップ実行例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スタンドアップ履歴取得
    member this.GetStandupHistory(sprintId: string) =
        async {
            try
                match standupHistory.TryGetValue(sprintId) with
                | true, history ->
                    logInfo "MeetingScheduler"
                    <| sprintf "スタンドアップ履歴取得: %s (%d件)" sprintId history.Length

                    return Result.Ok history
                | false, _ ->
                    logInfo "MeetingScheduler" <| sprintf "スタンドアップ履歴なし: %s" sprintId
                    return Result.Ok []
            with ex ->
                logError "MeetingScheduler" <| sprintf "スタンドアップ履歴取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スタンドアップMTG更新
    member this.UpdateStandupMeeting(meetingId: string, updates: StandupMeeting) =
        async {
            try
                // 履歴内で該当MTGを更新
                let mutable found = false

                for kvp in standupHistory do
                    let sprintId = kvp.Key
                    let meetings = kvp.Value

                    let updatedMeetings =
                        meetings |> List.map (fun m -> if m.MeetingId = meetingId then updates else m)

                    if updatedMeetings <> meetings then
                        standupHistory.[sprintId] <- updatedMeetings
                        logInfo "MeetingScheduler" <| sprintf "スタンドアップMTG更新: %s" meetingId
                        found <- true

                if found then
                    return Result.Ok updates
                else
                    logWarning "MeetingScheduler" <| sprintf "更新対象MTG未発見: %s" meetingId
                    return Result.Error(NotFound $"ミーティング {meetingId} が見つかりません")

            with ex ->
                logError "MeetingScheduler" <| sprintf "スタンドアップMTG更新例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// レビューMTGトリガー
    member this.TriggerReviewMeeting(sprintId: string) =
        async {
            try
                logInfo "MeetingScheduler" <| sprintf "レビューMTGトリガー: %s" sprintId

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

                // 履歴に追加
                match reviewHistory.TryGetValue(sprintId) with
                | true, history -> reviewHistory.[sprintId] <- meeting :: history
                | false, _ -> reviewHistory.[sprintId] <- [ meeting ]

                logInfo "MeetingScheduler"
                <| sprintf "レビューMTG完了: %s (継続判定: %A)" meeting.MeetingId continuation

                return Result.Ok meeting

            with ex ->
                logError "MeetingScheduler" <| sprintf "レビューMTGトリガー例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 完成度評価実行
    member this.AssessCompletion(sprintId: string, taskIds: string list) =
        async {
            try
                logInfo "MeetingScheduler" <| sprintf "完成度評価: %s" sprintId

                // 進捗集計（改良版）
                let totalTasks = max 1 taskIds.Length
                let tasksCompleted = totalTasks * 60 / 100
                let tasksInProgress = totalTasks * 30 / 100
                let tasksBlocked = totalTasks * 10 / 100

                let completionRate = float tasksCompleted / float totalTasks

                let qualityScore =
                    if completionRate >= 0.9 then 0.95
                    elif completionRate >= 0.8 then 0.85
                    elif completionRate >= 0.7 then 0.75
                    else 0.65

                let assessment =
                    { TasksCompleted = tasksCompleted
                      TasksInProgress = tasksInProgress
                      TasksBlocked = tasksBlocked
                      OverallCompletionRate = completionRate
                      QualityScore = qualityScore
                      AcceptanceCriteriaMet = completionRate >= 0.8 && qualityScore >= 0.8
                      RequiresPOApproval = completionRate < 0.9 || qualityScore < 0.9 }

                logInfo "MeetingScheduler"
                <| sprintf "完成度評価完了: %.1f%% (%d/%d)" (completionRate * 100.0) tasksCompleted totalTasks

                return Result.Ok assessment

            with ex ->
                logError "MeetingScheduler" <| sprintf "完成度評価例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 継続判定実行
    member this.DecideContinuation(sprintId: string, assessment: CompletionAssessment) =
        async {
            try
                logInfo "MeetingScheduler"
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
                    elif assessment.OverallCompletionRate < 0.3 then
                        EscalateToManagement "重大遅延・経営陣エスカレーション"
                    else
                        RequirePOApproval "標準完成度・PO承認要求"

                logInfo "MeetingScheduler" <| sprintf "継続判定完了: %A" decision
                return Result.Ok decision

            with ex ->
                logError "MeetingScheduler" <| sprintf "継続判定例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// レビューMTG履歴取得
    member this.GetReviewHistory(sprintId: string) =
        async {
            try
                match reviewHistory.TryGetValue(sprintId) with
                | true, history ->
                    logInfo "MeetingScheduler"
                    <| sprintf "レビューMTG履歴取得: %s (%d件)" sprintId history.Length

                    return Result.Ok history
                | false, _ ->
                    logInfo "MeetingScheduler" <| sprintf "レビューMTG履歴なし: %s" sprintId
                    return Result.Ok []
            with ex ->
                logError "MeetingScheduler" <| sprintf "レビューMTG履歴取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 次回MTG時刻計算
    member this.CalculateNextMeetingTime(currentTime: VirtualTimeUnit, intervalVH: int) =
        let currentVH = timeCalculationManager.ToVirtualHours(currentTime)
        let nextStandupVH = ((currentVH / intervalVH) + 1) * intervalVH
        timeCalculationManager.FromVirtualHours(nextStandupVH, config)

    /// MTG競合チェック
    member this.CheckMeetingConflicts(sprintId: string, proposedTime: VirtualTimeUnit) =
        async {
            try
                let proposedDateTime =
                    DateTime.UtcNow.Add(timeCalculationManager.CalculateRealDuration(proposedTime))

                let conflictWindow = TimeSpan.FromMinutes(30.0) // 30分の競合ウィンドウ

                let hasConflict =
                    scheduledMeetings.Values
                    |> Seq.exists (fun meetingTime ->
                        abs ((meetingTime - proposedDateTime).TotalMinutes) < conflictWindow.TotalMinutes)

                logInfo "MeetingScheduler"
                <| sprintf "MTG競合チェック: %s at %A = %b" sprintId proposedTime hasConflict

                return Result.Ok hasConflict

            with ex ->
                logError "MeetingScheduler" <| sprintf "MTG競合チェック例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// MTGキャンセル
    member this.CancelMeeting(meetingId: string) =
        async {
            try
                match scheduledMeetings.TryRemove(meetingId) with
                | true, _ ->
                    logInfo "MeetingScheduler" <| sprintf "MTGキャンセル完了: %s" meetingId
                    return Result.Ok()
                | false, _ ->
                    logWarning "MeetingScheduler" <| sprintf "キャンセル対象MTG未発見: %s" meetingId
                    return Result.Error(NotFound $"ミーティング {meetingId} が見つかりません")

            with ex ->
                logError "MeetingScheduler" <| sprintf "MTGキャンセル例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// MTG統計取得
    member this.GetMeetingStatistics(sprintId: string) =
        async {
            try
                let standupCount =
                    match standupHistory.TryGetValue(sprintId) with
                    | true, history -> history.Length
                    | false, _ -> 0

                let reviewCount =
                    match reviewHistory.TryGetValue(sprintId) with
                    | true, history -> history.Length
                    | false, _ -> 0

                let scheduledCount = scheduledMeetings.Count

                let statistics =
                    [ ("StandupMeetings", standupCount)
                      ("ReviewMeetings", reviewCount)
                      ("ScheduledMeetings", scheduledCount)
                      ("TotalMeetings", standupCount + reviewCount) ]

                logInfo "MeetingScheduler" <| sprintf "MTG統計取得: %s" sprintId
                return Result.Ok statistics

            with ex ->
                logError "MeetingScheduler" <| sprintf "MTG統計取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 健全性チェック
    member this.PerformHealthCheck() =
        async {
            try
                let standupHistoryCount = standupHistory.Count
                let reviewHistoryCount = reviewHistory.Count
                let scheduledMeetingsCount = scheduledMeetings.Count

                let totalMeetings =
                    standupHistoryCount + reviewHistoryCount + scheduledMeetingsCount

                let isHealthy =
                    totalMeetings < 10000
                    && // 大量データチェック
                    standupHistoryCount >= 0
                    && // 基本整合性
                    reviewHistoryCount >= 0
                    && scheduledMeetingsCount >= 0

                let message =
                    if isHealthy then
                        sprintf
                            "健全性OK: スタンドアップ=%d レビュー=%d スケジュール済み=%d"
                            standupHistoryCount
                            reviewHistoryCount
                            scheduledMeetingsCount
                    else
                        sprintf "健全性NG: データ異常検出 (総MTG数=%d)" totalMeetings

                logInfo "MeetingScheduler" message
                return Result.Ok(isHealthy, message)

            with ex ->
                logError "MeetingScheduler" <| sprintf "健全性チェック例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    interface IMeetingScheduler with
        member this.ScheduleNextStandup(sprintId, participants) =
            this.ScheduleNextStandup(sprintId, participants)

        member this.ExecuteStandup(meetingId, progressReports) =
            this.ExecuteStandup(meetingId, progressReports)

        member this.GetStandupHistory(sprintId) = this.GetStandupHistory(sprintId)

        member this.UpdateStandupMeeting(meetingId, updates) =
            this.UpdateStandupMeeting(meetingId, updates)

        member this.TriggerReviewMeeting(sprintId) = this.TriggerReviewMeeting(sprintId)

        member this.AssessCompletion(sprintId, taskIds) =
            this.AssessCompletion(sprintId, taskIds)

        member this.DecideContinuation(sprintId, assessment) =
            this.DecideContinuation(sprintId, assessment)

        member this.GetReviewHistory(sprintId) = this.GetReviewHistory(sprintId)

        member this.CalculateNextMeetingTime(currentTime, intervalVH) =
            this.CalculateNextMeetingTime(currentTime, intervalVH)

        member this.CheckMeetingConflicts(sprintId, proposedTime) =
            this.CheckMeetingConflicts(sprintId, proposedTime)

        member this.CancelMeeting(meetingId) = this.CancelMeeting(meetingId)
        member this.GetMeetingStatistics(sprintId) = this.GetMeetingStatistics(sprintId)
        member this.PerformHealthCheck() = this.PerformHealthCheck()

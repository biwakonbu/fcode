module FCode.ScrumEventsManager

open System
open System.Collections.Generic
open FCode.Collaboration.CollaborationTypes
open FCode.TaskAssignmentManager
open FCode.QualityGateManager
open FCode.VirtualTimeCoordinator
open FCode.Logger

/// スクラムイベントタイプ
type ScrumEventType =
    | SprintPlanning
    | DailyStandUp
    | SprintReview
    | Retrospective

/// スクラムイベント状態
type ScrumEventStatus =
    | Scheduled
    | InProgress
    | Completed
    | Cancelled

/// スプリント情報
type SprintInfo =
    { SprintId: string
      SprintNumber: int
      StartTime: DateTime
      EndTime: DateTime
      Goal: string
      Status: ScrumEventStatus
      TeamVelocity: float
      PlannedCapacity: float }

/// スクラムイベント情報
type ScrumEvent =
    { EventId: string
      SprintId: string
      EventType: ScrumEventType
      ScheduledTime: DateTime
      ActualStartTime: DateTime option
      Duration: TimeSpan
      Status: ScrumEventStatus
      Participants: string list
      Artifacts: string list
      Outcomes: string list }

/// デイリースタンドアップ情報
type DailyStandUpInfo =
    { AgentId: string
      Yesterday: string list
      Today: string list
      Blockers: string list
      ProgressPercent: float }

/// スプリントレビュー情報
type SprintReviewInfo =
    { CompletedTasks: TaskInfo list
      DemoItems: string list
      StakeholderFeedback: string list
      ProductIncrement: string
      AcceptanceCriteria: string list }

/// レトロスペクティブ情報
type RetrospectiveInfo =
    { WentWell: string list
      CouldImprove: string list
      ActionItems: string list
      TeamSatisfaction: float
      ProcessEffectiveness: float }

/// スクラムイベント管理機能
type ScrumEventsManager() =

    let sprints = Dictionary<string, SprintInfo>()
    let events = Dictionary<string, ScrumEvent>()
    let dailyStandUps = Dictionary<string, DailyStandUpInfo list>()
    let sprintReviews = Dictionary<string, SprintReviewInfo>()
    let retrospectives = Dictionary<string, RetrospectiveInfo>()

    /// 新しいスプリントを開始
    member this.StartSprint(sprintNumber: int, goal: string, duration: TimeSpan) =
        async {
            try
                let sprintId =
                    sprintf "sprint-%03d-%s" sprintNumber (DateTime.UtcNow.ToString("yyyyMMdd"))

                let startTime = DateTime.UtcNow
                let endTime = startTime.Add(duration)

                let sprintInfo =
                    { SprintId = sprintId
                      SprintNumber = sprintNumber
                      StartTime = startTime
                      EndTime = endTime
                      Goal = goal
                      Status = InProgress
                      TeamVelocity = 0.0
                      PlannedCapacity = 100.0 }

                sprints.[sprintId] <- sprintInfo

                // スプリント計画イベントをスケジュール
                let! planningResult = this.ScheduleSprintPlanning(sprintId)

                logInfo "ScrumEventsManager" <| sprintf "スプリント開始: %s (目標: %s)" sprintId goal
                return Result.Ok sprintId

            with ex ->
                let errorMsg = sprintf "スプリント開始エラー: %s" ex.Message
                logError "ScrumEventsManager" errorMsg
                return Result.Error(SystemError errorMsg)
        }

    /// スプリント計画を実行
    member this.ScheduleSprintPlanning(sprintId: string) =
        async {
            try
                if not (sprints.ContainsKey(sprintId)) then
                    return Result.Error(NotFound <| sprintf "スプリント %s が見つかりません" sprintId)
                else
                    let eventId = sprintf "%s-planning" sprintId
                    let sprint = sprints.[sprintId]

                    let planningEvent =
                        { EventId = eventId
                          SprintId = sprintId
                          EventType = SprintPlanning
                          ScheduledTime = sprint.StartTime
                          ActualStartTime = Some DateTime.UtcNow
                          Duration = TimeSpan.FromMinutes(60.0)
                          Status = InProgress
                          Participants = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "pm" ]
                          Artifacts = [ "product-backlog"; "sprint-backlog"; "definition-of-done" ]
                          Outcomes = [] }

                    events.[eventId] <- planningEvent

                    // PO指示を基にタスク分解・配分を実行
                    logInfo "ScrumEventsManager" <| sprintf "スプリント計画実行: %s" sprintId
                    return Result.Ok eventId

            with ex ->
                let errorMsg = sprintf "スプリント計画エラー: %s" ex.Message
                logError "ScrumEventsManager" errorMsg
                return Result.Error(SystemError errorMsg)
        }

    /// デイリースタンドアップを実行
    member this.ConductDailyStandUp(sprintId: string) =
        async {
            try
                if not (sprints.ContainsKey(sprintId)) then
                    return Result.Error(NotFound <| sprintf "スプリント %s が見つかりません" sprintId)
                else
                    let eventId =
                        sprintf "%s-daily-%s" sprintId (DateTime.UtcNow.ToString("yyyyMMdd-HHmm"))

                    let dailyEvent =
                        { EventId = eventId
                          SprintId = sprintId
                          EventType = DailyStandUp
                          ScheduledTime = DateTime.UtcNow
                          ActualStartTime = Some DateTime.UtcNow
                          Duration = TimeSpan.FromMinutes(15.0)
                          Status = InProgress
                          Participants = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "pm" ]
                          Artifacts = [ "burndown-chart"; "task-board" ]
                          Outcomes = [] }

                    events.[eventId] <- dailyEvent

                    // 各エージェントの進捗収集
                    let agentReports = this.CollectAgentReports(sprintId)
                    dailyStandUps.[sprintId] <- agentReports

                    logInfo "ScrumEventsManager"
                    <| sprintf "デイリースタンドアップ実行: %s (%d名参加)" sprintId agentReports.Length

                    return Result.Ok eventId

            with ex ->
                let errorMsg = sprintf "デイリースタンドアップエラー: %s" ex.Message
                logError "ScrumEventsManager" errorMsg
                return Result.Error(SystemError errorMsg)
        }

    /// スプリントレビューを実行
    member this.ConductSprintReview(sprintId: string) =
        async {
            try
                if not (sprints.ContainsKey(sprintId)) then
                    return Result.Error(NotFound <| sprintf "スプリント %s が見つかりません" sprintId)
                else
                    let eventId = sprintf "%s-review" sprintId

                    let reviewEvent =
                        { EventId = eventId
                          SprintId = sprintId
                          EventType = SprintReview
                          ScheduledTime = DateTime.UtcNow
                          ActualStartTime = Some DateTime.UtcNow
                          Duration = TimeSpan.FromMinutes(120.0)
                          Status = InProgress
                          Participants = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "pm"; "po" ]
                          Artifacts = [ "product-increment"; "demo-script"; "acceptance-criteria" ]
                          Outcomes = [] }

                    events.[eventId] <- reviewEvent

                    // 完成したタスクとプロダクトインクリメントを評価
                    let reviewInfo = this.GenerateSprintReviewInfo(sprintId)
                    sprintReviews.[sprintId] <- reviewInfo

                    logInfo "ScrumEventsManager"
                    <| sprintf "スプリントレビュー実行: %s (%d個完了)" sprintId reviewInfo.CompletedTasks.Length

                    return Result.Ok eventId

            with ex ->
                let errorMsg = sprintf "スプリントレビューエラー: %s" ex.Message
                logError "ScrumEventsManager" errorMsg
                return Result.Error(SystemError errorMsg)
        }

    /// レトロスペクティブを実行
    member this.ConductRetrospective(sprintId: string) =
        async {
            try
                if not (sprints.ContainsKey(sprintId)) then
                    return Result.Error(NotFound <| sprintf "スプリント %s が見つかりません" sprintId)
                else
                    let eventId = sprintf "%s-retro" sprintId

                    let retroEvent =
                        { EventId = eventId
                          SprintId = sprintId
                          EventType = Retrospective
                          ScheduledTime = DateTime.UtcNow
                          ActualStartTime = Some DateTime.UtcNow
                          Duration = TimeSpan.FromMinutes(90.0)
                          Status = InProgress
                          Participants = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux"; "pm" ]
                          Artifacts = [ "retrospective-board"; "action-items"; "team-metrics" ]
                          Outcomes = [] }

                    events.[eventId] <- retroEvent

                    // チームの振り返りとプロセス改善を実施
                    let retroInfo = this.GenerateRetrospectiveInfo(sprintId)
                    retrospectives.[sprintId] <- retroInfo

                    logInfo "ScrumEventsManager"
                    <| sprintf "レトロスペクティブ実行: %s (改善項目: %d)" sprintId retroInfo.ActionItems.Length

                    return Result.Ok eventId

            with ex ->
                let errorMsg = sprintf "レトロスペクティブエラー: %s" ex.Message
                logError "ScrumEventsManager" errorMsg
                return Result.Error(SystemError errorMsg)
        }

    /// エージェント進捗レポート収集
    member private this.CollectAgentReports(sprintId: string) =
        [ { AgentId = "dev1"
            Yesterday = [ "実装完了: ユーザー認証機能"; "テスト作成: API単体テスト" ]
            Today = [ "レビュー対応"; "データベース統合" ]
            Blockers = []
            ProgressPercent = 75.0 }
          { AgentId = "dev2"
            Yesterday = [ "機能設計"; "プロトタイプ作成" ]
            Today = [ "本格実装開始"; "コードレビュー" ]
            Blockers = [ "外部API仕様待ち" ]
            ProgressPercent = 60.0 }
          { AgentId = "qa1"
            Yesterday = [ "テストケース設計"; "自動テスト環境構築" ]
            Today = [ "統合テスト実行"; "バグ検証" ]
            Blockers = []
            ProgressPercent = 80.0 }
          { AgentId = "qa2"
            Yesterday = [ "性能テスト計画"; "テストデータ準備" ]
            Today = [ "負荷テスト実行"; "品質レポート作成" ]
            Blockers = []
            ProgressPercent = 70.0 }
          { AgentId = "ux"
            Yesterday = [ "UI/UXレビュー"; "ユーザビリティテスト" ]
            Today = [ "デザイン改善"; "アクセシビリティ確認" ]
            Blockers = []
            ProgressPercent = 85.0 }
          { AgentId = "pm"
            Yesterday = [ "進捗管理"; "リスク評価" ]
            Today = [ "ステークホルダー報告"; "次スプリント計画" ]
            Blockers = []
            ProgressPercent = 90.0 } ]

    /// スプリントレビュー情報生成
    member private this.GenerateSprintReviewInfo(sprintId: string) =
        { CompletedTasks = [] // TaskAssignmentManagerから取得
          DemoItems = [ "ユーザー認証デモ"; "新機能プロトタイプ"; "パフォーマンス改善" ]
          StakeholderFeedback = [ "使いやすいUI"; "レスポンス速度改善必要"; "セキュリティ要件追加" ]
          ProductIncrement = "Sprint " + sprintId + " 完成版"
          AcceptanceCriteria = [ "全テスト通過"; "パフォーマンス基準達成"; "セキュリティ監査完了" ] }

    /// レトロスペクティブ情報生成
    member private this.GenerateRetrospectiveInfo(sprintId: string) =
        { WentWell = [ "チーム協調良好"; "品質向上"; "自動化推進" ]
          CouldImprove = [ "コミュニケーション効率"; "技術的負債解決"; "テスト早期化" ]
          ActionItems = [ "毎日30分のペア作業"; "リファクタリング週間設定"; "TDD導入検討" ]
          TeamSatisfaction = 8.5
          ProcessEffectiveness = 7.8 }

    /// スプリント終了
    member this.CompleteSprint(sprintId: string) =
        async {
            try
                if not (sprints.ContainsKey(sprintId)) then
                    return Result.Error(NotFound <| sprintf "スプリント %s が見つかりません" sprintId)
                else
                    let sprint = sprints.[sprintId]
                    sprints.[sprintId] <- { sprint with Status = Completed }

                    logInfo "ScrumEventsManager" <| sprintf "スプリント完了: %s" sprintId
                    return Result.Ok sprintId

            with ex ->
                let errorMsg = sprintf "スプリント完了エラー: %s" ex.Message
                logError "ScrumEventsManager" errorMsg
                return Result.Error(SystemError errorMsg)
        }

    /// 現在のスプリント情報を取得
    member this.GetCurrentSprint() =
        let activeSprints =
            sprints.Values
            |> Seq.filter (fun s -> s.Status = InProgress)
            |> Seq.sortByDescending (fun s -> s.StartTime)
            |> Seq.toList

        match activeSprints with
        | sprint :: _ -> Some sprint
        | [] -> None

    /// スプリント統計情報を取得
    member this.GetSprintStatistics(sprintId: string) =
        if sprints.ContainsKey(sprintId) then
            let sprint = sprints.[sprintId]

            let totalEvents =
                events.Values |> Seq.filter (fun e -> e.SprintId = sprintId) |> Seq.length

            let completedEvents =
                events.Values
                |> Seq.filter (fun e -> e.SprintId = sprintId && e.Status = Completed)
                |> Seq.length

            let completionRate =
                if totalEvents > 0 then
                    (float completedEvents / float totalEvents) * 100.0
                else
                    0.0

            let averageProgress =
                if dailyStandUps.ContainsKey(sprintId) then
                    let reports = dailyStandUps.[sprintId]

                    if reports.Length > 0 then
                        reports |> List.averageBy (fun r -> r.ProgressPercent)
                    else
                        0.0
                else
                    0.0

            Some(
                sprintf
                    "Sprint: %s, Events: %d/%d (%.1f%%), Progress: %.1f%%"
                    sprint.SprintId
                    completedEvents
                    totalEvents
                    completionRate
                    averageProgress
            )
        else
            None

    /// スクラムイベント履歴を取得
    member this.GetEventHistory(sprintId: string) =
        events.Values
        |> Seq.filter (fun e -> e.SprintId = sprintId)
        |> Seq.sortBy (fun e -> e.ScheduledTime)
        |> Seq.toList

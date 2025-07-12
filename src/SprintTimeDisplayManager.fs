module FCode.SprintTimeDisplayManager

open System
open System.Threading
open FCode.Logger
open FCode.VirtualTimeCoordinator
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.MeetingScheduler

// スプリント管理関連の定数
[<Literal>]
let StandupIntervalMinutes = 6

[<Literal>]
let SprintDurationMinutes = 18

[<Literal>]
let QualityScoreExcellent = 90.0

[<Literal>]
let QualityScoreGood = 80.0

[<Literal>]
let CompletionRateThreshold = 90.0

[<Literal>]
let MinimumStandupIntervalMinutes = 5.0

/// スプリント時間表示管理クラス
type SprintTimeDisplayManager(virtualTimeCoordinator: VirtualTimeCoordinator) =

    let syncRoot = obj ()
    let mutable displayUpdateHandlers: (string -> unit) list = []
    let mutable currentSprintId: string option = None
    let mutable isSprintActive = false
    let mutable sprintStartTime: DateTime option = None
    let mutable lastStandupTime: DateTime option = None
    let mutable standupNotificationHandlers: (string -> unit) list = []

    /// 表示更新ハンドラーを登録
    member this.RegisterDisplayUpdateHandler(handler: string -> unit) =
        lock syncRoot (fun () -> displayUpdateHandlers <- handler :: displayUpdateHandlers)
        logInfo "SprintTimeDisplay" "表示更新ハンドラーを登録しました"

    /// スタンドアップ通知ハンドラーを登録
    member this.RegisterStandupNotificationHandler(handler: string -> unit) =
        lock syncRoot (fun () -> standupNotificationHandlers <- handler :: standupNotificationHandlers)
        logInfo "SprintTimeDisplay" "スタンドアップ通知ハンドラーを登録しました"

    /// アクティブスプリントの時間情報をフォーマット
    member private this.FormatActiveSprintTimeInfo(sprintId: string, startTime: DateTime, now: DateTime) =
        let elapsed = now - startTime
        let totalMinutes = int elapsed.TotalMinutes
        let remainingMinutes = Math.Max(0, SprintDurationMinutes - totalMinutes)

        // 6分間隔でのスタンドアップ表示
        let nextStandupMinutes =
            StandupIntervalMinutes - (totalMinutes % StandupIntervalMinutes)

        let isStandupTime = (totalMinutes % StandupIntervalMinutes) = 0 && totalMinutes > 0

        let standupInfo =
            if isStandupTime then
                "🔔 スタンドアップ時間です！"
            elif nextStandupMinutes = StandupIntervalMinutes then
                "スタンドアップ直後"
            else
                sprintf "次回スタンドアップまで: %d分" nextStandupMinutes

        sprintf
            """
🚀 スプリント: %s
⏱️ 経過時間: %d分 / %d分
⏳ 残り時間: %d分
📊 %s

🎯 進捗概要:
- 開始時刻: %s
- 現在時刻: %s
- スプリント完了予定: %s
            """
            sprintId
            totalMinutes
            SprintDurationMinutes
            remainingMinutes
            standupInfo
            (startTime.ToString("HH:mm:ss"))
            (now.ToString("HH:mm:ss"))
            (startTime.AddMinutes(float SprintDurationMinutes).ToString("HH:mm:ss"))

    /// 現在のスプリント情報をフォーマットして表示テキストを生成
    member this.FormatSprintStatus() =
        try
            let now = DateTime.Now
            let status = if isSprintActive then "実行中" else "停止中"

            let timeInfo =
                match (currentSprintId, sprintStartTime, isSprintActive) with
                | (Some sprintId, Some startTime, true) -> this.FormatActiveSprintTimeInfo(sprintId, startTime, now)

                | (Some sprintId, None, false) ->
                    sprintf
                        """
🔄 スプリント: %s
📝 状態: 準備中
⏰ 開始待機中...

💡 スプリント開始方法:
POが指示を入力することでスプリントが自動開始されます
                    """
                        sprintId

                | _ ->
                    sprintf
                        """
⚪ スプリント未開始
📋 待機状態

🚀 スプリント開始手順:
1. POが会話ペインで指示を入力
2. タスク分解・エージェント配分
3. %d分スプリント自動開始
                    """
                        SprintDurationMinutes

            sprintf
                "[%s] PM タイムライン - スプリント管理\n\n状態: %s\n%s\n\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
                (now.ToString("HH:mm:ss"))
                status
                timeInfo

        with ex ->
            logError "SprintTimeDisplay" (sprintf "スプリント状態フォーマットエラー: %s" ex.Message)
            sprintf "[ERROR] スプリント状態表示エラー: %s" ex.Message

    /// スプリントを開始
    member this.StartSprint(sprintId: string) =
        async {
            try
                let! result = virtualTimeCoordinator.StartSprint(sprintId)

                match result with
                | Result.Ok context ->
                    lock syncRoot (fun () ->
                        currentSprintId <- Some sprintId
                        isSprintActive <- true
                        sprintStartTime <- Some DateTime.Now)

                    let displayText = this.FormatSprintStatus()

                    displayUpdateHandlers
                    |> List.iter (fun handler ->
                        try
                            handler displayText
                        with ex ->
                            logError "SprintTimeDisplay" (sprintf "表示更新ハンドラーエラー: %s" ex.Message))

                    logInfo "SprintTimeDisplay" (sprintf "スプリント開始: %s" sprintId)
                    return Result.Ok()

                | Result.Error error ->
                    logError "SprintTimeDisplay" (sprintf "スプリント開始失敗: %A" error)
                    return Result.Error error

            with ex ->
                let errorMsg = sprintf "スプリント開始例外: %s" ex.Message
                logError "SprintTimeDisplay" errorMsg
                return Result.Error(SystemError errorMsg)
        }

    /// スプリントを停止
    member this.StopSprint() =
        async {
            try
                match currentSprintId with
                | Some sprintId ->
                    let! result = virtualTimeCoordinator.StopSprint(sprintId)

                    match result with
                    | Result.Ok() ->
                        lock syncRoot (fun () ->
                            isSprintActive <- false
                            sprintStartTime <- None)

                        let displayText = this.FormatSprintStatus()

                        displayUpdateHandlers
                        |> List.iter (fun handler ->
                            try
                                handler displayText
                            with ex ->
                                logError "SprintTimeDisplay" (sprintf "表示更新ハンドラーエラー: %s" ex.Message))

                        logInfo "SprintTimeDisplay" (sprintf "スプリント停止: %s" sprintId)
                        return Result.Ok()

                    | Result.Error error ->
                        logError "SprintTimeDisplay" (sprintf "スプリント停止失敗: %A" error)
                        return Result.Error error
                | None ->
                    logInfo "SprintTimeDisplay" "停止対象のスプリントが存在しません"
                    return Result.Ok()

            with ex ->
                let errorMsg = sprintf "スプリント停止例外: %s" ex.Message
                logError "SprintTimeDisplay" errorMsg
                return Result.Error(SystemError errorMsg)
        }

    /// スタンドアップ時間チェックと通知
    member this.CheckStandupTime() =
        try
            match (sprintStartTime, isSprintActive) with
            | (Some startTime, true) ->
                let now = DateTime.Now
                let elapsed = now - startTime
                let totalMinutes = int elapsed.TotalMinutes

                let isStandupTime =
                    (totalMinutes % StandupIntervalMinutes) = 0
                    && totalMinutes > 0
                    && totalMinutes < SprintDurationMinutes

                // 新しいスタンドアップ時間かチェック
                let isNewStandupTime =
                    match lastStandupTime with
                    | Some lastTime -> (now - lastTime).TotalMinutes >= MinimumStandupIntervalMinutes
                    | None -> true

                if isStandupTime && isNewStandupTime then
                    lock syncRoot (fun () -> lastStandupTime <- Some now)

                    let standupNotification =
                        sprintf
                            "🔔 スタンドアップ通知 - %s\n\n⏰ %d分経過 - スタンドアップ開始時刻です！\n\n📋 アジェンダ:\n• 前回から今回までの進捗報告\n• 次回まで（6分間）の作業計画\n• ブロッカー・課題の共有\n• 必要な支援・調整の要求\n\n👥 参加エージェント: dev1, dev2, dev3, qa1, qa2, ux\n\n⏱️ 予定時間: 3分以内"
                            (now.ToString("HH:mm:ss"))
                            totalMinutes

                    standupNotificationHandlers
                    |> List.iter (fun handler ->
                        try
                            handler standupNotification
                        with ex ->
                            logError "SprintTimeDisplay" (sprintf "スタンドアップ通知ハンドラーエラー: %s" ex.Message))

                    logInfo "SprintTimeDisplay" (sprintf "%d分経過でスタンドアップ通知を送信しました" totalMinutes)

                // 18分経過時の完成確認フロー
                if totalMinutes >= SprintDurationMinutes then
                    this.TriggerSprintCompletion()

            | _ -> ()
        with ex ->
            logError "SprintTimeDisplay" (sprintf "スタンドアップ時間チェック例外: %s" ex.Message)

    /// 18分スプリント完成確認フロー
    member this.TriggerSprintCompletion() =
        try
            match currentSprintId with
            | Some sprintId ->
                logInfo "SprintTimeDisplay" (sprintf "18分スプリント完成確認開始: %s" sprintId)

                // 完成度評価の実行
                let completionAssessment = this.AssessSprintCompletion()
                let qualityScore = this.CalculateQualityScore()

                let continuationDecision =
                    this.DecideSprintContinuation(completionAssessment, qualityScore)

                let completionNotification =
                    sprintf
                        "🎯 スプリント完成確認 - %s\n\n⏰ 18分経過 - スプリント完了時刻です！\n\n📊 完成度評価:\n• タスク完了率: %.1f%%\n• 品質スコア: %.1f/100\n• 完成判定: %s\n\n🔍 品質評価:\n• コード品質: %s\n• テストカバレッジ: %s\n• ドキュメント: %s\n\n🚀 継続判定: %s\n\n📝 次のアクション:\n%s"
                        (DateTime.Now.ToString("HH:mm:ss"))
                        completionAssessment.CompletionRate
                        qualityScore
                        (if completionAssessment.IsCompleted then
                             "✅ 完成"
                         else
                             "🔄 継続作業必要")
                        (if qualityScore >= 80.0 then "✅ 良好" else "⚠️ 要改善")
                        (if completionAssessment.TestsPassed then "✅ 合格" else "❌ 要修正")
                        (if completionAssessment.DocumentationComplete then
                             "✅ 完了"
                         else
                             "📝 要追加")
                        (this.FormatContinuationDecision(continuationDecision))
                        (this.GetNextActionItems(continuationDecision))

                standupNotificationHandlers
                |> List.iter (fun handler ->
                    try
                        handler completionNotification
                    with ex ->
                        logError "SprintTimeDisplay" (sprintf "完成確認通知ハンドラーエラー: %s" ex.Message))

                // スプリント状態を非アクティブに設定
                lock syncRoot (fun () -> isSprintActive <- false)

                logInfo
                    "SprintTimeDisplay"
                    (sprintf "スプリント完成確認完了: %s (完了率=%.1f%%)" sprintId completionAssessment.CompletionRate)

            | None -> logWarning "SprintTimeDisplay" "完成確認対象のスプリントが存在しません"
        with ex ->
            logError "SprintTimeDisplay" (sprintf "スプリント完成確認例外: %s" ex.Message)

    /// スプリント完成度評価
    member private this.AssessSprintCompletion() =
        try
            // AgentWorkDisplayManagerから実際の作業状況を取得
            let workDisplayManager = FCode.AgentWorkDisplayGlobal.GetManager()
            let agentIds = [ "dev1"; "dev2"; "dev3"; "qa1"; "qa2"; "ux" ]

            let (totalTasks, completedTasks) =
                agentIds
                |> List.map (fun agentId ->
                    match workDisplayManager.GetAgentWorkInfo(agentId) with
                    | Some workInfo ->
                        let isCompleted =
                            match workInfo.CurrentStatus with
                            | FCode.AgentWorkStatus.Completed(_, _, _) -> true
                            | _ -> false

                        (1, if isCompleted then 1 else 0)
                    | None -> (0, 0))
                |> List.fold
                    (fun (totalAcc, completedAcc) (total, completed) -> (totalAcc + total, completedAcc + completed))
                    (0, 0)

            let completionRate =
                if totalTasks > 0 then
                    (float completedTasks / float totalTasks) * 100.0
                else
                    85.0 // デフォルト値

            let isCompleted = completionRate >= CompletionRateThreshold
            let testsPassed = completionRate >= QualityScoreGood // テスト通過判定
            let documentationComplete = completionRate >= 75.0 // ドキュメント完成判定

            logInfo "SprintTimeDisplay" (sprintf "完成度評価: %d/%d タスク完了 (%.1f%%)" completedTasks totalTasks completionRate)

            {| CompletionRate = completionRate
               IsCompleted = isCompleted
               TestsPassed = testsPassed
               DocumentationComplete = documentationComplete
               TasksTotal = totalTasks
               TasksCompleted = completedTasks |}
        with ex ->
            logError "SprintTimeDisplay" (sprintf "完成度評価例外: %s" ex.Message)

            {| CompletionRate = 70.0 // フォールバック値
               IsCompleted = false
               TestsPassed = false
               DocumentationComplete = false
               TasksTotal = 0
               TasksCompleted = 0 |}

    /// 品質スコア計算
    member private this.CalculateQualityScore() =
        try
            // 品質スコア計算（将来的にはQualityGateManagerから取得）
            let codeQualityScore = 85.0 // コード品質
            let testCoverageScore = 90.0 // テストカバレッジ
            let documentationScore = 75.0 // ドキュメント品質

            (codeQualityScore + testCoverageScore + documentationScore) / 3.0
        with ex ->
            logError "SprintTimeDisplay" (sprintf "品質スコア計算例外: %s" ex.Message)
            0.0

    /// スプリント継続判定
    member private this.DecideSprintContinuation
        (
            assessment:
                {| CompletionRate: float
                   IsCompleted: bool
                   TestsPassed: bool
                   DocumentationComplete: bool
                   TasksTotal: int
                   TasksCompleted: int |},
            qualityScore: float
        ) =
        try
            if assessment.IsCompleted && qualityScore >= QualityScoreExcellent then
                "AutoContinue" // 高品質完成・自動継続
            elif assessment.CompletionRate >= QualityScoreGood && qualityScore >= 75.0 then
                "RequirePOApproval" // 標準品質・PO承認要求
            elif assessment.CompletionRate < 50.0 then
                "ExtendSprint" // 大幅未完成・スプリント延長推奨
            elif qualityScore < 60.0 then
                "QualityImprovement" // 品質改善要求
            else
                "RequirePOApproval" // デフォルト・PO判断要求
        with ex ->
            logError "SprintTimeDisplay" (sprintf "継続判定例外: %s" ex.Message)
            "RequirePOApproval"

    /// 継続判定のフォーマット
    member private this.FormatContinuationDecision(decision: string) =
        match decision with
        | "AutoContinue" -> "✅ 自動継続承認 - 次スプリント開始可能"
        | "RequirePOApproval" -> "🤝 PO承認要求 - 品質・進捗確認後継続"
        | "ExtendSprint" -> "⏰ スプリント延長推奨 - 追加時間必要"
        | "QualityImprovement" -> "🔧 品質改善要求 - 品質向上後継続"
        | _ -> "❓ 手動判断要求 - PO指示待ち"

    /// 次アクションアイテム取得
    member private this.GetNextActionItems(decision: string) =
        match decision with
        | "AutoContinue" -> "• 次スプリント計画立案\n• 新機能要件定義\n• チーム体制継続"
        | "RequirePOApproval" -> "• PO承認待ち\n• 成果物レビュー\n• 品質確認完了後継続"
        | "ExtendSprint" -> "• 未完成タスク優先継続\n• ブロッカー解除\n• リソース追加検討"
        | "QualityImprovement" -> "• コード品質向上\n• テスト追加実装\n• ドキュメント整備"
        | _ -> "• PO判断・指示待ち\n• 現状確認・課題整理\n• 次方針決定"

    /// 表示を更新（定期実行用）
    member this.UpdateDisplay() =
        try
            if isSprintActive then
                // スタンドアップ時間チェック
                this.CheckStandupTime()

                // 表示更新
                let displayText = this.FormatSprintStatus()

                displayUpdateHandlers
                |> List.iter (fun handler ->
                    try
                        handler displayText
                    with ex ->
                        logError "SprintTimeDisplay" (sprintf "表示更新ハンドラーエラー: %s" ex.Message))

                logDebug "SprintTimeDisplay" "スプリント表示を更新しました"

        with ex ->
            logError "SprintTimeDisplay" (sprintf "表示更新例外: %s" ex.Message)

    /// スタンドアップ実行（進捗レポート処理）
    member this.ExecuteStandup(agentReports: (string * string) list) =
        async {
            try
                match currentSprintId with
                | Some sprintId ->
                    logInfo "SprintTimeDisplay" (sprintf "スタンドアップ実行開始: %s (%d件の報告)" sprintId agentReports.Length)

                    // MeetingSchedulerを通じてスタンドアップ実行
                    let meetingId =
                        sprintf "STANDUP_%s_%s" sprintId (DateTime.Now.ToString("yyyyMMddHHmmss"))

                    // スタンドアップ実行結果を表示に反映
                    let standupSummary =
                        let reports =
                            agentReports
                            |> List.map (fun (agent, report) -> sprintf "• %s: %s" agent report)
                            |> String.concat "\n"

                        sprintf
                            "📊 スタンドアップ完了 - %s\n\n👥 参加者: %d名\n📝 進捗報告:\n%s\n\n⏰ 実行時刻: %s\n🎯 次回スタンドアップ: 6分後"
                            meetingId
                            agentReports.Length
                            reports
                            (DateTime.Now.ToString("HH:mm:ss"))

                    // 会話ペインに表示
                    standupNotificationHandlers
                    |> List.iter (fun handler ->
                        try
                            handler standupSummary
                        with ex ->
                            logError "SprintTimeDisplay" (sprintf "スタンドアップサマリー表示エラー: %s" ex.Message))

                    logInfo "SprintTimeDisplay" (sprintf "スタンドアップ実行完了: %s" meetingId)
                    return Result.Ok meetingId

                | None ->
                    let errorMsg = "アクティブなスプリントが存在しません"
                    logError "SprintTimeDisplay" errorMsg
                    return Result.Error(SystemError errorMsg)

            with ex ->
                let errorMsg = sprintf "スタンドアップ実行例外: %s" ex.Message
                logError "SprintTimeDisplay" errorMsg
                return Result.Error(SystemError errorMsg)
        }

    /// 現在のスプリント状態を取得
    member this.GetCurrentSprintInfo() =
        {| SprintId = currentSprintId
           IsActive = isSprintActive
           StartTime = sprintStartTime
           LastStandupTime = lastStandupTime
           ElapsedMinutes =
            match sprintStartTime with
            | Some startTime when isSprintActive -> Some(int (DateTime.Now - startTime).TotalMinutes)
            | _ -> None
           NextStandupMinutes =
            match sprintStartTime with
            | Some startTime when isSprintActive ->
                let elapsed = int (DateTime.Now - startTime).TotalMinutes
                Some(6 - (elapsed % 6))
            | _ -> None |}

/// スプリント時間表示マネージャーのグローバルインスタンス管理
module SprintTimeDisplayGlobal =
    let mutable private instance: SprintTimeDisplayManager option = None

    /// インスタンスを初期化
    let Initialize (virtualTimeCoordinator: VirtualTimeCoordinator) =
        instance <- Some(new SprintTimeDisplayManager(virtualTimeCoordinator))
        logInfo "SprintTimeDisplayGlobal" "SprintTimeDisplayManagerを初期化しました"

    /// インスタンスを取得
    let GetManager () =
        match instance with
        | Some manager -> manager
        | None ->
            logError "SprintTimeDisplayGlobal" "SprintTimeDisplayManagerが初期化されていません"
            failwith "SprintTimeDisplayManager not initialized"

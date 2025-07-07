module FCode.UnifiedActivityView

open System
open System.Collections.Concurrent
open Terminal.Gui
open NStack
open FCode.Logger
open FCode.AgentMessaging
open FCode.ColorSchemes

// ===============================================
// アクティビティ統合表示型定義
// ===============================================

/// エージェント活動種別
type ActivityType =
    | CodeGeneration // コード生成・実装
    | Testing // テスト実行・検証
    | QualityReview // 品質レビュー・コードレビュー
    | Documentation // ドキュメント作成・更新
    | TaskAssignment // タスク割り当て・指示
    | Progress // 進捗報告・状況更新
    | Escalation // エスカレーション・問題報告
    | Decision // 意思決定・判断要求
    | SystemMessage // システムメッセージ・通知

/// アクティビティ状態
type ActivityStatus =
    | Received // 受信済み
    | Processing // 処理中
    | Completed // 完了
    | Failed // 失敗
    | System // システム活動
    | Cancelled // キャンセル

/// 統合活動データ
type UnifiedActivity =
    { ActivityId: string // 活動一意ID
      AgentId: string // エージェントID
      ActivityType: ActivityType // 活動種別
      Message: string // 活動内容
      Timestamp: DateTime // 発生タイムスタンプ
      Priority: MessagePriority // 優先度
      Metadata: Map<string, string> // 追加メタデータ
      RelatedTaskId: string option // 関連タスクID
      Status: ActivityStatus } // 状態

// ===============================================
// 内部責務分離型 (SOLID準拠設計)
// ===============================================

/// 活動データ変換責務 (Single Responsibility)
type private ActivityTransformer() =
    /// AgentMessageからUnifiedActivity変換 - 入力検証強化
    member this.TransformMessage(message: AgentMessage) : Result<UnifiedActivity, string> =
        try
            // 基本的な入力検証
            if String.IsNullOrWhiteSpace(message.FromAgent) then
                Result.Error "エージェントIDが無効です"
            elif String.IsNullOrWhiteSpace(message.Content) then
                Result.Error "メッセージ内容が無効です"
            else
                let activityType =
                    match message.MessageType with
                    | MessageType.TaskAssignment -> ActivityType.TaskAssignment
                    | MessageType.Progress -> ActivityType.Progress
                    | MessageType.QualityReview -> ActivityType.QualityReview
                    | MessageType.Escalation -> ActivityType.Escalation
                    | MessageType.StateUpdate -> ActivityType.SystemMessage
                    | MessageType.ResourceRequest -> ActivityType.SystemMessage
                    | MessageType.Collaboration -> ActivityType.Decision
                    | MessageType.Notification -> ActivityType.SystemMessage

                let activity =
                    { ActivityId = this.GenerateActivityId()
                      AgentId = message.FromAgent
                      ActivityType = activityType
                      Message = message.Content
                      Timestamp = message.Timestamp
                      Priority = message.Priority
                      Metadata = message.Metadata
                      RelatedTaskId = message.Metadata.TryFind("task_id")
                      Status = Received }

                Result.Ok activity
        with ex ->
            Result.Error $"Activity transformation failed: {ex.Message}"

    /// 活動ID生成
    member this.GenerateActivityId() =
        let timestamp = DateTime.Now.ToString("HHmmss")
        let guidPart = Guid.NewGuid().ToString("N")[..3]
        $"act-{timestamp}-{guidPart}"

/// 活動ストレージ責務 (Single Responsibility) - スレッドセーフ実装
type private ActivityStorage() =
    let activities = ConcurrentQueue<UnifiedActivity>()
    let maxActivities = 1000
    let storageSpinLock = ref 0
    let mutable disposed = false

    /// オブジェクトロックを使ったスレッドセーフ操作
    let lockObj = obj ()
    let withLock f = lock lockObj f

    member this.AddActivity(activity: UnifiedActivity) : Result<unit, string> =
        try
            withLock (fun () ->
                activities.Enqueue(activity)
                // 最大数超過時の古い活動削除（スレッドセーフ）
                while activities.Count > maxActivities do
                    activities.TryDequeue() |> ignore)

            Result.Ok()
        with ex ->
            Result.Error $"Failed to add activity: {ex.Message}"

    member this.GetActivities() =
        withLock (fun () -> activities.ToArray())

    member this.GetActivityCount() = activities.Count

    member this.Clear() =
        withLock (fun () ->
            while activities.TryDequeue() |> fst do
                ())

    /// リソース解放
    member this.Dispose() =
        if not disposed then
            disposed <- true
            this.Clear()
            GC.SuppressFinalize(this)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

/// UI更新責務 (Single Responsibility) - スレッドセーフ実装
type private ActivityUIUpdater() =
    let mutable conversationTextView: TextView option = None
    let uiLockObj = obj ()
    let mutable disposed = false

    /// UI操作用オブジェクトロック
    let withUILock f = lock uiLockObj f

    member this.SetTextView(textView: TextView) =
        this.ThrowIfDisposed()

        try
            if isNull textView then
                logError "UnifiedActivityView" "Attempted to set null TextView"
            else
                withUILock (fun () ->
                    conversationTextView <- Some textView
                    logInfo "UnifiedActivityView" "TextView set successfully")
        with ex ->
            logException "UnifiedActivityView" "SetTextView failed" ex

    member this.UpdateDisplay(activities: UnifiedActivity[]) : Result<unit, string> =
        this.ThrowIfDisposed()

        try
            // 引数バリデーション
            if isNull activities then
                Result.Error "Activities array is null"
            else
                let currentTextView = withUILock (fun () -> conversationTextView)

                match currentTextView with
                | Some textView when not (isNull textView) ->
                    try
                        let displayText = this.FormatActivitiesForDisplay(activities)
                        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

                        if not isCI then
                            this.SafeUIUpdate(textView, displayText)

                        Result.Ok()
                    with ex ->
                        logException "UnifiedActivityView" "Display update failed" ex
                        Result.Error $"UI update failed: {ex.Message}"
                | Some _ -> Result.Error "TextView is null"
                | None -> Result.Error "TextView not set"
        with ex ->
            logException "UnifiedActivityView" "UpdateDisplay exception" ex
            Result.Error $"UpdateDisplay failed: {ex.Message}"

    /// 安全なUI更新
    member private this.SafeUIUpdate(textView: TextView, content: string) =
        try
            // 引数バリデーション
            if isNull content then
                logError "UnifiedActivityView" "Content is null"
            else if not (isNull Application.MainLoop) then
                Application.MainLoop.Invoke(fun () ->
                    try
                        textView.Text <- ustring.Make(content)
                        textView.SetNeedsDisplay()
                    with ex ->
                        logException "UnifiedActivityView" "UI thread update failed" ex)
            else
                // MainLoop未初期化の場合の直接更新
                textView.Text <- ustring.Make(content)
                textView.SetNeedsDisplay()
        with ex ->
            logException "UnifiedActivityView" "Safe UI update failed" ex

    /// 活動表示フォーマット
    member private this.FormatActivitiesForDisplay(activities: UnifiedActivity[]) =
        try
            let header = "=== 統合エージェント活動ログ ===\n\n"

            if isNull activities || activities.Length = 0 then
                header + "活動データがありません\n"
            else
                let recentActivities =
                    activities
                    |> Array.filter (fun a -> not (isNull a.Message))
                    |> Array.sortByDescending (fun a -> a.Timestamp)
                    |> Array.take (min 10 activities.Length)

                let activityLines =
                    recentActivities
                    |> Array.map (fun activity ->
                        try
                            let timeStr = activity.Timestamp.ToString("HH:mm:ss")

                            let agentStr =
                                if isNull activity.AgentId then
                                    "UNKNOWN"
                                else
                                    activity.AgentId.PadRight(6)

                            let typeStr = this.GetActivityTypeDisplay(activity.ActivityType)
                            let priorityStr = this.GetPriorityDisplay(activity.Priority)

                            let messagePreview =
                                if isNull activity.Message then
                                    "(空メッセージ)"
                                elif activity.Message.Length > 60 then
                                    activity.Message.[..57] + "..."
                                else
                                    activity.Message

                            $"[{timeStr}] {agentStr} {typeStr} {priorityStr} {messagePreview}"
                        with ex ->
                            logException "UnifiedActivityView" "Activity formatting failed" ex
                            $"[ERROR] 活動表示エラー: {ex.Message}")
                    |> String.concat "\n"

                let footer =
                    $"\n\n--- 最新{recentActivities.Length}件 / 総活動数: {activities.Length} ---\nキーバインド: ESC(終了) Ctrl+X(コマンド) Ctrl+Tab(ペイン切替)"

                header + activityLines + footer
        with ex ->
            logException "UnifiedActivityView" "Format activities failed" ex
            "=== 統合エージェント活動ログ ===\n\n[ERROR] 活動表示の生成に失敗しました\n"

    /// 活動種別表示
    member private this.GetActivityTypeDisplay(activityType: ActivityType) =
        match activityType with
        | CodeGeneration -> "🔧 CODE"
        | Testing -> "🧪 TEST"
        | QualityReview -> "📋 QA  "
        | Documentation -> "📝 DOC "
        | TaskAssignment -> "📌 TASK"
        | Progress -> "📊 PROG"
        | Escalation -> "🚨 ESC "
        | Decision -> "💭 DEC "
        | SystemMessage -> "⚙️ SYS "

    /// 優先度表示
    member private this.GetPriorityDisplay(priority: MessagePriority) =
        match priority with
        | Critical -> "[🔴]"
        | High -> "[🟡]"
        | Normal -> "[🟢]"
        | Low -> "[⚪]"

    /// リソース解放
    member this.Dispose() =
        if not disposed then
            disposed <- true
            withUILock (fun () -> conversationTextView <- None)
            GC.SuppressFinalize(this)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

    /// ファイナライザ
    override this.Finalize() = this.Dispose()

    /// リソース解放状態確認
    member private this.ThrowIfDisposed() =
        if disposed then
            raise (ObjectDisposedException("ActivityUIUpdater"))

// ===============================================
// 統合活動表示管理 (依存性注入によるSOLID設計)
// ===============================================

/// 統合活動表示管理クラス (リファクタリング版)
type UnifiedActivityManager() =
    // 依存性注入による責務分離
    let transformer = ActivityTransformer()
    let storage = new ActivityStorage()
    let uiUpdater = new ActivityUIUpdater()
    let mutable disposed = false

    /// 会話ペインTextView設定
    member this.SetConversationTextView(textView: TextView) =
        this.ThrowIfDisposed()

        if not (isNull textView) then
            uiUpdater.SetTextView(textView)
            logInfo "UnifiedActivityView" "Conversation TextView set for unified activity display"
        else
            logWarning "UnifiedActivityView" "Attempted to set null TextView for conversation display"

    /// リソース解放
    member this.Dispose() =
        if not disposed then
            disposed <- true
            (storage :> IDisposable).Dispose()
            (uiUpdater :> IDisposable).Dispose()
            GC.SuppressFinalize(this)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

    /// ファイナライザ
    override this.Finalize() = this.Dispose()

    /// リソース解放状態確認
    member private this.ThrowIfDisposed() =
        if disposed then
            raise (ObjectDisposedException("UnifiedActivityManager"))

    /// エージェント活動追加 (AgentMessage経由) - Result型対応
    member this.AddActivityFromMessage(message: AgentMessage) : Result<unit, string> =
        this.ThrowIfDisposed()

        match transformer.TransformMessage(message) with
        | Result.Ok activity -> this.AddActivity(activity)
        | Result.Error error ->
            logWarning "UnifiedActivityView" $"Message transformation failed: {error}"
            Result.Error error

    /// エージェント活動追加 (直接) - Result型対応
    member this.AddActivity(activity: UnifiedActivity) : Result<unit, string> =
        this.ThrowIfDisposed()

        match storage.AddActivity(activity) with
        | Result.Ok() ->
            // UI更新
            let activities = storage.GetActivities()

            match uiUpdater.UpdateDisplay(activities) with
            | Result.Ok() ->
                let messagePreview =
                    if activity.Message.Length > 50 then
                        activity.Message.[..50] + "..."
                    else
                        activity.Message

                logDebug
                    "UnifiedActivityView"
                    $"Activity added: {activity.AgentId} - {activity.ActivityType} - {messagePreview}"

                Result.Ok()
            | Result.Error uiError ->
                logWarning "UnifiedActivityView" $"UI update failed: {uiError}"
                Result.Ok() // データ追加は成功したので、UIエラーは警告のみ
        | Result.Error storageError ->
            logError "UnifiedActivityView" $"Storage error: {storageError}"
            Result.Error storageError

    /// カスタム活動追加 (システムメッセージ等) - Result型対応
    member this.AddSystemActivity
        (
            agentId: string,
            activityType: ActivityType,
            message: string,
            ?priority: MessagePriority,
            ?metadata: Map<string, string>
        ) : Result<unit, string> =
        let priority = defaultArg priority Normal
        let metadata = defaultArg metadata Map.empty

        let activity =
            { ActivityId = transformer.GenerateActivityId() // プライベートメンバーは外部クラス追加
              AgentId = agentId
              ActivityType = activityType
              Message = message
              Timestamp = DateTime.Now
              Priority = priority
              Metadata = metadata
              RelatedTaskId = None
              Status = System }

        this.AddActivity(activity)


    /// 指定エージェントの最新活動取得
    member this.GetLatestActivitiesByAgent(agentId: string, count: int) =
        let allActivities = storage.GetActivities()

        let filteredActivities =
            allActivities
            |> Array.filter (fun a -> a.AgentId = agentId)
            |> Array.sortByDescending (fun a -> a.Timestamp)

        filteredActivities |> Array.take (min count filteredActivities.Length)

    /// 指定活動種別の最新活動取得
    member this.GetLatestActivitiesByType(activityType: ActivityType, count: int) =
        let allActivities = storage.GetActivities()

        let filteredActivities =
            allActivities
            |> Array.filter (fun a -> a.ActivityType = activityType)
            |> Array.sortByDescending (fun a -> a.Timestamp)

        filteredActivities |> Array.take (min count filteredActivities.Length)

    /// 全活動取得
    member this.GetAllActivities() = storage.GetActivities()

    /// 活動数取得
    member this.GetActivityCount() = storage.GetActivityCount()

    /// 活動クリア - Result型対応
    member this.ClearActivities() : Result<unit, string> =
        try
            storage.Clear()
            let activities = storage.GetActivities()

            match uiUpdater.UpdateDisplay(activities) with
            | Result.Ok() ->
                logInfo "UnifiedActivityView" "All activities cleared"
                Result.Ok()
            | Result.Error uiError ->
                logWarning "UnifiedActivityView" $"UI update after clear failed: {uiError}"
                Result.Ok() // データクリアは成功
        with ex ->
            let errorMsg = $"Failed to clear activities: {ex.Message}"
            logError "UnifiedActivityView" errorMsg
            Result.Error errorMsg

// ===============================================
// 依存性注入対応グローバル管理インスタンス
// ===============================================

/// 依存性注入対応統合活動管理インスタンス（遅延初期化）
let mutable private activityManagerInstance: UnifiedActivityManager option = None

/// 統合活動管理インスタンス取得または作成
let private getOrCreateActivityManager () =
    match activityManagerInstance with
    | Some manager -> manager
    | None ->
        let manager = new UnifiedActivityManager()
        activityManagerInstance <- Some manager
        manager

/// AgentMessageから統合活動追加 (グローバル関数) - Result型対応
let addActivityFromMessage (message: AgentMessage) : Result<unit, string> =
    (getOrCreateActivityManager ()).AddActivityFromMessage(message)

/// システム活動追加 (グローバル関数) - Result型対応
let addSystemActivity (agentId: string) (activityType: ActivityType) (message: string) : Result<unit, string> =
    (getOrCreateActivityManager ()).AddSystemActivity(agentId, activityType, message)

/// 会話ペインTextView設定 (グローバル関数)
let setConversationTextView (textView: TextView) =
    (getOrCreateActivityManager ()).SetConversationTextView(textView)

/// 依存性注入: 既存のインスタンスを置き換え（テスト用）
let injectActivityManager (manager: UnifiedActivityManager) = activityManagerInstance <- Some manager

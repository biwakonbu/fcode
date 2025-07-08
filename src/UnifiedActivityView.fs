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
// インターフェース定義 (SOLID準拠設計)
// ===============================================

/// 活動データ変換インターフェース (Dependency Inversion Principle)
type IActivityTransformer =
    abstract member TransformMessage: AgentMessage -> Result<UnifiedActivity, string>
    abstract member GenerateActivityId: unit -> string

/// 活動ストレージインターフェース (Dependency Inversion Principle)
type IActivityStorage =
    abstract member AddActivity: UnifiedActivity -> Result<unit, string>
    abstract member GetActivities: unit -> UnifiedActivity[]
    abstract member GetActivityCount: unit -> int
    abstract member Clear: unit -> unit
    inherit IDisposable

/// 活動フォーマッターインターフェース (Interface Segregation Principle)
type IActivityFormatter =
    abstract member FormatActivitiesForDisplay: UnifiedActivity[] -> string
    abstract member GetActivityTypeDisplay: ActivityType -> string
    abstract member GetPriorityDisplay: MessagePriority -> string

/// UI更新インターフェース (Interface Segregation Principle)
type IUIUpdater =
    abstract member UpdateText: string -> Result<unit, string>
    abstract member SetTextView: TextView -> Result<unit, string>
    inherit IDisposable

/// 活動検証インターフェース (Single Responsibility Principle)
type IActivityValidator =
    abstract member ValidateActivities: UnifiedActivity[] -> Result<UnifiedActivity[], string>
    abstract member ValidateMessage: AgentMessage -> Result<AgentMessage, string>

// ===============================================
// 責務分離実装クラス (SOLID準拠設計)
// ===============================================

/// 活動データ変換実装 (Single Responsibility)
type private ActivityTransformer() =
    interface IActivityTransformer with
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
                        { ActivityId = (this :> IActivityTransformer).GenerateActivityId()
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
                Result.Error "Activity transformation failed"

        /// 活動ID生成
        member this.GenerateActivityId() =
            let timestamp = DateTime.Now.ToString("HHmmss")
            let guidPart = Guid.NewGuid().ToString("N")[..3]
            $"act-{timestamp}-{guidPart}"

/// 活動ストレージ実装 (Single Responsibility) - スレッドセーフ実装
type private ActivityStorage() =
    let activities = ConcurrentQueue<UnifiedActivity>()
    let maxActivities = 1000
    let storageSpinLock = ref 0
    let mutable disposed = false

    /// オブジェクトロックを使ったスレッドセーフ操作
    let lockObj = obj ()
    let withLock f = lock lockObj f

    interface IActivityStorage with
        member this.AddActivity(activity: UnifiedActivity) : Result<unit, string> =
            try
                withLock (fun () ->
                    activities.Enqueue(activity)
                    // 最大数超過時の古い活動削除（スレッドセーフ）
                    while activities.Count > maxActivities do
                        activities.TryDequeue() |> ignore)

                Result.Ok()
            with ex ->
                Result.Error "Failed to add activity"

        member this.GetActivities() =
            withLock (fun () -> activities.ToArray())

        member this.GetActivityCount() = activities.Count

        member this.Clear() =
            withLock (fun () ->
                while activities.TryDequeue() |> fst do
                    ())

    /// パブリックClearメソッド（互換性維持）
    member this.Clear() = (this :> IActivityStorage).Clear()

    /// リソース解放
    member this.Dispose() =
        if not disposed then
            disposed <- true
            this.Clear()
            GC.SuppressFinalize(this)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

/// 活動フォーマッター実装 (Single Responsibility)
type private ActivityFormatter() =
    interface IActivityFormatter with
        /// 活動表示フォーマット
        member this.FormatActivitiesForDisplay(activities: UnifiedActivity[]) =
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

                                let typeStr =
                                    (this :> IActivityFormatter).GetActivityTypeDisplay(activity.ActivityType)

                                let priorityStr = (this :> IActivityFormatter).GetPriorityDisplay(activity.Priority)

                                let messagePreview =
                                    if isNull activity.Message then
                                        "(空メッセージ)"
                                    elif activity.Message.Length > 60 then
                                        activity.Message.[..57] + "..."
                                    else
                                        activity.Message

                                $"[{timeStr}] {agentStr} {typeStr} {priorityStr} {messagePreview}"
                            with ex ->
                                logException "ActivityFormatter" "Activity formatting failed" ex
                                "[ERROR] 活動表示の処理に失敗しました")
                        |> String.concat "\n"

                    let footer =
                        $"\n\n--- 最新{recentActivities.Length}件 / 総活動数: {activities.Length} ---\nキーバインド: ESC(終了) Ctrl+X(コマンド) Ctrl+Tab(ペイン切替)"

                    header + activityLines + footer
            with ex ->
                logException "ActivityFormatter" "Format activities failed" ex
                "=== 統合エージェント活動ログ ===\n\n[ERROR] 活動表示の生成に失敗しました\n"

        /// 活動種別表示
        member this.GetActivityTypeDisplay(activityType: ActivityType) =
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
        member this.GetPriorityDisplay(priority: MessagePriority) =
            match priority with
            | Critical -> "[🔴]"
            | High -> "[🟡]"
            | Normal -> "[🟢]"
            | Low -> "[⚪]"

/// 活動検証実装 (Single Responsibility)
type private ActivityValidator() =
    interface IActivityValidator with
        member this.ValidateActivities(activities: UnifiedActivity[]) : Result<UnifiedActivity[], string> =
            try
                if isNull activities then
                    Result.Error "Activities array is null"
                else
                    let validActivities =
                        activities
                        |> Array.filter (fun a -> not (isNull a.AgentId) && not (String.IsNullOrWhiteSpace(a.Message)))

                    Result.Ok validActivities
            with ex ->
                Result.Error "Activity validation failed"

        member this.ValidateMessage(message: AgentMessage) : Result<AgentMessage, string> =
            try
                if String.IsNullOrWhiteSpace(message.FromAgent) then
                    Result.Error "エージェントIDが無効です"
                elif String.IsNullOrWhiteSpace(message.Content) then
                    Result.Error "メッセージ内容が無効です"
                else
                    Result.Ok message
            with ex ->
                Result.Error "Message validation failed"

/// UI更新実装 (Single Responsibility) - スレッドセーフ実装
type private ActivityUIUpdater() =
    let mutable conversationTextView: TextView option = None
    let uiLockObj = obj ()
    let mutable disposed = false

    /// UI操作用オブジェクトロック
    let withUILock f = lock uiLockObj f

    interface IUIUpdater with
        member this.SetTextView(textView: TextView) : Result<unit, string> =
            try
                if disposed then
                    Result.Error "UIUpdater is disposed"
                elif isNull textView then
                    Result.Error "TextView is null"
                else
                    withUILock (fun () ->
                        conversationTextView <- Some textView
                        logInfo "ActivityUIUpdater" "TextView set successfully")

                    Result.Ok()
            with ex ->
                logException "ActivityUIUpdater" "SetTextView failed" ex
                Result.Error "SetTextView failed"

        member this.UpdateText(content: string) : Result<unit, string> =
            try
                if disposed then
                    Result.Error "UIUpdater is disposed"
                elif isNull content then
                    Result.Error "Content is null"
                else
                    let currentTextView = withUILock (fun () -> conversationTextView)

                    match currentTextView with
                    | Some textView when not (isNull textView) ->
                        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

                        if not isCI then
                            this.SafeUIUpdate(textView, content)

                        Result.Ok()
                    | Some _ -> Result.Error "TextView is null"
                    | None -> Result.Error "TextView not set"
            with ex ->
                logException "ActivityUIUpdater" "UpdateText failed" ex
                Result.Error "UpdateText failed"

    /// 安全なUI更新
    member private this.SafeUIUpdate(textView: TextView, content: string) =
        try
            if not (isNull Application.MainLoop) then
                Application.MainLoop.Invoke(fun () ->
                    try
                        textView.Text <- ustring.Make(content)
                        textView.SetNeedsDisplay()
                    with ex ->
                        logException "ActivityUIUpdater" "UI thread update failed" ex)
            else
                // MainLoop未初期化の場合の直接更新
                textView.Text <- ustring.Make(content)
                textView.SetNeedsDisplay()
        with ex ->
            logException "ActivityUIUpdater" "Safe UI update failed" ex


    /// リソース解放
    member this.Dispose() =
        if not disposed then
            disposed <- true
            withUILock (fun () -> conversationTextView <- None)

    interface IDisposable with
        member this.Dispose() = this.Dispose()

// ===============================================
// 統合活動表示管理 (依存性注入によるSOLID設計)
// ===============================================

/// 統合活動表示管理クラス (真の依存性注入パターン)
type UnifiedActivityManager
    (
        transformer: IActivityTransformer,
        storage: IActivityStorage,
        uiUpdater: IUIUpdater,
        formatter: IActivityFormatter,
        validator: IActivityValidator
    ) =
    let mutable disposed = false

    /// デフォルトコンストラクタ (既存互換性維持)
    new() =
        let transformer = new ActivityTransformer() :> IActivityTransformer
        let storage = new ActivityStorage() :> IActivityStorage
        let uiUpdater = new ActivityUIUpdater() :> IUIUpdater
        let formatter = new ActivityFormatter() :> IActivityFormatter
        let validator = new ActivityValidator() :> IActivityValidator
        UnifiedActivityManager(transformer, storage, uiUpdater, formatter, validator)

    /// 会話ペインTextView設定
    member this.SetConversationTextView(textView: TextView) =
        this.ThrowIfDisposed()

        match uiUpdater.SetTextView(textView) with
        | Result.Ok() -> logInfo "UnifiedActivityView" "Conversation TextView set for unified activity display"
        | Result.Error error -> logWarning "UnifiedActivityView" $"Failed to set TextView: {error}"

    /// リソース解放
    member this.Dispose() =
        if not disposed then
            disposed <- true
            (storage :> IDisposable).Dispose()
            (uiUpdater :> IDisposable).Dispose()

    interface IDisposable with
        member this.Dispose() = this.Dispose()

    /// リソース解放状態確認
    member private this.ThrowIfDisposed() =
        if disposed then
            raise (ObjectDisposedException("UnifiedActivityManager"))

    /// エージェント活動追加 (AgentMessage経由) - Result型対応
    member this.AddActivityFromMessage(message: AgentMessage) : Result<unit, string> =
        this.ThrowIfDisposed()

        match validator.ValidateMessage(message) with
        | Result.Ok validMessage ->
            match transformer.TransformMessage(validMessage) with
            | Result.Ok activity -> this.AddActivity(activity)
            | Result.Error error ->
                logWarning "UnifiedActivityView" $"Message transformation failed: {error}"
                Result.Error error
        | Result.Error validationError ->
            logWarning "UnifiedActivityView" $"Message validation failed: {validationError}"
            Result.Error validationError

    /// エージェント活動追加 (直接) - Result型対応
    member this.AddActivity(activity: UnifiedActivity) : Result<unit, string> =
        this.ThrowIfDisposed()

        match storage.AddActivity(activity) with
        | Result.Ok() ->
            // 活動表示用にフォーマットしてUI更新
            let activities = storage.GetActivities()

            match validator.ValidateActivities(activities) with
            | Result.Ok validActivities ->
                let displayText = formatter.FormatActivitiesForDisplay(validActivities)

                match uiUpdater.UpdateText(displayText) with
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
            | Result.Error validationError ->
                logWarning "UnifiedActivityView" $"Activity validation failed: {validationError}"
                Result.Ok() // データ追加は成功
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
            let displayText = formatter.FormatActivitiesForDisplay(activities)

            match uiUpdater.UpdateText(displayText) with
            | Result.Ok() ->
                logInfo "UnifiedActivityView" "All activities cleared"
                Result.Ok()
            | Result.Error uiError ->
                logWarning "UnifiedActivityView" $"UI update after clear failed: {uiError}"
                Result.Ok() // データクリアは成功
        with ex ->
            let errorMsg = "Failed to clear activities"
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

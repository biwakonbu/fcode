module FCode.Collaboration.CollaborationTypes

open System

/// エージェント状態を表現
type AgentStatus =
    | Idle
    | Working
    | Blocked
    | Error
    | Completed

/// タスク状態を表現
type TaskStatus =
    | Pending
    | InProgress
    | Completed
    | Failed
    | Cancelled

/// タスク優先度
type TaskPriority =
    | Low = 1
    | Medium = 2
    | High = 3
    | Critical = 4

/// エラー結果型
type CollaborationError =
    | InvalidInput of string
    | NotFound of string
    | CircularDependency of string list
    | ConcurrencyError of string
    | SystemError of string
    | ConflictDetected of ConflictType list
    | DeadlockDetected of string list
    | ResourceUnavailable of string
    | EscalationFailed of string

/// 競合タイプ
and ConflictType =
    | ResourceConflict of string
    | TaskConflict of string * string
    | AgentConflict of string * string

/// エージェント状態情報
type AgentState =
    { AgentId: string
      Status: AgentStatus
      Progress: float
      LastUpdate: DateTime
      CurrentTask: string option
      WorkingDirectory: string
      ProcessId: int option }

    static member Create(agentId: string) =
        if String.IsNullOrWhiteSpace(agentId) then
            Result.Error(InvalidInput "AgentId cannot be null or empty")
        else
            Result.Ok
                { AgentId = agentId
                  Status = Idle
                  Progress = 0.0
                  LastUpdate = DateTime.UtcNow
                  CurrentTask = None
                  WorkingDirectory = ""
                  ProcessId = None }

/// タスク情報
type TaskInfo =
    { TaskId: string
      Title: string
      Description: string
      Status: TaskStatus
      AssignedAgent: string option
      Priority: TaskPriority
      EstimatedDuration: TimeSpan option
      ActualDuration: TimeSpan option
      RequiredResources: string list
      CreatedAt: DateTime
      UpdatedAt: DateTime }

    static member Create(taskId: string, title: string) =
        if String.IsNullOrWhiteSpace(taskId) then
            Result.Error(InvalidInput "TaskId cannot be null or empty")
        elif String.IsNullOrWhiteSpace(title) then
            Result.Error(InvalidInput "Title cannot be null or empty")
        else
            let now = DateTime.UtcNow

            Result.Ok
                { TaskId = taskId
                  Title = title
                  Description = ""
                  Status = Pending
                  AssignedAgent = None
                  Priority = TaskPriority.Medium
                  EstimatedDuration = None
                  ActualDuration = None
                  RequiredResources = []
                  CreatedAt = now
                  UpdatedAt = now }

    static member CreateWithResources(taskId: string, title: string, requiredResources: string list) =
        match TaskInfo.Create(taskId, title) with
        | Result.Ok task ->
            Result.Ok
                { task with
                    RequiredResources = requiredResources }
        | Result.Error e -> Result.Error e

/// 進捗サマリー
type ProgressSummary =
    { TotalTasks: int
      CompletedTasks: int
      InProgressTasks: int
      BlockedTasks: int
      ActiveAgents: int
      OverallProgress: float
      EstimatedTimeRemaining: TimeSpan option
      LastUpdated: DateTime }

/// タスク統計情報
type TaskStatistics =
    { TotalTasks: int
      CompletedTasks: int
      BlockedTasks: int
      ExecutableTasks: int
      CompletionRate: float }

/// スプリント統計情報
type SprintStatistic =
    | IntMetric of name: string * value: int
    | FloatMetric of name: string * value: float
    | TimeSpanMetric of name: string * value: TimeSpan
    | StringMetric of name: string * value: string

/// 依存関係情報
type TaskDependency =
    { TaskId: string
      DependsOn: string list
      Dependents: string list }

/// 協調イベント
type CollaborationEvent =
    | TaskStarted of agentId: string * taskId: string * resources: string list
    | TaskCompleted of agentId: string * taskId: string * resources: string list
    | SynchronizationRequested of agents: string list * reason: string
    | DeadlockDetected of agents: string list
    | SystemReset

/// エスカレーション致命度レベル
type EscalationSeverity =
    | Minor = 1 // 軽微: 自動対応可能
    | Moderate = 2 // 普通: 監視対象・自動回復試行
    | Important = 3 // 重要: PO即座通知・確認要求
    | Severe = 4 // 深刻: 緊急対応・作業停止推奨
    | Critical = 5 // 致命: 即座停止・データ保護・復旧優先

/// エスカレーション判断要素
type EscalationFactors =
    { ImpactScope: ImpactScope // 影響範囲: 単一タスク/複数タスク/全システム
      TimeConstraint: TimeConstraint // 時間制約: 緊急度・デッドライン
      RiskLevel: RiskLevel // リスク度: データ損失/品質/信頼性リスク
      BlockerType: BlockerType // ブロッカー種別: 技術/リソース/判断要求
      AutoRecoveryAttempts: int // 自動復旧試行回数
      DependentTaskCount: int } // 影響タスク数

/// 影響範囲
and ImpactScope =
    | SingleTask // 単一タスクのみ
    | RelatedTasks // 関連タスク群
    | AgentWorkflow // エージェントワークフロー
    | SystemWide // システム全体

/// 時間制約
and TimeConstraint =
    | NoUrgency // 緊急性なし
    | SoonDeadline of TimeSpan // 短期デッドライン
    | ImmediateAction // 即座対応必要
    | CriticalTiming // 致命的タイミング

/// リスクレベル
and RiskLevel =
    | LowRisk // 低リスク: 回復可能
    | ModerateRisk // 中リスク: 一部影響
    | HighRisk of string // 高リスク: 重大影響
    | CriticalRisk of string list // 致命リスク: 不可逆影響

/// ブロッカー種別
and BlockerType =
    | TechnicalIssue of string // 技術的問題
    | ResourceUnavailable of string // リソース不足
    | ExternalDependency of string // 外部依存
    | BusinessJudgment // ビジネス判断要求
    | QualityGate // 品質基準未達

/// エスカレーションコンテキスト
and EscalationContext =
    { EscalationId: string
      TaskId: string
      AgentId: string
      Severity: EscalationSeverity
      Factors: EscalationFactors
      Description: string
      DetectedAt: DateTime
      AutoRecoveryAttempted: bool
      RequiredActions: string list
      EstimatedResolutionTime: TimeSpan option }

/// エスカレーション対応アクション
type EscalationAction =
    | AutoRecover of string // 自動復旧実行
    | ContinueWithAlternative of string // 代替作業継続
    | WaitForPODecision of TimeSpan // PO判断待機
    | StopTaskExecution // タスク実行停止
    | EmergencyShutdown // 緊急シャットダウン
    | DataProtectionMode // データ保護モード

/// エスカレーション結果
type EscalationResult =
    { EscalationId: string
      Action: EscalationAction
      ResolvedAt: DateTime option
      ResolutionMethod: string option
      PONotified: bool
      ImpactMitigated: bool
      LessonsLearned: string list }

/// エスカレーションイベント
type EscalationEvent =
    | EscalationTriggered of EscalationContext
    | EscalationEscalated of string * EscalationSeverity
    | EscalationResolved of EscalationResult
    | PODecisionReceived of string * bool * string
    | AutoRecoverySucceeded of string
    | AutoRecoveryFailed of string * string

/// システムイベント
type SystemEvent =
    | AgentStateChanged of AgentState
    | TaskChanged of TaskInfo
    | ProgressUpdated of ProgressSummary
    | CollaborationEventOccurred of CollaborationEvent
    | EscalationOccurred of EscalationEvent
    | SystemReset

/// 設定情報
type CollaborationConfig =
    { MaxConcurrentAgents: int
      TaskTimeoutMinutes: int
      StaleAgentThreshold: TimeSpan
      MaxRetryAttempts: int
      // SQLite設定
      DatabasePath: string
      ConnectionPoolSize: int
      WALModeEnabled: bool
      AutoVacuumEnabled: bool
      MaxHistoryRetentionDays: int
      BackupEnabled: bool
      BackupIntervalHours: int
      // エスカレーション設定
      EscalationEnabled: bool
      AutoRecoveryMaxAttempts: int
      PONotificationThreshold: EscalationSeverity
      CriticalEscalationTimeoutMinutes: int
      DataProtectionModeEnabled: bool
      EmergencyShutdownEnabled: bool }

    static member Default =
        { MaxConcurrentAgents = 10
          TaskTimeoutMinutes = 30
          StaleAgentThreshold = TimeSpan.FromMinutes(5.0)
          MaxRetryAttempts = 3
          DatabasePath = "~/.fcode/tasks.db"
          ConnectionPoolSize = 5
          WALModeEnabled = true
          AutoVacuumEnabled = true
          MaxHistoryRetentionDays = 30
          BackupEnabled = true
          BackupIntervalHours = 24
          // エスカレーションデフォルト設定
          EscalationEnabled = true
          AutoRecoveryMaxAttempts = 3
          PONotificationThreshold = EscalationSeverity.Important
          CriticalEscalationTimeoutMinutes = 5
          DataProtectionModeEnabled = true
          EmergencyShutdownEnabled = false }

    member this.ConnectionString =
        let expandedPath =
            System.IO.Path.GetFullPath(
                this.DatabasePath.Replace(
                    "~",
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)
                )
            )

        $"Data Source={expandedPath};Cache=Shared;Pooling=true;Max Pool Size={this.ConnectionPoolSize}"

/// エスカレーション統計情報
type EscalationStatistics =
    { TotalEscalations: int
      EscalationsBySeverity: Map<EscalationSeverity, int>
      AutoRecoverySuccessRate: float
      AverageResolutionTime: TimeSpan
      PONotificationCount: int
      TopBlockerTypes: (BlockerType * int) list
      LastUpdated: DateTime }

// ========================================
// VirtualTimeSystem 型定義
// ========================================

/// 仮想時間単位
type VirtualTimeUnit =
    | VirtualHour of int // 1vh = 1分リアルタイム
    | VirtualDay of int // 1vd = 24分リアルタイム
    | VirtualSprint of int // 3vd = 72分リアルタイム

/// 仮想時間コンテキスト
type VirtualTimeContext =
    { StartTime: DateTime // スプリント開始時刻
      CurrentVirtualTime: VirtualTimeUnit // 現在の仮想時間
      ElapsedRealTime: TimeSpan // 実経過時間
      SprintDuration: TimeSpan // スプリント総時間 (72分)
      IsActive: bool // アクティブ状態
      LastUpdate: DateTime } // 最終更新時刻

/// スタンドアップMTG情報
type StandupMeeting =
    { MeetingId: string
      ScheduledTime: VirtualTimeUnit // 6vh毎
      ActualTime: DateTime
      Participants: string list // 参加エージェント
      ProgressReports: (string * string) list // エージェント別進捗報告
      Decisions: string list // 決定事項
      Adjustments: string list // 調整項目
      NextMeetingTime: VirtualTimeUnit }

/// レビューMTG情報
type ReviewMeeting =
    { MeetingId: string
      TriggerTime: VirtualTimeUnit // 72分（3vd）時点
      ActualTime: DateTime
      CompletionAssessment: CompletionAssessment
      QualityEvaluation: QualityEvaluationSummary
      NextSprintPlan: NextSprintPlan option
      ContinuationDecision: ContinuationDecision }

/// 完成度評価
and CompletionAssessment =
    { TasksCompleted: int
      TasksInProgress: int
      TasksBlocked: int
      OverallCompletionRate: float // 0.0-1.0
      QualityScore: float // 0.0-1.0
      AcceptanceCriteriaMet: bool
      RequiresPOApproval: bool }

/// 品質評価サマリー
and QualityEvaluationSummary =
    { CodeQuality: float // 0.0-1.0
      TestCoverage: float // 0.0-1.0
      DocumentationScore: float // 0.0-1.0
      SecurityCompliance: bool
      PerformanceMetrics: (string * float) list
      IssuesFound: string list
      RecommendedImprovements: string list }

/// 次スプリント計画
and NextSprintPlan =
    { SprintId: string
      Duration: VirtualTimeUnit
      PriorityTasks: string list
      ResourceAllocation: (string * int) list
      Dependencies: string list
      RiskMitigation: string list }

/// 継続判定
and ContinuationDecision =
    | AutoContinue of string // 自動継続 + 理由
    | RequirePOApproval of string // PO承認要求 + 理由
    | StopExecution of string // 実行停止 + 理由
    | EscalateToManagement of string // 経営陣エスカレーション + 理由

/// タイムイベント
type TimeEvent =
    | StandupScheduled of VirtualTimeUnit * string list // 時刻 + 参加者
    | ReviewMeetingTriggered of VirtualTimeUnit
    | TaskDeadlineApproaching of string * VirtualTimeUnit // タスクID + 期限
    | SprintCompleted of VirtualTimeUnit
    | EmergencyStop of string // 緊急停止理由

/// 仮想時間設定
type VirtualTimeConfig =
    { VirtualHourDurationMs: int // 1vh = 60000ms (1分)
      StandupIntervalVH: int // 6vh毎
      SprintDurationVD: int // 3vd
      AutoProgressReporting: bool
      EmergencyStopEnabled: bool
      MaxConcurrentSprints: int }

    static member Default =
        { VirtualHourDurationMs = 60000 // 1分
          StandupIntervalVH = 6 // 6vh毎
          SprintDurationVD = 3 // 3vd
          AutoProgressReporting = true
          EmergencyStopEnabled = true
          MaxConcurrentSprints = 5 }

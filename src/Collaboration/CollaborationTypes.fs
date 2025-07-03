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

/// システムイベント
type SystemEvent =
    | AgentStateChanged of AgentState
    | TaskChanged of TaskInfo
    | ProgressUpdated of ProgressSummary
    | CollaborationEventOccurred of CollaborationEvent
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
      BackupIntervalHours: int }

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
          BackupIntervalHours = 24 }

    member this.ConnectionString =
        let expandedPath =
            System.IO.Path.GetFullPath(
                this.DatabasePath.Replace(
                    "~",
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)
                )
            )

        $"Data Source={expandedPath};Cache=Shared;Pooling=true;Max Pool Size={this.ConnectionPoolSize}"

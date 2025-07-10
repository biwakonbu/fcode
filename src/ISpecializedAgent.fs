module FCode.ISpecializedAgent

open System
open System.Threading.Tasks
open FCode.Logger
open FCode.FCodeError

// ===============================================
// 専門エージェント統合インターフェース
// ===============================================

/// 専門分野定義
type AgentSpecialization =
    | Database // データベース設計・最適化
    | API // API設計・実装
    | TestAutomation // テスト自動化・品質保証
    | DevOps // DevOps・CI/CD・インフラ
    | Security // セキュリティ・脆弱性対策
    | Performance // パフォーマンス・最適化
    | Frontend // フロントエンド・UI/UX
    | Backend // バックエンド・アーキテクチャ
    | DataScience // データサイエンス・機械学習
    | Mobile // モバイル開発
    | CloudInfra // クラウドインフラ・アーキテクチャ
    | Documentation // ドキュメント・技術文書

/// エージェント能力定義
type AgentCapability =
    | CodeGeneration // コード生成
    | CodeReview // コードレビュー
    | ArchitectureDesign // アーキテクチャ設計
    | Testing // テスト設計・実行
    | Debugging // デバッグ・トラブルシューティング
    | Optimization // 最適化・パフォーマンス改善
    | Security // セキュリティ・脆弱性対策
    | Documentation // ドキュメント作成
    | Monitoring // 監視・メトリクス
    | Deployment // デプロイ・リリース
    | Consultation // コンサルティング・アドバイス
    | Training // 教育・トレーニング

/// エージェント統合設定
type AgentIntegrationConfig =
    { AgentId: string
      Specialization: AgentSpecialization
      Capabilities: AgentCapability list
      Priority: int // 優先度 (1-10)
      MaxConcurrency: int // 最大並行処理数
      TimeoutSeconds: int // タイムアウト秒数
      ModelSelection: string option // 推奨AIモデル
      CommandPrefix: string option // コマンドプレフィックス
      WorkingDirectory: string option // 専用作業ディレクトリ
      EnvironmentVariables: Map<string, string> // 環境変数
      ResourceLimits: ResourceLimits option // リソース制限
      SecuritySettings: SecuritySettings option } // セキュリティ設定

/// リソース制限
and ResourceLimits =
    { MaxMemoryMB: int
      MaxCpuPercent: int
      MaxDiskSpaceMB: int
      MaxNetworkMBps: int }

/// セキュリティ設定
and SecuritySettings =
    { AllowedCommands: string list
      BlockedCommands: string list
      AllowedPaths: string list
      BlockedPaths: string list
      RequireApproval: bool
      AuditEnabled: bool }

/// エージェント実行コンテキスト
type AgentExecutionContext =
    { RequestId: string
      UserId: string
      ProjectPath: string
      Task: string
      Context: Map<string, obj>
      Timestamp: DateTime
      Timeout: TimeSpan
      Priority: int }

/// エージェント実行結果
type AgentExecutionResult =
    { Success: bool
      Output: string
      Error: string option
      ExecutionTime: TimeSpan
      ResourceUsage: ResourceUsage option
      Recommendations: string list
      GeneratedFiles: string list
      ModifiedFiles: string list
      Timestamp: DateTime }

/// リソース使用量
and ResourceUsage =
    { MemoryUsedMB: int
      CpuPercent: int
      DiskUsedMB: int
      NetworkUsedMB: int
      Duration: TimeSpan }

/// エージェント状態
type AgentState =
    | Available // 利用可能
    | Busy // 実行中
    | Maintenance // メンテナンス中
    | Offline // オフライン
    | Error // エラー状態

/// エージェント健全性情報
type AgentHealthInfo =
    { State: AgentState
      LastHeartbeat: DateTime
      SuccessRate: float
      AverageResponseTime: TimeSpan
      ErrorCount: int
      TotalRequests: int
      UpTime: TimeSpan }

/// エージェント統計情報
type AgentStatistics =
    { TotalRequests: int
      SuccessfulRequests: int
      FailedRequests: int
      AverageExecutionTime: TimeSpan
      TotalExecutionTime: TimeSpan
      PeakMemoryUsage: int
      PeakCpuUsage: int
      LastActivityTime: DateTime
      CreatedTime: DateTime }

/// エージェント通知
type AgentNotification =
    | TaskStarted of string * DateTime
    | TaskCompleted of string * DateTime * bool
    | TaskFailed of string * DateTime * string
    | AgentStateChanged of AgentState * DateTime
    | ResourceThresholdExceeded of string * DateTime
    | SecurityViolation of string * DateTime

// ===============================================
// 専門エージェント統合インターフェース定義
// ===============================================

/// 専門エージェント統合インターフェース
type ISpecializedAgent =
    /// エージェントID
    abstract member AgentId: string

    /// 専門分野
    abstract member Specialization: AgentSpecialization

    /// 能力リスト
    abstract member Capabilities: AgentCapability list

    /// 設定
    abstract member Configuration: AgentIntegrationConfig

    /// 現在の状態
    abstract member CurrentState: AgentState

    /// 健全性情報
    abstract member HealthInfo: AgentHealthInfo

    /// 統計情報
    abstract member Statistics: AgentStatistics

    /// タスク実行
    abstract member ExecuteTask: AgentExecutionContext -> Task<Result<AgentExecutionResult, FCodeError>>

    /// エージェント初期化
    abstract member Initialize: unit -> Task<Result<unit, FCodeError>>

    /// エージェント終了
    abstract member Shutdown: unit -> Task<Result<unit, FCodeError>>

    /// 健全性チェック
    abstract member HealthCheck: unit -> Task<Result<AgentHealthInfo, FCodeError>>

    /// 設定更新
    abstract member UpdateConfiguration: AgentIntegrationConfig -> Task<Result<unit, FCodeError>>

    /// 統計情報リセット
    abstract member ResetStatistics: unit -> Task<Result<unit, FCodeError>>

    /// 通知イベント
    abstract member NotificationEvent: IEvent<AgentNotification>

// ===============================================
// 専門エージェント管理インターフェース
// ===============================================

/// 専門エージェント管理インターフェース
type ISpecializedAgentManager =
    /// エージェント登録
    abstract member RegisterAgent: ISpecializedAgent -> Task<Result<unit, FCodeError>>

    /// エージェント登録解除
    abstract member UnregisterAgent: string -> Task<Result<unit, FCodeError>>

    /// エージェント取得
    abstract member GetAgent: string -> Task<Result<ISpecializedAgent option, FCodeError>>

    /// 専門分野別エージェント取得
    abstract member GetAgentsBySpecialization: AgentSpecialization -> Task<Result<ISpecializedAgent list, FCodeError>>

    /// 能力別エージェント取得
    abstract member GetAgentsByCapability: AgentCapability -> Task<Result<ISpecializedAgent list, FCodeError>>

    /// 最適エージェント選択
    abstract member SelectBestAgent:
        AgentSpecialization * AgentCapability list * int -> Task<Result<ISpecializedAgent option, FCodeError>>

    /// 全エージェント取得
    abstract member GetAllAgents: unit -> Task<Result<ISpecializedAgent list, FCodeError>>

    /// 利用可能エージェント取得
    abstract member GetAvailableAgents: unit -> Task<Result<ISpecializedAgent list, FCodeError>>

    /// エージェント状態監視
    abstract member MonitorAgents: unit -> Task<Result<Map<string, AgentHealthInfo>, FCodeError>>

    /// エージェント統計取得
    abstract member GetAgentStatistics: string -> Task<Result<AgentStatistics option, FCodeError>>

    /// 全エージェント統計取得
    abstract member GetAllAgentStatistics: unit -> Task<Result<Map<string, AgentStatistics>, FCodeError>>

    /// エージェント通知イベント
    abstract member NotificationEvent: IEvent<string * AgentNotification>

// ===============================================
// 専門エージェント協調インターフェース
// ===============================================

/// エージェント協調タスク
type AgentCollaborationTask =
    { TaskId: string
      MainAgent: string
      CollaboratingAgents: string list
      Task: string
      Dependencies: string list
      EstimatedDuration: TimeSpan
      Priority: int
      Context: Map<string, obj>
      CreatedTime: DateTime }

/// 協調実行結果
type CollaborationResult =
    { TaskId: string
      Success: bool
      Results: Map<string, AgentExecutionResult>
      TotalExecutionTime: TimeSpan
      Recommendations: string list
      GeneratedFiles: string list
      ModifiedFiles: string list
      Timestamp: DateTime }

/// 専門エージェント協調インターフェース
type ISpecializedAgentCollaborator =
    /// 協調タスク実行
    abstract member ExecuteCollaborativeTask: AgentCollaborationTask -> Task<Result<CollaborationResult, FCodeError>>

    /// 協調タスク状態取得
    abstract member GetTaskStatus: string -> Task<Result<string * float, FCodeError>>

    /// 協調タスク中止
    abstract member CancelTask: string -> Task<Result<unit, FCodeError>>

    /// 協調タスク履歴取得
    abstract member GetTaskHistory: int -> Task<Result<AgentCollaborationTask list, FCodeError>>

    /// 協調結果履歴取得
    abstract member GetResultHistory: int -> Task<Result<CollaborationResult list, FCodeError>>

// ===============================================
// 専門エージェント統合ユーティリティ
// ===============================================

/// 専門エージェント統合ユーティリティ
module AgentIntegrationUtils =

    /// 専門分野名取得
    let getSpecializationName (spec: AgentSpecialization) =
        match spec with
        | Database -> "データベース設計・最適化"
        | API -> "API設計・実装"
        | TestAutomation -> "テスト自動化・品質保証"
        | DevOps -> "DevOps・CI/CD・インフラ"
        | AgentSpecialization.Security -> "セキュリティ・脆弱性対策"
        | Performance -> "パフォーマンス・最適化"
        | Frontend -> "フロントエンド・UI/UX"
        | Backend -> "バックエンド・アーキテクチャ"
        | DataScience -> "データサイエンス・機械学習"
        | Mobile -> "モバイル開発"
        | CloudInfra -> "クラウドインフラ・アーキテクチャ"
        | Documentation -> "ドキュメント・技術文書"

    /// 能力名取得
    let getCapabilityName (cap: AgentCapability) =
        match cap with
        | CodeGeneration -> "コード生成"
        | CodeReview -> "コードレビュー"
        | ArchitectureDesign -> "アーキテクチャ設計"
        | Testing -> "テスト設計・実行"
        | Debugging -> "デバッグ・トラブルシューティング"
        | Optimization -> "最適化・パフォーマンス改善"
        | AgentCapability.Security -> "セキュリティ・脆弱性対策"
        | AgentCapability.Documentation -> "ドキュメント作成"
        | Monitoring -> "監視・メトリクス"
        | Deployment -> "デプロイ・リリース"
        | Consultation -> "コンサルティング・アドバイス"
        | Training -> "教育・トレーニング"

    /// 状態名取得
    let getStateName (state: AgentState) =
        match state with
        | Available -> "利用可能"
        | Busy -> "実行中"
        | Maintenance -> "メンテナンス中"
        | Offline -> "オフライン"
        | Error -> "エラー状態"

    /// デフォルト設定作成
    let createDefaultConfig
        (agentId: string)
        (specialization: AgentSpecialization)
        (capabilities: AgentCapability list)
        =
        { AgentId = agentId
          Specialization = specialization
          Capabilities = capabilities
          Priority = 5
          MaxConcurrency = 3
          TimeoutSeconds = 300
          ModelSelection = None
          CommandPrefix = None
          WorkingDirectory = None
          EnvironmentVariables = Map.empty
          ResourceLimits =
            Some
                { MaxMemoryMB = 2048
                  MaxCpuPercent = 80
                  MaxDiskSpaceMB = 1024
                  MaxNetworkMBps = 10 }
          SecuritySettings =
            Some
                { AllowedCommands = []
                  BlockedCommands = [ "rm"; "rmdir"; "del"; "format"; "fdisk" ]
                  AllowedPaths = []
                  BlockedPaths = [ "/etc"; "/sys"; "/proc"; "C:\\Windows\\System32" ]
                  RequireApproval = false
                  AuditEnabled = true } }

    /// 実行コンテキスト作成
    let createExecutionContext (requestId: string) (userId: string) (projectPath: string) (task: string) =
        { RequestId = requestId
          UserId = userId
          ProjectPath = projectPath
          Task = task
          Context = Map.empty
          Timestamp = DateTime.Now
          Timeout = TimeSpan.FromMinutes(5.0)
          Priority = 5 }

    /// 協調タスク作成
    let createCollaborationTask (taskId: string) (mainAgent: string) (collaboratingAgents: string list) (task: string) =
        { TaskId = taskId
          MainAgent = mainAgent
          CollaboratingAgents = collaboratingAgents
          Task = task
          Dependencies = []
          EstimatedDuration = TimeSpan.FromMinutes(30.0)
          Priority = 5
          Context = Map.empty
          CreatedTime = DateTime.Now }

    /// リソース使用量チェック
    let checkResourceUsage (usage: ResourceUsage) (limits: ResourceLimits) =
        [ if usage.MemoryUsedMB > limits.MaxMemoryMB then
              yield $"メモリ使用量超過: {usage.MemoryUsedMB}MB > {limits.MaxMemoryMB}MB"
          if usage.CpuPercent > limits.MaxCpuPercent then
              yield $"CPU使用量超過: {usage.CpuPercent}% > {limits.MaxCpuPercent}%"
          if usage.DiskUsedMB > limits.MaxDiskSpaceMB then
              yield $"ディスク使用量超過: {usage.DiskUsedMB}MB > {limits.MaxDiskSpaceMB}MB"
          if usage.NetworkUsedMB > limits.MaxNetworkMBps then
              yield $"ネットワーク使用量超過: {usage.NetworkUsedMB}MB > {limits.MaxNetworkMBps}MB" ]

    /// セキュリティ設定検証
    let validateSecuritySettings (settings: SecuritySettings) (command: string) (path: string) =
        let commandBlocked = settings.BlockedCommands |> List.exists (command.Contains)
        let pathBlocked = settings.BlockedPaths |> List.exists (path.StartsWith)

        let commandAllowed =
            settings.AllowedCommands.IsEmpty
            || settings.AllowedCommands |> List.exists (command.Contains)

        let pathAllowed =
            settings.AllowedPaths.IsEmpty
            || settings.AllowedPaths |> List.exists (path.StartsWith)

        if commandBlocked then
            Error $"ブロックされたコマンド: {command}"
        elif pathBlocked then
            Error $"ブロックされたパス: {path}"
        elif not commandAllowed then
            Error $"許可されていないコマンド: {command}"
        elif not pathAllowed then
            Error $"許可されていないパス: {path}"
        else
            Ok()

    /// エージェント統計更新
    let updateStatistics (oldStats: AgentStatistics) (result: AgentExecutionResult) =
        { oldStats with
            TotalRequests = oldStats.TotalRequests + 1
            SuccessfulRequests = oldStats.SuccessfulRequests + (if result.Success then 1 else 0)
            FailedRequests = oldStats.FailedRequests + (if result.Success then 0 else 1)
            TotalExecutionTime = oldStats.TotalExecutionTime + result.ExecutionTime
            AverageExecutionTime =
                let totalTime = oldStats.TotalExecutionTime + result.ExecutionTime
                TimeSpan.FromMilliseconds(totalTime.TotalMilliseconds / float (oldStats.TotalRequests + 1))
            PeakMemoryUsage =
                match result.ResourceUsage with
                | Some usage -> max oldStats.PeakMemoryUsage usage.MemoryUsedMB
                | None -> oldStats.PeakMemoryUsage
            PeakCpuUsage =
                match result.ResourceUsage with
                | Some usage -> max oldStats.PeakCpuUsage usage.CpuPercent
                | None -> oldStats.PeakCpuUsage
            LastActivityTime = DateTime.Now }

    /// エージェント健全性評価
    let evaluateHealth (stats: AgentStatistics) (lastHeartbeat: DateTime) =
        let successRate =
            if stats.TotalRequests = 0 then
                1.0
            else
                float stats.SuccessfulRequests / float stats.TotalRequests

        let upTime = DateTime.Now - stats.CreatedTime
        let heartbeatAge = DateTime.Now - lastHeartbeat

        let state =
            if heartbeatAge > TimeSpan.FromMinutes(5.0) then
                Offline
            elif successRate < 0.5 then
                Error
            elif stats.PeakMemoryUsage > 1500 || stats.PeakCpuUsage > 90 then
                Maintenance
            else
                Available

        { State = state
          LastHeartbeat = lastHeartbeat
          SuccessRate = successRate
          AverageResponseTime = stats.AverageExecutionTime
          ErrorCount = stats.FailedRequests
          TotalRequests = stats.TotalRequests
          UpTime = upTime }

    /// ログ出力ヘルパー
    let logAgentActivity (agentId: string) (activity: string) =
        logInfo "ISpecializedAgent" $"Agent {agentId}: {activity}"

    /// エラーログ出力ヘルパー
    let logAgentError (agentId: string) (error: string) =
        logError "ISpecializedAgent" $"Agent {agentId}: {error}"

    /// 通知ログ出力ヘルパー
    let logAgentNotification (agentId: string) (notification: AgentNotification) =
        let message =
            match notification with
            | TaskStarted(taskId, timestamp) -> $"タスク開始: {taskId} at {timestamp}"
            | TaskCompleted(taskId, timestamp, success) -> $"タスク完了: {taskId} at {timestamp} (成功: {success})"
            | TaskFailed(taskId, timestamp, error) -> $"タスク失敗: {taskId} at {timestamp} ({error})"
            | AgentStateChanged(state, timestamp) -> $"状態変更: {getStateName state} at {timestamp}"
            | ResourceThresholdExceeded(resource, timestamp) -> $"リソース制限超過: {resource} at {timestamp}"
            | SecurityViolation(violation, timestamp) -> $"セキュリティ違反: {violation} at {timestamp}"

        logInfo "ISpecializedAgent" $"Agent {agentId}: {message}"

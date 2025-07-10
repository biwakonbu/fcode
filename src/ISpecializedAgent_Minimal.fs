module FCode.ISpecializedAgent

open System
open System.Threading.Tasks
open FCode.Logger
open FCode.FCodeError

// ===============================================
// 専門エージェント統合インターフェース (最小実装)
// ===============================================

/// 専門分野定義
type AgentSpecialization =
    | Database
    | API
    | TestAutomation
    | DevOps
    | Security
    | Performance
    | Frontend
    | Backend
    | DataScience
    | Mobile
    | CloudInfra
    | Documentation

/// エージェント能力定義
type AgentCapability =
    | CodeGeneration
    | CodeReview
    | ArchitectureDesign
    | Testing
    | Debugging
    | Optimization
    | Security
    | Documentation
    | Monitoring
    | Deployment
    | Consultation
    | Training

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
      Timestamp: DateTime }

/// エージェント状態
type AgentState =
    | Available
    | Busy
    | Maintenance
    | Offline
    | Error

/// 専門エージェントインターフェース (最小実装)
type ISpecializedAgent =
    abstract member AgentId: string
    abstract member Specialization: AgentSpecialization
    abstract member Capabilities: AgentCapability list
    abstract member CurrentState: AgentState
    abstract member ExecuteTask: AgentExecutionContext -> Task<Result<AgentExecutionResult, FCodeError>>

/// 専門エージェント管理インターフェース (最小実装)
type ISpecializedAgentManager =
    abstract member RegisterAgent: ISpecializedAgent -> Task<Result<unit, FCodeError>>
    abstract member GetAgent: string -> Task<Result<ISpecializedAgent option, FCodeError>>
    abstract member GetAvailableAgents: unit -> Task<Result<ISpecializedAgent list, FCodeError>>

/// ユーティリティ関数
module AgentIntegrationUtils =

    /// 専門分野名取得
    let getSpecializationName (spec: AgentSpecialization) =
        match spec with
        | Database -> "データベース設計・最適化"
        | API -> "API設計・実装"
        | TestAutomation -> "テスト自動化・品質保証"
        | DevOps -> "DevOps・CI/CD・インフラ"
        | Security -> "セキュリティ・脆弱性対策"
        | Performance -> "パフォーマンス・最適化"
        | Frontend -> "フロントエンド・UI/UX"
        | Backend -> "バックエンド・アーキテクチャ"
        | DataScience -> "データサイエンス・機械学習"
        | Mobile -> "モバイル開発"
        | CloudInfra -> "クラウドインフラ・アーキテクチャ"
        | Documentation -> "ドキュメント・技術文書"

    /// 状態名取得
    let getStateName (state: AgentState) =
        match state with
        | Available -> "利用可能"
        | Busy -> "実行中"
        | Maintenance -> "メンテナンス中"
        | Offline -> "オフライン"
        | Error -> "エラー状態"

    /// ログ出力ヘルパー
    let logAgentActivity (agentId: string) (activity: string) =
        logInfo "ISpecializedAgent" $"Agent {agentId}: {activity}"

module FCode.Configuration

open System
open System.IO
open FCode.Logger

// ===============================================
// 設定管理システム
// ===============================================

/// アプリケーション設定定義
type ApplicationConfig =
    { // プロセス設定
      ProcessTimeouts: ProcessTimeoutConfig
      // ポート設定
      PortConfig: PortConfiguration
      // セキュリティ設定
      SecurityConfig: SecurityConfiguration
      // AI モデル設定
      AIModelConfig: AIModelConfiguration }

/// プロセスタイムアウト設定
and ProcessTimeoutConfig =
    { GitTimeout: int // ミリ秒
      DockerTimeout: int // ミリ秒
      BuildTimeout: int // ミリ秒
      TestTimeout: int // ミリ秒
      DeployTimeout: int // ミリ秒
      ProcessKillTimeout: int } // ミリ秒

/// ポート設定
and PortConfiguration =
    { ApplicationPort: int
      DatabasePort: int
      MonitoringPort: int option
      CustomPorts: Map<string, int> }

/// セキュリティ設定
and SecurityConfiguration =
    { MaxBranchNameLength: int
      AllowedFileExtensions: string list
      DangerousPathPatterns: string list
      MaxPathLength: int }

/// AI モデル設定
and AIModelConfiguration =
    { DefaultModel: string
      MaxTokens: int
      CostThreshold: float
      TimeoutThreshold: TimeSpan }

// ===============================================
// デフォルト設定定義
// ===============================================

/// デフォルト設定値
module DefaultConfig =

    let processTimeouts =
        { GitTimeout = 5000
          DockerTimeout = 10000
          BuildTimeout = 60000
          TestTimeout = 30000
          DeployTimeout = 90000
          ProcessKillTimeout = 1000 }

    let portConfig =
        { ApplicationPort = 8080
          DatabasePort = 5432
          MonitoringPort = Some 3000
          CustomPorts = Map.empty }

    let securityConfig =
        { MaxBranchNameLength = 100
          AllowedFileExtensions = [ ".fs"; ".fsproj"; ".md"; ".yml"; ".yaml"; ".json"; ".txt" ]
          DangerousPathPatterns = [ "../"; "..\\"; ";"; "|"; "&"; "`"; "$"; "'"; "\""; "\n"; "\r"; "\t" ]
          MaxPathLength = 260 }

    let aiModelConfig =
        { DefaultModel = "claude-3-sonnet"
          MaxTokens = 200000
          CostThreshold = 1.0
          TimeoutThreshold = TimeSpan.FromMinutes(2.0) }

    let applicationConfig =
        { ProcessTimeouts = processTimeouts
          PortConfig = portConfig
          SecurityConfig = securityConfig
          AIModelConfig = aiModelConfig }

// ===============================================
// 設定管理マネージャー
// ===============================================

/// 設定ファイル管理
type ConfigurationManager() =
    static let mutable currentConfig = DefaultConfig.applicationConfig

    /// 現在の設定取得
    static member Current = currentConfig

    /// 設定の更新
    static member UpdateConfig(newConfig: ApplicationConfig) =
        currentConfig <- newConfig
        logInfo "ConfigurationManager" "アプリケーション設定が更新されました"

    /// 環境変数からの設定読み込み
    static member LoadFromEnvironment() =
        try
            let getEnvInt key defaultValue =
                match Environment.GetEnvironmentVariable(key) with
                | null
                | "" -> defaultValue
                | value ->
                    match Int32.TryParse(value) with
                    | true, parsed -> parsed
                    | false, _ ->
                        logWarning "ConfigurationManager" $"環境変数 {key} の値が無効です。デフォルト値を使用します: {defaultValue}"
                        defaultValue

            let updatedTimeouts =
                { GitTimeout = getEnvInt "FCODE_GIT_TIMEOUT" DefaultConfig.processTimeouts.GitTimeout
                  DockerTimeout = getEnvInt "FCODE_DOCKER_TIMEOUT" DefaultConfig.processTimeouts.DockerTimeout
                  BuildTimeout = getEnvInt "FCODE_BUILD_TIMEOUT" DefaultConfig.processTimeouts.BuildTimeout
                  TestTimeout = getEnvInt "FCODE_TEST_TIMEOUT" DefaultConfig.processTimeouts.TestTimeout
                  DeployTimeout = getEnvInt "FCODE_DEPLOY_TIMEOUT" DefaultConfig.processTimeouts.DeployTimeout
                  ProcessKillTimeout =
                    getEnvInt "FCODE_PROCESS_KILL_TIMEOUT" DefaultConfig.processTimeouts.ProcessKillTimeout }

            let updatedPorts =
                { ApplicationPort = getEnvInt "FCODE_APP_PORT" DefaultConfig.portConfig.ApplicationPort
                  DatabasePort = getEnvInt "FCODE_DB_PORT" DefaultConfig.portConfig.DatabasePort
                  MonitoringPort =
                    match getEnvInt "FCODE_MONITORING_PORT" 0 with
                    | 0 -> None
                    | port -> Some port
                  CustomPorts = DefaultConfig.portConfig.CustomPorts }

            let updatedConfig =
                { DefaultConfig.applicationConfig with
                    ProcessTimeouts = updatedTimeouts
                    PortConfig = updatedPorts }

            ConfigurationManager.UpdateConfig(updatedConfig)
            logInfo "ConfigurationManager" "環境変数から設定を読み込みました"

        with ex ->
            logError "ConfigurationManager" $"環境変数設定読み込みエラー: {ex.Message}"
            logInfo "ConfigurationManager" "デフォルト設定を使用します"

// ===============================================
// 設定アクセスヘルパー
// ===============================================

/// 設定値への簡単アクセス
module Config =

    /// プロセスタイムアウト取得
    let getGitTimeout () =
        ConfigurationManager.Current.ProcessTimeouts.GitTimeout

    let getDockerTimeout () =
        ConfigurationManager.Current.ProcessTimeouts.DockerTimeout

    let getBuildTimeout () =
        ConfigurationManager.Current.ProcessTimeouts.BuildTimeout

    let getTestTimeout () =
        ConfigurationManager.Current.ProcessTimeouts.TestTimeout

    let getDeployTimeout () =
        ConfigurationManager.Current.ProcessTimeouts.DeployTimeout

    let getProcessKillTimeout () =
        ConfigurationManager.Current.ProcessTimeouts.ProcessKillTimeout

    /// ポート設定取得
    let getApplicationPort () =
        ConfigurationManager.Current.PortConfig.ApplicationPort

    let getDatabasePort () =
        ConfigurationManager.Current.PortConfig.DatabasePort

    let getMonitoringPort () =
        ConfigurationManager.Current.PortConfig.MonitoringPort

    /// セキュリティ設定取得
    let getMaxBranchNameLength () =
        ConfigurationManager.Current.SecurityConfig.MaxBranchNameLength

    let getAllowedFileExtensions () =
        ConfigurationManager.Current.SecurityConfig.AllowedFileExtensions

    let getDangerousPathPatterns () =
        ConfigurationManager.Current.SecurityConfig.DangerousPathPatterns

    let getMaxPathLength () =
        ConfigurationManager.Current.SecurityConfig.MaxPathLength

    /// AI モデル設定取得
    let getDefaultModel () =
        ConfigurationManager.Current.AIModelConfig.DefaultModel

    let getMaxTokens () =
        ConfigurationManager.Current.AIModelConfig.MaxTokens

    let getCostThreshold () =
        ConfigurationManager.Current.AIModelConfig.CostThreshold

    let getTimeoutThreshold () =
        ConfigurationManager.Current.AIModelConfig.TimeoutThreshold

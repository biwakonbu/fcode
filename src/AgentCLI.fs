module FCode.AgentCLI

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open FCode.Logger
open FCode.AgentConfiguration

// ===============================================
// エージェント能力・出力定義
// ===============================================

/// エージェントの専門能力定義
type AgentCapability =
    | CodeGeneration // コード生成・実装
    | Testing // テスト実装・品質検証
    | Documentation // ドキュメント作成・更新
    | Debugging // デバッグ・問題解決
    | Refactoring // リファクタリング・最適化
    | ArchitectureDesign // アーキテクチャ設計
    | CodeReview // コードレビュー・改善提案
    | ProjectManagement // プロジェクト管理・進捗追跡
    | UserExperience // UX設計・ユーザビリティ
    | QualityAssurance // 品質保証・探索的テスト

/// エージェント実行ステータス
type AgentStatus =
    | Success // 正常完了
    | Error // エラー発生
    | InProgress // 実行中
    | Timeout // タイムアウト
    | Cancelled // キャンセル

/// エージェント出力の構造化データ
type AgentOutput =
    { Status: AgentStatus // 実行ステータス
      Content: string // 主要出力内容
      Metadata: Map<string, string> // 追加メタデータ
      Timestamp: DateTime // 出力タイムスタンプ
      SourceAgent: string // 出力元エージェント名
      Capabilities: AgentCapability list } // 実行時使用能力

/// エージェント統合設定
type AgentIntegrationConfig =
    { Name: string // エージェント名
      CliPath: string // CLI実行パス
      DefaultArgs: string list // デフォルト引数
      OutputFormat: string // 出力形式 (text/json/markdown)
      Timeout: TimeSpan // タイムアウト時間
      MaxRetries: int // 最大リトライ回数
      SupportedCapabilities: AgentCapability list // サポート能力
      EnvironmentVariables: Map<string, string> } // 環境変数

// ===============================================
// IAgentCLI汎用インターフェース定義
// ===============================================

/// 汎用CLI統合インターフェース
type IAgentCLI =
    /// エージェント名
    abstract member Name: string

    /// CLI実行コマンド構築
    abstract member StartCommand: string -> ProcessStartInfo

    /// 出力解析・構造化データ変換
    abstract member ParseOutput: string -> AgentOutput

    /// サポート能力一覧
    abstract member SupportedCapabilities: AgentCapability list

    /// エージェント固有設定
    abstract member Config: AgentIntegrationConfig

// ===============================================
// Claude Code CLI実装
// ===============================================

/// Claude Code CLI統合実装
type ClaudeCodeCLI(config: AgentIntegrationConfig) =
    interface IAgentCLI with
        member _.Name = "Claude Code"

        member _.StartCommand(input: string) =
            let startInfo = ProcessStartInfo()
            startInfo.FileName <- config.CliPath
            startInfo.Arguments <- String.Join(" ", config.DefaultArgs) + " " + input
            startInfo.UseShellExecute <- false
            startInfo.RedirectStandardOutput <- true
            startInfo.RedirectStandardError <- true
            startInfo.RedirectStandardInput <- true
            startInfo.CreateNoWindow <- true
            startInfo.WorkingDirectory <- Environment.CurrentDirectory

            // 環境変数設定
            for kvp in config.EnvironmentVariables do
                startInfo.Environment.[kvp.Key] <- kvp.Value

            logDebug "ClaudeCodeCLI" $"StartCommand: {startInfo.FileName} {startInfo.Arguments}"
            startInfo

        member _.ParseOutput(rawOutput: string) =
            try
                // Claude Code出力の解析
                let lines = rawOutput.Split('\n')

                let content =
                    String.Join(
                        "\n",
                        lines
                        |> Array.filter (fun line -> not (line.StartsWith("DEBUG:") || line.StartsWith("INFO:")))
                    )

                { Status =
                    if content.Contains("error") || content.Contains("Error") then
                        Error
                    else
                        Success
                  Content = content.Trim()
                  Metadata =
                    Map.empty.Add("output_length", content.Length.ToString()).Add("line_count", lines.Length.ToString())
                  Timestamp = DateTime.Now
                  SourceAgent = "Claude Code"
                  Capabilities = [ CodeGeneration; Documentation; Debugging; CodeReview ] }
            with ex ->
                logError "ClaudeCodeCLI" $"ParseOutput failed: {ex.Message}"

                { Status = Error
                  Content = $"Parse error: {ex.Message}"
                  Metadata = Map.empty.Add("error_type", "parse_failure")
                  Timestamp = DateTime.Now
                  SourceAgent = "Claude Code"
                  Capabilities = [] }

        member _.SupportedCapabilities =
            [ CodeGeneration
              Testing
              Documentation
              Debugging
              Refactoring
              ArchitectureDesign
              CodeReview ]

        member _.Config = config

// ===============================================
// カスタムスクリプトCLI実装
// ===============================================

/// カスタムスクリプトCLI統合実装
type CustomScriptCLI(config: AgentIntegrationConfig) =
    interface IAgentCLI with
        member _.Name = config.Name

        member _.StartCommand(input: string) =
            let startInfo = ProcessStartInfo()

            // スクリプト種別判定とインタープリター設定
            let (interpreter, scriptArgs) =
                if config.CliPath.EndsWith(".py") then
                    ("python3", [ config.CliPath ])
                elif config.CliPath.EndsWith(".sh") then
                    ("bash", [ config.CliPath ])
                elif config.CliPath.EndsWith(".js") then
                    ("node", [ config.CliPath ])
                else
                    (config.CliPath, []) // 実行可能ファイル直接実行

            startInfo.FileName <- interpreter
            startInfo.Arguments <- String.Join(" ", scriptArgs @ config.DefaultArgs @ [ input ])
            startInfo.UseShellExecute <- false
            startInfo.RedirectStandardOutput <- true
            startInfo.RedirectStandardError <- true
            startInfo.RedirectStandardInput <- true
            startInfo.CreateNoWindow <- true

            // 環境変数設定
            for kvp in config.EnvironmentVariables do
                startInfo.Environment.[kvp.Key] <- kvp.Value

            logDebug "CustomScriptCLI" $"StartCommand: {startInfo.FileName} {startInfo.Arguments}"
            startInfo

        member _.ParseOutput(rawOutput: string) =
            try
                // JSON形式チェック
                if config.OutputFormat = "json" && rawOutput.Trim().StartsWith("{") then
                    let jsonDoc = JsonDocument.Parse(rawOutput)
                    let root = jsonDoc.RootElement

                    { Status =
                        match root.TryGetProperty("status") with
                        | (true, statusProp) ->
                            match statusProp.GetString() with
                            | "success" -> Success
                            | "error" -> Error
                            | "in_progress" -> InProgress
                            | "timeout" -> Timeout
                            | "cancelled" -> Cancelled
                            | _ -> Success
                        | _ -> Success
                      Content =
                        match root.TryGetProperty("content") with
                        | (true, contentProp) -> contentProp.GetString()
                        | _ -> rawOutput
                      Metadata = Map.empty.Add("format", "json")
                      Timestamp = DateTime.Now
                      SourceAgent = config.Name
                      Capabilities = config.SupportedCapabilities }
                else
                    // プレーンテキスト解析
                    { Status =
                        if rawOutput.Contains("error") || rawOutput.Contains("Error") then
                            Error
                        else
                            Success
                      Content = rawOutput.Trim()
                      Metadata = Map.empty.Add("format", "text")
                      Timestamp = DateTime.Now
                      SourceAgent = config.Name
                      Capabilities = config.SupportedCapabilities }
            with ex ->
                logError "CustomScriptCLI" $"ParseOutput failed: {ex.Message}"

                { Status = Error
                  Content = $"Parse error: {ex.Message}"
                  Metadata = Map.empty.Add("error_type", "parse_failure")
                  Timestamp = DateTime.Now
                  SourceAgent = config.Name
                  Capabilities = [] }

        member _.SupportedCapabilities = config.SupportedCapabilities
        member _.Config = config

// ===============================================
// Cursor AI CLI実装
// ===============================================

/// Cursor AI CLI統合実装
type CursorAICLI(config: AgentIntegrationConfig) =
    interface IAgentCLI with
        member _.Name = "Cursor AI"

        member _.StartCommand(input: string) =
            let startInfo = ProcessStartInfo()
            startInfo.FileName <- config.CliPath
            startInfo.Arguments <- String.Join(" ", config.DefaultArgs) + " " + $"\"{input}\""
            startInfo.UseShellExecute <- false
            startInfo.RedirectStandardOutput <- true
            startInfo.RedirectStandardError <- true
            startInfo.RedirectStandardInput <- true
            startInfo.CreateNoWindow <- true
            startInfo.WorkingDirectory <- Environment.CurrentDirectory

            // Cursor AI専用環境変数設定
            for kvp in config.EnvironmentVariables do
                startInfo.Environment.[kvp.Key] <- kvp.Value

            // Cursor AI最適化環境変数
            startInfo.Environment.["CURSOR_AI_CONTEXT"] <- "true"
            startInfo.Environment.["CURSOR_OUTPUT_MODE"] <- "structured"

            logDebug "CursorAICLI" $"StartCommand: {startInfo.FileName} {startInfo.Arguments}"
            startInfo

        member _.ParseOutput(rawOutput: string) =
            try
                // Cursor AI出力の解析（MarkDown形式に対応）
                let lines = rawOutput.Split('\n')

                let content =
                    String.Join(
                        "\n",
                        lines
                        |> Array.filter (fun line ->
                            not (
                                line.StartsWith("DEBUG:")
                                || line.StartsWith("INFO:")
                                || line.StartsWith("TRACE:")
                            ))
                    )

                let status =
                    if
                        content.Contains("error")
                        || content.Contains("Error")
                        || content.Contains("failed")
                    then
                        Error
                    elif
                        content.Contains("completed")
                        || content.Contains("success")
                        || content.Length > 0
                    then
                        Success
                    else
                        InProgress

                { Status = status
                  Content = content.Trim()
                  Metadata =
                    Map.empty
                        .Add("output_length", content.Length.ToString())
                        .Add("line_count", lines.Length.ToString())
                        .Add("format", "markdown")
                        .Add("agent_type", "cursor_ai")
                  Timestamp = DateTime.Now
                  SourceAgent = "Cursor AI"
                  Capabilities = [ CodeGeneration; Refactoring; CodeReview; ArchitectureDesign ] }
            with ex ->
                logError "CursorAICLI" $"ParseOutput failed: {ex.Message}"

                { Status = Error
                  Content = $"Parse error: {ex.Message}"
                  Metadata = Map.empty.Add("error_type", "parse_failure").Add("agent_type", "cursor_ai")
                  Timestamp = DateTime.Now
                  SourceAgent = "Cursor AI"
                  Capabilities = [] }

        member _.SupportedCapabilities =
            [ CodeGeneration; Refactoring; ArchitectureDesign; CodeReview; Debugging ]

        member _.Config = config

// ===============================================
// エージェントファクトリー
// ===============================================

/// エージェントCLI生成ファクトリー
type AgentFactory() =

    /// Claude Code CLI生成
    static member CreateClaudeCodeCLI(claudePath: string option) =
        let config = getConfiguration ()

        let path =
            match claudePath with
            | Some p -> p
            | None ->
                try
                    findExecutablePath (config.ClaudeCodePaths)
                with _ ->
                    logWarning "AgentFactory" "Claude Code executable not found, using 'claude' as fallback"
                    "claude"

        let agentConfig = getConfiguration ()

        let config =
            { Name = "Claude Code"
              CliPath = path
              DefaultArgs = [ "--no-color" ]
              OutputFormat = "text"
              Timeout = TimeSpan.FromMinutes(float agentConfig.DefaultTimeoutMinutes)
              MaxRetries = 3
              SupportedCapabilities =
                [ CodeGeneration
                  Testing
                  Documentation
                  Debugging
                  Refactoring
                  ArchitectureDesign
                  CodeReview ]
              EnvironmentVariables = Map.empty }

        ClaudeCodeCLI(config) :> IAgentCLI

    /// カスタムスクリプトCLI生成
    static member CreateCustomScriptCLI(name: string, scriptPath: string, capabilities: AgentCapability list) =
        let config =
            { Name = name
              CliPath = scriptPath
              DefaultArgs = []
              OutputFormat = "text"
              Timeout = TimeSpan.FromMinutes(2.0)
              MaxRetries = 2
              SupportedCapabilities = capabilities
              EnvironmentVariables = Map.empty }

        CustomScriptCLI(config) :> IAgentCLI

    /// Cursor AI CLI生成
    static member CreateCursorAICLI(cursorPath: string option) =
        let config = getConfiguration ()

        let path =
            match cursorPath with
            | Some p -> p
            | None ->
                try
                    findExecutablePath (config.CursorAIPaths)
                with _ ->
                    logWarning "AgentFactory" "Cursor AI executable not found, using 'cursor' as fallback"
                    "cursor"

        let agentConfig = getConfiguration ()

        let config =
            { Name = "Cursor AI"
              CliPath = path
              DefaultArgs = [ "--cli"; "--output-json" ]
              OutputFormat = "json"
              Timeout = TimeSpan.FromMinutes(float agentConfig.DefaultTimeoutMinutes)
              MaxRetries = 2
              SupportedCapabilities = [ CodeGeneration; Refactoring; ArchitectureDesign; CodeReview; Debugging ]
              EnvironmentVariables = Map.empty.Add("CURSOR_AI_MODE", "cli").Add("CURSOR_CONTEXT_AWARE", "true") }

        CursorAICLI(config) :> IAgentCLI

    /// GitHub Copilot CLI生成
    static member CreateGitHubCopilotCLI() =
        let agentConfig = getConfiguration ()

        let config =
            { Name = "GitHub Copilot"
              CliPath = agentConfig.GitHubCopilotCommand
              DefaultArgs = [ "copilot"; "suggest" ]
              OutputFormat = "text"
              Timeout = TimeSpan.FromMinutes(float agentConfig.DefaultTimeoutMinutes)
              MaxRetries = 3
              SupportedCapabilities = [ CodeGeneration; Debugging; Documentation ]
              EnvironmentVariables = Map.empty.Add("GH_COPILOT_CONTEXT", "true").Add("GH_COPILOT_FORMAT", "text") }

        CustomScriptCLI(config) :> IAgentCLI

// ===============================================
// ユーティリティ関数
// ===============================================

/// 能力ベースのエージェント選択
let selectAgentByCapability (agents: IAgentCLI list) (requiredCapability: AgentCapability) =
    agents
    |> List.filter (fun agent -> agent.SupportedCapabilities |> List.contains requiredCapability)
    |> List.sortBy (fun agent -> agent.SupportedCapabilities.Length) // 特化度でソート
    |> List.tryHead

/// 出力フォーマット統一
let formatAgentOutput (output: AgentOutput) =
    let capabilitiesStr =
        output.Capabilities
        |> List.map (fun cap -> cap.ToString())
        |> String.concat ", "

    let metadataStr =
        output.Metadata
        |> Map.toList
        |> List.map (fun (k, v) -> sprintf "%s=%s" k v)
        |> String.concat "; "

    let statusStr =
        match output.Status with
        | Success -> "SUCCESS"
        | Error -> "ERROR"
        | InProgress -> "IN_PROGRESS"
        | Timeout -> "TIMEOUT"
        | Cancelled -> "CANCELLED"

    sprintf
        "[%s] %s\nTime: %s\nCapabilities: %s\n\n%s\n\nMetadata: %s"
        output.SourceAgent
        statusStr
        (output.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
        capabilitiesStr
        output.Content
        metadataStr

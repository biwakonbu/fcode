module FCode.AgentConfiguration

open System
open System.IO
open System.Text.Json
open FCode.Logger

// ===============================================
// 設定ファイル管理
// ===============================================

/// エージェント設定定義
type AgentConfigurationFile =
    { ClaudeCodePaths: string list
      CursorAIPaths: string list
      GitHubCopilotCommand: string
      DefaultTimeoutMinutes: int
      MaxConcurrentProcesses: int
      ResourceLimits: ResourceLimitConfig }

/// リソース制限設定
and ResourceLimitConfig =
    { MaxMemoryMB: int64
      MaxCpuPercent: float
      MonitoringIntervalMs: int }

/// デフォルト設定
let defaultAgentConfig =
    { ClaudeCodePaths =
        [ "claude"
          "/usr/local/bin/claude"
          "/usr/bin/claude"
          "/opt/claude/bin/claude"
          "~/.local/bin/claude" ]
      CursorAIPaths =
        [ "cursor"
          "/usr/local/bin/cursor"
          "/opt/cursor/cursor"
          "/usr/bin/cursor"
          "~/.local/bin/cursor" ]
      GitHubCopilotCommand = "gh"
      DefaultTimeoutMinutes = 5
      MaxConcurrentProcesses = 5
      ResourceLimits =
        { MaxMemoryMB = 1024L
          MaxCpuPercent = 50.0
          MonitoringIntervalMs = 500 } }

/// 設定ファイルパス取得
let getConfigFilePath () =
    let homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    Path.Combine(homeDir, ".config", "fcode", "agent-config.json")

/// 設定ファイル読み込み
let loadAgentConfiguration () =
    let configPath = getConfigFilePath ()

    try
        if File.Exists(configPath) then
            let jsonContent = File.ReadAllText(configPath)
            let options = JsonSerializerOptions()
            options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
            options.WriteIndented <- true

            let config =
                JsonSerializer.Deserialize<AgentConfigurationFile>(jsonContent, options)

            logInfo "AgentConfiguration" $"Configuration loaded from: {configPath}"
            config
        else
            logInfo "AgentConfiguration" $"Configuration file not found, using defaults: {configPath}"
            defaultAgentConfig
    with ex ->
        logError "AgentConfiguration" $"Failed to load configuration: {ex.Message}"
        logInfo "AgentConfiguration" "Using default configuration"
        defaultAgentConfig

/// 設定ファイル保存
let saveAgentConfiguration (config: AgentConfigurationFile) =
    let configPath = getConfigFilePath ()

    try
        let configDir = Path.GetDirectoryName(configPath)

        if not (Directory.Exists(configDir)) then
            Directory.CreateDirectory(configDir) |> ignore

        let options = JsonSerializerOptions()
        options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        options.WriteIndented <- true

        let jsonContent = JsonSerializer.Serialize(config, options)
        File.WriteAllText(configPath, jsonContent)

        logInfo "AgentConfiguration" $"Configuration saved to: {configPath}"
        true
    with ex ->
        logError "AgentConfiguration" $"Failed to save configuration: {ex.Message}"
        false

/// パス解決（チルダ展開）
let expandPath (path: string) =
    if path.StartsWith("~") then
        let homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        path.Replace("~", homeDir)
    else
        path

/// 実行可能パス検索
let findExecutablePath (candidates: string list) =
    candidates
    |> List.map expandPath
    |> List.find (fun p ->
        try
            File.Exists(p) || (Path.GetFileName(p) = p && not (p.Contains("/")))
        with _ ->
            false)

/// グローバル設定インスタンス
let mutable private globalConfig = None

/// 設定取得
let getConfiguration () =
    match globalConfig with
    | Some config -> config
    | None ->
        let config = loadAgentConfiguration ()
        globalConfig <- Some config
        config

/// 設定更新
let updateConfiguration (newConfig: AgentConfigurationFile) =
    globalConfig <- Some newConfig
    saveAgentConfiguration (newConfig) |> ignore

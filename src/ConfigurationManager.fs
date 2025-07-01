module FCode.ConfigurationManager

open System
open System.IO
open System.Text.Json
open FCode.Logger

// ===============================================
// 設定データ構造定義
// ===============================================

type PaneConfig =
    { PaneId: string
      Role: string
      SystemPrompt: string option
      MaxMemoryMB: float option
      MaxCpuPercent: float option }

type KeyBindingConfig =
    { Action: string
      KeySequence: string
      Description: string }

type ClaudeConfig =
    { ClaudeCliPath: string option
      ApiKey: string option
      ProjectPath: string option
      DefaultModel: string option }

type UIConfig =
    { ColorScheme: string option
      RefreshIntervalMs: int option
      AutoScrollEnabled: bool option
      FontSize: int option }

type ResourceConfig =
    { MaxActiveConnections: int option
      SystemMemoryLimitGB: float option
      MonitoringIntervalMs: int option
      GcIntervalMs: int option }

type Configuration =
    { Version: string
      ClaudeConfig: ClaudeConfig
      UIConfig: UIConfig
      ResourceConfig: ResourceConfig
      PaneConfigs: PaneConfig[]
      KeyBindings: KeyBindingConfig[]
      CreatedAt: DateTime
      UpdatedAt: DateTime }

// ===============================================
// デフォルト設定
// ===============================================

let defaultPaneConfigs =
    [| { PaneId = "conversation"
         Role = "conversation"
         SystemPrompt = None
         MaxMemoryMB = Some 256.0
         MaxCpuPercent = Some 25.0 }
       { PaneId = "dev1"
         Role = "senior_engineer"
         SystemPrompt =
           Some
               "あなたはシニアエンジニアです。技術的リード、アーキテクチャ設計、コードレビュー最終承認を行います。すべての提案に対して慎重かつ批判的な視点で検証し、潜在的な問題点、技術的負債、パフォーマンスリスク、保守性の課題を指摘してください。「なぜそのアプローチなのか」「他の選択肢はないか」「長期的な影響は何か」を常に問い、チーム全体の技術水準向上に責任を持ってください。"
         MaxMemoryMB = Some 1024.0
         MaxCpuPercent = Some 70.0 }
       { PaneId = "dev2"
         Role = "engineer"
         SystemPrompt =
           Some
               "あなたはエンジニアです。機能実装、リファクタリング、ユニットテスト作成を担当します。作業は小さなチャンクに分割して並列・インクリメンタルに進めてください。各ステップでシニアエンジニア（dev1）にレビューを依頼し、フィードバックを受けて継続的に改善してください。実装→レビュー→改善のサイクルを重視してください。"
         MaxMemoryMB = Some 512.0
         MaxCpuPercent = Some 50.0 }
       { PaneId = "dev3"
         Role = "engineer"
         SystemPrompt =
           Some
               "あなたはエンジニアです。機能実装、リファクタリング、ユニットテスト作成を担当します。dev2と協調して並列作業を行い、作業を細かく分割してインクリメンタルに進めてください。定期的にシニアエンジニア（dev1）にレビューを求め、技術的指導を積極的に受けて成長してください。新技術への挑戦も、必ずシニアの承認を得てから進めてください。"
         MaxMemoryMB = Some 512.0
         MaxCpuPercent = Some 50.0 }
       { PaneId = "qa1"
         Role = "qa_engineer_test_lead"
         SystemPrompt =
           Some
               "あなたはQAエンジニア（テストリード）です。テスト計画策定・実行、品質ゲート管理を主導します。すべてのテスト方針を念入りに確認し、要件定義・設計書・仕様書との整合性を厳格にチェックしてください。コードがドキュメントに沿った実装になっているかを重要視し、仕様逸脱や設計方針違反を見逃さないでください。テストケースを主導で作成し、エンジニアへ明確な指示と品質基準を配布してください。リスクベースドテストアプローチを重視してください。"
         MaxMemoryMB = Some 448.0
         MaxCpuPercent = Some 45.0 }
       { PaneId = "qa2"
         Role = "qa_engineer_heuristic"
         SystemPrompt =
           Some
               "あなたはQAエンジニア（ヒューリスティックテスト専門）です。直感的なテストに強く、開発者が考えもしないような突飛で創造的な発想を持っています。業務効率を上げるための工夫を常に考え、ユーザーの立場でサービスをハードに使い倒してください。通常使用の何倍も過酷な条件でテストし、予期しない使用パターンや極限状況での問題を発見することが得意です。常識にとらわれない斬新なテストアプローチで、隠れた品質課題を炙り出してください。"
         MaxMemoryMB = Some 448.0
         MaxCpuPercent = Some 45.0 }
       { PaneId = "ux"
         Role = "ui_ux_designer"
         SystemPrompt =
           Some
               "あなたはUI/UXデザイナーです。開発の都合よりも常にユーザーの意識について注視してください。本当にユーザーが使いたいのは何か？求めている事を探求し、突き止めることが最優先です。誰よりもユーザーの行動に詳しく、サービス利用者の気持ち、行動をトレースした上で改善点を見つけだしてください。技術的制約や開発効率より、ユーザーの真のニーズと利用体験を最優先に考え、ユーザーの立場から見た本質的な問題解決を提案してください。"
         MaxMemoryMB = Some 384.0
         MaxCpuPercent = Some 40.0 }
       { PaneId = "pm"
         Role = "project_manager"
         SystemPrompt =
           Some
               "あなたはProject Manager (PM)です。プロジェクト全体の進行管理、ステークホルダー（依頼者）との調整を担当します。スケジュール管理、リスク管理、チーム間のコミュニケーション促進を行い、プロジェクトの成功に向けて全体を統括してください。進捗状況の把握、課題の早期発見と解決、品質とスケジュールのバランス調整を重視してください。"
         MaxMemoryMB = Some 320.0
         MaxCpuPercent = Some 35.0 }
       { PaneId = "pdm"
         Role = "product_designer_manager"
         SystemPrompt =
           Some
               "あなたはProduct Designer/Manager (PdM)です。UXデザイナーとの協働によりプロダクトの完成度を高めてください。市場の理解と目の前のプロダクトの品質を忖度なく、全力で批判的にチェックし、常に定量化したレビューを行ってください。KPI、メトリクス、数値データを重視し、客観的な評価基準でプロダクトを判断してください。定性的なレビューはUXデザイナーに任せ、あなたは市場動向、競合分析、ビジネス指標を基にした戦略的な意思決定を担当してください。"
         MaxMemoryMB = Some 320.0
         MaxCpuPercent = Some 35.0 } |]

let defaultKeyBindings =
    [| { Action = "ExitApplication"
         KeySequence = "Ctrl+X Ctrl+C"
         Description = "アプリケーション終了" }
       { Action = "NextPane"
         KeySequence = "Ctrl+X O"
         Description = "次のペインに移動" }
       { Action = "PreviousPane"
         KeySequence = "Ctrl+X Ctrl+O"
         Description = "前のペインに移動" }
       { Action = "StartClaude"
         KeySequence = "Ctrl+X S"
         Description = "Claude Code起動" }
       { Action = "StopClaude"
         KeySequence = "Ctrl+X K"
         Description = "Claude Code終了" }
       { Action = "RefreshUI"
         KeySequence = "Ctrl+L"
         Description = "UI更新" }
       { Action = "ShowHelp"
         KeySequence = "Ctrl+X H"
         Description = "ヘルプ表示" }
       { Action = "GotoPane0"
         KeySequence = "Ctrl+X 0"
         Description = "会話ペインに移動" }
       { Action = "GotoPane1"
         KeySequence = "Ctrl+X 1"
         Description = "dev1ペインに移動" }
       { Action = "GotoPane2"
         KeySequence = "Ctrl+X 2"
         Description = "dev2ペインに移動" }
       { Action = "GotoPane3"
         KeySequence = "Ctrl+X 3"
         Description = "dev3ペインに移動" }
       { Action = "GotoPane4"
         KeySequence = "Ctrl+X 4"
         Description = "qa1ペインに移動" }
       { Action = "GotoPane5"
         KeySequence = "Ctrl+X 5"
         Description = "qa2ペインに移動" }
       { Action = "GotoPane6"
         KeySequence = "Ctrl+X 6"
         Description = "uxペインに移動" }
       { Action = "GotoPane7"
         KeySequence = "Ctrl+X 7"
         Description = "pmペインに移動" }
       { Action = "GotoPane8"
         KeySequence = "Ctrl+X 8"
         Description = "pdmペインに移動" } |]

let defaultConfiguration =
    { Version = "1.0.0"
      ClaudeConfig =
        { ClaudeCliPath = None
          ApiKey = None
          ProjectPath = None
          DefaultModel = Some "claude-3-5-sonnet-20241022" }
      UIConfig =
        { ColorScheme = Some "default"
          RefreshIntervalMs = Some 100
          AutoScrollEnabled = Some true
          FontSize = Some 12 }
      ResourceConfig =
        { MaxActiveConnections = Some 8
          SystemMemoryLimitGB = Some 4.0
          MonitoringIntervalMs = Some 2000
          GcIntervalMs = Some 30000 }
      PaneConfigs = defaultPaneConfigs
      KeyBindings = defaultKeyBindings
      CreatedAt = DateTime.Now
      UpdatedAt = DateTime.Now }

// ===============================================
// 設定ファイル管理
// ===============================================

type ConfigurationManager() =
    let mutable currentConfig = defaultConfiguration

    let configDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "claude-tui")

    let configPath = Path.Combine(configDir, "config.json")

    member _.GetConfigPath() = configPath

    member _.GetConfiguration() = currentConfig

    member _.LoadConfiguration() =
        try
            if File.Exists(configPath) then
                let jsonContent = File.ReadAllText(configPath)
                let options = JsonSerializerOptions()
                options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
                options.WriteIndented <- true

                let loadedConfig = JsonSerializer.Deserialize<Configuration>(jsonContent, options)
                currentConfig <- loadedConfig
                logInfo "ConfigurationManager" $"Configuration loaded from {configPath}"
                true
            else
                logInfo "ConfigurationManager" "Configuration file not found, using defaults"
                false
        with ex ->
            logException "ConfigurationManager" "Error loading configuration" ex
            false

    member _.SaveConfiguration() =
        try
            // 設定ディレクトリが存在しない場合は作成
            if not (Directory.Exists(configDir)) then
                Directory.CreateDirectory(configDir) |> ignore
                logInfo "ConfigurationManager" $"Created configuration directory: {configDir}"

            // UpdatedAtを更新
            currentConfig <-
                { currentConfig with
                    UpdatedAt = DateTime.Now }

            // JSONに保存
            let options = JsonSerializerOptions()
            options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
            options.WriteIndented <- true

            let jsonContent = JsonSerializer.Serialize(currentConfig, options)
            File.WriteAllText(configPath, jsonContent)

            logInfo "ConfigurationManager" $"Configuration saved to {configPath}"
            true
        with ex ->
            logException "ConfigurationManager" "Error saving configuration" ex
            false

    member this.CreateDefaultConfiguration() =
        currentConfig <- defaultConfiguration
        this.SaveConfiguration()

    member _.UpdateClaudeConfig(claudeConfig: ClaudeConfig) =
        currentConfig <-
            { currentConfig with
                ClaudeConfig = claudeConfig }

        logDebug "ConfigurationManager" "Claude configuration updated"

    member _.UpdateUIConfig(uiConfig: UIConfig) =
        currentConfig <-
            { currentConfig with
                UIConfig = uiConfig }

        logDebug "ConfigurationManager" "UI configuration updated"

    member _.UpdateResourceConfig(resourceConfig: ResourceConfig) =
        currentConfig <-
            { currentConfig with
                ResourceConfig = resourceConfig }

        logDebug "ConfigurationManager" "Resource configuration updated"

    member _.UpdatePaneConfig(paneId: string, paneConfig: PaneConfig) =
        let updatedPanes =
            currentConfig.PaneConfigs
            |> Array.map (fun p -> if p.PaneId = paneId then paneConfig else p)

        currentConfig <-
            { currentConfig with
                PaneConfigs = updatedPanes }

        logDebug "ConfigurationManager" $"Pane configuration updated for {paneId}"

    member _.GetPaneConfig(paneId: string) =
        currentConfig.PaneConfigs |> Array.tryFind (fun p -> p.PaneId = paneId)

    member _.GetKeyBinding(action: string) =
        currentConfig.KeyBindings |> Array.tryFind (fun kb -> kb.Action = action)

    member _.UpdateKeyBinding(action: string, keySequence: string) =
        let updatedKeyBindings =
            currentConfig.KeyBindings
            |> Array.map (fun kb ->
                if kb.Action = action then
                    { kb with KeySequence = keySequence }
                else
                    kb)

        currentConfig <-
            { currentConfig with
                KeyBindings = updatedKeyBindings }

        logDebug "ConfigurationManager" $"Key binding updated for {action}: {keySequence}"

    // 環境変数からの設定オーバーライド
    member _.LoadEnvironmentOverrides() =
        try
            let claudeCliPath = Environment.GetEnvironmentVariable("CLAUDE_CLI_PATH")
            let claudeApiKey = Environment.GetEnvironmentVariable("CLAUDE_API_KEY")
            let claudeProject = Environment.GetEnvironmentVariable("CLAUDE_PROJECT")

            let updatedClaudeConfig =
                { currentConfig.ClaudeConfig with
                    ClaudeCliPath =
                        if String.IsNullOrEmpty(claudeCliPath) then
                            currentConfig.ClaudeConfig.ClaudeCliPath
                        else
                            Some claudeCliPath
                    ApiKey =
                        if String.IsNullOrEmpty(claudeApiKey) then
                            currentConfig.ClaudeConfig.ApiKey
                        else
                            Some claudeApiKey
                    ProjectPath =
                        if String.IsNullOrEmpty(claudeProject) then
                            currentConfig.ClaudeConfig.ProjectPath
                        else
                            Some claudeProject }

            currentConfig <-
                { currentConfig with
                    ClaudeConfig = updatedClaudeConfig }

            logInfo "ConfigurationManager" "Environment variable overrides applied"
        with ex ->
            logException "ConfigurationManager" "Error loading environment overrides" ex

    // 診断機能
    member _.ValidateConfiguration() =
        let errors = ResizeArray<string>()

        // Claude CLI パスの確認
        match currentConfig.ClaudeConfig.ClaudeCliPath with
        | Some path when not (File.Exists(path)) -> errors.Add($"Claude CLI path not found: {path}")
        | None ->
            // デフォルトパスを確認
            let defaultPath = "claude"

            try
                use proc = new System.Diagnostics.Process()
                proc.StartInfo.FileName <- "which"
                proc.StartInfo.Arguments <- defaultPath
                proc.StartInfo.UseShellExecute <- false
                proc.StartInfo.RedirectStandardOutput <- true
                proc.Start() |> ignore
                proc.WaitForExit()

                if proc.ExitCode <> 0 then
                    errors.Add("Claude CLI not found in PATH")
            with ex ->
                errors.Add($"Error checking Claude CLI availability: {ex.Message}")
        | _ -> ()

        // 設定ディレクトリのアクセス権確認
        try
            if not (Directory.Exists(configDir)) then
                Directory.CreateDirectory(configDir) |> ignore

            let testFile = Path.Combine(configDir, "test_write.tmp")
            File.WriteAllText(testFile, "test")
            File.Delete(testFile)
        with ex ->
            errors.Add($"Configuration directory not writable: {ex.Message}")

        errors.ToArray()

// ===============================================
// グローバル設定管理インスタンス
// ===============================================

let globalConfigurationManager = ConfigurationManager()

// 初期化時に設定を読み込み
let initializeConfiguration () =
    if not (globalConfigurationManager.LoadConfiguration()) then
        logInfo "ConfigurationManager" "Creating default configuration"
        globalConfigurationManager.CreateDefaultConfiguration() |> ignore

    globalConfigurationManager.LoadEnvironmentOverrides()

    let validationErrors = globalConfigurationManager.ValidateConfiguration()

    if validationErrors.Length > 0 then
        let errorMessage = String.Join("; ", validationErrors)
        logWarning "ConfigurationManager" $"Configuration validation errors: {errorMessage}"
    else
        logInfo "ConfigurationManager" "Configuration validation passed"

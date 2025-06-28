namespace FCode

open System
open System.Collections.Generic
open System.Text.Json

/// プロセス間環境変数独立化モジュール
module EnvironmentIsolation =

    // 型定義
    type IsolatedEnvironment =
        { PaneId: string
          ClaudeRole: string // dev/qa/ux/pm
          WorkingDirectory: string
          SessionId: string
          CustomVars: Map<string, string>
          InheritedVars: Set<string> // 継承する環境変数リスト
          CreatedAt: DateTime
          LastUpdated: DateTime }

    type EnvironmentConfig =
        { IsolatedVars: Set<string> // 分離する環境変数
          SharedVars: Set<string> // 共有する環境変数
          DefaultValues: Map<string, string>
          RoleSpecificVars: Map<string, Map<string, string>> } // ロール別デフォルト値

    type EnvironmentUpdateResult =
        | UpdateSuccess
        | UpdateError of Reason: string
        | VariableConflict of ConflictingVar: string

    // デフォルト設定
    let defaultConfig =
        { IsolatedVars =
            Set.ofList
                [ "CLAUDE_ROLE"
                  "WORKING_DIR"
                  "SESSION_ID"
                  "PANE_ID"
                  "TEMP_DIR"
                  "OUTPUT_DIR"
                  "CLAUDE_CONTEXT"
                  "WORKSPACE_ROOT" ]
          SharedVars =
            Set.ofList
                [ "PATH"
                  "HOME"
                  "USER"
                  "SHELL"
                  "TERM"
                  "LANG"
                  "LC_ALL"
                  "CLAUDE_API_KEY"
                  "ANTHROPIC_API_KEY" ]
          DefaultValues = Map.ofList [ ("CLAUDE_CONTEXT", "fcode-tui"); ("WORKSPACE_ROOT", "/tmp/fcode-workspace") ]
          RoleSpecificVars =
            Map.ofList
                [ ("dev",
                   Map.ofList
                       [ ("CLAUDE_ROLE", "developer")
                         ("CLAUDE_PERSONA", "熟練のソフトウェアエンジニア")
                         ("CLAUDE_FOCUS", "コード品質、パフォーマンス、保守性") ])
                  ("qa",
                   Map.ofList
                       [ ("CLAUDE_ROLE", "qa_engineer")
                         ("CLAUDE_PERSONA", "品質保証の専門家")
                         ("CLAUDE_FOCUS", "テスト戦略、バグ検出、品質向上") ])
                  ("ux",
                   Map.ofList
                       [ ("CLAUDE_ROLE", "ux_designer")
                         ("CLAUDE_PERSONA", "UX/UIデザインの専門家")
                         ("CLAUDE_FOCUS", "ユーザビリティ、アクセシビリティ、使いやすさ") ])
                  ("pm",
                   Map.ofList
                       [ ("CLAUDE_ROLE", "project_manager")
                         ("CLAUDE_PERSONA", "プロジェクトマネージャー")
                         ("CLAUDE_FOCUS", "進捗管理、リスク管理、品質管理") ]) ] }

    // ロール検証
    let private validateRole (role: string) =
        let validRoles = Set.ofList [ "dev"; "qa"; "ux"; "pm" ]

        if validRoles.Contains(role) then
            Ok role
        else
            let validRolesList = String.Join(", ", validRoles)
            Error $"無効なロール: {role}. 有効なロール: {validRolesList}"

    // セッションID生成
    let private generateSessionId (paneId: string) =
        let timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss")
        let random = Random().Next(1000, 9999)
        $"fcode-{paneId}-{timestamp}-{random}"

    // 環境変数の安全な取得
    let private safeGetEnvironmentVariable (varName: string) =
        try
            match Environment.GetEnvironmentVariable(varName) with
            | null -> None
            | value -> Some value
        with ex ->
            Logger.logWarning "EnvironmentIsolation" $"環境変数取得エラー: {varName} - {ex.Message}"
            None

    // 継承すべき環境変数の収集
    let private collectInheritedVariables (config: EnvironmentConfig) =
        let inheritedVars = Dictionary<string, string>()

        for varName in config.SharedVars do
            match safeGetEnvironmentVariable varName with
            | Some value ->
                inheritedVars.[varName] <- value
                Logger.logDebug "EnvironmentIsolation" $"環境変数継承: {varName}={value}"
            | None -> Logger.logDebug "EnvironmentIsolation" $"環境変数未設定: {varName}"

        inheritedVars

    // ロール別環境変数の設定
    let private applyRoleSpecificVariables (config: EnvironmentConfig) (role: string) =
        match config.RoleSpecificVars.TryFind(role) with
        | Some roleVars ->
            Logger.logInfo "EnvironmentIsolation" $"ロール別環境変数適用: {role} - {roleVars.Count}個"
            roleVars
        | None ->
            Logger.logWarning "EnvironmentIsolation" $"ロール別設定未定義: {role}"
            Map.empty

    // 分離環境の作成
    let createIsolatedEnvironment (config: EnvironmentConfig) (paneId: string) (role: string) (workingDir: string) =
        try
            match validateRole role with
            | Error e -> Error e
            | Ok validRole ->
                let sessionId = generateSessionId paneId
                let inheritedVars = collectInheritedVariables config
                let roleVars = applyRoleSpecificVariables config validRole

                // カスタム環境変数の構築
                let mutable customVars = Map.empty

                // デフォルト値を追加
                for kvp in config.DefaultValues do
                    customVars <- customVars.Add(kvp.Key, kvp.Value)

                // ロール別変数を追加
                for kvp in roleVars do
                    customVars <- customVars.Add(kvp.Key, kvp.Value)

                // 基本変数を追加
                customVars <-
                    customVars
                        .Add("PANE_ID", paneId)
                        .Add("SESSION_ID", sessionId)
                        .Add("WORKING_DIR", workingDir)
                        .Add("CLAUDE_ROLE", validRole)

                let environment =
                    { PaneId = paneId
                      ClaudeRole = validRole
                      WorkingDirectory = workingDir
                      SessionId = sessionId
                      CustomVars = customVars
                      InheritedVars = config.SharedVars
                      CreatedAt = DateTime.UtcNow
                      LastUpdated = DateTime.UtcNow }

                Logger.logInfo "EnvironmentIsolation" $"分離環境作成完了: {paneId} ({validRole}) - {customVars.Count}個のカスタム変数"
                Ok environment
        with ex ->
            Logger.logException "EnvironmentIsolation" $"分離環境作成エラー: {paneId}" ex
            Error $"環境作成エラー: {ex.Message}"

    // 環境変数辞書の構築
    let buildEnvironmentDictionary (environment: IsolatedEnvironment) (config: EnvironmentConfig) =
        try
            let envDict = Dictionary<string, string>()

            // 継承変数を追加
            let inheritedVars = collectInheritedVariables config

            for kvp in inheritedVars do
                envDict.[kvp.Key] <- kvp.Value

            // カスタム変数を追加（継承変数より優先）
            for kvp in environment.CustomVars do
                envDict.[kvp.Key] <- kvp.Value

            Logger.logDebug "EnvironmentIsolation" $"環境辞書構築完了: {environment.PaneId} - {envDict.Count}個の変数"
            Ok envDict
        with ex ->
            Logger.logException "EnvironmentIsolation" $"環境辞書構築エラー: {environment.PaneId}" ex
            Error $"辞書構築エラー: {ex.Message}"

    // 環境変数の動的更新
    let updateEnvironmentVariable
        (environment: IsolatedEnvironment)
        (varName: string)
        (value: string)
        (config: EnvironmentConfig)
        =
        try
            // 分離対象変数かチェック
            if config.IsolatedVars.Contains(varName) then
                let updatedCustomVars = environment.CustomVars.Add(varName, value)

                let updatedEnvironment =
                    { environment with
                        CustomVars = updatedCustomVars
                        LastUpdated = DateTime.UtcNow }

                Logger.logInfo "EnvironmentIsolation" $"環境変数更新: {environment.PaneId}.{varName}={value}"
                Ok(updatedEnvironment, UpdateSuccess)
            elif config.SharedVars.Contains(varName) then
                Logger.logWarning "EnvironmentIsolation" $"共有変数への更新試行: {varName} (ペイン: {environment.PaneId})"
                Ok(environment, VariableConflict varName)
            else
                // 新しい分離変数として追加
                let updatedCustomVars = environment.CustomVars.Add(varName, value)

                let updatedEnvironment =
                    { environment with
                        CustomVars = updatedCustomVars
                        LastUpdated = DateTime.UtcNow }

                Logger.logInfo "EnvironmentIsolation" $"新規分離変数追加: {environment.PaneId}.{varName}={value}"
                Ok(updatedEnvironment, UpdateSuccess)
        with ex ->
            Logger.logException "EnvironmentIsolation" $"環境変数更新エラー: {environment.PaneId}.{varName}" ex
            Error $"更新エラー: {ex.Message}"

    // 環境変数の削除
    let removeEnvironmentVariable (environment: IsolatedEnvironment) (varName: string) (config: EnvironmentConfig) =
        try
            if environment.CustomVars.ContainsKey(varName) then
                let updatedCustomVars = environment.CustomVars.Remove(varName)

                let updatedEnvironment =
                    { environment with
                        CustomVars = updatedCustomVars
                        LastUpdated = DateTime.UtcNow }

                Logger.logInfo "EnvironmentIsolation" $"環境変数削除: {environment.PaneId}.{varName}"
                Ok updatedEnvironment
            else
                Logger.logWarning "EnvironmentIsolation" $"削除対象変数未存在: {environment.PaneId}.{varName}"
                Ok environment
        with ex ->
            Logger.logException "EnvironmentIsolation" $"環境変数削除エラー: {environment.PaneId}.{varName}" ex
            Error $"削除エラー: {ex.Message}"

    // 環境の状態保存
    let saveEnvironmentState (environment: IsolatedEnvironment) (baseDir: string) =
        try
            let stateFile = System.IO.Path.Combine(baseDir, "environment.json")

            let json =
                JsonSerializer.Serialize(environment, JsonSerializerOptions(WriteIndented = true))

            System.IO.File.WriteAllText(stateFile, json)

            Logger.logDebug "EnvironmentIsolation" $"環境状態保存完了: {environment.PaneId}"
            Ok()
        with ex ->
            Logger.logException "EnvironmentIsolation" $"環境状態保存エラー: {environment.PaneId}" ex
            Error $"保存エラー: {ex.Message}"

    // 環境の状態復元
    let loadEnvironmentState (paneId: string) (baseDir: string) =
        try
            let stateFile = System.IO.Path.Combine(baseDir, "environment.json")

            if System.IO.File.Exists(stateFile) then
                let json = System.IO.File.ReadAllText(stateFile)
                let environment = JsonSerializer.Deserialize<IsolatedEnvironment>(json)
                Logger.logDebug "EnvironmentIsolation" $"環境状態復元完了: {paneId}"
                Ok(Some environment)
            else
                Logger.logDebug "EnvironmentIsolation" $"環境状態ファイル未存在: {paneId}"
                Ok None
        with ex ->
            Logger.logException "EnvironmentIsolation" $"環境状態復元エラー: {paneId}" ex
            Error $"復元エラー: {ex.Message}"

    // 環境の比較・差分検出
    let compareEnvironments (env1: IsolatedEnvironment) (env2: IsolatedEnvironment) =
        try
            let added = ResizeArray<string * string>()
            let modified = ResizeArray<string * string * string>()
            let removed = ResizeArray<string>()

            // env2で追加された変数
            for kvp in env2.CustomVars do
                if not (env1.CustomVars.ContainsKey(kvp.Key)) then
                    added.Add((kvp.Key, kvp.Value))

            // 変更された変数
            for kvp in env1.CustomVars do
                match env2.CustomVars.TryFind(kvp.Key) with
                | Some newValue when newValue <> kvp.Value -> modified.Add((kvp.Key, kvp.Value, newValue))
                | None -> removed.Add(kvp.Key)
                | _ -> ()

            let comparison =
                {| Added = added.ToArray()
                   Modified = modified.ToArray()
                   Removed = removed.ToArray()
                   HasChanges = added.Count > 0 || modified.Count > 0 || removed.Count > 0 |}

            Logger.logDebug
                "EnvironmentIsolation"
                $"環境比較完了: {env1.PaneId} vs {env2.PaneId} - 変更: {comparison.HasChanges}"

            Ok comparison
        with ex ->
            Logger.logException "EnvironmentIsolation" $"環境比較エラー: {env1.PaneId} vs {env2.PaneId}" ex
            Error $"比較エラー: {ex.Message}"

    // 環境の妥当性検証
    let validateEnvironment (environment: IsolatedEnvironment) (config: EnvironmentConfig) =
        try
            let errors = ResizeArray<string>()

            // 必須変数のチェック
            let requiredVars = [ "PANE_ID"; "SESSION_ID"; "WORKING_DIR"; "CLAUDE_ROLE" ]

            for varName in requiredVars do
                if not (environment.CustomVars.ContainsKey(varName)) then
                    errors.Add($"必須変数未設定: {varName}")

            // ロール妥当性チェック
            match validateRole environment.ClaudeRole with
            | Error e -> errors.Add(e)
            | Ok _ -> ()

            // 作業ディレクトリ存在チェック
            if not (System.IO.Directory.Exists(environment.WorkingDirectory)) then
                errors.Add($"作業ディレクトリ未存在: {environment.WorkingDirectory}")

            if errors.Count = 0 then
                Logger.logDebug "EnvironmentIsolation" $"環境妥当性検証成功: {environment.PaneId}"
                Ok()
            else
                let errorMessage = String.Join("; ", errors)
                Logger.logError "EnvironmentIsolation" $"環境妥当性検証失敗: {environment.PaneId} - {errorMessage}"
                Error errorMessage

        with ex ->
            Logger.logException "EnvironmentIsolation" $"環境妥当性検証例外: {environment.PaneId}" ex
            Error $"検証例外: {ex.Message}"

# 設計書 - 設定管理システム

## 概要

設定管理システムは、fcodeのマルチエージェント環境において、システム全体の設定を一元管理するシステムです。階層的設定管理、動的設定変更、環境別設定、設定検証、テンプレート管理、変更履歴、バックアップ・復旧機能を提供し、一貫性のある設定管理と効率的な運用を実現します。

## アーキテクチャ

### システムアーキテクチャ

```
┌─────────────────────────────────────────────────────────────┐
│                    設定管理システム                          │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ │
│ │ 設定            │ │ 階層            │ │ 動的変更        │ │
│ │ マネージャー    │ │ 管理器          │ │ 管理器          │ │
│ └─────────────────┘ └─────────────────┘ └─────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ │
│ │ 環境別          │ │ 検証            │ │ テンプレート    │ │
│ │ 管理器          │ │ エンジン        │ │ 管理器          │ │
│ └─────────────────┘ └─────────────────┘ └─────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ │
│ │ 履歴            │ │ バックアップ    │ │ 通知            │ │
│ │ 管理器          │ │ 管理器          │ │ システム        │ │
│ └─────────────────┘ └─────────────────┘ └─────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### データフローアーキテクチャ

```
[設定要求] → [階層解決] → [検証] → [適用] → [通知]
     ↓           ↓         ↓       ↓       ↓
[履歴記録] → [バックアップ] → [監査ログ] → [変更追跡]
```

### 階層構造

```
システム設定 (最高優先度)
    ↓
プロジェクト設定
    ↓
エージェント設定
    ↓
ユーザー設定 (最低優先度)
```

## コンポーネントとインターフェース

### ConfigurationManager

メインの設定管理コンポーネント

```fsharp
type ConfigurationManager() =
    
    // 基本設定操作
    member _.GetConfiguration<'T>(key: string) : Result<'T option, ConfigError>
    member _.SetConfiguration<'T>(key: string, value: 'T) : Result<unit, ConfigError>
    member _.RemoveConfiguration(key: string) : Result<unit, ConfigError>
    member _.GetAllConfigurations() : Result<Map<string, obj>, ConfigError>
    
    // 階層設定操作
    member _.GetConfigurationWithHierarchy<'T>(key: string, scope: ConfigScope) : Result<'T option, ConfigError>
    member _.SetConfigurationInScope<'T>(key: string, value: 'T, scope: ConfigScope) : Result<unit, ConfigError>
    member _.GetEffectiveConfiguration<'T>(key: string) : Result<'T option, ConfigError>
    
    // 動的設定変更
    member _.UpdateConfigurationDynamically<'T>(key: string, value: 'T) : Result<unit, ConfigError>
    member _.ApplyConfigurationChanges(changes: ConfigChange list) : Result<unit, ConfigError>
    member _.RollbackConfiguration(changeId: string) : Result<unit, ConfigError>
    
    // 設定監視・通知
    member _.SubscribeToChanges(key: string, callback: ConfigChangeCallback) : Result<string, ConfigError>
    member _.UnsubscribeFromChanges(subscriptionId: string) : Result<unit, ConfigError>
```

### HierarchyManager

階層的設定管理コンポーネント

```fsharp
type HierarchyManager() =
    
    // 階層管理
    member _.ResolveConfiguration<'T>(key: string, scopes: ConfigScope list) : Result<'T option, ConfigError>
    member _.GetConfigurationHierarchy(key: string) : Result<ConfigHierarchy, ConfigError>
    member _.ValidateHierarchy(hierarchy: ConfigHierarchy) : Result<unit, ConfigError>
    
    // 継承管理
    member _.InheritConfiguration(parentScope: ConfigScope, childScope: ConfigScope) : Result<unit, ConfigError>
    member _.OverrideConfiguration<'T>(key: string, value: 'T, scope: ConfigScope) : Result<unit, ConfigError>
    member _.GetInheritanceChain(key: string) : Result<ConfigScope list, ConfigError>
    
    // 優先度管理
    member _.SetScopePriority(scope: ConfigScope, priority: int) : Result<unit, ConfigError>
    member _.GetScopePriority(scope: ConfigScope) : Result<int, ConfigError>
    member _.ResolvePriorityConflicts(conflicts: ConfigConflict list) : Result<unit, ConfigError>
```

### DynamicChangeManager

動的設定変更管理コンポーネント

```fsharp
type DynamicChangeManager() =
    
    // 動的変更
    member _.ApplyDynamicChange<'T>(key: string, value: 'T) : Result<ChangeResult, ConfigError>
    member _.ValidateChange<'T>(key: string, value: 'T) : Result<ValidationResult, ConfigError>
    member _.PreviewChange<'T>(key: string, value: 'T) : Result<ChangePreview, ConfigError>
    
    // 変更管理
    member _.CreateChangeSet(changes: ConfigChange list) : Result<ChangeSet, ConfigError>
    member _.ApplyChangeSet(changeSet: ChangeSet) : Result<ChangeResult, ConfigError>
    member _.RollbackChangeSet(changeSetId: string) : Result<unit, ConfigError>
    
    // 影響分析
    member _.AnalyzeImpact(change: ConfigChange) : Result<ImpactAnalysis, ConfigError>
    member _.GetAffectedComponents(key: string) : Result<string list, ConfigError>
    member _.NotifyAffectedComponents(change: ConfigChange) : Result<unit, ConfigError>
```

### EnvironmentManager

環境別設定管理コンポーネント

```fsharp
type EnvironmentManager() =
    
    // 環境管理
    member _.GetCurrentEnvironment() : Result<Environment, ConfigError>
    member _.SetCurrentEnvironment(environment: Environment) : Result<unit, ConfigError>
    member _.GetEnvironmentConfiguration(environment: Environment) : Result<Map<string, obj>, ConfigError>
    
    // 環境切り替え
    member _.SwitchEnvironment(targetEnvironment: Environment) : Result<unit, ConfigError>
    member _.ValidateEnvironmentSwitch(targetEnvironment: Environment) : Result<ValidationResult, ConfigError>
    member _.GetEnvironmentDifferences(env1: Environment, env2: Environment) : Result<ConfigDifference list, ConfigError>
    
    // 環境同期
    member _.SynchronizeEnvironments(sourceEnv: Environment, targetEnv: Environment) : Result<unit, ConfigError>
    member _.CreateEnvironmentFromTemplate(environment: Environment, template: ConfigTemplate) : Result<unit, ConfigError>
    member _.BackupEnvironmentConfiguration(environment: Environment) : Result<string, ConfigError>
```

### ValidationEngine

設定検証エンジン

```fsharp
type ValidationEngine() =
    
    // 基本検証
    member _.ValidateConfiguration<'T>(key: string, value: 'T) : Result<ValidationResult, ConfigError>
    member _.ValidateConfigurationSet(configurations: Map<string, obj>) : Result<ValidationResult, ConfigError>
    member _.ValidateSchema(configuration: obj, schema: ConfigSchema) : Result<ValidationResult, ConfigError>
    
    // 整合性検証
    member _.ValidateConsistency(configurations: Map<string, obj>) : Result<ConsistencyResult, ConfigError>
    member _.ValidateDependencies(key: string, value: obj) : Result<DependencyValidation, ConfigError>
    member _.ValidateConstraints(key: string, value: obj) : Result<ConstraintValidation, ConfigError>
    
    // カスタム検証
    member _.RegisterValidator(key: string, validator: ConfigValidator) : Result<unit, ConfigError>
    member _.UnregisterValidator(key: string) : Result<unit, ConfigError>
    member _.GetValidationRules(key: string) : Result<ValidationRule list, ConfigError>
```

### TemplateManager

テンプレート管理コンポーネント

```fsharp
type TemplateManager() =
    
    // テンプレート管理
    member _.CreateTemplate(name: string, configuration: Map<string, obj>) : Result<ConfigTemplate, ConfigError>
    member _.GetTemplate(name: string) : Result<ConfigTemplate option, ConfigError>
    member _.UpdateTemplate(name: string, template: ConfigTemplate) : Result<unit, ConfigError>
    member _.DeleteTemplate(name: string) : Result<unit, ConfigError>
    
    // テンプレート適用
    member _.ApplyTemplate(templateName: string, targetScope: ConfigScope) : Result<unit, ConfigError>
    member _.PreviewTemplateApplication(templateName: string, targetScope: ConfigScope) : Result<TemplatePreview, ConfigError>
    member _.CustomizeTemplate(templateName: string, customizations: Map<string, obj>) : Result<ConfigTemplate, ConfigError>
    
    // プリセット管理
    member _.CreatePreset(name: string, configurations: Map<string, obj>) : Result<ConfigPreset, ConfigError>
    member _.ApplyPreset(presetName: string) : Result<unit, ConfigError>
    member _.GetAvailablePresets() : Result<ConfigPreset list, ConfigError>
```

## データモデル

### 基本設定型

```fsharp
type ConfigScope =
    | System
    | Project of projectId: string
    | Agent of agentId: string
    | User of userId: string

type Environment =
    | Development
    | Testing
    | Staging
    | Production
    | Custom of name: string

type ConfigChange = {
    ChangeId: string
    Key: string
    OldValue: obj option
    NewValue: obj
    Scope: ConfigScope
    Environment: Environment
    Timestamp: DateTime
    UserId: string
    Reason: string option
}

type ConfigHierarchy = {
    Key: string
    Values: Map<ConfigScope, obj>
    EffectiveValue: obj
    ResolutionOrder: ConfigScope list
}
```

### 検証・制約型

```fsharp
type ValidationResult = {
    IsValid: bool
    Errors: ValidationError list
    Warnings: ValidationWarning list
    Suggestions: string list
}

type ValidationError = {
    ErrorType: ValidationErrorType
    Message: string
    Key: string
    Value: obj option
    Severity: ErrorSeverity
}

type ValidationErrorType =
    | TypeMismatch
    | ValueOutOfRange
    | RequiredFieldMissing
    | InvalidFormat
    | DependencyViolation
    | ConstraintViolation
    | SchemaViolation

type ConfigConstraint = {
    ConstraintType: ConstraintType
    Parameters: Map<string, obj>
    ErrorMessage: string
}

type ConstraintType =
    | Range of min: obj * max: obj
    | Pattern of regex: string
    | Enum of values: obj list
    | Dependency of dependentKey: string
    | Custom of validator: obj -> bool
```

### テンプレート・プリセット型

```fsharp
type ConfigTemplate = {
    Name: string
    Description: string
    Version: string
    Author: string
    CreatedAt: DateTime
    UpdatedAt: DateTime
    Configuration: Map<string, obj>
    Variables: TemplateVariable list
    Dependencies: string list
}

type TemplateVariable = {
    Name: string
    Type: Type
    DefaultValue: obj option
    Description: string
    Required: bool
    Constraints: ConfigConstraint list
}

type ConfigPreset = {
    Name: string
    Description: string
    Category: string
    Configuration: Map<string, obj>
    ApplicableScopes: ConfigScope list
    ApplicableEnvironments: Environment list
}

type TemplatePreview = {
    Template: ConfigTemplate
    TargetScope: ConfigScope
    GeneratedConfiguration: Map<string, obj>
    Conflicts: ConfigConflict list
    RequiredVariables: TemplateVariable list
}
```

### 変更管理型

```fsharp
type ChangeSet = {
    ChangeSetId: string
    Name: string
    Description: string
    Changes: ConfigChange list
    CreatedAt: DateTime
    CreatedBy: string
    Status: ChangeSetStatus
    AppliedAt: DateTime option
}

type ChangeSetStatus =
    | Draft
    | PendingApproval
    | Approved
    | Applied
    | Failed
    | RolledBack

type ChangeResult = {
    ChangeId: string
    Success: bool
    AppliedChanges: ConfigChange list
    FailedChanges: (ConfigChange * string) list
    AffectedComponents: string list
    RollbackInfo: RollbackInfo option
}

type RollbackInfo = {
    RollbackId: string
    OriginalValues: Map<string, obj>
    RollbackProcedure: RollbackStep list
}

type RollbackStep = {
    StepId: string
    Action: RollbackAction
    Parameters: Map<string, obj>
    Order: int
}

type RollbackAction =
    | RestoreValue of key: string * value: obj
    | RemoveKey of key: string
    | NotifyComponent of componentId: string
    | ExecuteCustomAction of actionId: string
```

### 履歴・監査型

```fsharp
type ConfigHistory = {
    Key: string
    Changes: ConfigChange list
    FirstChange: DateTime
    LastChange: DateTime
    ChangeCount: int
}

type AuditEntry = {
    EntryId: string
    Timestamp: DateTime
    UserId: string
    Action: AuditAction
    Target: string
    OldValue: obj option
    NewValue: obj option
    Result: AuditResult
    Context: Map<string, string>
}

type AuditAction =
    | Read
    | Create
    | Update
    | Delete
    | Apply
    | Rollback
    | Validate
    | Backup
    | Restore

type AuditResult =
    | Success
    | Failure of reason: string
    | PartialSuccess of details: string
```

## コアアルゴリズム

### 階層設定解決アルゴリズム

```fsharp
let resolveHierarchicalConfiguration<'T> (key: string) (scopes: ConfigScope list) : Result<'T option, ConfigError> =
    // 1. スコープを優先度順にソート
    let sortedScopes = 
        scopes 
        |> List.sortBy getScopePriority
        |> List.rev // 高優先度から低優先度へ
    
    // 2. 各スコープで設定を検索
    let rec findConfiguration remainingScopes =
        match remainingScopes with
        | [] -> Ok None
        | scope :: rest ->
            match getConfigurationFromScope<'T> key scope with
            | Ok (Some value) -> Ok (Some value)
            | Ok None -> findConfiguration rest
            | Error err -> Error err
    
    // 3. 継承関係の処理
    let processInheritance value scope =
        match getInheritanceRules scope with
        | Ok rules -> applyInheritanceRules value rules
        | Error _ -> Ok value
    
    // 4. 最終的な設定値の決定
    match findConfiguration sortedScopes with
    | Ok (Some value) -> 
        let effectiveScope = findEffectiveScope key sortedScopes
        processInheritance value effectiveScope
    | Ok None -> Ok None
    | Error err -> Error err

let getScopePriority = function
    | System -> 1000
    | Project _ -> 800
    | Agent _ -> 600
    | User _ -> 400
```

### 動的設定変更アルゴリズム

```fsharp
let applyDynamicConfigurationChange<'T> (key: string) (newValue: 'T) : Result<ChangeResult, ConfigError> =
    // 1. 変更前の検証
    let validateChange() = async {
        let! validationResult = validateConfigurationValue key newValue
        let! impactAnalysis = analyzeChangeImpact key newValue
        let! dependencyCheck = validateDependencies key newValue
        
        return {
            ValidationResult = validationResult
            ImpactAnalysis = impactAnalysis
            DependencyCheck = dependencyCheck
        }
    }
    
    // 2. 変更の適用
    let applyChange() = async {
        let! oldValue = getCurrentConfigurationValue key
        let! backupResult = createConfigurationBackup key oldValue
        
        try
            let! setResult = setConfigurationValue key newValue
            let! notificationResult = notifyAffectedComponents key newValue
            
            return {
                Success = true
                OldValue = oldValue
                NewValue = newValue
                BackupId = backupResult.BackupId
                AffectedComponents = notificationResult.NotifiedComponents
            }
        with
        | ex ->
            // ロールバック処理
            let! rollbackResult = rollbackConfiguration key oldValue
            return {
                Success = false
                Error = ex.Message
                RollbackResult = rollbackResult
            }
    }
    
    // 3. 変更後の検証
    let verifyChange changeResult = async {
        if changeResult.Success then
            let! verificationResult = verifyConfigurationIntegrity()
            let! consistencyCheck = validateSystemConsistency()
            
            return {
                ChangeResult = changeResult
                VerificationResult = verificationResult
                ConsistencyCheck = consistencyCheck
            }
        else
            return {
                ChangeResult = changeResult
                VerificationResult = None
                ConsistencyCheck = None
            }
    }
    
    // 4. 全体の実行
    async {
        let! validation = validateChange()
        if validation.ValidationResult.IsValid then
            let! changeResult = applyChange()
            let! finalResult = verifyChange changeResult
            return Ok finalResult
        else
            return Error (ValidationError validation.ValidationResult)
    } |> Async.RunSynchronously
```

### 設定検証アルゴリズム

```fsharp
let validateConfigurationComprehensively (configurations: Map<string, obj>) : Result<ValidationResult, ConfigError> =
    // 1. 個別設定の検証
    let validateIndividualConfigurations() =
        configurations
        |> Map.toList
        |> List.map (fun (key, value) -> async {
            let! typeValidation = validateConfigurationType key value
            let! valueValidation = validateConfigurationValue key value
            let! constraintValidation = validateConfigurationConstraints key value
            
            return {
                Key = key
                TypeValidation = typeValidation
                ValueValidation = valueValidation
                ConstraintValidation = constraintValidation
            }
        })
        |> Async.Parallel
    
    // 2. 設定間の整合性検証
    let validateConsistency() = async {
        let! dependencyValidation = validateConfigurationDependencies configurations
        let! crossReferenceValidation = validateCrossReferences configurations
        let! businessRuleValidation = validateBusinessRules configurations
        
        return {
            DependencyValidation = dependencyValidation
            CrossReferenceValidation = crossReferenceValidation
            BusinessRuleValidation = businessRuleValidation
        }
    }
    
    // 3. スキーマ検証
    let validateSchema() = async {
        let! schemaValidation = validateAgainstSchema configurations
        let! structureValidation = validateConfigurationStructure configurations
        
        return {
            SchemaValidation = schemaValidation
            StructureValidation = structureValidation
        }
    }
    
    // 4. 結果の統合
    async {
        let! individualResults = validateIndividualConfigurations()
        let! consistencyResult = validateConsistency()
        let! schemaResult = validateSchema()
        
        let allErrors = 
            individualResults 
            |> Array.collect (fun r -> [r.TypeValidation.Errors; r.ValueValidation.Errors; r.ConstraintValidation.Errors])
            |> Array.concat
            |> Array.append consistencyResult.DependencyValidation.Errors
            |> Array.append schemaResult.SchemaValidation.Errors
        
        let allWarnings = 
            individualResults 
            |> Array.collect (fun r -> [r.TypeValidation.Warnings; r.ValueValidation.Warnings])
            |> Array.concat
        
        return Ok {
            IsValid = Array.isEmpty allErrors
            Errors = allErrors |> Array.toList
            Warnings = allWarnings |> Array.toList
            IndividualResults = individualResults |> Array.toList
            ConsistencyResult = consistencyResult
            SchemaResult = schemaResult
        }
    } |> Async.RunSynchronously
```

## エラーハンドリング

### 設定管理エラータイプ

```fsharp
type ConfigError =
    | ConfigurationNotFound of key: string
    | InvalidConfigurationType of key: string * expectedType: Type * actualType: Type
    | ValidationError of validationResult: ValidationResult
    | HierarchyError of scope: ConfigScope * error: string
    | EnvironmentError of environment: Environment * error: string
    | TemplateError of templateName: string * error: string
    | ChangeApplicationError of changeId: string * error: string
    | BackupError of operation: string * error: string
    | PersistenceError of operation: string * error: string

let handleConfigurationError error =
    match error with
    | ConfigurationNotFound key ->
        // 設定未発見エラーの処理
        logConfigurationNotFound key
        suggestSimilarConfigurations key
        provideDefaultConfiguration key
    | InvalidConfigurationType (key, expected, actual) ->
        // 型不一致エラーの処理
        logTypeError key expected actual
        attemptTypeConversion key actual expected
        suggestCorrectType key expected
    | ValidationError validationResult ->
        // 検証エラーの処理
        logValidationErrors validationResult.Errors
        provideValidationSuggestions validationResult.Suggestions
        offerAutoFix validationResult
    | HierarchyError (scope, error) ->
        // 階層エラーの処理
        logHierarchyError scope error
        analyzeHierarchyStructure scope
        suggestHierarchyFix scope
    | EnvironmentError (environment, error) ->
        // 環境エラーの処理
        logEnvironmentError environment error
        validateEnvironmentConfiguration environment
        suggestEnvironmentFix environment
    | TemplateError (templateName, error) ->
        // テンプレートエラーの処理
        logTemplateError templateName error
        validateTemplate templateName
        suggestTemplateAlternatives templateName
    | ChangeApplicationError (changeId, error) ->
        // 変更適用エラーの処理
        logChangeError changeId error
        initiateRollback changeId
        analyzeChangeFailure changeId
    | BackupError (operation, error) ->
        // バックアップエラーの処理
        logBackupError operation error
        retryBackupOperation operation
        useAlternativeBackupMethod operation
    | PersistenceError (operation, error) ->
        // 永続化エラーの処理
        logPersistenceError operation error
        retryPersistenceOperation operation
        useAlternativePersistence operation
```

## パフォーマンス最適化

### キャッシュ戦略

```fsharp
type ConfigurationCache = {
    ConfigurationCache: LRUCache<string, obj>
    HierarchyCache: LRUCache<string, ConfigHierarchy>
    ValidationCache: LRUCache<string, ValidationResult>
    TemplateCache: LRUCache<string, ConfigTemplate>
}

let cacheStrategy = {
    ConfigurationCache = LRUCache.create 1000 (TimeSpan.FromMinutes 15.0)
    HierarchyCache = LRUCache.create 500 (TimeSpan.FromMinutes 10.0)
    ValidationCache = LRUCache.create 200 (TimeSpan.FromMinutes 5.0)
    TemplateCache = LRUCache.create 100 (TimeSpan.FromMinutes 30.0)
}
```

### バッチ処理

```fsharp
let batchProcessConfigurationChanges (changes: ConfigChange list) : Result<ChangeResult list, ConfigError> =
    // バッチサイズの設定
    let batchSize = 50
    let batches = changes |> List.chunkBySize batchSize
    
    // バッチ処理の実行
    batches
    |> List.map (fun batch -> async {
        return processBatch batch
    })
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.toList
    |> List.fold (fun acc results ->
        match acc with
        | Ok accResults -> 
            match results with
            | Ok batchResults -> Ok (accResults @ batchResults)
            | Error err -> Error err
        | Error err -> Error err) (Ok [])
```

## 統合ポイント

### 既存システムとの統合

- **AgentStateManager**: エージェント設定との連携
- **TaskAssignmentManager**: タスク配分設定との統合
- **QualityGateManager**: 品質設定との連携
- **WorkflowOrchestrator**: ワークフロー設定との統合

### 外部システム統合

- **環境変数システム**: OS環境変数との連携
- **設定ファイルシステム**: 外部設定ファイルとの同期
- **クラウド設定サービス**: クラウドベース設定管理との統合

## セキュリティ考慮事項

- **設定暗号化**: 機密設定の暗号化保存
- **アクセス制御**: 設定操作の権限管理
- **監査ログ**: 設定変更の完全な追跡
- **データ保護**: 設定データの機密保護

## テスト戦略

### 単体テスト

```fsharp
[<Fact>]
let ``設定取得 - 正常ケース`` () =
    // Given
    let manager = ConfigurationManager()
    let key = "test.setting"
    let expectedValue = "test value"
    manager.SetConfiguration(key, expectedValue) |> ignore
    
    // When
    let result = manager.GetConfiguration<string>(key)
    
    // Then
    match result with
    | Ok (Some actualValue) -> Assert.Equal(expectedValue, actualValue)
    | _ -> Assert.True(false, "設定取得に失敗")

[<Fact>]
let ``階層設定解決 - 優先度テスト`` () =
    // Given
    let manager = ConfigurationManager()
    let hierarchyManager = HierarchyManager()
    let key = "hierarchy.test"
    
    // システム設定
    manager.SetConfigurationInScope(key, "system value", System) |> ignore
    // プロジェクト設定
    manager.SetConfigurationInScope(key, "project value", Project "test-project") |> ignore
    
    // When
    let result = hierarchyManager.ResolveConfiguration<string>(key, [System; Project "test-project"])
    
    // Then
    match result with
    | Ok (Some value) -> Assert.Equal("system value", value) // システム設定が優先
    | _ -> Assert.True(false, "階層設定解決に失敗")
```

### 統合テスト

```fsharp
[<Fact>]
let ``設定管理 - エンドツーエンド`` () =
    // Given
    let manager = createIntegrationTestManager()
    let key = "integration.test"
    let value = "integration value"
    
    // When
    let setResult = manager.SetConfiguration(key, value)
    let getResult = manager.GetConfiguration<string>(key)
    let historyResult = manager.GetConfigurationHistory(key)
    
    // Then
    match setResult, getResult, historyResult with
    | Ok (), Ok (Some retrievedValue), Ok history -> 
        Assert.Equal(value, retrievedValue)
        Assert.True(history.ChangeCount > 0)
    | _ -> Assert.True(false, "統合テストに失敗")
```

## 監視とメトリクス

### 主要パフォーマンス指標

```fsharp
type ConfigurationMetrics = {
    TotalConfigurations: int
    ConfigurationReads: int64
    ConfigurationWrites: int64
    CacheHitRate: float
    AverageResponseTime: TimeSpan
    ValidationErrors: int64
    ChangeApplications: int64
}
```

### リアルタイム監視

- 設定変更の監視
- パフォーマンス指標の追跡
- エラー率の監視
- キャッシュ効率の分析

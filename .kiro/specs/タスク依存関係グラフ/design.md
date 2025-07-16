# 設計書 - タスク依存関係グラフ

## 概要

タスク依存関係グラフは、fcodeのマルチエージェント環境において、タスク間の複雑な依存関係を管理するシステムです。依存関係の自動検出、グラフ構造の管理、実行順序の最適化、循環依存の検出・解決を行い、効率的な並行処理を実現します。

## アーキテクチャ

### システムアーキテクチャ

```
┌─────────────────────────────────────────────────────────────┐
│                  タスク依存関係グラフ                       │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ │
│ │ 依存関係        │ │ グラフ          │ │ 実行順序        │ │
│ │ 検出器          │ │ 管理器          │ │ 最適化器        │ │
│ └─────────────────┘ └─────────────────┘ └─────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ │
│ │ 循環依存        │ │ クリティカル    │ │ 依存関係        │ │
│ │ 検出器          │ │ パス分析器      │ │ 可視化器        │ │
│ └─────────────────┘ └─────────────────┘ └─────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ │
│ │ 動的依存関係    │ │ パフォーマンス  │ │ グラフ          │ │
│ │ 管理器          │ │ 監視器          │ │ 永続化器        │ │
│ └─────────────────┘ └─────────────────┘ └─────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### データフローアーキテクチャ

```
[タスク作成] → [依存関係検出] → [グラフ更新]
     ↓              ↓              ↓
[循環依存チェック] → [実行順序計算] → [クリティカルパス分析]
     ↓              ↓              ↓
[可視化生成] → [パフォーマンス監視] → [動的調整]
```

## コンポーネントとインターフェース

### TaskDependencyGraph

メインの依存関係グラフ管理コンポーネント

```fsharp
type TaskDependencyGraph() =
    
    // タスクの追加
    member _.AddTask(taskInfo: TaskInfo) : Result<unit, GraphError>
    
    // 依存関係の追加
    member _.AddDependency(fromTask: string, toTask: string) : Result<unit, GraphError>
    
    // 依存関係の削除
    member _.RemoveDependency(fromTask: string, toTask: string) : Result<unit, GraphError>
    
    // 実行順序の取得
    member _.GetExecutionOrder() : Result<string list, GraphError>
    
    // クリティカルパスの取得
    member _.GetCriticalPath() : Result<CriticalPath, GraphError>
    
    // 循環依存の検出
    member _.DetectCycles() : Result<CyclicDependency list, GraphError>
```

### DependencyDetector

依存関係検出コンポーネント

```fsharp
type DependencyDetector() =
    
    // 自動依存関係検出
    member _.DetectDependencies(taskInfo: TaskInfo, existingTasks: TaskInfo list) : DependencyInfo list
    
    // 依存関係の検証
    member _.ValidateDependency(fromTask: string, toTask: string) : ValidationResult
    
    // 依存関係の推奨
    member _.SuggestDependencies(taskInfo: TaskInfo, context: TaskContext) : DependencySuggestion list
    
    // 依存関係パターンの学習
    member _.LearnDependencyPatterns(completedTasks: CompletedTask list) : unit
```

### GraphManager

グラフ構造管理コンポーネント

```fsharp
type GraphManager() =
    
    // グラフ構造の構築
    member _.BuildGraph(tasks: TaskInfo list, dependencies: DependencyInfo list) : TaskGraph
    
    // グラフの更新
    member _.UpdateGraph(graph: TaskGraph, changes: GraphChange list) : Result<TaskGraph, GraphError>
    
    // グラフの最適化
    member _.OptimizeGraph(graph: TaskGraph) : OptimizedGraph
    
    // グラフの検証
    member _.ValidateGraph(graph: TaskGraph) : GraphValidationResult
    
    // グラフの統計情報
    member _.GetGraphStatistics(graph: TaskGraph) : GraphStatistics
```

### ExecutionOrderOptimizer

実行順序最適化コンポーネント

```fsharp
type ExecutionOrderOptimizer() =
    
    // 最適実行順序の計算
    member _.CalculateOptimalOrder(graph: TaskGraph, constraints: ExecutionConstraint list) : ExecutionOrder
    
    // 並行実行可能性の分析
    member _.AnalyzeParallelizability(graph: TaskGraph) : ParallelizationAnalysis
    
    // リソース制約を考慮した最適化
    member _.OptimizeWithResourceConstraints(graph: TaskGraph, resources: ResourceConstraint list) : OptimizedExecutionPlan
    
    // 実行順序の動的調整
    member _.AdjustExecutionOrder(currentOrder: ExecutionOrder, runtimeChanges: RuntimeChange list) : ExecutionOrder
```

## データモデル

### TaskGraph

```fsharp
type TaskGraph = {
    Nodes: Map<string, TaskNode>
    Edges: Map<string, DependencyEdge list>
    Metadata: GraphMetadata
    Statistics: GraphStatistics
    LastUpdated: DateTime
}

and TaskNode = {
    TaskId: string
    TaskInfo: TaskInfo
    Dependencies: string list
    Dependents: string list
    ExecutionLevel: int
    CriticalPathMember: bool
    EstimatedDuration: TimeSpan
    Priority: TaskPriority
}

and DependencyEdge = {
    FromTask: string
    ToTask: string
    DependencyType: DependencyType
    Strength: DependencyStrength
    CreatedAt: DateTime
    Reason: string option
}

and DependencyType =
    | FinishToStart
    | StartToStart
    | FinishToFinish
    | StartToFinish
    | ResourceDependency of resource: string
    | DataDependency of data: string
```

### CriticalPath

```fsharp
type CriticalPath = {
    PathId: string
    Tasks: string list
    TotalDuration: TimeSpan
    BottleneckTasks: string list
    OptimizationOpportunities: OptimizationOpportunity list
    RiskFactors: PathRiskFactor list
}

and OptimizationOpportunity = {
    OpportunityId: string
    OpportunityType: OptimizationType
    AffectedTasks: string list
    PotentialTimeSaving: TimeSpan
    ImplementationCost: float
    Feasibility: float
}

and OptimizationType =
    | TaskParallelization
    | ResourceReallocation
    | DependencyRemoval
    | TaskOptimization
    | SkillUpgrading
```

### CyclicDependency

```fsharp
type CyclicDependency = {
    CycleId: string
    CyclePath: string list
    CycleType: CycleType
    Severity: CycleSeverity
    ResolutionStrategies: CycleResolutionStrategy list
    DetectedAt: DateTime
}

and CycleType =
    | DirectCycle of length: int
    | IndirectCycle of intermediateNodes: string list
    | ComplexCycle of subCycles: CyclicDependency list

and CycleResolutionStrategy = {
    StrategyId: string
    StrategyType: ResolutionStrategyType
    AffectedDependencies: (string * string) list
    Impact: ResolutionImpact
    Recommendation: string
}

and ResolutionStrategyType =
    | RemoveDependency of dependency: (string * string)
    | ReorderTasks of newOrder: string list
    | SplitTask of taskId: string * subTasks: string list
    | MergeTasks of taskIds: string list * newTaskId: string
```

### ExecutionOrder

```fsharp
type ExecutionOrder = {
    OrderId: string
    ExecutionLevels: ExecutionLevel list
    ParallelGroups: ParallelGroup list
    TotalEstimatedTime: TimeSpan
    ResourceUtilization: ResourceUtilization
    OptimizationScore: float
}

and ExecutionLevel = {
    Level: int
    Tasks: string list
    EstimatedDuration: TimeSpan
    RequiredResources: ResourceRequirement list
    Parallelizable: bool
}

and ParallelGroup = {
    GroupId: string
    Tasks: string list
    MaxParallelism: int
    ResourceConstraints: ResourceConstraint list
    SynchronizationPoints: SynchronizationPoint list
}
```

## コアアルゴリズム

### 依存関係検出アルゴリズム

```fsharp
let detectDependencies (newTask: TaskInfo) (existingTasks: TaskInfo list) : DependencyInfo list =
    let dependencies = ResizeArray<DependencyInfo>()
    
    // 1. データ依存関係の検出
    let dataDependencies = detectDataDependencies newTask existingTasks
    dependencies.AddRange(dataDependencies)
    
    // 2. リソース依存関係の検出
    let resourceDependencies = detectResourceDependencies newTask existingTasks
    dependencies.AddRange(resourceDependencies)
    
    // 3. 論理依存関係の検出
    let logicalDependencies = detectLogicalDependencies newTask existingTasks
    dependencies.AddRange(logicalDependencies)
    
    // 4. 時間依存関係の検出
    let temporalDependencies = detectTemporalDependencies newTask existingTasks
    dependencies.AddRange(temporalDependencies)
    
    // 5. 依存関係の強度評価
    dependencies
    |> Seq.map (fun dep -> evaluateDependencyStrength dep newTask existingTasks)
    |> Seq.filter (fun dep -> dep.Strength >= MinimumDependencyStrength)
    |> Seq.toList
```

### 循環依存検出アルゴリズム

```fsharp
let detectCycles (graph: TaskGraph) : CyclicDependency list =
    let visited = HashSet<string>()
    let recursionStack = HashSet<string>()
    let cycles = ResizeArray<CyclicDependency>()
    
    let rec dfs (nodeId: string) (path: string list) =
        if recursionStack.Contains(nodeId) then
            // 循環依存を発見
            let cycleStart = path |> List.findIndex ((=) nodeId)
            let cyclePath = path |> List.skip cycleStart
            let cycle = createCyclicDependency cyclePath graph
            cycles.Add(cycle)
        elif not (visited.Contains(nodeId)) then
            visited.Add(nodeId) |> ignore
            recursionStack.Add(nodeId) |> ignore
            
            // 隣接ノードを探索
            match graph.Edges.TryFind(nodeId) with
            | Some edges ->
                edges |> List.iter (fun edge -> 
                    dfs edge.ToTask (nodeId :: path))
            | None -> ()
            
            recursionStack.Remove(nodeId) |> ignore
    
    // 全ノードから探索開始
    graph.Nodes.Keys |> Seq.iter (fun nodeId -> dfs nodeId [])
    
    cycles |> Seq.toList
```

### クリティカルパス計算アルゴリズム

```fsharp
let calculateCriticalPath (graph: TaskGraph) : CriticalPath =
    // 1. トポロジカルソート
    let topologicalOrder = topologicalSort graph
    
    // 2. 最早開始時間の計算
    let earliestStart = calculateEarliestStartTimes graph topologicalOrder
    
    // 3. 最遅開始時間の計算
    let latestStart = calculateLatestStartTimes graph (List.rev topologicalOrder)
    
    // 4. クリティカルタスクの特定
    let criticalTasks = 
        graph.Nodes.Keys
        |> Seq.filter (fun taskId -> 
            earliestStart.[taskId] = latestStart.[taskId])
        |> Seq.toList
    
    // 5. クリティカルパスの構築
    let criticalPath = buildCriticalPath criticalTasks graph
    
    // 6. ボトルネックの特定
    let bottlenecks = identifyBottlenecks criticalPath graph
    
    // 7. 最適化機会の分析
    let optimizations = analyzeOptimizationOpportunities criticalPath graph
    
    {
        PathId = Guid.NewGuid().ToString()
        Tasks = criticalPath
        TotalDuration = calculatePathDuration criticalPath graph
        BottleneckTasks = bottlenecks
        OptimizationOpportunities = optimizations
        RiskFactors = analyzePathRisks criticalPath graph
    }
```

### 実行順序最適化アルゴリズム

```fsharp
let optimizeExecutionOrder (graph: TaskGraph) (constraints: ExecutionConstraint list) : ExecutionOrder =
    // 1. 基本実行レベルの計算
    let basicLevels = calculateExecutionLevels graph
    
    // 2. 並行実行可能性の分析
    let parallelizationAnalysis = analyzeParallelizability graph basicLevels
    
    // 3. リソース制約の適用
    let resourceConstrainedLevels = applyResourceConstraints basicLevels constraints
    
    // 4. 並行グループの最適化
    let optimizedGroups = optimizeParallelGroups resourceConstrainedLevels parallelizationAnalysis
    
    // 5. 実行順序の検証
    let validationResult = validateExecutionOrder optimizedGroups graph
    
    // 6. 最適化スコアの計算
    let optimizationScore = calculateOptimizationScore optimizedGroups graph constraints
    
    {
        OrderId = Guid.NewGuid().ToString()
        ExecutionLevels = resourceConstrainedLevels
        ParallelGroups = optimizedGroups
        TotalEstimatedTime = calculateTotalExecutionTime resourceConstrainedLevels
        ResourceUtilization = calculateResourceUtilization optimizedGroups constraints
        OptimizationScore = optimizationScore
    }
```

## エラーハンドリング

### グラフ管理エラータイプ

```fsharp
type GraphError =
    | CyclicDependencyError of cycle: CyclicDependency
    | InvalidDependencyError of fromTask: string * toTask: string * reason: string
    | TaskNotFoundError of taskId: string
    | GraphInconsistencyError of inconsistency: GraphInconsistency
    | OptimizationError of optimization: OptimizationError
    | PerformanceError of performance: PerformanceError

let handleGraphError error =
    match error with
    | CyclicDependencyError cycle ->
        // 循環依存エラーの処理
        logCyclicDependency cycle
        suggestCycleResolution cycle
    | InvalidDependencyError (fromTask, toTask, reason) ->
        // 無効な依存関係エラーの処理
        logInvalidDependency fromTask toTask reason
        suggestValidDependencies fromTask toTask
    | TaskNotFoundError taskId ->
        // タスク未発見エラーの処理
        logTaskNotFound taskId
        suggestSimilarTasks taskId
    | GraphInconsistencyError inconsistency ->
        // グラフ不整合エラーの処理
        logGraphInconsistency inconsistency
        repairGraphInconsistency inconsistency
    | OptimizationError optimization ->
        // 最適化エラーの処理
        logOptimizationError optimization
        fallbackToBasicOptimization optimization
    | PerformanceError performance ->
        // パフォーマンスエラーの処理
        logPerformanceError performance
        optimizeGraphPerformance performance
```

## パフォーマンス最適化

### キャッシュ戦略

```fsharp
type GraphCache = {
    GraphStructureCache: LRUCache<string, TaskGraph>
    ExecutionOrderCache: LRUCache<string, ExecutionOrder>
    CriticalPathCache: LRUCache<string, CriticalPath>
    DependencyCache: LRUCache<string, DependencyInfo list>
}

let cacheStrategy = {
    GraphStructureCache = LRUCache.create 100 (TimeSpan.FromMinutes 10.0)
    ExecutionOrderCache = LRUCache.create 200 (TimeSpan.FromMinutes 5.0)
    CriticalPathCache = LRUCache.create 50 (TimeSpan.FromMinutes 15.0)
    DependencyCache = LRUCache.create 500 (TimeSpan.FromMinutes 2.0)
}
```

### 増分更新

```fsharp
let incrementalGraphUpdate (currentGraph: TaskGraph) (changes: GraphChange list) : TaskGraph =
    // 変更の影響範囲を分析
    let affectedNodes = analyzeChangeImpact changes currentGraph
    
    // 影響を受けるノードのみを更新
    let updatedNodes = updateAffectedNodes affectedNodes changes currentGraph
    
    // グラフ構造の部分的再構築
    let updatedGraph = rebuildAffectedSubgraph updatedNodes currentGraph
    
    // キャッシュの部分的無効化
    invalidateAffectedCache affectedNodes
    
    updatedGraph
```

## 統合ポイント

### 既存システムとの統合

- **TaskAssignmentManager**: タスク配分時の依存関係考慮
- **ProgressAggregator**: 依存関係を考慮した進捗計算
- **AgentStateManager**: エージェント状態と依存関係の同期
- **RealtimeCollaborationFacade**: 協調作業での依存関係管理

### 外部システム統合

- **プロジェクト管理ツール**: 外部PMツールとの依存関係同期
- **ワークフローエンジン**: ワークフロー実行との連携
- **分析プラットフォーム**: 依存関係分析データの提供
- **可視化ツール**: グラフ可視化システムとの連携

## セキュリティ考慮事項

- **依存関係データ保護**: 依存関係情報の機密性保護
- **アクセス制御**: 依存関係変更への適切なアクセス制御
- **データ整合性**: 依存関係データの改ざん防止
- **監査ログ**: 依存関係変更の追跡可能性

## テスト戦略

### 単体テスト

```fsharp
[<Fact>]
let ``依存関係追加 - 正常ケース`` () =
    // Given
    let graph = TaskDependencyGraph()
    let task1 = createTestTask "task1"
    let task2 = createTestTask "task2"
    
    // When
    graph.AddTask(task1) |> ignore
    graph.AddTask(task2) |> ignore
    let result = graph.AddDependency("task1", "task2")
    
    // Then
    match result with
    | Ok _ -> Assert.True(true)
    | Error error -> Assert.True(false, $"予期しないエラー: {error}")

[<Fact>]
let ``循環依存検出 - 循環依存あり`` () =
    // Given
    let graph = createTestGraphWithCycle()
    
    // When
    let cycles = graph.DetectCycles()
    
    // Then
    match cycles with
    | Ok cycleList -> Assert.NotEmpty(cycleList)
    | Error error -> Assert.True(false, $"循環依存検出に失敗: {error}")
```

### 統合テスト

```fsharp
[<Fact>]
let ``依存関係グラフ - エンドツーエンド`` () =
    // Given
    let graph = createIntegrationTestGraph()
    let tasks = createTestTasks 10
    
    // When
    tasks |> List.iter (fun task -> graph.AddTask(task) |> ignore)
    let executionOrder = graph.GetExecutionOrder()
    let criticalPath = graph.GetCriticalPath()
    
    // Then
    match executionOrder, criticalPath with
    | Ok order, Ok path -> 
        Assert.NotEmpty(order)
        Assert.NotEmpty(path.Tasks)
    | _ -> Assert.True(false, "統合テストに失敗")
```

## 監視とメトリクス

### 主要パフォーマンス指標

```fsharp
type GraphMetrics = {
    GraphUpdateLatency: TimeSpan
    DependencyDetectionAccuracy: float
    CycleDetectionTime: TimeSpan
    OptimizationEffectiveness: float
    MemoryUsage: float
    CacheHitRate: float
}
```

### リアルタイム監視

- グラフ更新のレイテンシ監視
- 依存関係検出精度の追跡
- 循環依存検出性能の監視
- 最適化効果の測定
- メモリ使用量の監視

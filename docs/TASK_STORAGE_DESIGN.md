# fcode タスクストレージ設計 (SQLite3)

**目的**: リアルタイム協調機能のためのタスク永続化戦略

## 1. ストレージ戦略概要

### 1.1 設計方針

**SQLite3採用理由**:
- ✅ **ACID保証**: タスク依存関係の整合性確保
- ✅ **並行アクセス**: WALモードによる読み取り並行性
- ✅ **軽量**: 単一ファイル、組み込み可能
- ✅ **F#/.NET親和性**: Microsoft.Data.Sqlite公式サポート
- ✅ **クエリ性能**: 依存関係解析、進捗集約に最適

### 1.2 アーキテクチャ統合

```
┌─────────────────────────────────────────────────────────────────┐
│                RealtimeCollaborationFacade                    │
│                        統合ファサード                           │
├─────────────────────────────────────────────────────────────────┤
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ │
│ │AgentState   │ │TaskDependency│ │Progress     │ │Collaboration│ │
│ │Manager      │ │Graph        │ │Aggregator   │ │Coordinator  │ │
│ └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘ │
├─────────────────────────────────────────────────────────────────┤
│                    TaskStorageManager                          │
│                   SQLite3統合レイヤー                            │
├─────────────────────────────────────────────────────────────────┤
│ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ │
│ │tasks        │ │task_        │ │agent_state_ │ │progress_    │ │
│ │テーブル      │ │dependencies │ │history      │ │events       │ │
│ └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

## 2. データベーススキーマ

### 2.1 テーブル設計

#### 2.1.1 tasks - タスクメインテーブル

```sql
CREATE TABLE tasks (
    -- 基本情報
    task_id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    description TEXT,
    
    -- 状態管理
    status TEXT NOT NULL CHECK (status IN ('Pending', 'InProgress', 'Completed', 'Failed', 'Cancelled')),
    assigned_agent TEXT,
    priority INTEGER NOT NULL CHECK (priority BETWEEN 1 AND 4), -- TaskPriority enum値
    
    -- 時間管理
    estimated_duration_minutes INTEGER,
    actual_duration_minutes INTEGER,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    completed_at DATETIME,
    
    -- PO指示トレーサビリティ
    source_instruction TEXT, -- 元のPO指示文
    po_session_id TEXT,      -- PO会話セッションID
    
    -- 追加メタデータ
    tags TEXT, -- JSON配列形式でタグ保存
    context_data TEXT -- JSON形式で追加コンテキスト
);
```

#### 2.1.2 task_dependencies - タスク依存関係テーブル

```sql
CREATE TABLE task_dependencies (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id TEXT NOT NULL,
    depends_on_task_id TEXT NOT NULL,
    dependency_type TEXT DEFAULT 'hard' CHECK (dependency_type IN ('hard', 'soft')),
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by TEXT, -- 依存関係作成者（PO/システム自動）
    
    FOREIGN KEY (task_id) REFERENCES tasks (task_id) ON DELETE CASCADE,
    FOREIGN KEY (depends_on_task_id) REFERENCES tasks (task_id) ON DELETE CASCADE,
    UNIQUE (task_id, depends_on_task_id)
);
```

#### 2.1.3 task_resources - リソース管理テーブル

```sql
CREATE TABLE task_resources (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id TEXT NOT NULL,
    resource_name TEXT NOT NULL,
    resource_type TEXT DEFAULT 'system' CHECK (resource_type IN ('system', 'file', 'api', 'memory', 'custom')),
    allocation_amount INTEGER DEFAULT 1, -- リソース使用量
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (task_id) REFERENCES tasks (task_id) ON DELETE CASCADE,
    UNIQUE (task_id, resource_name)
);
```

#### 2.1.4 agent_state_history - エージェント状態履歴テーブル

```sql
CREATE TABLE agent_state_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    agent_id TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('Idle', 'Working', 'Blocked', 'Error', 'Completed')),
    progress REAL NOT NULL CHECK (progress >= 0.0 AND progress <= 100.0),
    current_task_id TEXT,
    working_directory TEXT,
    process_id INTEGER,
    timestamp DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    -- 詳細状態情報
    memory_usage_mb INTEGER,
    cpu_usage_percent REAL,
    error_message TEXT,
    
    FOREIGN KEY (current_task_id) REFERENCES tasks (task_id),
    
    -- パーティション戦略: 月単位でインデックス分離
    CHECK (timestamp >= datetime('now', '-6 months'))
);
```

#### 2.1.5 progress_events - 進捗イベントテーブル

```sql
CREATE TABLE progress_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_type TEXT NOT NULL CHECK (event_type IN ('TaskStarted', 'TaskCompleted', 'ProgressUpdate', 'Error', 'Milestone')),
    agent_id TEXT NOT NULL,
    task_id TEXT,
    progress_value REAL,
    
    -- イベント詳細データ (JSON)
    event_data TEXT, -- JSON形式: {"duration_ms": 1500, "resources_used": ["cpu", "memory"]}
    
    timestamp DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    correlation_id TEXT, -- 関連イベントの紐付け用
    
    FOREIGN KEY (task_id) REFERENCES tasks (task_id),
    
    -- パーティション戦略: 週単位で古いデータ削除
    CHECK (timestamp >= datetime('now', '-4 weeks'))
);
```

#### 2.1.6 collaboration_locks - リソースロック管理テーブル

```sql
CREATE TABLE collaboration_locks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    resource_name TEXT NOT NULL UNIQUE,
    locked_by_agent TEXT NOT NULL,
    task_id TEXT NOT NULL,
    locked_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at DATETIME NOT NULL,
    lock_type TEXT DEFAULT 'exclusive' CHECK (lock_type IN ('exclusive', 'shared')),
    
    FOREIGN KEY (task_id) REFERENCES tasks (task_id) ON DELETE CASCADE
);
```

### 2.2 インデックス戦略

```sql
-- ===============================================
-- パフォーマンス最適化インデックス
-- ===============================================

-- タスク検索最適化
CREATE INDEX idx_tasks_status ON tasks (status);
CREATE INDEX idx_tasks_assigned_agent ON tasks (assigned_agent);
CREATE INDEX idx_tasks_priority_created ON tasks (priority DESC, created_at ASC);
CREATE INDEX idx_tasks_po_session ON tasks (po_session_id);

-- 依存関係解析最適化
CREATE INDEX idx_dependencies_task_id ON task_dependencies (task_id);
CREATE INDEX idx_dependencies_depends_on ON task_dependencies (depends_on_task_id);
CREATE INDEX idx_dependencies_composite ON task_dependencies (task_id, depends_on_task_id);

-- エージェント状態監視最適化
CREATE INDEX idx_agent_history_agent_timestamp ON agent_state_history (agent_id, timestamp DESC);
CREATE INDEX idx_agent_history_task_id ON agent_state_history (current_task_id);

-- 進捗分析最適化
CREATE INDEX idx_progress_events_timestamp ON progress_events (timestamp DESC);
CREATE INDEX idx_progress_events_task_agent ON progress_events (task_id, agent_id);
CREATE INDEX idx_progress_events_type_timestamp ON progress_events (event_type, timestamp DESC);

-- リソース管理最適化
CREATE INDEX idx_task_resources_task ON task_resources (task_id);
CREATE INDEX idx_collaboration_locks_resource ON collaboration_locks (resource_name);
CREATE INDEX idx_collaboration_locks_agent ON collaboration_locks (locked_by_agent);
```

### 2.3 ビュー定義

#### 2.3.1 実行可能タスク一覧ビュー

```sql
CREATE VIEW executable_tasks AS
SELECT 
    t.*,
    COUNT(tr.resource_name) as required_resource_count
FROM tasks t
LEFT JOIN task_resources tr ON t.task_id = tr.task_id
WHERE t.status = 'Pending'
  AND NOT EXISTS (
    SELECT 1
    FROM task_dependencies td
    JOIN tasks dep ON td.depends_on_task_id = dep.task_id
    WHERE td.task_id = t.task_id
      AND dep.status NOT IN ('Completed')
      AND td.dependency_type = 'hard'
  )
GROUP BY t.task_id
ORDER BY t.priority DESC, t.created_at ASC;
```

#### 2.3.2 エージェント別進捗サマリービュー

```sql
CREATE VIEW agent_progress_summary AS
SELECT 
    agent_id,
    COUNT(*) as total_tasks,
    COUNT(CASE WHEN status = 'Completed' THEN 1 END) as completed_tasks,
    COUNT(CASE WHEN status = 'InProgress' THEN 1 END) as active_tasks,
    COUNT(CASE WHEN status = 'Failed' THEN 1 END) as failed_tasks,
    AVG(CASE WHEN status = 'Completed' AND actual_duration_minutes IS NOT NULL 
        THEN actual_duration_minutes END) as avg_completion_time_minutes,
    MAX(updated_at) as last_activity
FROM tasks
WHERE assigned_agent IS NOT NULL
GROUP BY assigned_agent;
```

#### 2.3.3 リアルタイム進捗ダッシュボードビュー

```sql
CREATE VIEW realtime_progress_dashboard AS
SELECT
    -- 全体サマリー
    COUNT(*) as total_tasks,
    COUNT(CASE WHEN status = 'Completed' THEN 1 END) as completed_tasks,
    COUNT(CASE WHEN status = 'InProgress' THEN 1 END) as active_tasks,
    COUNT(CASE WHEN status = 'Blocked' THEN 1 END) as blocked_tasks,
    
    -- 進捗率計算
    ROUND(
        COUNT(CASE WHEN status = 'Completed' THEN 1 END) * 100.0 / COUNT(*), 
        2
    ) as completion_percentage,
    
    -- 推定残り時間
    AVG(CASE WHEN status = 'InProgress' AND estimated_duration_minutes IS NOT NULL 
        THEN estimated_duration_minutes END) as avg_remaining_time_minutes,
    
    -- アクティブエージェント数
    COUNT(DISTINCT assigned_agent) as active_agents,
    
    MAX(updated_at) as last_update
FROM tasks
WHERE created_at >= datetime('now', '-1 day');
```

#### 2.3.4 デッドロック検出支援ビュー

```sql
CREATE VIEW potential_deadlocks AS
WITH RECURSIVE dependency_chain AS (
    -- 基点となる依存関係
    SELECT 
        task_id,
        depends_on_task_id,
        1 as depth,
        task_id || ' -> ' || depends_on_task_id as chain
    FROM task_dependencies
    
    UNION ALL
    
    -- 再帰的に依存関係を追跡
    SELECT 
        dc.task_id,
        td.depends_on_task_id,
        dc.depth + 1,
        dc.chain || ' -> ' || td.depends_on_task_id
    FROM dependency_chain dc
    JOIN task_dependencies td ON dc.depends_on_task_id = td.task_id
    WHERE dc.depth < 10 -- 循環防止
)
SELECT DISTINCT
    task_id as potential_deadlock_task,
    chain as dependency_chain,
    depth
FROM dependency_chain
WHERE depends_on_task_id = task_id  -- 循環検出
ORDER BY depth DESC;
```

## 3. F#統合実装

### 3.1 TaskStorageManager実装

```fsharp
module FCode.Collaboration.TaskStorageManager

open System
open System.Data
open Microsoft.Data.Sqlite
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

type TaskStorageManager(connectionString: string) =
    
    /// データベース接続作成
    let createConnection () = 
        new SqliteConnection(connectionString)
    
    /// データベース初期化
    member _.InitializeDatabase() =
        async {
            try
                use connection = createConnection()
                do! connection.OpenAsync() |> Async.AwaitTask
                
                // スキーマ作成SQLを実行
                let schemaCommands = [
                    // tasks テーブル作成
                    """CREATE TABLE IF NOT EXISTS tasks (
                        task_id TEXT PRIMARY KEY,
                        title TEXT NOT NULL,
                        description TEXT,
                        status TEXT NOT NULL CHECK (status IN ('Pending', 'InProgress', 'Completed', 'Failed', 'Cancelled')),
                        assigned_agent TEXT,
                        priority INTEGER NOT NULL CHECK (priority BETWEEN 1 AND 4),
                        estimated_duration_minutes INTEGER,
                        actual_duration_minutes INTEGER,
                        created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        completed_at DATETIME,
                        source_instruction TEXT,
                        po_session_id TEXT,
                        tags TEXT,
                        context_data TEXT
                    )""";
                    
                    // インデックス作成
                    "CREATE INDEX IF NOT EXISTS idx_tasks_status ON tasks (status)";
                    "CREATE INDEX IF NOT EXISTS idx_tasks_assigned_agent ON tasks (assigned_agent)";
                    // ... 他のインデックス
                ]
                
                for sql in schemaCommands do
                    use command = new SqliteCommand(sql, connection)
                    do! command.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore
                
                logInfo "TaskStorageManager" "Database initialized successfully"
                return Result.Ok()
                
            with ex ->
                logError "TaskStorageManager" $"Database initialization failed: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }
    
    /// タスク保存
    member _.SaveTask(task: TaskInfo) =
        async {
            try
                use connection = createConnection()
                do! connection.OpenAsync() |> Async.AwaitTask
                
                let sql = """
                    INSERT OR REPLACE INTO tasks 
                    (task_id, title, description, status, assigned_agent, priority, 
                     estimated_duration_minutes, actual_duration_minutes, 
                     created_at, updated_at, completed_at, source_instruction, po_session_id)
                    VALUES (@taskId, @title, @description, @status, @assignedAgent, @priority,
                            @estimatedDuration, @actualDuration, @createdAt, @updatedAt, @completedAt,
                            @sourceInstruction, @poSessionId)
                """
                
                use command = new SqliteCommand(sql, connection)
                command.Parameters.AddWithValue("@taskId", task.TaskId) |> ignore
                command.Parameters.AddWithValue("@title", task.Title) |> ignore
                command.Parameters.AddWithValue("@description", task.Description) |> ignore
                command.Parameters.AddWithValue("@status", task.Status.ToString()) |> ignore
                command.Parameters.AddWithValue("@assignedAgent", 
                    match task.AssignedAgent with Some agent -> agent | None -> DBNull.Value) |> ignore
                command.Parameters.AddWithValue("@priority", int task.Priority) |> ignore
                command.Parameters.AddWithValue("@estimatedDuration", 
                    match task.EstimatedDuration with 
                    | Some duration -> duration.TotalMinutes 
                    | None -> DBNull.Value) |> ignore
                command.Parameters.AddWithValue("@actualDuration", 
                    match task.ActualDuration with 
                    | Some duration -> duration.TotalMinutes 
                    | None -> DBNull.Value) |> ignore
                command.Parameters.AddWithValue("@createdAt", task.CreatedAt) |> ignore
                command.Parameters.AddWithValue("@updatedAt", task.UpdatedAt) |> ignore
                command.Parameters.AddWithValue("@completedAt", 
                    match task.Status with 
                    | Completed -> DateTime.UtcNow 
                    | _ -> DBNull.Value) |> ignore
                command.Parameters.AddWithValue("@sourceInstruction", DBNull.Value) |> ignore // 将来実装
                command.Parameters.AddWithValue("@poSessionId", DBNull.Value) |> ignore // 将来実装
                
                let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask
                
                logInfo "TaskStorageManager" $"Task saved: {task.TaskId}"
                return Result.Ok rowsAffected
                
            with ex ->
                logError "TaskStorageManager" $"Failed to save task {task.TaskId}: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }
    
    /// 実行可能タスク取得
    member _.GetExecutableTasks() =
        async {
            try
                use connection = createConnection()
                do! connection.OpenAsync() |> Async.AwaitTask
                
                let sql = "SELECT * FROM executable_tasks LIMIT 100"
                use command = new SqliteCommand(sql, connection)
                
                use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask
                let tasks = ResizeArray<TaskInfo>()
                
                while! reader.ReadAsync() |> Async.AwaitTask do
                    let task = {
                        TaskId = reader.GetString("task_id")
                        Title = reader.GetString("title")
                        Description = reader.GetString("description")
                        Status = 
                            match reader.GetString("status") with
                            | "Pending" -> Pending
                            | "InProgress" -> InProgress
                            | "Completed" -> Completed
                            | "Failed" -> Failed
                            | "Cancelled" -> Cancelled
                            | _ -> Pending
                        AssignedAgent = 
                            if reader.IsDBNull("assigned_agent") then None 
                            else Some (reader.GetString("assigned_agent"))
                        Priority = enum<TaskPriority>(reader.GetInt32("priority"))
                        EstimatedDuration = 
                            if reader.IsDBNull("estimated_duration_minutes") then None
                            else Some (TimeSpan.FromMinutes(reader.GetDouble("estimated_duration_minutes")))
                        ActualDuration = 
                            if reader.IsDBNull("actual_duration_minutes") then None
                            else Some (TimeSpan.FromMinutes(reader.GetDouble("actual_duration_minutes")))
                        RequiredResources = [] // 別途取得が必要
                        CreatedAt = reader.GetDateTime("created_at")
                        UpdatedAt = reader.GetDateTime("updated_at")
                    }
                    tasks.Add(task)
                
                logInfo "TaskStorageManager" $"Retrieved {tasks.Count} executable tasks"
                return Result.Ok(tasks.ToArray() |> Array.toList)
                
            with ex ->
                logError "TaskStorageManager" $"Failed to get executable tasks: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }
    
    /// タスク依存関係保存
    member _.SaveTaskDependency(taskId: string, dependsOnTaskId: string, dependencyType: string) =
        async {
            try
                use connection = createConnection()
                do! connection.OpenAsync() |> Async.AwaitTask
                
                let sql = """
                    INSERT OR IGNORE INTO task_dependencies 
                    (task_id, depends_on_task_id, dependency_type, created_by)
                    VALUES (@taskId, @dependsOnTaskId, @dependencyType, @createdBy)
                """
                
                use command = new SqliteCommand(sql, connection)
                command.Parameters.AddWithValue("@taskId", taskId) |> ignore
                command.Parameters.AddWithValue("@dependsOnTaskId", dependsOnTaskId) |> ignore
                command.Parameters.AddWithValue("@dependencyType", dependencyType) |> ignore
                command.Parameters.AddWithValue("@createdBy", "system") |> ignore
                
                let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask
                
                logInfo "TaskStorageManager" $"Dependency saved: {taskId} -> {dependsOnTaskId}"
                return Result.Ok rowsAffected
                
            with ex ->
                logError "TaskStorageManager" $"Failed to save dependency: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }
    
    /// エージェント状態履歴保存
    member _.SaveAgentStateHistory(agentState: AgentState) =
        async {
            try
                use connection = createConnection()
                do! connection.OpenAsync() |> Async.AwaitTask
                
                let sql = """
                    INSERT INTO agent_state_history 
                    (agent_id, status, progress, current_task_id, working_directory, process_id, timestamp)
                    VALUES (@agentId, @status, @progress, @currentTaskId, @workingDirectory, @processId, @timestamp)
                """
                
                use command = new SqliteCommand(sql, connection)
                command.Parameters.AddWithValue("@agentId", agentState.AgentId) |> ignore
                command.Parameters.AddWithValue("@status", agentState.Status.ToString()) |> ignore
                command.Parameters.AddWithValue("@progress", agentState.Progress) |> ignore
                command.Parameters.AddWithValue("@currentTaskId", 
                    match agentState.CurrentTask with Some task -> task | None -> DBNull.Value) |> ignore
                command.Parameters.AddWithValue("@workingDirectory", agentState.WorkingDirectory) |> ignore
                command.Parameters.AddWithValue("@processId", 
                    match agentState.ProcessId with Some pid -> pid | None -> DBNull.Value) |> ignore
                command.Parameters.AddWithValue("@timestamp", agentState.LastUpdate) |> ignore
                
                let! rowsAffected = command.ExecuteNonQueryAsync() |> Async.AwaitTask
                return Result.Ok rowsAffected
                
            with ex ->
                logError "TaskStorageManager" $"Failed to save agent state history: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }
    
    /// 進捗サマリー取得
    member _.GetProgressSummary() =
        async {
            try
                use connection = createConnection()
                do! connection.OpenAsync() |> Async.AwaitTask
                
                let sql = "SELECT * FROM realtime_progress_dashboard"
                use command = new SqliteCommand(sql, connection)
                
                use! reader = command.ExecuteReaderAsync() |> Async.AwaitTask
                
                if! reader.ReadAsync() |> Async.AwaitTask then
                    let summary = {
                        TotalTasks = reader.GetInt32("total_tasks")
                        CompletedTasks = reader.GetInt32("completed_tasks")
                        InProgressTasks = reader.GetInt32("active_tasks")
                        BlockedTasks = reader.GetInt32("blocked_tasks")
                        ActiveAgents = reader.GetInt32("active_agents")
                        OverallProgress = reader.GetDouble("completion_percentage")
                        EstimatedTimeRemaining = 
                            if reader.IsDBNull("avg_remaining_time_minutes") then None
                            else Some (TimeSpan.FromMinutes(reader.GetDouble("avg_remaining_time_minutes")))
                        LastUpdated = reader.GetDateTime("last_update")
                    }
                    return Result.Ok summary
                else
                    return Result.Error(NotFound "No progress data available")
                
            with ex ->
                logError "TaskStorageManager" $"Failed to get progress summary: {ex.Message}"
                return Result.Error(SystemError ex.Message)
        }
    
    /// リソース解放
    interface IDisposable with
        member _.Dispose() = ()
```

### 3.2 設定統合

```fsharp
type CollaborationConfig = {
    // 既存設定
    MaxConcurrentAgents: int
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
    BackupIntervalHours: int
} with
    static member Default = {
        MaxConcurrentAgents = 10
        TaskTimeoutMinutes = 30
        StaleAgentThreshold = TimeSpan.FromMinutes(5.0)
        MaxRetryAttempts = 3
        
        DatabasePath = "~/.fcode/tasks.db"
        ConnectionPoolSize = 5
        WALModeEnabled = true
        AutoVacuumEnabled = true
        MaxHistoryRetentionDays = 30
        BackupEnabled = true
        BackupIntervalHours = 24
    }
    
    member this.ConnectionString =
        let expandedPath = System.IO.Path.GetFullPath(this.DatabasePath.Replace("~", System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)))
        $"Data Source={expandedPath};Cache=Shared;Pooling=true;Max Pool Size={this.ConnectionPoolSize}"
```

## 4. 運用・パフォーマンス

### 4.1 SQLite最適化設定

```sql
-- WALモード有効化（並行読み取り改善）
PRAGMA journal_mode = WAL;

-- ページサイズ最適化
PRAGMA page_size = 4096;

-- キャッシュサイズ設定
PRAGMA cache_size = 10000;

-- 外部キー制約有効化
PRAGMA foreign_keys = ON;

-- 自動VACUUM有効化
PRAGMA auto_vacuum = INCREMENTAL;

-- 同期設定（パフォーマンス重視）
PRAGMA synchronous = NORMAL;

-- 一時ファイル設定
PRAGMA temp_store = MEMORY;
```

### 4.2 メンテナンス戦略

```sql
-- 古いエージェント状態履歴削除（30日より古い）
DELETE FROM agent_state_history 
WHERE timestamp < datetime('now', '-30 days');

-- 古い進捗イベント削除（7日より古い）
DELETE FROM progress_events 
WHERE timestamp < datetime('now', '-7 days');

-- 完了したタスクのリソース情報削除（90日より古い）
DELETE FROM task_resources 
WHERE task_id IN (
    SELECT task_id FROM tasks 
    WHERE status = 'Completed' 
    AND completed_at < datetime('now', '-90 days')
);

-- データベース最適化
PRAGMA optimize;
PRAGMA incremental_vacuum;
```

### 4.3 バックアップ戦略

```fsharp
member _.CreateBackup(backupPath: string) =
    async {
        try
            use sourceConnection = createConnection()
            do! sourceConnection.OpenAsync() |> Async.AwaitTask
            
            use backupConnection = new SqliteConnection($"Data Source={backupPath}")
            do! backupConnection.OpenAsync() |> Async.AwaitTask
            
            sourceConnection.BackupDatabase(backupConnection, "main", "main", -1, null, 0) |> ignore
            
            logInfo "TaskStorageManager" $"Backup created: {backupPath}"
            return Result.Ok()
            
        with ex ->
            logError "TaskStorageManager" $"Backup failed: {ex.Message}"
            return Result.Error(SystemError ex.Message)
    }
```

## 5. 統合テスト戦略

### 5.1 テストデータ準備

```fsharp
module FCode.Tests.TaskStorageTests

let createTestDatabase() =
    let testDbPath = System.IO.Path.GetTempFileName()
    let connectionString = $"Data Source={testDbPath}"
    let storage = new TaskStorageManager(connectionString)
    
    // 初期化
    storage.InitializeDatabase() |> Async.RunSynchronously |> ignore
    
    storage, testDbPath

[<Fact>]
let ``TaskStorageManager - タスク保存・取得テスト`` () =
    let storage, dbPath = createTestDatabase()
    
    try
        let testTask = {
            TaskId = "test-task-1"
            Title = "Test Task"
            Description = "Test Description"
            Status = Pending
            AssignedAgent = None
            Priority = TaskPriority.High
            EstimatedDuration = Some (TimeSpan.FromHours(2.0))
            ActualDuration = None
            RequiredResources = ["cpu"; "memory"]
            CreatedAt = DateTime.UtcNow
            UpdatedAt = DateTime.UtcNow
        }
        
        // 保存テスト
        let saveResult = storage.SaveTask(testTask) |> Async.RunSynchronously
        Assert.True(match saveResult with Result.Ok _ -> true | _ -> false)
        
        // 取得テスト
        let executableTasks = storage.GetExecutableTasks() |> Async.RunSynchronously
        match executableTasks with
        | Result.Ok tasks ->
            Assert.True(tasks |> List.exists (fun t -> t.TaskId = "test-task-1"))
        | Result.Error _ ->
            Assert.True(false, "タスク取得に失敗")
            
    finally
        storage.Dispose()
        System.IO.File.Delete(dbPath)
```

## 6. 移行戦略

### 6.1 段階的導入

**Phase 1**: 基本SQLite統合
- TaskStorageManager実装
- 基本CRUD操作
- 既存メモリベース実装との並行動作

**Phase 2**: 高度機能統合
- 依存関係永続化
- エージェント状態履歴
- 進捗分析強化

**Phase 3**: 運用最適化
- パフォーマンス調整
- バックアップ・復旧機能
- 監視・アラート機能

### 6.2 既存システムとの互換性

```fsharp
// 既存のメモリベース実装をSQLiteに段階移行
type HybridTaskDependencyGraph(config: CollaborationConfig, storage: TaskStorageManager option) =
    inherit TaskDependencyGraph(config)
    
    // SQLiteが利用可能な場合は永続化、そうでなければメモリ内動作
    override this.AddTask(task: TaskInfo) =
        match storage with
        | Some storage -> 
            // SQLiteに保存 + メモリ内キャッシュ更新
            storage.SaveTask(task) |> Async.RunSynchronously |> ignore
            base.AddTask(task)
        | None -> 
            // 従来のメモリ内処理
            base.AddTask(task)
```

---

**最終更新**: 2025-07-02  
**設計者**: Claude Code  
**レビュー**: SQLite3設計完了・F#統合実装準備完了
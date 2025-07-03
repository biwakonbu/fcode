module FCode.TaskAssignmentManager

open System
open System.Text.RegularExpressions
open FCode.Collaboration.CollaborationTypes

/// エージェント専門分野
type AgentSpecialization =
    | Development of languages: string list
    | Testing of types: string list
    | UXDesign of areas: string list
    | ProjectManagement of skills: string list

/// エージェント能力プロファイル
type AgentCapabilityProfile =
    { AgentId: string
      Specializations: AgentSpecialization list
      LoadCapacity: float
      CurrentLoad: float
      SuccessRate: float
      AverageTaskDuration: TimeSpan
      LastAssignedTask: DateTime option }

/// タスク分解結果
type TaskBreakdown =
    { OriginalInstruction: string
      ParsedTasks: ParsedTask list
      EstimatedComplexity: float
      RequiredSpecializations: AgentSpecialization list }

/// 解析されたタスク
and ParsedTask =
    { TaskId: string
      Title: string
      Description: string
      RequiredSpecialization: AgentSpecialization
      EstimatedDuration: TimeSpan
      Priority: TaskPriority
      Dependencies: string list }

/// 自然言語解析エンジン
type NaturalLanguageProcessor() =

    // 開発関連キーワード
    let developmentKeywords =
        [ ("implement|code|develop|build|create", [ "F#"; "C#"; ".NET" ])
          ("fix|bug|debug|error", [ "debugging"; "testing" ])
          ("refactor|optimize|improve", [ "refactoring"; "optimization" ])
          ("api|service|endpoint", [ "backend"; "API" ])
          ("ui|interface|frontend", [ "frontend"; "UI" ]) ]

    // QA・テスト関連キーワード
    let testingKeywords =
        [ ("test|testing|verify", [ "unit-testing"; "integration-testing" ])
          ("quality|qa|review", [ "code-review"; "quality-assurance" ])
          ("performance|load|stress", [ "performance-testing" ])
          ("security|vulnerability", [ "security-testing" ]) ]

    // UX関連キーワード
    let uxKeywords =
        [ ("design|layout|interface", [ "UI-design"; "layout" ])
          ("user|experience|usability", [ "UX-research"; "usability" ])
          ("accessibility|a11y", [ "accessibility" ])
          ("responsive|mobile", [ "responsive-design" ]) ]

    // PM関連キーワード
    let pmKeywords =
        [ ("plan|schedule|timeline", [ "project-planning" ])
          ("coordinate|manage|organize", [ "coordination"; "management" ])
          ("requirement|spec|document", [ "requirements"; "documentation" ])
          ("review|approve|decide", [ "decision-making"; "approval" ]) ]

    /// 自然言語からタスクを解析
    member this.ParseInstruction(instruction: string) : TaskBreakdown =
        let cleanInstruction = instruction.Trim().ToLower()

        let sentences =
            cleanInstruction.Split([| '.'; '!'; '?' |], StringSplitOptions.RemoveEmptyEntries)

        let parsedTasks =
            sentences
            |> Array.mapi (fun i sentence -> this.ParseSentenceToTask(sentence.Trim(), i))
            |> Array.filter (fun task -> not (String.IsNullOrWhiteSpace(task.Title)))
            |> Array.toList

        let complexity = this.EstimateComplexity(cleanInstruction, parsedTasks)
        let requiredSpecs = this.ExtractRequiredSpecializations(cleanInstruction)

        { OriginalInstruction = instruction
          ParsedTasks = parsedTasks
          EstimatedComplexity = complexity
          RequiredSpecializations = requiredSpecs }

    /// 文章を個別タスクに解析
    member this.ParseSentenceToTask(sentence: string, index: int) : ParsedTask =
        let taskId = $"task_{DateTime.UtcNow.Ticks}_{index}"
        let specialization = this.DetectSpecialization(sentence)
        let priority = this.DetectPriority(sentence)
        let duration = this.EstimateDuration(sentence, specialization)

        { TaskId = taskId
          Title = this.ExtractTitle(sentence)
          Description = sentence
          RequiredSpecialization = specialization
          EstimatedDuration = duration
          Priority = priority
          Dependencies = [] }

    /// 専門分野を検出
    member this.DetectSpecialization(text: string) : AgentSpecialization =
        let checkKeywords keywords defaultSpec =
            keywords
            |> List.tryFind (fun (pattern, skills) -> Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            |> Option.map (fun (_, skills) -> defaultSpec skills)
            |> Option.defaultValue (defaultSpec [])

        if
            developmentKeywords
            |> List.exists (fun (pattern, _) -> Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
        then
            checkKeywords developmentKeywords Development
        elif
            testingKeywords
            |> List.exists (fun (pattern, _) -> Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
        then
            checkKeywords testingKeywords Testing
        elif
            uxKeywords
            |> List.exists (fun (pattern, _) -> Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
        then
            checkKeywords uxKeywords UXDesign
        elif
            pmKeywords
            |> List.exists (fun (pattern, _) -> Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
        then
            checkKeywords pmKeywords ProjectManagement
        else
            Development [ "general" ]

    /// 優先度を検出
    member this.DetectPriority(text: string) : TaskPriority =
        if Regex.IsMatch(text, @"\b(urgent|critical|emergency|asap)\b", RegexOptions.IgnoreCase) then
            TaskPriority.Critical
        elif Regex.IsMatch(text, @"\b(important|high|priority)\b", RegexOptions.IgnoreCase) then
            TaskPriority.High
        elif Regex.IsMatch(text, @"\b(low|minor|later)\b", RegexOptions.IgnoreCase) then
            TaskPriority.Low
        else
            TaskPriority.Medium

    /// 作業時間を推定
    member this.EstimateDuration(text: string, specialization: AgentSpecialization) : TimeSpan =
        let baseHours =
            match specialization with
            | Development _ -> 4.0
            | Testing _ -> 2.0
            | UXDesign _ -> 3.0
            | ProjectManagement _ -> 1.0

        let complexityMultiplier =
            if Regex.IsMatch(text, @"\b(complex|difficult|advanced|comprehensive)\b", RegexOptions.IgnoreCase) then
                2.0
            elif Regex.IsMatch(text, @"\b(simple|easy|basic|quick)\b", RegexOptions.IgnoreCase) then
                0.5
            else
                1.0

        TimeSpan.FromHours(baseHours * complexityMultiplier)

    /// タイトルを抽出
    member this.ExtractTitle(sentence: string) : string =
        let words = sentence.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)

        if words.Length <= 6 then
            String.Join(" ", words)
        else
            String.Join(" ", words.[0..5]) + "..."

    /// 複雑度を推定
    member this.EstimateComplexity(instruction: string, tasks: ParsedTask list) : float =
        let baseComplexity = float tasks.Length * 0.3

        let durationComplexity =
            tasks |> List.sumBy (fun t -> t.EstimatedDuration.TotalHours) |> (*) 0.1

        let keywordComplexity =
            if Regex.IsMatch(instruction, @"\b(integrate|complex|system|architecture)\b", RegexOptions.IgnoreCase) then
                0.5
            else
                0.0

        Math.Min(1.0, baseComplexity + durationComplexity + keywordComplexity)

    /// 必要な専門分野を抽出
    member this.ExtractRequiredSpecializations(instruction: string) : AgentSpecialization list =
        [ if
              developmentKeywords
              |> List.exists (fun (pattern, _) -> Regex.IsMatch(instruction, pattern, RegexOptions.IgnoreCase))
          then
              yield Development [ "F#"; ".NET" ]

          if
              testingKeywords
              |> List.exists (fun (pattern, _) -> Regex.IsMatch(instruction, pattern, RegexOptions.IgnoreCase))
          then
              yield Testing [ "unit-testing"; "integration-testing" ]

          if
              uxKeywords
              |> List.exists (fun (pattern, _) -> Regex.IsMatch(instruction, pattern, RegexOptions.IgnoreCase))
          then
              yield UXDesign [ "UI-design"; "UX-research" ]

          if
              pmKeywords
              |> List.exists (fun (pattern, _) -> Regex.IsMatch(instruction, pattern, RegexOptions.IgnoreCase))
          then
              yield ProjectManagement [ "coordination"; "planning" ] ]

/// エージェント専門性マッチングエンジン
type AgentSpecializationMatcher() =

    /// エージェントとタスクの適合度を計算
    member this.CalculateMatchScore(agent: AgentCapabilityProfile, task: ParsedTask) : float =
        let specializationScore =
            this.CalculateSpecializationScore(agent.Specializations, task.RequiredSpecialization)

        let loadScore = this.CalculateLoadScore(agent.LoadCapacity, agent.CurrentLoad)
        let performanceScore = agent.SuccessRate
        let availabilityScore = this.CalculateAvailabilityScore(agent.LastAssignedTask)

        // 重み付き平均
        (specializationScore * 0.4)
        + (loadScore * 0.3)
        + (performanceScore * 0.2)
        + (availabilityScore * 0.1)

    /// 専門分野適合度を計算
    member this.CalculateSpecializationScore
        (agentSpecs: AgentSpecialization list, requiredSpec: AgentSpecialization)
        : float =
        let hasMatchingSpec =
            agentSpecs
            |> List.exists (fun spec ->
                match (spec, requiredSpec) with
                | (Development agentLangs, Development reqLangs) ->
                    reqLangs |> List.exists (fun req -> agentLangs |> List.contains req)
                | (Testing agentTypes, Testing reqTypes) ->
                    reqTypes |> List.exists (fun req -> agentTypes |> List.contains req)
                | (UXDesign agentAreas, UXDesign reqAreas) ->
                    reqAreas |> List.exists (fun req -> agentAreas |> List.contains req)
                | (ProjectManagement agentSkills, ProjectManagement reqSkills) ->
                    reqSkills |> List.exists (fun req -> agentSkills |> List.contains req)
                | _ -> false)

        if hasMatchingSpec then 1.0 else 0.3

    /// 負荷適合度を計算
    member this.CalculateLoadScore(capacity: float, currentLoad: float) : float =
        if currentLoad >= capacity then
            0.0
        else
            (capacity - currentLoad) / capacity

    /// 可用性スコアを計算
    member this.CalculateAvailabilityScore(lastAssigned: DateTime option) : float =
        match lastAssigned with
        | None -> 1.0
        | Some lastTime ->
            let timeSinceLastAssignment = DateTime.UtcNow - lastTime
            Math.Min(1.0, timeSinceLastAssignment.TotalHours / 24.0)

/// 動的再配分システム
type DynamicReassignmentSystem() =

    /// タスク再配分が必要かを判定
    member this.NeedsReassignment(task: TaskInfo, agent: AgentState) : bool =
        match (task.Status, agent.Status) with
        | (InProgress, Blocked) -> true
        | (InProgress, Error) -> true
        | (InProgress, _) when
            agent.Progress < 0.1
            && DateTime.UtcNow - agent.LastUpdate > TimeSpan.FromMinutes(30.0)
            ->
            true
        | _ -> false

    /// 再配分理由を取得
    member this.GetReassignmentReason(task: TaskInfo, agent: AgentState) : string =
        match (task.Status, agent.Status) with
        | (InProgress, Blocked) -> $"エージェント {agent.AgentId} がブロック状態"
        | (InProgress, Error) -> $"エージェント {agent.AgentId} でエラー発生"
        | (InProgress, _) when agent.Progress < 0.1 -> $"エージェント {agent.AgentId} の進捗停滞"
        | _ -> "不明な理由"

/// TaskAssignmentManagerメインクラス
type TaskAssignmentManager
    (nlp: NaturalLanguageProcessor, matcher: AgentSpecializationMatcher, reassignmentSystem: DynamicReassignmentSystem)
    =

    let mutable agentProfiles: Map<string, AgentCapabilityProfile> = Map.empty

    /// エージェントプロファイルを登録
    member this.RegisterAgent(profile: AgentCapabilityProfile) : unit =
        agentProfiles <- agentProfiles.Add(profile.AgentId, profile)

    /// PO指示をタスクに分解して配分
    member this.ProcessInstructionAndAssign(instruction: string) : Result<(ParsedTask * string) list, string> =
        try
            let breakdown = nlp.ParseInstruction(instruction)

            let assignments =
                breakdown.ParsedTasks
                |> List.map (fun task ->
                    let bestAgent = this.FindBestAgent(task)

                    match bestAgent with
                    | Some agentId ->
                        this.UpdateAgentLoad(agentId, task)
                        (task, agentId)
                    | None -> (task, "unassigned"))

            Result.Ok assignments
        with ex ->
            Result.Error $"指示処理エラー: {ex.Message}"

    /// タスクに最適なエージェントを見つける
    member this.FindBestAgent(task: ParsedTask) : string option =
        agentProfiles
        |> Map.toList
        |> List.map (fun (agentId, profile) -> (agentId, matcher.CalculateMatchScore(profile, task)))
        |> List.filter (fun (_, score) -> score > 0.5)
        |> List.sortByDescending snd
        |> List.tryHead
        |> Option.map fst

    /// エージェントの負荷を更新
    member this.UpdateAgentLoad(agentId: string, task: ParsedTask) : unit =
        match agentProfiles.TryFind(agentId) with
        | Some profile ->
            let newLoad = profile.CurrentLoad + (task.EstimatedDuration.TotalHours / 8.0)

            let updatedProfile =
                { profile with
                    CurrentLoad = newLoad
                    LastAssignedTask = Some DateTime.UtcNow }

            agentProfiles <- agentProfiles.Add(agentId, updatedProfile)
        | None -> ()

    /// タスク再配分のチェック
    member this.CheckForReassignment(tasks: TaskInfo list, agents: AgentState list) : (TaskInfo * string) list =
        let agentMap = agents |> List.map (fun a -> a.AgentId, a) |> Map.ofList

        tasks
        |> List.filter (fun task ->
            match task.AssignedAgent with
            | Some agentId ->
                match agentMap.TryFind(agentId) with
                | Some agent -> reassignmentSystem.NeedsReassignment(task, agent)
                | None -> true
            | None -> false)
        |> List.choose (fun task ->
            let newTaskForReassignment =
                { TaskId = task.TaskId
                  Title = task.Title
                  Description = task.Description
                  RequiredSpecialization = Development [ "general" ] // 簡略化
                  EstimatedDuration = task.EstimatedDuration |> Option.defaultValue (TimeSpan.FromHours(2.0))
                  Priority = task.Priority
                  Dependencies = [] }

            match this.FindBestAgent(newTaskForReassignment) with
            | Some newAgentId -> Some(task, newAgentId)
            | None -> None)

    /// エージェント状況レポート取得
    member this.GetAgentStatusReport() : string =
        agentProfiles
        |> Map.toList
        |> List.map (fun (agentId, profile) ->
            $"Agent {agentId}: Load {profile.CurrentLoad:F1}/{profile.LoadCapacity:F1}, Success Rate {profile.SuccessRate:P}")
        |> String.concat "\n"

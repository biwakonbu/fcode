module FCode.Collaboration.TimeCalculationManager

open System
open System.Collections.Concurrent
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.ITimeCalculationManager
open FCode.Logger

/// 時間計算管理実装
type TimeCalculationManager(config: VirtualTimeConfig) =

    let sprintContexts = ConcurrentDictionary<string, VirtualTimeContext>()

    /// 実経過時間から仮想時間計算
    member this.CalculateVirtualTime(realElapsed: TimeSpan) =
        let totalMinutes = int realElapsed.TotalMinutes
        let virtualHours = totalMinutes // 1vh = 1分リアルタイム

        // スプリント完了判定 (18分 = 3vd)
        let sprintTotalMinutes = config.SprintDurationVD * 6

        if virtualHours >= sprintTotalMinutes then
            VirtualSprint(virtualHours / sprintTotalMinutes)
        else if
            // 6分未満は時間単位、6分以上は日単位で表現
            virtualHours >= 6
        then
            VirtualDay(virtualHours / 6)
        else
            VirtualHour virtualHours

    /// 仮想時間から実時間計算
    member this.CalculateRealDuration(virtualTime: VirtualTimeUnit) =
        match virtualTime with
        | VirtualHour hours -> TimeSpan.FromMinutes(float hours)
        | VirtualDay days -> TimeSpan.FromMinutes(float (days * 6))
        | VirtualSprint sprints -> TimeSpan.FromMinutes(float (sprints * config.SprintDurationVD * 6))

    /// 現在の仮想時間取得
    member this.GetCurrentVirtualTime(sprintId: string) =
        async {
            try
                match sprintContexts.TryGetValue(sprintId) with
                | true, context ->
                    let realElapsed = DateTime.UtcNow - context.StartTime
                    let virtualTime = this.CalculateVirtualTime(realElapsed)

                    logInfo "TimeCalculationManager"
                    <| sprintf "仮想時間取得: %s = %A" sprintId virtualTime

                    return Result.Ok virtualTime
                | false, _ ->
                    logError "TimeCalculationManager" <| sprintf "スプリントコンテキスト未発見: %s" sprintId
                    return Result.Error(NotFound $"スプリント {sprintId} のコンテキストが見つかりません")
            with ex ->
                logError "TimeCalculationManager" <| sprintf "仮想時間取得例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 仮想時間コンテキスト更新
    member this.UpdateVirtualTimeContext(sprintId: string) =
        async {
            try
                match sprintContexts.TryGetValue(sprintId) with
                | true, context ->
                    let realElapsed = DateTime.UtcNow - context.StartTime
                    let virtualTime = this.CalculateVirtualTime(realElapsed)

                    let updatedContext =
                        { context with
                            CurrentVirtualTime = virtualTime
                            ElapsedRealTime = realElapsed
                            LastUpdate = DateTime.UtcNow }

                    sprintContexts.[sprintId] <- updatedContext
                    logInfo "TimeCalculationManager" <| sprintf "仮想時間コンテキスト更新: %s" sprintId
                    return Result.Ok updatedContext
                | false, _ ->
                    logError "TimeCalculationManager" <| sprintf "スプリントコンテキスト未発見: %s" sprintId
                    return Result.Error(NotFound $"スプリント {sprintId} のコンテキストが見つかりません")
            with ex ->
                logError "TimeCalculationManager" <| sprintf "コンテキスト更新例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スプリント時間コンテキスト作成
    member this.CreateSprintContext(sprintId: string, config: VirtualTimeConfig) =
        let context =
            { StartTime = DateTime.UtcNow
              CurrentVirtualTime = VirtualHour 0
              ElapsedRealTime = TimeSpan.Zero
              SprintDuration = TimeSpan.FromMinutes(float (config.SprintDurationVD * 6))
              IsActive = true
              LastUpdate = DateTime.UtcNow }

        sprintContexts.[sprintId] <- context
        logInfo "TimeCalculationManager" <| sprintf "スプリントコンテキスト作成: %s" sprintId
        context

    /// スプリント経過時間計算
    member this.CalculateSprintProgress(sprintId: string) =
        async {
            try
                match sprintContexts.TryGetValue(sprintId) with
                | true, context ->
                    let elapsed = DateTime.UtcNow - context.StartTime
                    let totalDuration = context.SprintDuration
                    let progress = elapsed.TotalMinutes / totalDuration.TotalMinutes
                    let normalizedProgress = min 1.0 (max 0.0 progress)

                    logInfo "TimeCalculationManager"
                    <| sprintf "スプリント進捗: %s = %.1f%%" sprintId (normalizedProgress * 100.0)

                    return Result.Ok normalizedProgress
                | false, _ ->
                    logError "TimeCalculationManager" <| sprintf "スプリントコンテキスト未発見: %s" sprintId
                    return Result.Error(NotFound $"スプリント {sprintId} のコンテキストが見つかりません")
            with ex ->
                logError "TimeCalculationManager" <| sprintf "進捗計算例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// スプリント残り時間計算
    member this.CalculateRemainingTime(sprintId: string) =
        async {
            try
                match sprintContexts.TryGetValue(sprintId) with
                | true, context ->
                    let elapsed = DateTime.UtcNow - context.StartTime
                    let remaining = context.SprintDuration - elapsed
                    let normalizedRemaining = max TimeSpan.Zero remaining

                    logInfo "TimeCalculationManager"
                    <| sprintf "残り時間: %s = %A" sprintId normalizedRemaining

                    return Result.Ok normalizedRemaining
                | false, _ ->
                    logError "TimeCalculationManager" <| sprintf "スプリントコンテキスト未発見: %s" sprintId
                    return Result.Error(NotFound $"スプリント {sprintId} のコンテキストが見つかりません")
            with ex ->
                logError "TimeCalculationManager" <| sprintf "残り時間計算例外: %s" ex.Message
                return Result.Error(SystemError ex.Message)
        }

    /// 仮想時間単位をVirtualHour換算
    member this.ToVirtualHours(virtualTime: VirtualTimeUnit) =
        match virtualTime with
        | VirtualHour hours -> hours
        | VirtualDay days -> days * 6
        | VirtualSprint sprints -> sprints * config.SprintDurationVD * 6

    /// VirtualHourから仮想時間単位作成
    member this.FromVirtualHours(hours: int, config: VirtualTimeConfig) =
        let sprintTotalHours = config.SprintDurationVD * 6

        if hours >= sprintTotalHours then
            VirtualSprint(hours / sprintTotalHours)
        elif hours >= 6 then
            VirtualDay(hours / 6)
        else
            VirtualHour hours

    /// 時間単位の妥当性検証
    member this.ValidateTimeUnit(virtualTime: VirtualTimeUnit, config: VirtualTimeConfig) =
        let hours = this.ToVirtualHours(virtualTime)
        let maxHours = config.SprintDurationVD * 24 * config.MaxConcurrentSprints

        hours >= 0 && hours <= maxHours

    /// コンテキスト削除
    member this.RemoveSprintContext(sprintId: string) =
        match sprintContexts.TryRemove(sprintId) with
        | true, _ ->
            logInfo "TimeCalculationManager" <| sprintf "スプリントコンテキスト削除: %s" sprintId
            true
        | false, _ ->
            logWarning "TimeCalculationManager" <| sprintf "削除対象コンテキスト未発見: %s" sprintId
            false

    /// アクティブスプリント一覧取得
    member this.GetActiveSprintContexts() =
        sprintContexts.Values |> Seq.filter (fun ctx -> ctx.IsActive) |> Seq.toList

    interface ITimeCalculationManager with
        member this.CalculateVirtualTime(realElapsed) = this.CalculateVirtualTime(realElapsed)
        member this.CalculateRealDuration(virtualTime) = this.CalculateRealDuration(virtualTime)
        member this.GetCurrentVirtualTime(sprintId) = this.GetCurrentVirtualTime(sprintId)
        member this.UpdateVirtualTimeContext(sprintId) = this.UpdateVirtualTimeContext(sprintId)

        member this.CreateSprintContext(sprintId, config) =
            this.CreateSprintContext(sprintId, config)

        member this.CalculateSprintProgress(sprintId) = this.CalculateSprintProgress(sprintId)
        member this.CalculateRemainingTime(sprintId) = this.CalculateRemainingTime(sprintId)
        member this.ToVirtualHours(virtualTime) = this.ToVirtualHours(virtualTime)
        member this.FromVirtualHours(hours, config) = this.FromVirtualHours(hours, config)

        member this.ValidateTimeUnit(virtualTime, config) =
            this.ValidateTimeUnit(virtualTime, config)

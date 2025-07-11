/// 軽量メモリ監視システム - 型安全・最小実装
module FCode.SimpleMemoryMonitor

open System
open System.Diagnostics
open FCode.Logger

// ===============================================
// 軽量メモリ監視設定
// ===============================================

/// 軽量メモリ監視設定
type SimpleMemoryConfig =
    { WarningThresholdMB: int64
      MaxMemoryMB: int64
      CheckIntervalMinutes: int }

/// メモリ制限定数
[<Literal>]
let private WARNING_THRESHOLD_MB = 400L

[<Literal>]
let private MAX_MEMORY_MB = 500L

[<Literal>]
let private CHECK_INTERVAL_MINUTES = 10

/// デフォルト設定（実用的な値）
let defaultMemoryConfig =
    { WarningThresholdMB = WARNING_THRESHOLD_MB // 400MB で警告
      MaxMemoryMB = MAX_MEMORY_MB // 500MB で制限
      CheckIntervalMinutes = CHECK_INTERVAL_MINUTES } // 10分間隔でチェック

// ===============================================
// メモリ監視機能
// ===============================================

/// 軽量メモリ監視クラス
type SimpleMemoryMonitor(config: SimpleMemoryConfig) =
    let mutable lastCheckTime = DateTime.MinValue

    /// 現在のメモリ使用量を取得（MB）
    member public this.GetCurrentMemoryMB() : int64 =
        let currentProcess = Process.GetCurrentProcess()
        currentProcess.WorkingSet64 / (1024L * 1024L)

    /// メモリ使用量チェック
    member public this.CheckMemoryUsage() : string option =
        if this.ShouldCheckMemory() then
            let currentMemory = this.GetCurrentMemoryMB()
            this.EvaluateMemoryUsage(currentMemory)
        else
            None

    /// チェック実行判定
    member private this.ShouldCheckMemory() : bool =
        let now = DateTime.UtcNow
        let timeSinceLastCheck = now - lastCheckTime

        let shouldCheck =
            timeSinceLastCheck.TotalMinutes >= float config.CheckIntervalMinutes

        if shouldCheck then
            lastCheckTime <- now

        shouldCheck

    /// メモリ使用量評価
    member private this.EvaluateMemoryUsage(currentMemory: int64) : string option =
        if currentMemory >= config.MaxMemoryMB then
            let message = $"メモリ使用量が上限を超過: {currentMemory}MB >= {config.MaxMemoryMB}MB"
            logError "SimpleMemoryMonitor" message
            Some message
        elif currentMemory >= config.WarningThresholdMB then
            let message = $"メモリ使用量警告: {currentMemory}MB >= {config.WarningThresholdMB}MB"
            logWarning "SimpleMemoryMonitor" message
            Some message
        else
            logDebug "SimpleMemoryMonitor" $"メモリ使用量正常: {currentMemory}MB"
            None

    /// 軽量GC実行（条件付き）
    member public this.OptionalGC() : bool =
        let currentMemory = this.GetCurrentMemoryMB()

        if currentMemory >= config.WarningThresholdMB then
            try
                // 軽量GC実行（強制GCは避ける）
                GC.Collect(0, GCCollectionMode.Optimized)
                let afterGC = this.GetCurrentMemoryMB()
                let memoryFreed = currentMemory - afterGC

                if memoryFreed > 0L then
                    logInfo "SimpleMemoryMonitor" $"軽量GC実行: {currentMemory}MB -> {afterGC}MB (解放: {memoryFreed}MB)"
                    true
                else
                    logDebug "SimpleMemoryMonitor" $"軽量GC実行: メモリ解放なし ({currentMemory}MB)"
                    false
            with
            | :? OutOfMemoryException as ex ->
                logError "SimpleMemoryMonitor" $"軽量GC実行エラー（OutOfMemory）: {ex.Message}"
                false
            | ex ->
                logError "SimpleMemoryMonitor" $"軽量GC実行エラー: {ex.Message}"
                false
        else
            false

    /// メモリ状態レポート
    member public this.GetMemoryReport() : string =
        let currentMemory = this.GetCurrentMemoryMB()

        let warningLevel =
            if currentMemory >= config.MaxMemoryMB then "危険"
            elif currentMemory >= config.WarningThresholdMB then "警告"
            else "正常"

        $"メモリ状態: {currentMemory}MB ({warningLevel}) - 警告閾値: {config.WarningThresholdMB}MB, 上限: {config.MaxMemoryMB}MB"

// ===============================================
// グローバルインスタンス
// ===============================================

/// グローバル軽量メモリ監視インスタンス
let globalMemoryMonitor = SimpleMemoryMonitor(defaultMemoryConfig)

/// 便利関数: メモリチェック実行
let checkMemoryUsage () = globalMemoryMonitor.CheckMemoryUsage()

/// 便利関数: メモリレポート取得
let getMemoryReport () =
    try
        globalMemoryMonitor.GetMemoryReport()
    with
    | :? OutOfMemoryException as ex -> $"メモリレポート取得エラー（OutOfMemory）: {ex.Message}"
    | ex -> $"メモリレポート取得エラー: {ex.Message}"

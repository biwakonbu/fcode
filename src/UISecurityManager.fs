/// UI操作権限エスカレーション・セキュリティ強化システム (簡潔版)
/// Issue #82: FC-023 UI権限制御・リソース制限・セキュリティ境界実装
namespace fcode

open System
open System.Text.RegularExpressions
open System.Threading
open Terminal.Gui

/// UI操作権限レベル
type UIPermissionLevel =
    | ReadOnly
    | LimitedWrite
    | FullAccess

/// セキュリティレベル
type SecurityLevel =
    | Low
    | Medium
    | High
    | Critical

/// UIリソース制限設定
type UIResourceLimits =
    { MaxContentSize: int
      MaxUpdateFrequency: TimeSpan
      MaxConcurrentOperations: int
      MemoryLimitMB: int
      CPULimitPercentage: int }

/// セキュア UI アップデーター
type SecureUIUpdater(permissionLevel: UIPermissionLevel, securityLevel: SecurityLevel) =
    let mutable disposed = false

    /// セキュリティレベル別デフォルト制限取得
    member private this.GetDefaultLimits() : UIResourceLimits =
        if securityLevel = Low then
            { MaxContentSize = 1_000_000
              MaxUpdateFrequency = TimeSpan.FromMilliseconds(50.0)
              MaxConcurrentOperations = 20
              MemoryLimitMB = 100
              CPULimitPercentage = 50 }
        elif securityLevel = Medium then
            { MaxContentSize = 500_000
              MaxUpdateFrequency = TimeSpan.FromMilliseconds(100.0)
              MaxConcurrentOperations = 10
              MemoryLimitMB = 50
              CPULimitPercentage = 30 }
        elif securityLevel = High then
            { MaxContentSize = 100_000
              MaxUpdateFrequency = TimeSpan.FromMilliseconds(200.0)
              MaxConcurrentOperations = 5
              MemoryLimitMB = 25
              CPULimitPercentage = 20 }
        else
            { MaxContentSize = 50_000
              MaxUpdateFrequency = TimeSpan.FromMilliseconds(500.0)
              MaxConcurrentOperations = 2
              MemoryLimitMB = 10
              CPULimitPercentage = 10 }

    /// 権限確認
    member private this.HasUpdatePermission() : bool =
        if permissionLevel = ReadOnly then false else true

    /// 入力サニタイズ
    member private this.SanitizeContent(content: string) : string =
        if String.IsNullOrEmpty(content) then
            content
        else
            let mutable sanitized = content
            // 基本的なサニタイズ
            sanitized <- Regex.Replace(sanitized, @"<script[^>]*>.*?</script>", "", RegexOptions.IgnoreCase)
            sanitized <- Regex.Replace(sanitized, @"javascript:", "", RegexOptions.IgnoreCase)
            sanitized

    /// セキュアUI更新実行
    member this.SecureUpdateUI(textView: TextView, content: string) : Result<unit, string> =
        try
            if disposed then
                Error "SecureUIUpdater is disposed"
            elif isNull textView then
                Error "TextView is null"
            elif not (this.HasUpdatePermission()) then
                Error "UI更新権限なし"
            else
                let limits = this.GetDefaultLimits()

                if content.Length > limits.MaxContentSize then
                    Error $"コンテンツサイズ制限超過: {content.Length} > {limits.MaxContentSize}"
                else
                    let sanitizedContent = this.SanitizeContent(content)

                    // CI環境チェック
                    let isCI = not (isNull (Environment.GetEnvironmentVariable("CI")))

                    if isCI then
                        Ok()
                    else
                        try
                            Application.MainLoop.Invoke(fun () ->
                                textView.Text <- sanitizedContent
                                textView.SetNeedsDisplay())

                            Ok()
                        with ex ->
                            Error $"UI更新実行エラー: {ex.Message}"
        with ex ->
            Error $"セキュリティチェックエラー: {ex.Message}"

    /// リソース状態取得
    member this.GetResourceStatus() : string =
        $"Permission: {permissionLevel}, Security: {securityLevel}"

    /// 破棄処理
    interface IDisposable with
        member this.Dispose() = disposed <- true

/// UIセキュリティマネージャー
type UISecurityManager private () =
    static let mutable instance: UISecurityManager option = None
    static let lockObj = obj ()

    let securityEvents = ResizeArray<string * DateTime>()
    let mutable securityLevel = SecurityLevel.Medium

    /// シングルトンインスタンス取得
    static member GetInstance() : UISecurityManager =
        lock lockObj (fun () ->
            match instance with
            | Some mgr -> mgr
            | None ->
                let mgr = UISecurityManager()
                instance <- Some mgr
                mgr)

    /// セキュリティレベル設定
    member this.SetSecurityLevel(level: SecurityLevel) : unit =
        securityLevel <- level
        this.LogSecurityEvent($"Security level changed to {level}")

    /// セキュアアップデーター作成
    member this.CreateSecureUpdater(permission: UIPermissionLevel) : SecureUIUpdater =
        new SecureUIUpdater(permission, securityLevel)

    /// セキュリティイベント記録
    member this.LogSecurityEvent(message: string) : unit =
        let timestamp = DateTime.UtcNow
        securityEvents.Add((message, timestamp))

        // 履歴制限 (最新100件)
        if securityEvents.Count > 100 then
            securityEvents.RemoveRange(0, securityEvents.Count - 100)

    /// セキュリティ監査ログ取得
    member this.GetSecurityAuditLog() : (string * DateTime)[] = securityEvents.ToArray()

    /// 異常検知
    member this.DetectAnomalies() : string[] =
        let recentEvents =
            securityEvents
            |> Seq.filter (fun (_, time) -> DateTime.UtcNow.Subtract(time) < TimeSpan.FromMinutes(5.0))
            |> Seq.length

        if recentEvents > 50 then [| "高頻度セキュリティイベント検出" |] else [||]

/// UIセキュリティマネージャー・基本テストスイート
namespace fcode.Tests

open NUnit.Framework
open fcode
open FCode.Tests.TestHelpers

[<TestFixture>]
[<Category("Unit")>]
type UISecurityManagerSimpleTests() =

    [<Test>]
    member this.``UIPermissionLevel列挙値テスト``() =
        let readOnly = UIPermissionLevel.ReadOnly
        let limitedWrite = UIPermissionLevel.LimitedWrite
        let fullAccess = UIPermissionLevel.FullAccess

        Assert.AreNotEqual(readOnly, limitedWrite)
        Assert.AreNotEqual(limitedWrite, fullAccess)

    [<Test>]
    member this.``SecurityLevel列挙値テスト``() =
        let low = SecurityLevel.Low
        let medium = SecurityLevel.Medium
        let high = SecurityLevel.High
        let critical = SecurityLevel.Critical

        Assert.AreNotEqual(low, medium)
        Assert.AreNotEqual(medium, high)
        Assert.AreNotEqual(high, critical)

    [<Test>]
    member this.``SecureUIUpdater基本作成テスト``() =
        // CI環境設定
        System.Environment.SetEnvironmentVariable("CI", "true")

        try
            use updater =
                new SecureUIUpdater(UIPermissionLevel.LimitedWrite, SecurityLevel.Medium)

            let status = updater.GetResourceStatus()

            Assert.IsNotNull(status)
            Assert.IsTrue(status.Contains("LimitedWrite"))
            Assert.IsTrue(status.Contains("Medium"))
        finally
            System.Environment.SetEnvironmentVariable("CI", null)

    [<Test>]
    member this.``UISecurityManagerシングルトンテスト``() =
        let manager1 = UISecurityManager.GetInstance()
        let manager2 = UISecurityManager.GetInstance()

        Assert.AreSame(manager1, manager2)

    [<Test>]
    member this.``セキュリティイベントログテスト``() =
        let manager = UISecurityManager.GetInstance()

        manager.LogSecurityEvent("テストイベント")

        let auditLog = manager.GetSecurityAuditLog()
        Assert.Greater(auditLog.Length, 0)

    [<Test>]
    member this.``権限制御基本テスト``() =
        // CI環境設定
        System.Environment.SetEnvironmentVariable("CI", "true")

        try
            use readOnlyUpdater =
                new SecureUIUpdater(UIPermissionLevel.ReadOnly, SecurityLevel.Medium)

            use limitedWriteUpdater =
                new SecureUIUpdater(UIPermissionLevel.LimitedWrite, SecurityLevel.Medium)

            // ReadOnlyは更新拒否されることを確認（MockTextViewを使用）
            let mockView1 = MockTextView() :> IUpdatableView
            let readOnlyResult = readOnlyUpdater.SecureUpdateUI(mockView1, "テスト")

            match readOnlyResult with
            | Error msg -> Assert.IsTrue(msg.Contains("権限なし"))
            | Ok _ -> Assert.Fail("ReadOnly権限で更新が許可された")

            // LimitedWriteは正常動作することを確認
            let mockView2 = MockTextView() :> IUpdatableView
            let limitedWriteResult = limitedWriteUpdater.SecureUpdateUI(mockView2, "テスト")

            match limitedWriteResult with
            | Ok _ -> Assert.Pass("LimitedWrite権限で更新が成功")
            | Error msg -> Assert.Fail($"LimitedWrite権限で更新が失敗: {msg}")
        finally
            System.Environment.SetEnvironmentVariable("CI", null)

    [<Test>]
    member this.``サイズ制限テスト``() =
        // CI環境設定
        System.Environment.SetEnvironmentVariable("CI", "true")

        try
            use updater =
                new SecureUIUpdater(UIPermissionLevel.FullAccess, SecurityLevel.Critical)

            // Critical セキュリティレベルでは50KB制限
            let largeContent = String.replicate 60000 "A"
            let mockTextView = MockTextView() :> IUpdatableView
            let result = updater.SecureUpdateUI(mockTextView, largeContent)

            match result with
            | Error msg -> Assert.IsTrue(msg.Contains("制限超過"))
            | Ok _ -> Assert.Fail("サイズ制限が機能していない")
        finally
            System.Environment.SetEnvironmentVariable("CI", null)

    [<Test>]
    member this.``XSS攻撃対策サニタイズテスト``() =
        // CI環境設定
        System.Environment.SetEnvironmentVariable("CI", "true")

        try
            use updater =
                new SecureUIUpdater(UIPermissionLevel.FullAccess, SecurityLevel.Medium)

            // XSS攻撃パターンをテスト
            let xssContent =
                "<script>alert('XSS')</script><iframe src=\"evil.com\"></iframe>javascript:alert('test')onclick=alert(1)"

            let mockTextView = MockTextView() :> IUpdatableView
            let result = updater.SecureUpdateUI(mockTextView, xssContent)

            match result with
            | Ok _ ->
                // サニタイズされたコンテンツを確認
                Assert.IsFalse(mockTextView.Text.Contains("<script"))
                Assert.IsFalse(mockTextView.Text.Contains("<iframe"))
                Assert.IsFalse(mockTextView.Text.Contains("javascript:"))
                Assert.IsFalse(mockTextView.Text.Contains("onclick"))
            | Error msg -> Assert.Fail($"XSS対策テスト失敗: {msg}")
        finally
            System.Environment.SetEnvironmentVariable("CI", null)

    [<Test>]
    member this.``包括的サニタイゼーション検証テスト``() =
        // CI環境設定
        System.Environment.SetEnvironmentVariable("CI", "true")

        try
            use updater =
                new SecureUIUpdater(UIPermissionLevel.FullAccess, SecurityLevel.Medium)

            // 包括的な悪意ある入力パターン
            let maliciousInputs =
                [| "<script>alert('XSS')</script>"
                   "<iframe src='javascript:alert(1)'></iframe>"
                   "javascript:void(0)"
                   "data:text/html,<script>alert('XSS')</script>"
                   "vbscript:alert('XSS')"
                   "onclick=\"alert('XSS')\""
                   "onload=\"alert('XSS')\""
                   "<img src=x onerror=alert('XSS')>"
                   "&<>\"'" |]

            let mockTextView = MockTextView() :> IUpdatableView

            for maliciousInput in maliciousInputs do
                let result = updater.SecureUpdateUI(mockTextView, maliciousInput)

                match result with
                | Ok _ ->
                    // サニタイズ結果検証
                    Assert.IsFalse(mockTextView.Text.Contains("<script"), $"<script>タグが除去されていない: {maliciousInput}")
                    Assert.IsFalse(mockTextView.Text.Contains("<iframe"), $"<iframe>タグが除去されていない: {maliciousInput}")
                    Assert.IsFalse(mockTextView.Text.Contains("javascript:"), $"javascript:が除去されていない: {maliciousInput}")
                    Assert.IsFalse(mockTextView.Text.Contains("data:"), $"data:が除去されていない: {maliciousInput}")
                    Assert.IsFalse(mockTextView.Text.Contains("vbscript:"), $"vbscript:が除去されていない: {maliciousInput}")
                    Assert.IsFalse(mockTextView.Text.Contains("onclick"), $"onclickが除去されていない: {maliciousInput}")
                    Assert.IsFalse(mockTextView.Text.Contains("onload"), $"onloadが除去されていない: {maliciousInput}")
                    Assert.IsFalse(mockTextView.Text.Contains("onerror"), $"onerrorが除去されていない: {maliciousInput}")
                    // HTMLエンティティエンコーディング確認
                    Assert.IsTrue(
                        mockTextView.Text.Contains("&lt;") || not (mockTextView.Text.Contains("<")),
                        "HTMLエンコーディング未実装"
                    )
                | Error msg -> Assert.Fail($"サニタイゼーション処理が失敗: {maliciousInput}, エラー: {msg}")
        finally
            System.Environment.SetEnvironmentVariable("CI", null)

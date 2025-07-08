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

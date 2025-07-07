namespace FCode.Tests

open NUnit.Framework
open System
open System.IO

[<TestFixture>]
[<Category("Unit")>]
type SimpleSecurityTests() =

    [<Test>]
    [<Category("Unit")>]
    member this.``機密情報除去基本テスト``() =
        // 機密情報を含むメッセージのテスト
        let testMessage = "API Key: sk-1234567890abcdef and password: secret123"
        let sanitized = FCode.SecurityUtils.sanitizeLogMessage testMessage

        // API Keyが除去されていることを確認
        Assert.IsFalse(sanitized.Contains("sk-"), "API Keyが除去されていません")

        // デバッグ用に実際の結果を出力
        printfn $"Original: {testMessage}"
        printfn $"Sanitized: {sanitized}"

        // パスワード情報が除去されていることを確認
        Assert.IsFalse(sanitized.Contains("secret123"), "パスワードが除去されていません")

    [<Test>]
    [<Category("Unit")>]
    member this.``Logger例外安全化テスト``() =
        // 機密情報を含む例外を作成
        let sensitiveException =
            new ArgumentException("Invalid API key: sk-1234567890abcdef")

        // Logger.Exception メソッドのテスト
        let testLogger = FCode.Logger.Logger()
        testLogger.Exception("TestCategory", "Test operation with sensitive data", sensitiveException)

        // ログファイルを読み取り、機密情報が除去されていることを確認
        let logContent = File.ReadAllText(testLogger.LogPath)

        // API Keyが除去されていることを確認
        Assert.IsFalse(logContent.Contains("sk-1234567890abcdef"), "ログファイル内にAPI Keyが残存しています")

        // 例外の型情報は保持されていることを確認
        Assert.IsTrue(logContent.Contains("ArgumentException"), "例外の型情報が失われています")

    [<Test>]
    [<Category("Unit")>]
    member this.``環境変数フィルタリング基本テスト``() =
        // 機密情報を含む環境変数のマップ
        let sensitiveEnv =
            Map.ofList [ ("API_KEY", "sk-1234567890abcdef"); ("NORMAL_VAR", "safe-value") ]

        let filteredEnv = FCode.SecurityUtils.filterSensitiveEnvironment sensitiveEnv

        // 機密情報が除去されていることを確認
        Assert.IsFalse(filteredEnv.ContainsKey("API_KEY"), "API_KEYが除去されていません")

        // 安全な環境変数は保持されていることを確認
        Assert.IsTrue(filteredEnv.ContainsKey("NORMAL_VAR"), "安全な環境変数が削除されています")

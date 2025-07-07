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
        // 機密情報を含むメッセージのテスト（テスト用のフェイクキー）
        let testMessage = "API Key: sk-test12345678901234fake and password: secret123"
        let sanitized = FCode.SecurityUtils.sanitizeLogMessage testMessage

        // API Keyが除去されていることを確認
        Assert.IsFalse(sanitized.Contains("sk-"), "API Keyが除去されていません")

        // パスワード情報が除去されていることを確認
        Assert.IsFalse(sanitized.Contains("secret123"), "パスワードが除去されていません")

    [<Test>]
    [<Category("Unit")>]
    member this.``Logger例外安全化テスト``() =
        // 機密情報を含む例外を作成（テスト用のフェイクキー）
        let sensitiveException =
            new ArgumentException("Invalid API key: sk-test12345678901234fake")

        // Logger.Exception メソッドのテスト
        let testLogger = FCode.Logger.Logger()
        testLogger.Exception("TestCategory", "Test operation with sensitive data", sensitiveException)

        // ログファイルを読み取り、機密情報が除去されていることを確認
        let logContent = File.ReadAllText(testLogger.LogPath)

        // API Keyが除去されていることを確認
        Assert.IsFalse(logContent.Contains("sk-test12345678901234fake"), "ログファイル内にAPI Keyが残存しています")

        // 例外の型情報は保持されていることを確認
        Assert.IsTrue(logContent.Contains("ArgumentException"), "例外の型情報が失われています")

    [<Test>]
    [<Category("Unit")>]
    member this.``環境変数フィルタリング基本テスト``() =
        // 機密情報を含む環境変数のマップ
        let sensitiveEnv =
            Map.ofList [ ("API_KEY", "sk-test12345678901234fake"); ("NORMAL_VAR", "safe-value") ]

        let filteredEnv = FCode.SecurityUtils.filterSensitiveEnvironment sensitiveEnv

        // 機密情報が除去されていることを確認
        Assert.IsFalse(filteredEnv.ContainsKey("API_KEY"), "API_KEYが除去されていません")

        // 安全な環境変数は保持されていることを確認
        Assert.IsTrue(filteredEnv.ContainsKey("NORMAL_VAR"), "安全な環境変数が削除されています")

    [<Test>]
    [<Category("Unit")>]
    member this.``環境変数フィルタリング包括テスト``() =
        // 包括的な環境変数テストセット
        let testEnv =
            Map.ofList
                [ ("API_KEY", "sk-test12345678901234fake")
                  ("APIKEY", "test-key")
                  ("SECRET_TOKEN", "secret-value")
                  ("PASSWORD", "test-password")
                  ("JWT_TOKEN", "jwt-token")
                  ("DATABASE_URL", "postgresql://user:pass@host:5432/db")
                  ("CONNECTION_STRING", "Server=localhost;Database=test")
                  ("NORMAL_VAR", "safe-value")
                  ("DEBUG_MODE", "true")
                  ("PORT", "8080")
                  ("AUTH_SERVICE", "auth-service-url") // AUTH を含むが正当な変数
                  ("AUTHORIZATION_HEADER", "Bearer token") // AUTH を含むが正当な変数
                  ("OAUTH_CLIENT_ID", "oauth-client-id") // AUTH を含むが正当な変数
                  ("AUTH", "auth-value") // AUTH のみ（フィルタ対象）
                  ("MY_AUTH", "my-auth-value") // _AUTH 形式（フィルタ対象）
                  ("TEST_AUTH_FLAG", "true") ] // _AUTH_ 形式（フィルタ対象）

        let filteredEnv = FCode.SecurityUtils.filterSensitiveEnvironment testEnv

        // 機密情報が除去されていることを確認
        Assert.IsFalse(filteredEnv.ContainsKey("API_KEY"), "API_KEYが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("APIKEY"), "APIKEYが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("SECRET_TOKEN"), "SECRET_TOKENが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("PASSWORD"), "PASSWORDが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("JWT_TOKEN"), "JWT_TOKENが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("DATABASE_URL"), "DATABASE_URLが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("CONNECTION_STRING"), "CONNECTION_STRINGが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("AUTH"), "AUTHが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("MY_AUTH"), "MY_AUTHが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("TEST_AUTH_FLAG"), "TEST_AUTH_FLAGが除去されていません")

        // 安全な環境変数は保持されていることを確認
        Assert.IsTrue(filteredEnv.ContainsKey("NORMAL_VAR"), "NORMAL_VARが削除されています")
        Assert.IsTrue(filteredEnv.ContainsKey("DEBUG_MODE"), "DEBUG_MODEが削除されています")
        Assert.IsTrue(filteredEnv.ContainsKey("PORT"), "PORTが削除されています")

        // 誤検出防止の確認（AUTHを含むが正当な変数）
        Assert.IsTrue(filteredEnv.ContainsKey("AUTH_SERVICE"), "AUTH_SERVICEが誤って削除されています")
        Assert.IsTrue(filteredEnv.ContainsKey("AUTHORIZATION_HEADER"), "AUTHORIZATION_HEADERが誤って削除されています")
        Assert.IsTrue(filteredEnv.ContainsKey("OAUTH_CLIENT_ID"), "OAUTH_CLIENT_IDが誤って削除されています")

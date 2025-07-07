namespace FCode.Tests

open NUnit.Framework
open System
open FCode.SecurityUtils

[<TestFixture>]
[<Category("Unit")>]
type SecurityUtilsFalsePositiveTests() =

    [<Test>]
    [<Category("Unit")>]
    member this.``正常なGit commit hashが削除されないことを確認``() =
        // 正常なGit commit hash（40文字16進数）- メッセージとハッシュの対応
        let testCases =
            [ ("Build completed: commit abc123def456789012345678901234567890", "abc123def456789012345678901234567890")
              ("Merge commit: a1b2c3d4e5f6789012345678901234567890abcd", "a1b2c3d4e5f6789012345678901234567890abcd")
              ("HEAD is at 1234567890abcdef1234567890abcdef12345678", "1234567890abcdef1234567890abcdef12345678")
              ("git log --oneline a1b2c3d4e5f6789012345678901234567890abcd..HEAD",
               "a1b2c3d4e5f6789012345678901234567890abcd") ]

        for (message, expectedHash) in testCases do
            let sanitized = sanitizeLogMessage message

            // 該当するCommit hashが保持されていることを確認
            Assert.IsTrue(sanitized.Contains(expectedHash), $"Git commit hash '{expectedHash}' が削除されました: {sanitized}")
            // 元のメッセージ構造が保持されていることを確認
            Assert.AreEqual(message, sanitized, $"メッセージが変更されました: {message} -> {sanitized}")

    [<Test>]
    [<Category("Unit")>]
    member this.``正常なDocker image IDが削除されないことを確認``() =
        // 正常なDocker image ID（64文字16進数）- メッセージとimage IDの対応
        let testCases =
            [ ("Image deployed: sha256:a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef12345678",
               "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef12345678")
              ("Container started with image a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef12345678",
               "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef12345678")
              ("docker pull nginx@sha256:b1c2d3e4f5a6789012345678901234567890abcdef1234567890abcdef12345678",
               "b1c2d3e4f5a6789012345678901234567890abcdef1234567890abcdef12345678") ]

        for (message, expectedImageID) in testCases do
            let sanitized = sanitizeLogMessage message

            // 該当するDocker image IDが保持されていることを確認
            Assert.IsTrue(
                sanitized.Contains(expectedImageID),
                $"Docker image ID '{expectedImageID}' が削除されました: {sanitized}"
            )
            // 元のメッセージ構造が保持されていることを確認
            Assert.AreEqual(message, sanitized, $"メッセージが変更されました: {message} -> {sanitized}")

    [<Test>]
    [<Category("Unit")>]
    member this.``正常なUUIDが削除されないことを確認``() =
        // 正常なUUID（32文字英数字、ハイフン区切り）- メッセージとUUIDの対応
        let testCases =
            [ ("Session ID: 550e8400-e29b-41d4-a716-446655440000", "550e8400-e29b-41d4-a716-446655440000")
              ("Request ID: f47ac10b-58cc-4372-a567-0e02b2c3d479", "f47ac10b-58cc-4372-a567-0e02b2c3d479")
              ("Correlation ID: 123e4567-e89b-12d3-a456-426614174000", "123e4567-e89b-12d3-a456-426614174000") ]

        for (message, expectedUUID) in testCases do
            let sanitized = sanitizeLogMessage message

            // 該当するUUIDが保持されていることを確認（ハイフンがあるため３２文字連続ではない）
            Assert.IsTrue(sanitized.Contains(expectedUUID), $"UUID '{expectedUUID}' が削除されました: {sanitized}")
            // 元のメッセージ構造が保持されていることを確認
            Assert.AreEqual(message, sanitized, $"メッセージが変更されました: {message} -> {sanitized}")

    [<Test>]
    [<Category("Unit")>]
    member this.``正常なBase64文字列が削除されないことを確認``() =
        // 正常なBase64エンコード文字列（32文字以上、"="パディング含む）- メッセージとBase64の対応
        let testCases =
            [ ("Certificate: LS0tLS1CRUdJTiBDRVJUSUZJQ0FURS0tLS0t", "LS0tLS1CRUdJTiBDRVJUSUZJQ0FURS0tLS0t")
              ("Data encoded: SGVsbG8gd29ybGQgdGhpcyBpcyBhIGxvbmcgc3RyaW5nIGZvciB0ZXN0aW5n",
               "SGVsbG8gd29ybGQgdGhpcyBpcyBhIGxvbmcgc3RyaW5nIGZvciB0ZXN0aW5n")
              ("Config: eyJuYW1lIjoidGVzdCIsInZlcnNpb24iOiIxLjAuMCJ9", "eyJuYW1lIjoidGVzdCIsInZlcnNpb24iOiIxLjAuMCJ9") ]

        for (message, expectedBase64) in testCases do
            let sanitized = sanitizeLogMessage message

            // 該当するBase64文字列が保持されていることを確認（"="パディングや"."を含むため32文字連続の英数字ではない）
            Assert.IsTrue(sanitized.Contains(expectedBase64), $"Base64文字列 '{expectedBase64}' が削除されました: {sanitized}")
            // 元のメッセージ構造が保持されていることを確認
            Assert.AreEqual(message, sanitized, $"メッセージが変更されました: {message} -> {sanitized}")

    [<Test>]
    [<Category("Unit")>]
    member this.``機密トークンは適切に削除されることを確認``() =
        // 機密トークン（実際に削除されるべきもの）
        let sensitiveMessages =
            [ "OpenAI API Key: sk-1234567890abcdefghijklmnopqrstuvwxyz123456"
              "GitHub Token: ghp_1234567890abcdefghijklmnopqrstuvwx"
              "AWS Access Key: AKIAIOSFODNN7EXAMPLE"
              "JWT Token: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c" ]

        for message in sensitiveMessages do
            let sanitized = sanitizeLogMessage message

            // 機密トークンが適切に削除されていることを確認
            Assert.IsFalse(
                sanitized.Contains("sk-1234567890abcdefghijklmnopqrstuvwxyz123456"),
                $"OpenAI API Keyが削除されていません: {sanitized}"
            )

            Assert.IsFalse(
                sanitized.Contains("ghp_1234567890abcdefghijklmnopqrstuvwx"),
                $"GitHub Tokenが削除されていません: {sanitized}"
            )

            Assert.IsFalse(sanitized.Contains("AKIAIOSFODNN7EXAMPLE"), $"AWS Access Keyが削除されていません: {sanitized}")

            Assert.IsFalse(
                sanitized.Contains("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9"),
                $"JWT Tokenが削除されていません: {sanitized}"
            )

    [<Test>]
    [<Category("Unit")>]
    member this.``環境変数フィルタリングの精密性テスト``() =
        // 正常な環境変数（削除されるべきでない）
        let normalEnv =
            Map.ofList
                [ ("CONNECTION_TIMEOUT", "30")
                  ("AUTH_SERVICE_URL", "https://auth.example.com")
                  ("OAUTH_CLIENT_ID", "client123")
                  ("AUTHORIZATION_HEADER", "Bearer")
                  ("PATH", "/usr/bin:/bin")
                  ("HOME", "/home/user")
                  ("USER", "testuser") ]

        let filteredEnv = filterSensitiveEnvironment normalEnv

        // 正常な環境変数が保持されていることを確認
        Assert.IsTrue(filteredEnv.ContainsKey("CONNECTION_TIMEOUT"), "CONNECTION_TIMEOUTが削除されました")
        Assert.IsTrue(filteredEnv.ContainsKey("AUTH_SERVICE_URL"), "AUTH_SERVICE_URLが削除されました")
        Assert.IsTrue(filteredEnv.ContainsKey("OAUTH_CLIENT_ID"), "OAUTH_CLIENT_IDが削除されました")
        Assert.IsTrue(filteredEnv.ContainsKey("AUTHORIZATION_HEADER"), "AUTHORIZATION_HEADERが削除されました")
        Assert.IsTrue(filteredEnv.ContainsKey("PATH"), "PATHが削除されました")
        Assert.IsTrue(filteredEnv.ContainsKey("HOME"), "HOMEが削除されました")
        Assert.IsTrue(filteredEnv.ContainsKey("USER"), "USERが削除されました")

    [<Test>]
    [<Category("Unit")>]
    member this.``機密環境変数は適切に削除されることを確認``() =
        // 機密環境変数（削除されるべき）
        let sensitiveEnv =
            Map.ofList
                [ ("API_KEY", "sk-test123")
                  ("SECRET_TOKEN", "secret123")
                  ("PASSWORD", "pass123")
                  ("MY_API_KEY", "test-key")
                  ("SERVICE_SECRET", "secret-value")
                  ("DB_PASSWORD", "db-pass")
                  ("USER_AUTH", "auth-token") ]

        let filteredEnv = filterSensitiveEnvironment sensitiveEnv

        // 機密環境変数が削除されていることを確認
        Assert.IsFalse(filteredEnv.ContainsKey("API_KEY"), "API_KEYが削除されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("SECRET_TOKEN"), "SECRET_TOKENが削除されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("PASSWORD"), "PASSWORDが削除されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("MY_API_KEY"), "MY_API_KEYが削除されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("SERVICE_SECRET"), "SERVICE_SECRETが削除されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("DB_PASSWORD"), "DB_PASSWORDが削除されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("USER_AUTH"), "USER_AUTHが削除されていません")

    [<Test>]
    [<Category("Unit")>]
    member this.``データベース接続文字列の完全サポートテスト``() =
        // 各種データベース接続文字列
        let dbMessages =
            [ "MongoDB: mongodb://user:pass@cluster.example.com:27017/database"
              "PostgreSQL: postgresql://dbuser:dbpass@postgres.example.com:5432/mydb"
              "Redis: redis://user:pass@redis.example.com:6379"
              "SQL Server: Server=sql.example.com;Database=mydb;User=admin;Password=sqlpass123;" ]

        for message in dbMessages do
            let sanitized = sanitizeLogMessage message

            // データベース接続文字列が適切に削除されていることを確認
            if message.Contains("mongodb://") then
                Assert.IsFalse(
                    sanitized.Contains("user:pass@cluster.example.com"),
                    $"MongoDB接続文字列が削除されていません: {sanitized}"
                )

                Assert.IsTrue(sanitized.Contains("mongodb://[REDACTED]"), $"MongoDB接続文字列が適切に置換されていません: {sanitized}")
            elif message.Contains("postgresql://") then
                Assert.IsFalse(
                    sanitized.Contains("dbuser:dbpass@postgres.example.com"),
                    $"PostgreSQL接続文字列が削除されていません: {sanitized}"
                )

                Assert.IsTrue(
                    sanitized.Contains("postgresql://[REDACTED]"),
                    $"PostgreSQL接続文字列が適切に置換されていません: {sanitized}"
                )
            elif message.Contains("redis://") then
                Assert.IsFalse(sanitized.Contains("user:pass@redis.example.com"), $"Redis接続文字列が削除されていません: {sanitized}")
                Assert.IsTrue(sanitized.Contains("redis://[REDACTED]"), $"Redis接続文字列が適切に置換されていません: {sanitized}")
            elif message.Contains("Password=sqlpass123") then
                Assert.IsFalse(sanitized.Contains("Password=sqlpass123"), $"SQL Server接続文字列が削除されていません: {sanitized}")
                Assert.IsTrue(sanitized.Contains("PASSWORD=[REDACTED]"), $"SQL Server接続文字列が適切に置換されていません: {sanitized}")

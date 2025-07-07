namespace FCode.Tests

open NUnit.Framework
open System
open System.IO
open FCode.SecurityUtils
open FCode.Logger
open FCode.FCodeError

[<TestFixture>]
[<Category("Unit")>]
type SecurityUtilsTests() =

    [<Test>]
    [<Category("Unit")>]
    member this.``ログメッセージ機密情報除去テスト``() =
        // 機密情報を含むメッセージのテスト
        let sensitiveMessages =
            [ "API Key: sk-1234567890abcdefghijklmnopqrstuvwxyz"
              "Database connection: Server=localhost;Password=secret123;Database=mydb;"
              "User home path: /home/sensitive-user/documents/config.json"
              "Stack trace: at System.Exception.ThrowHelper(String message) in /home/user/.dotnet/runtime/System.Private.CoreLib.dll:line 123"
              "Environment: API_KEY=sk-abcd1234 SECRET_TOKEN=xyz789 DATABASE_URL=postgresql://user:pass@host/db" ]

        for message in sensitiveMessages do
            let sanitized = FCode.SecurityUtils.sanitizeLogMessage message

            // API Keyが除去されていることを確認
            Assert.IsFalse(sanitized.Contains("sk-"), $"API Keyが除去されていません: {sanitized}")

            // パスワード情報が除去されていることを確認
            Assert.IsFalse(sanitized.Contains("Password=secret123"), $"パスワードが除去されていません: {sanitized}")

            // ホームディレクトリが匿名化されていることを確認
            Assert.IsFalse(sanitized.Contains("/home/sensitive-user"), $"ホームディレクトリが除去されていません: {sanitized}")

            // スタックトレースが制限されていることを確認
            Assert.IsFalse(sanitized.Contains("ThrowHelper"), $"スタックトレースが除去されていません: {sanitized}")

    [<Test>]
    [<Category("Unit")>]
    member this.``例外メッセージ安全化テスト``() =
        // 機密情報を含む例外を作成
        let sensitiveException =
            new ArgumentException(
                "Invalid API key: sk-1234567890abcdef, check configuration file /home/user/.config/app/secret.json"
            )

        // Logger.Exception メソッドのテスト
        let testLogger = Logger()
        testLogger.Exception("TestCategory", "Test operation with sensitive data", sensitiveException)

        // ログファイルを読み取り、機密情報が除去されていることを確認
        let logContent = File.ReadAllText(testLogger.LogPath)

        // API Keyが除去されていることを確認
        Assert.IsFalse(logContent.Contains("sk-1234567890abcdef"), "ログファイル内にAPI Keyが残存しています")

        // 機密ファイルパスが匿名化されていることを確認
        Assert.IsFalse(logContent.Contains("/home/user/.config"), "ログファイル内に機密パスが残存しています")

        // 例外の型情報は保持されていることを確認
        Assert.IsTrue(logContent.Contains("ArgumentException"), "例外の型情報が失われています")

    [<Test>]
    [<Category("Unit")>]
    member this.``FCodeError機密情報安全化テスト``() =
        // 機密情報を含む例外からFCodeErrorを作成
        let sensitiveException =
            new UnauthorizedAccessException("Access denied to /home/admin/.ssh/id_rsa with API key sk-sensitive123")

        let fCodeError =
            FCode.FCodeError.ErrorHandling.fromException "TestComponent" "TestOperation" sensitiveException

        let errorMessage = fCodeError.ToUserMessage()

        // TechnicalDetailsから機密情報が除去されていることを確認
        Assert.IsFalse(errorMessage.TechnicalDetails.Contains("sk-sensitive123"), "FCodeError内にAPI Keyが残存しています")
        Assert.IsFalse(errorMessage.TechnicalDetails.Contains("/home/admin"), "FCodeError内に機密パスが残存しています")

        // エラーの基本情報は保持されていることを確認
        Assert.IsTrue(errorMessage.TechnicalDetails.Contains("TestComponent"), "コンポーネント情報が失われています")
        Assert.IsTrue(errorMessage.TechnicalDetails.Contains("TestOperation"), "操作情報が失われています")

    [<Test>]
    [<Category("Unit")>]
    member this.``セッションID安全性検証テスト``() =
        // 危険なセッションIDのテスト
        let dangerousSessionIds =
            [ "../../../etc/passwd"
              "..\\..\\..\\windows\\system32\\config\\sam"
              "session/../../../tmp/malicious"
              "test|rm -rf /"
              "test`cat /etc/passwd`" ]

        for sessionId in dangerousSessionIds do
            match FCode.SecurityUtils.sanitizeSessionId sessionId with
            | Ok safeId ->
                // 危険な文字が除去されていることを確認
                Assert.IsFalse(safeId.Contains(".."), "ディレクトリトラバーサル文字列が残存しています")
                Assert.IsFalse(safeId.Contains("|"), "パイプ文字が残存しています")
                Assert.IsFalse(safeId.Contains("`"), "バッククォート文字が残存しています")
                Assert.IsFalse(safeId.Contains("/"), "スラッシュ文字が残存しています")

                // セッションIDが64文字以下であることを確認
                Assert.LessOrEqual(safeId.Length, 64, "セッションIDが長すぎます")
            | Error _ ->
                // エラーの場合も正常（危険なIDは拒否されるべき）
                ()

    [<Test>]
    [<Category("Unit")>]
    member this.``機密環境変数フィルタリングテスト``() =
        // 機密情報を含む環境変数のマップ
        let sensitiveEnv =
            Map.ofList
                [ ("API_KEY", "sk-1234567890abcdef")
                  ("SECRET_TOKEN", "sensitive-token-123")
                  ("PASSWORD", "my-secret-password")
                  ("DATABASE_URL", "postgresql://user:pass@host/db")
                  ("NORMAL_VAR", "safe-value")
                  ("PATH", "/usr/bin:/bin") ]

        let filteredEnv = FCode.SecurityUtils.filterSensitiveEnvironment sensitiveEnv

        // 機密情報が除去されていることを確認
        Assert.IsFalse(filteredEnv.ContainsKey("API_KEY"), "API_KEYが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("SECRET_TOKEN"), "SECRET_TOKENが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("PASSWORD"), "PASSWORDが除去されていません")
        Assert.IsFalse(filteredEnv.ContainsKey("DATABASE_URL"), "DATABASE_URLが除去されていません")

        // 安全な環境変数は保持されていることを確認
        Assert.IsTrue(filteredEnv.ContainsKey("NORMAL_VAR"), "安全な環境変数が削除されています")
        Assert.IsTrue(filteredEnv.ContainsKey("PATH"), "PATH環境変数が削除されています")

module FCode.Tests.ColorSchemesTests

open NUnit.Framework
open Terminal.Gui
open FCode.ColorSchemes
open FCode.Tests.TestHelpers

[<TestFixture>]
[<Category("Unit")>]
type ColorSchemesTests() =

    [<SetUp>]
    member _.Setup() = initializeTerminalGui ()

    [<TearDown>]
    member _.TearDown() = shutdownTerminalGui ()

    [<Test>]
    [<Category("Unit")>]
    member _.``カラースキーム定義の存在テスト``() =
        // 各カラースキームが定義されていることを確認
        Assert.IsNotNull(chatScheme, "chatSchemeが定義されていること")
        Assert.IsNotNull(devScheme, "devSchemeが定義されていること")
        Assert.IsNotNull(qaScheme, "qaSchemeが定義されていること")
        Assert.IsNotNull(uxScheme, "uxSchemeが定義されていること")
        Assert.IsNotNull(pmScheme, "pmSchemeが定義されていること")

    [<Test>]
    [<Category("Unit")>]
    member _.``カラースキームの属性設定テスト``() =
        // カラースキームの基本属性が設定されていることを確認
        Assert.IsNotNull(chatScheme.Normal, "chatScheme.Normalが設定されていること")
        Assert.IsNotNull(chatScheme.Focus, "chatScheme.Focusが設定されていること")
        Assert.IsNotNull(chatScheme.HotNormal, "chatScheme.HotNormalが設定されていること")
        Assert.IsNotNull(chatScheme.HotFocus, "chatScheme.HotFocusが設定されていること")

    [<Test>]
    [<Category("Unit")>]
    member _.``開発者ペイン用カラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // dev1ペインのカラースキーム適用
        applySchemeByRoleTestable frameView "dev1"
        Assert.AreEqual(devScheme, frameView.ColorScheme, "dev1にdevSchemeが適用されること")

        // dev2ペインのカラースキーム適用
        applySchemeByRoleTestable frameView "dev2"
        Assert.AreEqual(devScheme, frameView.ColorScheme, "dev2にdevSchemeが適用されること")

        // dev3ペインのカラースキーム適用
        applySchemeByRoleTestable frameView "dev3"
        Assert.AreEqual(devScheme, frameView.ColorScheme, "dev3にdevSchemeが適用されること")

    [<Test>]
    [<Category("Unit")>]
    member _.``QAペイン用カラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // qa1ペインのカラースキーム適用
        applySchemeByRoleTestable frameView "qa1"
        Assert.AreEqual(qaScheme, frameView.ColorScheme, "qa1にqaSchemeが適用されること")

        // qa2ペインのカラースキーム適用
        applySchemeByRoleTestable frameView "qa2"
        Assert.AreEqual(qaScheme, frameView.ColorScheme, "qa2にqaSchemeが適用されること")

    [<Test>]
    [<Category("Unit")>]
    member _.``UXペイン用カラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        applySchemeByRoleTestable frameView "ux"
        Assert.AreEqual(uxScheme, frameView.ColorScheme, "uxにuxSchemeが適用されること")

    [<Test>]
    [<Category("Unit")>]
    member _.``PMペイン用カラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // pm役割のテスト
        applySchemeByRoleTestable frameView "pm"
        Assert.AreEqual(pmScheme, frameView.ColorScheme, "pmにpmSchemeが適用されること")

        // pdm役割のテスト
        applySchemeByRoleTestable frameView "pdm"
        Assert.AreEqual(pmScheme, frameView.ColorScheme, "pdmにpmSchemeが適用されること")

        // timeline役割のテスト
        applySchemeByRoleTestable frameView "timeline"
        Assert.AreEqual(pmScheme, frameView.ColorScheme, "timelineにpmSchemeが適用されること")

    [<Test>]
    [<Category("Unit")>]
    member _.``会話ペイン用カラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // chat役割のテスト
        applySchemeByRoleTestable frameView "chat"
        Assert.AreEqual(chatScheme, frameView.ColorScheme, "chatにchatSchemeが適用されること")

        // 日本語の「会話」役割のテスト
        applySchemeByRoleTestable frameView "会話"
        Assert.AreEqual(chatScheme, frameView.ColorScheme, "会話にchatSchemeが適用されること")

    [<Test>]
    [<Category("Unit")>]
    member _.``大文字小文字を無視したカラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // 大文字でのテスト
        applySchemeByRoleTestable frameView "DEV1"
        Assert.AreEqual(devScheme, frameView.ColorScheme, "DEV1（大文字）にdevSchemeが適用されること")

        // 混合ケースでのテスト
        applySchemeByRoleTestable frameView "Qa1"
        Assert.AreEqual(qaScheme, frameView.ColorScheme, "Qa1（混合ケース）にqaSchemeが適用されること")

    [<Test>]
    [<Category("Unit")>]
    member _.``未定義役割のデフォルトカラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // 未定義の役割
        applySchemeByRoleTestable frameView "unknown"
        Assert.AreEqual(devScheme, frameView.ColorScheme, "未定義役割にはdevScheme（デフォルト）が適用されること")

        // 空文字列
        applySchemeByRoleTestable frameView ""
        Assert.AreEqual(devScheme, frameView.ColorScheme, "空文字列にはdevScheme（デフォルト）が適用されること")

    [<Test>]
    [<Category("Unit")>]
    member _.``カラースキーム統一テスト``() =
        // 現在は全カラースキームが統一されたdefaultSchemeを使用していることを確認
        Assert.AreEqual(devScheme, chatScheme, "chatSchemeとdevSchemeは同じこと（統一カラー）")
        Assert.AreEqual(qaScheme, devScheme, "devSchemeとqaSchemeは同じこと（統一カラー）")
        Assert.AreEqual(uxScheme, qaScheme, "qaSchemeとuxSchemeは同じこと（統一カラー）")
        Assert.AreEqual(pmScheme, uxScheme, "uxSchemeとpmSchemeは同じこと（統一カラー）")

    [<Test>]
    [<Category("Unit")>]
    member _.``複数ペインへの同時適用テスト``() =
        let dev1Pane = createTestableFrameView "dev1"
        let dev2Pane = createTestableFrameView "dev2"
        let qa1Pane = createTestableFrameView "qa1"

        // 複数ペインに異なるカラースキームを適用
        applySchemeByRoleTestable dev1Pane "dev1"
        applySchemeByRoleTestable dev2Pane "dev2"
        applySchemeByRoleTestable qa1Pane "qa1"

        // それぞれが適切なスキームを持つことを確認
        Assert.AreEqual(devScheme, dev1Pane.ColorScheme, "dev1Paneが適切なスキーム")
        Assert.AreEqual(devScheme, dev2Pane.ColorScheme, "dev2Paneが適切なスキーム")
        Assert.AreEqual(qaScheme, qa1Pane.ColorScheme, "qa1Paneが適切なスキーム")

        // 現在は全て統一スキームなので全て同じであることを確認
        Assert.AreEqual(dev2Pane.ColorScheme, dev1Pane.ColorScheme, "dev1とdev2は同じスキーム")
        Assert.AreEqual(qa1Pane.ColorScheme, dev1Pane.ColorScheme, "統一カラーにより全て同じスキーム")

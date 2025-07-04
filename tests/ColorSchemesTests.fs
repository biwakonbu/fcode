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
    member _.``カラースキーム定義の存在テスト``() =
        // 各カラースキームが定義されていることを確認
        Assert.That(chatScheme, Is.Not.Null, "chatSchemeが定義されていること")
        Assert.That(devScheme, Is.Not.Null, "devSchemeが定義されていること")
        Assert.That(qaScheme, Is.Not.Null, "qaSchemeが定義されていること")
        Assert.That(uxScheme, Is.Not.Null, "uxSchemeが定義されていること")
        Assert.That(pmScheme, Is.Not.Null, "pmSchemeが定義されていること")

    [<Test>]
    member _.``カラースキームの属性設定テスト``() =
        // カラースキームの基本属性が設定されていることを確認
        Assert.That(chatScheme.Normal, Is.Not.EqualTo(null), "chatScheme.Normalが設定されていること")
        Assert.That(chatScheme.Focus, Is.Not.EqualTo(null), "chatScheme.Focusが設定されていること")
        Assert.That(chatScheme.HotNormal, Is.Not.EqualTo(null), "chatScheme.HotNormalが設定されていること")
        Assert.That(chatScheme.HotFocus, Is.Not.EqualTo(null), "chatScheme.HotFocusが設定されていること")

    [<Test>]
    member _.``開発者ペイン用カラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // dev1ペインのカラースキーム適用
        applySchemeByRoleTestable frameView "dev1"
        Assert.That(frameView.ColorScheme, Is.EqualTo(devScheme), "dev1にdevSchemeが適用されること")

        // dev2ペインのカラースキーム適用
        applySchemeByRoleTestable frameView "dev2"
        Assert.That(frameView.ColorScheme, Is.EqualTo(devScheme), "dev2にdevSchemeが適用されること")

        // dev3ペインのカラースキーム適用
        applySchemeByRoleTestable frameView "dev3"
        Assert.That(frameView.ColorScheme, Is.EqualTo(devScheme), "dev3にdevSchemeが適用されること")

    [<Test>]
    member _.``QAペイン用カラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // qa1ペインのカラースキーム適用
        applySchemeByRoleTestable frameView "qa1"
        Assert.That(frameView.ColorScheme, Is.EqualTo(qaScheme), "qa1にqaSchemeが適用されること")

        // qa2ペインのカラースキーム適用
        applySchemeByRoleTestable frameView "qa2"
        Assert.That(frameView.ColorScheme, Is.EqualTo(qaScheme), "qa2にqaSchemeが適用されること")

    [<Test>]
    member _.``UXペイン用カラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        applySchemeByRoleTestable frameView "ux"
        Assert.That(frameView.ColorScheme, Is.EqualTo(uxScheme), "uxにuxSchemeが適用されること")

    [<Test>]
    member _.``PMペイン用カラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // pm役割のテスト
        applySchemeByRoleTestable frameView "pm"
        Assert.That(frameView.ColorScheme, Is.EqualTo(pmScheme), "pmにpmSchemeが適用されること")

        // pdm役割のテスト
        applySchemeByRoleTestable frameView "pdm"
        Assert.That(frameView.ColorScheme, Is.EqualTo(pmScheme), "pdmにpmSchemeが適用されること")

        // timeline役割のテスト
        applySchemeByRoleTestable frameView "timeline"
        Assert.That(frameView.ColorScheme, Is.EqualTo(pmScheme), "timelineにpmSchemeが適用されること")

    [<Test>]
    member _.``会話ペイン用カラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // chat役割のテスト
        applySchemeByRoleTestable frameView "chat"
        Assert.That(frameView.ColorScheme, Is.EqualTo(chatScheme), "chatにchatSchemeが適用されること")

        // 日本語の「会話」役割のテスト
        applySchemeByRoleTestable frameView "会話"
        Assert.That(frameView.ColorScheme, Is.EqualTo(chatScheme), "会話にchatSchemeが適用されること")

    [<Test>]
    member _.``大文字小文字を無視したカラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // 大文字でのテスト
        applySchemeByRoleTestable frameView "DEV1"
        Assert.That(frameView.ColorScheme, Is.EqualTo(devScheme), "DEV1（大文字）にdevSchemeが適用されること")

        // 混合ケースでのテスト
        applySchemeByRoleTestable frameView "Qa1"
        Assert.That(frameView.ColorScheme, Is.EqualTo(qaScheme), "Qa1（混合ケース）にqaSchemeが適用されること")

    [<Test>]
    member _.``未定義役割のデフォルトカラースキーム適用テスト``() =
        let frameView = createTestableFrameView "test"

        // 未定義の役割
        applySchemeByRoleTestable frameView "unknown"
        Assert.That(frameView.ColorScheme, Is.EqualTo(devScheme), "未定義役割にはdevScheme（デフォルト）が適用されること")

        // 空文字列
        applySchemeByRoleTestable frameView ""
        Assert.That(frameView.ColorScheme, Is.EqualTo(devScheme), "空文字列にはdevScheme（デフォルト）が適用されること")

    [<Test>]
    member _.``カラースキーム統一テスト``() =
        // 現在は全カラースキームが統一されたdefaultSchemeを使用していることを確認
        Assert.That(chatScheme, Is.EqualTo(devScheme), "chatSchemeとdevSchemeは同じこと（統一カラー）")
        Assert.That(devScheme, Is.EqualTo(qaScheme), "devSchemeとqaSchemeは同じこと（統一カラー）")
        Assert.That(qaScheme, Is.EqualTo(uxScheme), "qaSchemeとuxSchemeは同じこと（統一カラー）")
        Assert.That(uxScheme, Is.EqualTo(pmScheme), "uxSchemeとpmSchemeは同じこと（統一カラー）")

    [<Test>]
    member _.``複数ペインへの同時適用テスト``() =
        let dev1Pane = createTestableFrameView "dev1"
        let dev2Pane = createTestableFrameView "dev2"
        let qa1Pane = createTestableFrameView "qa1"

        // 複数ペインに異なるカラースキームを適用
        applySchemeByRoleTestable dev1Pane "dev1"
        applySchemeByRoleTestable dev2Pane "dev2"
        applySchemeByRoleTestable qa1Pane "qa1"

        // それぞれが適切なスキームを持つことを確認
        Assert.That(dev1Pane.ColorScheme, Is.EqualTo(devScheme), "dev1Paneが適切なスキーム")
        Assert.That(dev2Pane.ColorScheme, Is.EqualTo(devScheme), "dev2Paneが適切なスキーム")
        Assert.That(qa1Pane.ColorScheme, Is.EqualTo(qaScheme), "qa1Paneが適切なスキーム")

        // 現在は全て統一スキームなので全て同じであることを確認
        Assert.That(dev1Pane.ColorScheme, Is.EqualTo(dev2Pane.ColorScheme), "dev1とdev2は同じスキーム")
        Assert.That(dev1Pane.ColorScheme, Is.EqualTo(qa1Pane.ColorScheme), "統一カラーにより全て同じスキーム")

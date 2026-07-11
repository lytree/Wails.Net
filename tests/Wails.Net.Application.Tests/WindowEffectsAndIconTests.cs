using System.IO;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Icons;
using Wails.Net.Application.Windows;
using Wails.Net.Runtime.Js;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 窗口特效（WindowEffects）和图标生成（IconCommand）的单元测试（TUnit）。
/// 验证 WindowEffect 枚举值、WindowEffects 属性默认值、ICO 编解码。
/// </summary>
[NotInParallel]
public sealed class WindowEffectsAndIconTests
{
    // ---------------------------------------------------------------------
    // WindowEffect 枚举（先赋值给局部变量，避免 TUnit 常量值断言错误）
    // ---------------------------------------------------------------------

    [Test]
    public async Task WindowEffect_None_HasValue0()
    {
        int value = (int)WindowEffect.None;
        await Assert.That(value).IsEqualTo(0);
    }

    [Test]
    public async Task WindowEffect_Mica_HasValue1()
    {
        int value = (int)WindowEffect.Mica;
        await Assert.That(value).IsEqualTo(1);
    }

    [Test]
    public async Task WindowEffect_Acrylic_HasValue2()
    {
        int value = (int)WindowEffect.Acrylic;
        await Assert.That(value).IsEqualTo(2);
    }

    [Test]
    public async Task WindowEffect_BlurBehind_HasValue3()
    {
        int value = (int)WindowEffect.BlurBehind;
        await Assert.That(value).IsEqualTo(3);
    }

    [Test]
    public async Task WindowEffect_Transparent_HasValue4()
    {
        int value = (int)WindowEffect.Transparent;
        await Assert.That(value).IsEqualTo(4);
    }

    // ---------------------------------------------------------------------
    // WindowEffects 类
    // ---------------------------------------------------------------------

    [Test]
    public async Task WindowEffects_DefaultEffect_IsNone()
    {
        var effects = new WindowEffects();
        WindowEffect effect = effects.Effect;
        await Assert.That(effect).IsEqualTo(WindowEffect.None);
    }

    [Test]
    public async Task WindowEffects_DefaultState_IsTrue()
    {
        var effects = new WindowEffects();
        bool state = effects.State;
        await Assert.That(state).IsTrue();
    }

    [Test]
    public async Task WindowEffects_DefaultRadius_IsZero()
    {
        var effects = new WindowEffects();
        int radius = effects.Radius;
        await Assert.That(radius).IsEqualTo(0);
    }

    [Test]
    public async Task WindowEffects_DefaultColor_IsNull()
    {
        var effects = new WindowEffects();
        string? color = effects.Color;
        await Assert.That(color).IsNull();
    }

    [Test]
    public async Task WindowEffects_SetMicaEffect_ReturnsMica()
    {
        var effects = new WindowEffects { Effect = WindowEffect.Mica };
        WindowEffect effect = effects.Effect;
        await Assert.That(effect).IsEqualTo(WindowEffect.Mica);
    }

    [Test]
    public async Task WindowEffects_SetAcrylicEffect_ReturnsAcrylic()
    {
        var effects = new WindowEffects { Effect = WindowEffect.Acrylic };
        WindowEffect effect = effects.Effect;
        await Assert.That(effect).IsEqualTo(WindowEffect.Acrylic);
    }

    [Test]
    public async Task WindowEffects_SetStateFalse_ReturnsFalse()
    {
        var effects = new WindowEffects { State = false };
        bool state = effects.State;
        await Assert.That(state).IsFalse();
    }

    [Test]
    public async Task WindowEffects_SetRadius42_Returns42()
    {
        var effects = new WindowEffects { Radius = 42 };
        int radius = effects.Radius;
        await Assert.That(radius).IsEqualTo(42);
    }

    [Test]
    public async Task WindowEffects_SetColor_ReturnsColor()
    {
        var effects = new WindowEffects { Color = "#FF0000" };
        string? color = effects.Color;
        await Assert.That(color).IsEqualTo("#FF0000");
    }

    // ---------------------------------------------------------------------
    // TaskbarProgressState 枚举
    // ---------------------------------------------------------------------

    [Test]
    public async Task TaskbarProgressState_None_HasValue0()
    {
        int value = (int)TaskbarProgressState.None;
        await Assert.That(value).IsEqualTo(0);
    }

    [Test]
    public async Task TaskbarProgressState_Normal_HasValue2()
    {
        int value = (int)TaskbarProgressState.Normal;
        await Assert.That(value).IsEqualTo(2);
    }

    [Test]
    public async Task TaskbarProgressState_Paused_HasValue8()
    {
        int value = (int)TaskbarProgressState.Paused;
        await Assert.That(value).IsEqualTo(8);
    }

    // ---------------------------------------------------------------------
    // ICO 编解码（使用 IcoEncoder/IcoDecoder）
    // ---------------------------------------------------------------------

    [Test]
    public async Task IcoEncoder_EncodeSingleEntry_DecodeReturnsEntry()
    {
        // 创建模拟 PNG 数据
        var pngData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };

        var entry = new IconEntry
        {
            Width = 32,
            Height = 32,
            BitCount = 32,
            Data = pngData,
            Format = IconImageFormat.Png,
        };

        var multiIcon = new MultiSizeIcon();
        multiIcon.Add(entry);

        var icoBytes = IcoEncoder.Encode(multiIcon.Entries);
        await Assert.That(icoBytes.Length).IsGreaterThan(0);

        // 解码验证
        var decoded = IcoDecoder.Decode(icoBytes);
        await Assert.That(decoded.Count).IsEqualTo(1);
        await Assert.That(decoded.Entries[0].ActualWidth).IsEqualTo(32);
        await Assert.That(decoded.Entries[0].ActualHeight).IsEqualTo(32);
    }

    [Test]
    public async Task IcoEncoder_EncodeMultipleEntries_DecodeReturnsAll()
    {
        var sizes = new[] { 16, 32, 48, 64 };
        var multiIcon = new MultiSizeIcon();

        foreach (var size in sizes)
        {
            var entry = new IconEntry
            {
                Width = size,
                Height = size,
                BitCount = 32,
                Data = new byte[size * size * 4], // 模拟 RGBA 数据
                Format = IconImageFormat.Png,
            };
            multiIcon.Add(entry);
        }

        var icoBytes = IcoEncoder.Encode(multiIcon.Entries);
        await Assert.That(icoBytes.Length).IsGreaterThan(0);

        var decoded = IcoDecoder.Decode(icoBytes);
        await Assert.That(decoded.Count).IsEqualTo(4);
    }

    // ---------------------------------------------------------------------
    // RuntimeGenerator JS API 验证
    // ---------------------------------------------------------------------

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsSetSkipTaskbar()
    {
        var js = RuntimeGenerator.GenerateApi(new RuntimeOptions());
        await Assert.That(js).Contains("setSkipTaskbar");
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsSetIgnoreCursorEvents()
    {
        var js = RuntimeGenerator.GenerateApi(new RuntimeOptions());
        await Assert.That(js).Contains("setIgnoreCursorEvents");
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsSetEffects()
    {
        var js = RuntimeGenerator.GenerateApi(new RuntimeOptions());
        await Assert.That(js).Contains("setEffects");
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsSetBadgeCount()
    {
        var js = RuntimeGenerator.GenerateApi(new RuntimeOptions());
        await Assert.That(js).Contains("setBadgeCount");
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsSetBadgeLabel()
    {
        var js = RuntimeGenerator.GenerateApi(new RuntimeOptions());
        await Assert.That(js).Contains("setBadgeLabel");
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsSetVisibleOnAllWorkspaces()
    {
        var js = RuntimeGenerator.GenerateApi(new RuntimeOptions());
        await Assert.That(js).Contains("setVisibleOnAllWorkspaces");
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsSetBorderColor()
    {
        var js = RuntimeGenerator.GenerateApi(new RuntimeOptions());
        await Assert.That(js).Contains("setBorderColor");
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsSetFileDropEnabled()
    {
        var js = RuntimeGenerator.GenerateApi(new RuntimeOptions());
        await Assert.That(js).Contains("setFileDropEnabled");
    }
}

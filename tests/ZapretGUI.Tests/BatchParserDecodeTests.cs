using System.Text;
using Xunit;
using ZapretGUI.Services;

namespace ZapretGUI.Tests;

public class BatchParserDecodeTests
{
    // Ensure the codepages provider is registered for the cp866 tests.
    // App.OnStartup does this in the actual app; the test process needs it too.
    static BatchParserDecodeTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Decode_AsciiBytes_AsUtf8()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("winws.exe --filter-tcp=443\r\n");
        string text = BatchParser.DecodeBytes(bytes);
        Assert.Equal("winws.exe --filter-tcp=443\r\n", text);
    }

    [Fact]
    public void Decode_Utf8WithBom_StripsBomAndDecodes()
    {
        byte[] bom = { 0xEF, 0xBB, 0xBF };
        byte[] body = Encoding.UTF8.GetBytes("rem Стратегия\r\nwinws.exe\r\n");
        byte[] bytes = [.. bom, .. body];

        string text = BatchParser.DecodeBytes(bytes);

        // Exact equality proves the BOM was stripped — the expected literal has no BOM.
        Assert.Equal("rem Стратегия\r\nwinws.exe\r\n", text);
        Assert.False(text.StartsWith('﻿'), "Decoded string must not start with U+FEFF (BOM)");
    }

    [Fact]
    public void Decode_Utf8NoBom_CyrillicComments_DecodesAsUtf8()
    {
        // No BOM, but the bytes are valid UTF-8. Strict UTF-8 should accept.
        byte[] bytes = Encoding.UTF8.GetBytes("rem Стратегия для Discord\r\nwinws.exe --фильтр\r\n");
        string text = BatchParser.DecodeBytes(bytes);
        Assert.Equal("rem Стратегия для Discord\r\nwinws.exe --фильтр\r\n", text);
    }

    [Fact]
    public void Decode_Cp866_CyrillicComments_FallsBackAndDecodesCorrectly()
    {
        // Cp866 bytes for "rem Стратегия". These are NOT valid UTF-8, so the
        // strict UTF-8 decode must fail and the cp866 fallback must kick in.
        const string original = "rem Стратегия\r\nwinws.exe\r\n";
        byte[] bytes = Encoding.GetEncoding(866).GetBytes(original);

        // Sanity: prove the test input is actually NOT valid UTF-8.
        Assert.Throws<DecoderFallbackException>(() =>
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes));

        string text = BatchParser.DecodeBytes(bytes);
        Assert.Equal(original, text);
    }

    [Fact]
    public void Decode_Cp866_PreservesEveryCyrillicCharacter()
    {
        // Round-trip the full lowercase Russian alphabet + uppercase + ё/Ё.
        // Cp866 covers all of these; a wrong fallback would mangle some chars.
        const string original = "абвгдеёжзийклмнопрстуфхцчшщъыьэюяАБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ";
        byte[] bytes = Encoding.GetEncoding(866).GetBytes(original);

        string text = BatchParser.DecodeBytes(bytes);

        Assert.Equal(original, text);
    }

    [Fact]
    public void Decode_Utf16LeWithBom_Decodes()
    {
        byte[] bom = { 0xFF, 0xFE };
        byte[] body = Encoding.Unicode.GetBytes("winws.exe\r\n");
        byte[] bytes = [.. bom, .. body];

        string text = BatchParser.DecodeBytes(bytes);
        Assert.Equal("winws.exe\r\n", text);
    }

    [Fact]
    public void Decode_Utf16BeWithBom_Decodes()
    {
        byte[] bom = { 0xFE, 0xFF };
        byte[] body = Encoding.BigEndianUnicode.GetBytes("winws.exe\r\n");
        byte[] bytes = [.. bom, .. body];

        string text = BatchParser.DecodeBytes(bytes);
        Assert.Equal("winws.exe\r\n", text);
    }

    [Fact]
    public void Decode_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, BatchParser.DecodeBytes(Array.Empty<byte>()));
    }
}

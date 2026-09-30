using System.Text;
using System.Text.Json;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.Tests;

public class ValidationTests
{
    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("fd00::10")]
    [InlineData("2001:db8::1")]
    public void IpAddress_Valid(string ip) => Assert.True(HostsDocument.IsValidIpAddress(ip));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("10.1")]
    [InlineData("256.0.0.1")]
    [InlineData("10.0.0.1.5")]
    [InlineData("app.example.com")]
    [InlineData("fe80::1%12")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData(" 10.0.0.1")]
    public void IpAddress_Invalid(string ip) => Assert.False(HostsDocument.IsValidIpAddress(ip));

    [Theory]
    [InlineData("app.example.com")]
    [InlineData("localhost")]
    [InlineData("my_host-01.example.com")]
    public void Hostname_Valid(string hostname) => Assert.True(HostsDocument.IsValidHostname(hostname));

    [Theory]
    [InlineData("")]
    [InlineData("app example.com")]
    [InlineData("app.example.com#x")]
    [InlineData("アプリ.example.com")]
    [InlineData("app/example")]
    public void Hostname_Invalid(string hostname) => Assert.False(HostsDocument.IsValidHostname(hostname));

    [Theory]
    [InlineData("", true)]
    [InlineData("test server", true)]
    [InlineData("検証環境", true)]
    [InlineData("see #12", false)]
    public void Comment_Validation(string comment, bool expected)
        => Assert.Equal(expected, HostsDocument.IsValidComment(comment));

    [Fact]
    public void ValidInputs_RoundTripAsEntries()
    {
        var entry = new HostEntry { IpAddress = "fd00::10", Hostname = "my_host-01.example.com", Comment = "test" };
        var parsed = Assert.Single(HostsDocument.Parse(HostsDocument.Render(new[] { entry })));
        Assert.False(parsed.IsRawLine);
        Assert.Equal("fd00::10", parsed.IpAddress);
        Assert.Equal("my_host-01.example.com", parsed.Hostname);
        Assert.Equal("test", parsed.Comment);
    }
}

public class EncodingDetectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hatch-tests-" + Guid.NewGuid().ToString("N"));

    public EncodingDetectionTests()
    {
        Directory.CreateDirectory(_dir);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteBytes(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private const string JapaneseText = "10.0.0.1 app.example.com # 検証環境\r\n";

    [Fact]
    public void DetectsCp932()
    {
        var path = WriteBytes("sjis.txt", Encoding.GetEncoding(932).GetBytes(JapaneseText));
        Assert.Equal(932, HostsFileService.DetectEncoding(path).CodePage);
        Assert.Equal(JapaneseText, HostsFileService.ReadTextDetect(path));
    }

    [Fact]
    public void DetectsUtf8WithoutBom()
    {
        var path = WriteBytes("utf8.txt", new UTF8Encoding(false).GetBytes(JapaneseText));
        var enc = HostsFileService.DetectEncoding(path);
        Assert.Equal(65001, enc.CodePage);
        Assert.Empty(enc.GetPreamble());
        Assert.Equal(JapaneseText, HostsFileService.ReadTextDetect(path));
    }

    [Fact]
    public void DetectsUtf8WithBom_AndStripsIt()
    {
        var path = WriteBytes("utf8bom.txt", new UTF8Encoding(true).GetPreamble()
            .Concat(new UTF8Encoding(false).GetBytes(JapaneseText)).ToArray());
        Assert.Equal(65001, HostsFileService.DetectEncoding(path).CodePage);
        Assert.Equal(JapaneseText, HostsFileService.ReadTextDetect(path));
    }

    [Fact]
    public void AsciiIsTreatedAsUtf8()
    {
        var path = WriteBytes("ascii.txt", Encoding.ASCII.GetBytes("10.0.0.1 app.example.com\r\n"));
        Assert.Equal(65001, HostsFileService.DetectEncoding(path).CodePage);
    }

    [Fact]
    public void Cp932CommentSurvivesParseRender()
    {
        var path = WriteBytes("sjis-roundtrip.txt", Encoding.GetEncoding(932).GetBytes(JapaneseText));
        var entry = Assert.Single(HostsDocument.Parse(HostsFileService.ReadTextDetect(path)));
        Assert.Equal("検証環境", entry.Comment);
        Assert.Contains("# 検証環境", HostsDocument.Render(new[] { entry }));
    }
}

public class GroupColorTests
{
    [Fact]
    public void StableHash_IsFnv1a()
    {
        Assert.Equal(2166136261u, GroupColorConverter.StableHash(""));
        Assert.Equal(0xE40C292Cu, GroupColorConverter.StableHash("a"));
    }

    [Fact]
    public void StableHash_IsDeterministic()
    {
        Assert.Equal(GroupColorConverter.StableHash("業務"), GroupColorConverter.StableHash("業務"));
        Assert.NotEqual(GroupColorConverter.StableHash("業務"), GroupColorConverter.StableHash("個人"));
    }
}

public class PresetJsonTests
{
    [Fact]
    public void Preset_UsesLegacyDtoPropertyNames()
    {
        var json = "{\"Name\":\"work\",\"EnabledGroups\":[\"業務/案件A\",\"業務/案件B\"]}";
        var preset = JsonSerializer.Deserialize<Preset>(json)!;
        Assert.Equal("work", preset.Name);
        Assert.Equal(new[] { "業務/案件A", "業務/案件B" }, preset.EnabledGroups);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(preset));
        Assert.Equal(new[] { "Name", "EnabledGroups" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Settings_SaveKeepsJapaneseReadable()
    {
        var path = Path.Combine(Path.GetTempPath(), "hatch-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var svc = new SettingsService(path);
            var settings = new AppSettings { ActivePresetName = "環境A" };
            settings.Presets.Add(new Preset { Name = "環境A", EnabledGroups = { "業務/案件A" } });
            svc.Save(settings);
            var json = File.ReadAllText(path);
            Assert.Contains("環境A", json);
            Assert.Contains("業務/案件A", json);
            Assert.DoesNotContain(@"\u", json);
            Assert.Equal("環境A", svc.Load().ActivePresetName);
        }
        finally { File.Delete(path); }
    }
}

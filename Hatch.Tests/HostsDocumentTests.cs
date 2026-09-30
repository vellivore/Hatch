using Hatch.Models;
using Hatch.Services;

namespace Hatch.Tests;

public class HostsDocumentTests
{
    private const string Sample =
        "# Copyright notice of the original file\r\n" +
        "#\r\n" +
        "127.0.0.1 localhost\r\n" +
        "::1 localhost\r\n" +
        "\r\n" +
        "10.0.0.1 app.example.com api.example.com # staging\r\n" +
        "# 10.0.0.2 app.example.com\r\n" +
        "# 192.168.1.1 is the router\r\n" +
        "10.0.0.3\tdb.example.com\r\n";

    private static List<HostEntry> Entries(IEnumerable<HostEntry> all) => all.Where(e => !e.IsRawLine).ToList();

    private static string Signature(IEnumerable<HostEntry> entries) =>
        string.Join("\n", entries.Select(e => e.IsRawLine
            ? $"RAW[{e.RawText}]"
            : $"{(e.IsEnabled ? "on" : "off")}|{e.IpAddress}|{e.Hostname}|{e.Comment}|{e.GroupName}|{e.IsSystem}"));

    // ---------- round trip ----------

    [Fact]
    public void RoundTrip_ParseRenderParse_IsStable()
    {
        var first = HostsDocument.Parse(Sample);
        var second = HostsDocument.Parse(HostsDocument.Render(first));
        Assert.Equal(Signature(first), Signature(second));
    }

    [Fact]
    public void RoundTrip_RenderIsIdempotent_NoBlankLineGrowth()
    {
        var once = HostsDocument.Render(HostsDocument.Parse(Sample));
        var twice = HostsDocument.Render(HostsDocument.Parse(once));
        var thrice = HostsDocument.Render(HostsDocument.Parse(twice));
        Assert.Equal(once, twice);
        Assert.Equal(twice, thrice);
    }

    [Fact]
    public void Render_HeaderIsFollowedByExactlyOneBlankLine()
    {
        var text = "# Managed by Hatch\r\n\r\n\r\n\r\n10.0.0.1 app.example.com\r\n";
        var rendered = HostsDocument.Render(HostsDocument.Parse(text));
        Assert.Equal("# Managed by Hatch\r\n\r\n10.0.0.1 app.example.com\r\n", rendered);
    }

    [Fact]
    public void Parse_AcceptsLfOnlyInput()
    {
        var entries = Entries(HostsDocument.Parse("10.0.0.1 a.example.com\n10.0.0.2 b.example.com\n"));
        Assert.Equal(2, entries.Count);
        Assert.Equal("b.example.com", entries[1].Hostname);
    }

    // ---------- header ----------

    [Fact]
    public void Parse_DropsExistingHeader()
    {
        var all = HostsDocument.Parse("# Managed by Hatch\r\n\r\n10.0.0.1 app.example.com\r\n");
        Assert.DoesNotContain(all, e => e.IsRawLine && e.RawText.Contains("Managed by Hatch"));
        Assert.Single(all);
    }

    [Fact]
    public void Render_RegeneratesHeaderOnce()
    {
        var rendered = HostsDocument.Render(HostsDocument.Parse(Sample));
        Assert.StartsWith("# Managed by Hatch\r\n\r\n", rendered);
        Assert.Equal(1, CountOccurrences(rendered, "# Managed by Hatch"));

        var again = HostsDocument.Render(HostsDocument.Parse(rendered));
        Assert.Equal(1, CountOccurrences(again, "# Managed by Hatch"));
    }

    [Fact]
    public void Render_EmptyList_IsHeaderOnly()
    {
        Assert.Equal("# Managed by Hatch\r\n\r\n", HostsDocument.Render(Array.Empty<HostEntry>()));
    }

    // ---------- entries ----------

    [Fact]
    public void Parse_DisabledEntry()
    {
        var entry = Assert.Single(Entries(HostsDocument.Parse("# 10.0.0.2 app.example.com\r\n")));
        Assert.False(entry.IsEnabled);
        Assert.Equal("10.0.0.2", entry.IpAddress);
        Assert.Equal("app.example.com", entry.Hostname);
    }

    [Fact]
    public void Parse_DisabledEntryWithoutSpaceAfterHash()
    {
        var entry = Assert.Single(Entries(HostsDocument.Parse("#10.0.0.2 app.example.com\r\n")));
        Assert.False(entry.IsEnabled);
    }

    [Fact]
    public void Parse_DisabledSingleLabelHostname()
    {
        var entry = Assert.Single(Entries(HostsDocument.Parse("# 10.0.0.5 fileserver\r\n")));
        Assert.False(entry.IsEnabled);
        Assert.Equal("fileserver", entry.Hostname);
    }

    [Fact]
    public void Render_DisabledEntry()
    {
        var entry = new HostEntry { IpAddress = "10.0.0.2", Hostname = "app.example.com", IsEnabled = false };
        Assert.EndsWith("\r\n# 10.0.0.2 app.example.com\r\n", HostsDocument.Render(new[] { entry }));
    }

    [Fact]
    public void Parse_SplitsMultipleHostnames()
    {
        var entries = Entries(HostsDocument.Parse("10.0.0.1 app.example.com  api.example.com\tweb.example.com # staging\r\n"));
        Assert.Equal(new[] { "app.example.com", "api.example.com", "web.example.com" }, entries.Select(e => e.Hostname));
        Assert.All(entries, e =>
        {
            Assert.Equal("10.0.0.1", e.IpAddress);
            Assert.Equal("staging", e.Comment);
            Assert.True(e.IsEnabled);
        });
    }

    [Fact]
    public void Render_WritesOneLinePerHostname()
    {
        var rendered = HostsDocument.Render(HostsDocument.Parse("10.0.0.1 app.example.com api.example.com # staging\r\n"));
        Assert.Contains("10.0.0.1 app.example.com # staging\r\n10.0.0.1 api.example.com # staging\r\n", rendered);
    }

    [Fact]
    public void Parse_SeparatesComment()
    {
        var entry = Assert.Single(Entries(HostsDocument.Parse("10.0.0.1 app.example.com   #   test server  \r\n")));
        Assert.Equal("app.example.com", entry.Hostname);
        Assert.Equal("test server", entry.Comment);
    }

    [Fact]
    public void Parse_CommentOnlyLineStaysRaw()
    {
        var all = HostsDocument.Parse("# This is a comment\r\n10.0.0.1 app.example.com\r\n");
        Assert.True(all[0].IsRawLine);
        Assert.Equal("# This is a comment", all[0].RawText);
    }

    [Fact]
    public void Parse_CommentMentioningIpStaysRaw()
    {
        const string line = "# 192.168.1.1 is the router";
        var entry = Assert.Single(HostsDocument.Parse(line + "\r\n"));
        Assert.True(entry.IsRawLine);
        Assert.Equal(line, entry.RawText);
        Assert.Contains(line + "\r\n", HostsDocument.Render(new[] { entry }));
    }

    [Theory]
    [InlineData("# 10.0.0.1 see: docs")]
    [InlineData("# 10:30 meeting")]
    [InlineData("# 10.0.0.1 is down, use 10.0.0.2")]
    public void Parse_OtherProseCommentsStayRaw(string line)
    {
        Assert.True(Assert.Single(HostsDocument.Parse(line)).IsRawLine);
    }

    [Fact]
    public void Parse_EntryWithoutHostnameStaysRaw()
    {
        var entry = Assert.Single(HostsDocument.Parse("10.0.0.1 # memo only\r\n"));
        Assert.True(entry.IsRawLine);
    }

    [Fact]
    public void Parse_PreservesIndentedRawLines()
    {
        var entry = Assert.Single(HostsDocument.Parse("    # indented comment\r\n"));
        Assert.Equal("    # indented comment", entry.RawText);
    }

    [Fact]
    public void Parse_TrimsLeadingBlankLinesOnly()
    {
        var all = HostsDocument.Parse("\r\n\r\n10.0.0.1 a.example.com\r\n\r\n10.0.0.2 b.example.com\r\n");
        Assert.False(all[0].IsRawLine);
        Assert.True(all[1].IsRawLine);
        Assert.Equal(3, all.Count);
    }

    // ---------- system entries ----------

    [Theory]
    [InlineData("127.0.0.1 localhost", true)]
    [InlineData("::1 localhost", true)]
    [InlineData("127.0.0.1 LocalHost", true)]
    [InlineData("# 127.0.0.1 localhost", true)]
    [InlineData("127.0.0.1 app.example.com", false)]
    [InlineData("10.0.0.1 localhost", false)]
    public void Parse_DetectsSystemEntries(string line, bool expected)
    {
        var entry = Assert.Single(Entries(HostsDocument.Parse(line)));
        Assert.Equal(expected, entry.IsSystem);
    }

    [Fact]
    public void Parse_IPv6Entry()
    {
        var entry = Assert.Single(Entries(HostsDocument.Parse("fd00::1 app.example.com\r\n")));
        Assert.Equal("fd00::1", entry.IpAddress);
    }

    [Fact]
    public void Parse_RejectsOutOfRangeIPv4AsRaw()
    {
        Assert.True(Assert.Single(HostsDocument.Parse("999.0.0.1 app.example.com")).IsRawLine);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(value, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += value.Length;
        }
        return count;
    }
}

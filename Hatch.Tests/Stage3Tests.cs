using System.Text.Json;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.Tests;

public class GroupTagTests
{
    private static HostEntry Single(string text) => Assert.Single(HostsDocument.Parse(text), e => !e.IsRawLine);

    [Fact]
    public void Parse_TagAndComment()
    {
        var e = Single("10.0.0.1 app.example.com # [業務/案件A/環境A] 検証環境\r\n");
        Assert.Equal("業務/案件A/環境A", e.GroupName);
        Assert.Equal("検証環境", e.Comment);
        Assert.True(e.IsEnabled);
    }

    [Fact]
    public void Parse_DisabledLineWithTagOnly()
    {
        var e = Single("# 10.0.0.2 app.example.com # [業務/案件A/環境B]\r\n");
        Assert.False(e.IsEnabled);
        Assert.Equal("業務/案件A/環境B", e.GroupName);
        Assert.Equal("", e.Comment);
    }

    [Fact]
    public void Parse_TagAppliesToEveryHostnameOnTheLine()
    {
        var entries = HostsDocument.Parse("10.0.0.1 a.example.com b.example.com # [業務] memo");
        Assert.All(entries, e =>
        {
            Assert.Equal("業務", e.GroupName);
            Assert.Equal("memo", e.Comment);
        });
    }

    [Theory]
    [InlineData("10.0.0.1 app.example.com # [draft notes", "[draft notes")]
    [InlineData("10.0.0.1 app.example.com # [] empty tag", "[] empty tag")]
    [InlineData("10.0.0.1 app.example.com # note [x] later", "note [x] later")]
    public void Parse_NonTagBracketsStayInComment(string line, string expectedComment)
    {
        var e = Single(line);
        Assert.Null(e.GroupName);
        Assert.Equal(expectedComment, e.Comment);
    }

    [Fact]
    public void Parse_TagWithSurroundingSpacesIsTrimmed()
    {
        var e = Single("10.0.0.1 app.example.com #   [ 業務/案件A ]   memo  ");
        Assert.Equal("業務/案件A", e.GroupName);
        Assert.Equal("memo", e.Comment);
    }

    [Fact]
    public void Render_PutsTagFirst()
    {
        var entries = new[]
        {
            new HostEntry { IpAddress = "10.0.0.1", Hostname = "app.example.com", GroupName = "業務/案件A/環境A", Comment = "検証環境" },
            new HostEntry { IpAddress = "10.0.0.2", Hostname = "app.example.com", GroupName = "業務/案件A/環境B", IsEnabled = false },
            new HostEntry { IpAddress = "10.0.0.3", Hostname = "db.example.com", Comment = "no group" },
            new HostEntry { IpAddress = "10.0.0.4", Hostname = "x.example.com" },
        };

        var rendered = HostsDocument.Render(entries);

        Assert.Equal(
            "# Managed by Hatch\r\n\r\n" +
            "10.0.0.1 app.example.com # [業務/案件A/環境A] 検証環境\r\n" +
            "# 10.0.0.2 app.example.com # [業務/案件A/環境B]\r\n" +
            "10.0.0.3 db.example.com # no group\r\n" +
            "10.0.0.4 x.example.com\r\n",
            rendered);
    }

    [Fact]
    public void RoundTrip_PreservesGroupsAndComments()
    {
        var text =
            "10.0.0.1 app.example.com # [業務/案件A/環境A] 検証環境\r\n" +
            "# 10.0.0.2 app.example.com # [業務/案件A/環境B]\r\n" +
            "10.0.0.3 other.example.com # [draft\r\n";
        var first = HostsDocument.Render(HostsDocument.Parse(text));
        var second = HostsDocument.Render(HostsDocument.Parse(first));
        Assert.Equal(first, second);

        var entries = HostsDocument.Parse(second).Where(e => !e.IsRawLine).ToList();
        Assert.Equal(new string?[] { "業務/案件A/環境A", "業務/案件A/環境B", null }, entries.Select(e => e.GroupName));
        Assert.Equal(new[] { "検証環境", "", "[draft" }, entries.Select(e => e.Comment));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("業務", true)]
    [InlineData("業務/案件A/環境A", true)]
    [InlineData("業務/案件 A", true)]
    [InlineData("業務/", false)]
    [InlineData("/業務", false)]
    [InlineData("業務//A", false)]
    [InlineData("業務/[A]", false)]
    [InlineData("業務#1", false)]
    [InlineData(" 業務", false)]
    public void GroupName_Validation(string? group, bool expected)
        => Assert.Equal(expected, HostsDocument.IsValidGroupName(group));

    [Theory]
    [InlineData("[memo] text", false)]
    [InlineData("  [memo] text", false)]
    [InlineData("[draft without close", true)]
    [InlineData("see [1]", true)]
    public void Comment_BracketPrefixValidation(string comment, bool expected)
        => Assert.Equal(expected, HostsDocument.IsValidComment(comment));
}

public class PresetLogicTests
{
    private static HostEntry E(string group, bool enabled, string host = "app.example.com") =>
        new() { IpAddress = "10.0.0.1", Hostname = host, GroupName = group, IsEnabled = enabled };

    [Theory]
    [InlineData("業務/案件A", "業務/案件A", true)]
    [InlineData("業務/案件A/環境A", "業務/案件A", true)]
    [InlineData("業務/案件A/環境A", "業務", true)]
    [InlineData("業務/案件AB", "業務/案件A", false)]
    [InlineData("業務", "業務/案件A", false)]
    [InlineData("個人", "業務", false)]
    public void Includes_IsPrefixByPathSegment(string group, string presetGroup, bool expected)
        => Assert.Equal(expected, PresetLogic.Includes(new[] { presetGroup }, group));

    [Fact]
    public void Apply_EnablesDescendantsOfPresetGroups_AndLeavesOthersAlone()
    {
        var a = E("業務/案件A/環境A", false);
        var b = E("業務/案件A/環境B", false);
        var c = E("業務/案件B", true);
        var noGroup = new HostEntry { IpAddress = "10.0.0.9", Hostname = "free.example.com", IsEnabled = true };
        var system = new HostEntry { IpAddress = "127.0.0.1", Hostname = "localhost", IsSystem = true, IsEnabled = true, GroupName = "業務" };
        var raw = new HostEntry { IsRawLine = true, RawText = "# comment" };

        PresetLogic.Apply(new[] { a, b, c, noGroup, system, raw }, new[] { "業務/案件A" });

        Assert.True(a.IsEnabled);
        Assert.True(b.IsEnabled);
        Assert.False(c.IsEnabled);
        Assert.True(noGroup.IsEnabled);
        Assert.True(system.IsEnabled);
    }

    [Fact]
    public void Apply_NullMeansAllGroupedEntriesDisabled()
    {
        var a = E("業務", true);
        var noGroup = new HostEntry { IpAddress = "10.0.0.9", Hostname = "free.example.com", IsEnabled = true };
        PresetLogic.Apply(new[] { a, noGroup }, null);
        Assert.False(a.IsEnabled);
        Assert.True(noGroup.IsEnabled);
    }

    [Fact]
    public void CaptureEnabledGroups_DropsGroupsCoveredByAnEnabledAncestor()
    {
        var entries = new[]
        {
            E("業務/案件A", true),
            E("業務/案件A/環境A", true),
            E("業務/案件B/環境A", true),
            E("業務/案件B/環境B", false),
            E("個人", false),
        };
        Assert.Equal(new[] { "業務/案件A", "業務/案件B/環境A" }, PresetLogic.CaptureEnabledGroups(entries));
    }

    [Fact]
    public void CaptureThenApply_ReproducesState()
    {
        var entries = new[]
        {
            E("業務/案件A/環境A", true, "a.example.com"),
            E("業務/案件A/環境B", false, "b.example.com"),
            E("業務/案件B", true, "c.example.com"),
            E("個人", false, "d.example.com"),
        };
        var expected = entries.Select(e => e.IsEnabled).ToArray();
        var groups = PresetLogic.CaptureEnabledGroups(entries);

        foreach (var e in entries) e.IsEnabled = !e.IsEnabled;
        PresetLogic.Apply(entries, groups);

        Assert.Equal(expected, entries.Select(e => e.IsEnabled));
    }

    [Fact]
    public void OrderAsTree_PutsChildrenRightAfterParent()
    {
        var ordered = PresetLogic.OrderAsTree(new[] { "業務-x", "業務/B", "業務", "業務/A/1", "個人", "業務/A" });
        Assert.Equal(new[] { "個人", "業務", "業務/A", "業務/A/1", "業務/B", "業務-x" }, ordered);
    }

    [Theory]
    [InlineData("業務", 0)]
    [InlineData("業務/案件A/環境A", 2)]
    public void Depth_CountsSeparators(string group, int depth) => Assert.Equal(depth, PresetLogic.Depth(group));

    [Theory]
    [InlineData("なし", "すべて無効")]
    [InlineData(null, "すべて無効")]
    [InlineData("work", "work")]
    public void DisplayName_MapsSentinel(string? name, string expected)
        => Assert.Equal(expected, PresetLogic.DisplayName(name));
}

public class LegacyMigrationTests
{
    [Fact]
    public void Apply_AssignsGroupsToUntaggedEntriesOnly()
    {
        var entries = HostsDocument.Parse(
            "10.0.0.1 app.example.com\r\n" +
            "# 10.0.0.2 app.example.com\r\n" +
            "10.0.0.3 api.example.com # [個人] tagged already\r\n" +
            "10.0.0.4 other.example.com\r\n").ToList();

        var assignments = new Dictionary<string, List<string>>
        {
            ["業務/案件A/環境A"] = new() { "10.0.0.1|app.example.com" },
            ["業務/案件A/環境B"] = new() { "10.0.0.2|app.example.com", "10.0.0.3|api.example.com" },
            ["broken"] = new() { "no-separator" },
        };

        var applied = LegacyGroupMigration.Apply(entries, assignments);

        Assert.Equal(2, applied);
        Assert.Equal("業務/案件A/環境A", entries[0].GroupName);
        Assert.Equal("業務/案件A/環境B", entries[1].GroupName);
        Assert.Equal("個人", entries[2].GroupName);
        Assert.Null(entries[3].GroupName);

        // the migrated groups are written as tags
        var rendered = HostsDocument.Render(entries);
        Assert.Contains("10.0.0.1 app.example.com # [業務/案件A/環境A]\r\n", rendered);
        Assert.Contains("# 10.0.0.2 app.example.com # [業務/案件A/環境B]\r\n", rendered);
    }

    [Fact]
    public void Apply_NothingPending_ReturnsZero()
    {
        var entries = HostsDocument.Parse("10.0.0.1 app.example.com\r\n");
        Assert.Equal(0, LegacyGroupMigration.Apply(entries, null));
        Assert.Equal(0, LegacyGroupMigration.Apply(entries, new Dictionary<string, List<string>>()));
    }

    [Fact]
    public void Apply_SkipsGroupNamesThatCannotBeWrittenAsTags()
    {
        var entries = HostsDocument.Parse("10.0.0.1 app.example.com\r\n");
        var assignments = new Dictionary<string, List<string>> { ["bad]name"] = new() { "10.0.0.1|app.example.com" } };
        Assert.Equal(0, LegacyGroupMigration.Apply(entries, assignments));
        Assert.Null(entries[0].GroupName);
    }
}

public class SettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hatch-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void ResolveDefaultPath_PrefersPortableFileNextToExe()
    {
        var exeDir = Path.Combine(_root, "app");
        var local = Path.Combine(_root, "local");
        Directory.CreateDirectory(exeDir);

        Assert.Equal(Path.Combine(local, "Hatch", "hatch-config.json"), SettingsService.ResolveDefaultPath(exeDir, local));

        File.WriteAllText(Path.Combine(exeDir, "hatch-config.json"), "{}");
        Assert.Equal(Path.Combine(exeDir, "hatch-config.json"), SettingsService.ResolveDefaultPath(exeDir, local));
    }

    [Fact]
    public void Load_V13Config_WithNaNPositionAndLegacyFields()
    {
        var path = Path.Combine(_root, "hatch-config.json");
        File.WriteAllText(path, """
        {
          "WindowLeft": "NaN",
          "WindowTop": "NaN",
          "WindowWidth": 1000,
          "WindowHeight": 600,
          "Groups": [ "業務" ],
          "GroupAssignments": { "業務/案件A": [ "10.0.0.1|app.example.com" ] },
          "Presets": [ { "Name": "work", "EnabledGroups": [ "業務/案件A" ] } ],
          "ActivePresetName": "work"
        }
        """);

        var settings = new SettingsService(path).Load();

        Assert.Null(settings.WindowLeft);
        Assert.Null(settings.WindowTop);
        Assert.Equal(1000, settings.WindowWidth);
        Assert.Equal(new[] { "10.0.0.1|app.example.com" }, settings.GroupAssignments!["業務/案件A"]);
        Assert.Equal("work", Assert.Single(settings.Presets).Name);
        Assert.Equal("work", settings.ActivePresetName);
    }

    [Fact]
    public void Load_NumericPosition()
    {
        var path = Path.Combine(_root, "hatch-config.json");
        File.WriteAllText(path, """{ "WindowLeft": 120.5, "WindowTop": -8 }""");
        var settings = new SettingsService(path).Load();
        Assert.Equal(120.5, settings.WindowLeft);
        Assert.Equal(-8, settings.WindowTop);
    }

    [Fact]
    public void Save_CreatesDirectory_AndOmitsNullsAndMigratedAssignments()
    {
        var path = Path.Combine(_root, "local", "Hatch", "hatch-config.json");
        var svc = new SettingsService(path);
        var settings = new AppSettings
        {
            WindowLeft = null,
            WindowTop = 10,
            GroupAssignments = null,
            Presets = { new Preset { Name = "work", EnabledGroups = { "業務" } } },
        };

        svc.Save(settings);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.DoesNotContain("WindowLeft", names);
        Assert.DoesNotContain("GroupAssignments", names);
        Assert.DoesNotContain("Groups", names);
        Assert.Equal(10, doc.RootElement.GetProperty("WindowTop").GetDouble());

        var loaded = svc.Load();
        Assert.Null(loaded.WindowLeft);
        Assert.Equal(10, loaded.WindowTop);
        Assert.Equal("業務", Assert.Single(Assert.Single(loaded.Presets).EnabledGroups));
    }
}

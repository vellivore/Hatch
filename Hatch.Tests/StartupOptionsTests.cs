using Hatch.Services;

namespace Hatch.Tests;

public class StartupOptionsTests
{
    [Fact]
    public void Parse_Empty_UsesDefaults()
    {
        var o = StartupOptions.Parse(Array.Empty<string>());
        Assert.Null(o.HostsPath);
        Assert.Null(o.ConfigPath);
        Assert.False(o.SkipElevation);
        Assert.Equal(HostsFileService.DefaultHostsPath, o.CreateHostsService().HostsPath);
    }

    [Fact]
    public void Parse_AllSwitches_ResolvesAbsolutePaths()
    {
        var o = StartupOptions.Parse(new[] { "--hosts", "test-hosts.txt", "--CONFIG", "cfg.json", "--no-elevate" });
        Assert.Equal(Path.GetFullPath("test-hosts.txt"), o.HostsPath);
        Assert.Equal(Path.GetFullPath("cfg.json"), o.ConfigPath);
        Assert.True(o.SkipElevation);

        var svc = o.CreateHostsService();
        Assert.Equal(o.HostsPath, svc.HostsPath);
        Assert.Equal(Path.Combine(HostsFileService.DefaultBackupDirectory, "dev"), svc.BackupDirectory);
        Assert.Equal(o.ConfigPath, o.CreateSettingsService().SettingsPath);
    }

    [Fact]
    public void NoElevate_WithoutHosts_IsIgnored()
    {
        var o = StartupOptions.Parse(new[] { "--no-elevate", "--config", "cfg.json" });
        Assert.False(o.SkipElevation);
    }

    [Fact]
    public void Parse_SwitchWithoutValue_IsIgnored()
    {
        var o = StartupOptions.Parse(new[] { "--hosts" });
        Assert.Null(o.HostsPath);
    }

    [Fact]
    public void ToArgs_RoundTrips()
    {
        var o = StartupOptions.Parse(new[] { "--hosts", "h.txt", "--no-elevate" });
        var again = StartupOptions.Parse(o.ToArgs());
        Assert.Equal(o.HostsPath, again.HostsPath);
        Assert.True(again.SkipElevation);
    }
}

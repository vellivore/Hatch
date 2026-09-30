using System.Text;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.Tests;

public class HostsFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hatch-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _etc;
    private readonly string _hosts;
    private readonly string _backups;
    private DateTime _now = new(2030, 1, 2, 3, 4, 5);

    public HostsFileServiceTests()
    {
        _etc = Path.Combine(_root, "etc");
        _backups = Path.Combine(_root, "backups");
        _hosts = Path.Combine(_etc, "hosts");
        Directory.CreateDirectory(_etc);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private HostsFileService CreateService() => new(_hosts, _backups) { Clock = () => _now };

    private const string Original = "127.0.0.1 localhost\r\n10.0.0.1 app.example.com\r\n";

    [Fact]
    public void Write_ReplacesContent_AsUtf8WithoutBom_AndLeavesNoTempFiles()
    {
        File.WriteAllText(_hosts, Original);
        var svc = CreateService();

        svc.WriteHostsText("10.0.0.2 app.example.com # 検証\r\n");

        var bytes = File.ReadAllBytes(_hosts);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("10.0.0.2 app.example.com # 検証\r\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal(new[] { "hosts" }, Directory.GetFiles(_etc).Select(Path.GetFileName));
    }

    [Fact]
    public void Write_BacksUpPreviousContent()
    {
        File.WriteAllText(_hosts, Original);
        var svc = CreateService();

        svc.WriteHostsFile(new[] { new HostEntry { IpAddress = "10.0.0.9", Hostname = "new.example.com" } });

        var backup = Assert.Single(svc.ListBackups());
        Assert.Equal("hosts.20300102-030405.txt", Path.GetFileName(backup));
        Assert.Equal(Original, File.ReadAllText(backup));
        Assert.Contains("10.0.0.9 new.example.com", File.ReadAllText(_hosts));
    }

    [Fact]
    public void Write_WithoutExistingHosts_CreatesFileAndNoBackup()
    {
        var svc = CreateService();
        svc.WriteHostsText("10.0.0.1 app.example.com\r\n");
        Assert.True(File.Exists(_hosts));
        Assert.Empty(svc.ListBackups());
    }

    [Fact]
    public void Backups_KeepNewest20()
    {
        File.WriteAllText(_hosts, Original);
        var svc = CreateService();

        for (var i = 0; i < 25; i++)
        {
            svc.WriteHostsText($"10.0.0.{i + 1} app.example.com\r\n");
            _now = _now.AddSeconds(1);
        }

        var backups = svc.ListBackups();
        Assert.Equal(HostsFileService.BackupGenerations, backups.Count);
        Assert.Equal(20, Directory.GetFiles(_backups).Length);
        // newest first: the last write backed up the content of write #24
        Assert.Equal("hosts.20300102-030429.txt", Path.GetFileName(backups[0]));
        Assert.Equal("10.0.0.24 app.example.com\r\n", File.ReadAllText(backups[0]));
        // the five oldest (03:04:05 .. 03:04:09) were pruned
        Assert.Equal("hosts.20300102-030410.txt", Path.GetFileName(backups[^1]));
    }

    [Fact]
    public void Backups_SameSecond_GetSequenceSuffix_AndSortNewestFirst()
    {
        File.WriteAllText(_hosts, Original);
        var svc = CreateService();

        svc.WriteHostsText("10.0.0.1 a.example.com\r\n");
        svc.WriteHostsText("10.0.0.2 b.example.com\r\n");
        svc.WriteHostsText("10.0.0.3 c.example.com\r\n");

        var names = svc.ListBackups().Select(Path.GetFileName).ToList();
        Assert.Equal(new[]
        {
            "hosts.20300102-030405-3.txt",
            "hosts.20300102-030405-2.txt",
            "hosts.20300102-030405.txt",
        }, names);
        Assert.Equal("10.0.0.2 b.example.com\r\n", File.ReadAllText(svc.GetLatestBackup()!));
    }

    [Fact]
    public void ListBackups_IgnoresUnrelatedFiles()
    {
        Directory.CreateDirectory(_backups);
        File.WriteAllText(Path.Combine(_backups, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_backups, "hosts.backup.txt"), "x");
        Assert.Empty(CreateService().ListBackups());
    }

    [Fact]
    public void RestoreBackup_WritesBackupContentBack()
    {
        File.WriteAllText(_hosts, Original);
        var svc = CreateService();
        svc.WriteHostsText("10.0.0.2 changed.example.com\r\n");
        _now = _now.AddSeconds(1);

        svc.RestoreBackup(svc.GetLatestBackup()!);

        Assert.Equal(Original, File.ReadAllText(_hosts));
        // restoring is itself undoable: the overwritten state was backed up too
        Assert.Equal("10.0.0.2 changed.example.com\r\n", File.ReadAllText(svc.GetLatestBackup()!));
    }

    [Fact]
    public void RestoreBackup_ReadsCp932Backup()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(_hosts, Original);
        var legacy = Path.Combine(_root, "legacy.txt");
        File.WriteAllBytes(legacy, Encoding.GetEncoding(932).GetBytes("10.0.0.1 app.example.com # 検証環境\r\n"));

        CreateService().RestoreBackup(legacy);

        Assert.Equal("10.0.0.1 app.example.com # 検証環境\r\n", File.ReadAllText(_hosts, Encoding.UTF8));
    }

    [Fact]
    public void Write_VerificationMismatch_RestoresBackupAndThrows()
    {
        var originalBytes = Encoding.ASCII.GetBytes(Original);
        File.WriteAllBytes(_hosts, originalBytes);
        var svc = CreateService();
        svc.AfterReplaceForTest = () => File.WriteAllText(_hosts, "10.0.0.66 tampered.example.com\r\n");

        var ex = Assert.Throws<HostsWriteVerificationException>(
            () => svc.WriteHostsText("10.0.0.2 app.example.com\r\n"));

        Assert.Contains("hosts.20300102-030405.txt", ex.Message);
        Assert.Equal(originalBytes, File.ReadAllBytes(_hosts));
        Assert.Equal(new[] { "hosts" }, Directory.GetFiles(_etc).Select(Path.GetFileName));
    }

    [Fact]
    public void Write_Verification_IgnoresLineEndingDifferences()
    {
        File.WriteAllText(_hosts, Original);
        var svc = CreateService();
        svc.AfterReplaceForTest = () =>
            File.WriteAllText(_hosts, File.ReadAllText(_hosts).Replace("\r\n", "\n"));

        svc.WriteHostsText("10.0.0.2 app.example.com\r\n");
        Assert.Contains("10.0.0.2 app.example.com", File.ReadAllText(_hosts));
    }

    [Fact]
    public void GetBackupTime_ParsesName()
    {
        Assert.Equal(new DateTime(2030, 1, 2, 3, 4, 5),
            HostsFileService.GetBackupTime(@"x\hosts.20300102-030405-2.txt"));
        Assert.Null(HostsFileService.GetBackupTime("hosts.txt"));
    }
}

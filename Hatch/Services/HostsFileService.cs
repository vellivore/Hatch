using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Hatch.Models;

namespace Hatch.Services;

public class HostsFileService
{
    private static readonly string HostsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                     "drivers", "etc", "hosts");


    private static readonly Regex EntryPattern =
        new(@"^(?<disabled>#\s*)?(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}|[0-9a-fA-F]*:[0-9a-fA-F:]+)\s+(?<hosts>.+)$");

    public List<HostEntry> ReadHostsFile()
    {
        var entries = new List<HostEntry>();

        if (!File.Exists(HostsPath))
            return entries;

        var lines = File.ReadAllLines(HostsPath, DetectEncoding());

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // ブランク行
            if (string.IsNullOrWhiteSpace(line))
            {
                entries.Add(new HostEntry { IsRawLine = true, RawText = line });
                continue;
            }

            // "# Managed by Hatch" は保持しない（書き込み時に再生成する）
            if (trimmed.StartsWith("# Managed by Hatch", StringComparison.OrdinalIgnoreCase))
                continue;

            var match = EntryPattern.Match(trimmed);
            if (!match.Success)
            {
                // 純粋なコメント行
                entries.Add(new HostEntry { IsRawLine = true, RawText = line });
                continue;
            }

            var disabled = !string.IsNullOrEmpty(match.Groups["disabled"].Value);
            var ip = match.Groups["ip"].Value;
            var hostsRaw = match.Groups["hosts"].Value;

            // コメント部分の分離 (hostname # comment)
            var comment = string.Empty;
            var hostsPart = hostsRaw;
            var commentIdx = hostsRaw.IndexOf('#');
            if (commentIdx >= 0)
            {
                comment = hostsRaw[(commentIdx + 1)..].Trim();
                hostsPart = hostsRaw[..commentIdx].Trim();
            }

            // 複数ホスト名の分割
            var hostnames = hostsPart.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var hostname in hostnames)
            {
                entries.Add(new HostEntry
                {
                    IpAddress = ip,
                    Hostname = hostname,
                    IsEnabled = !disabled,
                    Comment = comment,
                });
            }
        }

        return entries;
    }

    public void WriteHostsFile(List<HostEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Managed by Hatch");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            if (entry.IsRawLine)
            {
                sb.AppendLine(entry.RawText);
                continue;
            }

            var line = entry.IsEnabled
                ? $"{entry.IpAddress} {entry.Hostname}"
                : $"# {entry.IpAddress} {entry.Hostname}";

            if (!string.IsNullOrWhiteSpace(entry.Comment))
                line += $" # {entry.Comment}";

            sb.AppendLine(line);
        }

        File.WriteAllText(HostsPath, sb.ToString(), new UTF8Encoding(false));
    }

    public string ReadHostsFileRaw()
    {
        if (!File.Exists(HostsPath))
            return string.Empty;

        return File.ReadAllText(HostsPath, DetectEncoding());
    }

    public void WriteHostsFileRaw(string content)
    {
        File.WriteAllText(HostsPath, content, new UTF8Encoding(false));
    }

    public static string GetHostsPath() => HostsPath;

    /// <summary>
    /// hosts ファイルのエンコーディングを検出する。BOM があれば UTF-8、なければシステムデフォルト。
    /// </summary>
    private static Encoding DetectEncoding()
    {
        if (!File.Exists(HostsPath)) return Encoding.UTF8;
        try
        {
            var bom = new byte[3];
            using var fs = File.OpenRead(HostsPath);
            fs.Read(bom, 0, 3);
            if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return Encoding.UTF8;
        }
        catch { }
        // BOM なし → システムデフォルト（日本語環境では Shift_JIS）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    public bool CanWriteHostsFile()
    {
        try
        {
            using var fs = File.Open(HostsPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void FlushDns()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch
        {
            // DNS フラッシュ失敗は致命的ではない
        }
    }

}

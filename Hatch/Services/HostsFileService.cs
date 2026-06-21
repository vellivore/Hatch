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


    // IPv4 は各オクテット 0-255 に限定（999.x.x.x 等の不正値を弾く）。IPv6 は緩め。
    private const string Ipv4Octet = @"(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)";
    private static readonly Regex EntryPattern =
        new(@"^(?<disabled>#\s*)?(?<ip>" + Ipv4Octet + @"(?:\." + Ipv4Octet + @"){3}|[0-9a-fA-F]*:[0-9a-fA-F:]+)\s+(?<hosts>.+)$");

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
                    IsSystem = IsSystemEntry(ip, hostname),
                });
            }
        }

        // 先頭の空行を除去（書き込み時に header 直後へ空行1行を必ず付与するため、
        // ここで除去しないと保存のたびに空行が1行ずつ増殖する）
        while (entries.Count > 0 && entries[0].IsRawLine && string.IsNullOrWhiteSpace(entries[0].RawText))
            entries.RemoveAt(0);

        return entries;
    }

    /// <summary>
    /// localhost 等のシステム既定エントリかどうかを判定する（削除・無効化の保護対象）。
    /// </summary>
    private static bool IsSystemEntry(string ip, string hostname)
        => (ip == "127.0.0.1" || ip == "::1")
           && hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase);

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
    /// hosts ファイルのエンコーディングを内容ベースで検出する。
    /// BOM あり、またはバイト列が妥当な UTF-8 なら UTF-8（ASCII も妥当 UTF-8 なので無害）。
    /// UTF-8 として不正な場合のみ CP932(Shift_JIS) にフォールバックする。
    /// Hatch は常に UTF-8(BOM なし) で書き込むため、この判定で読み書きが往復一致する。
    /// </summary>
    private static Encoding DetectEncoding() => DetectEncoding(HostsPath);

    private static Encoding DetectEncoding(string path)
    {
        var utf8NoBom = new UTF8Encoding(false);
        if (!File.Exists(path)) return utf8NoBom;
        try
        {
            var bytes = File.ReadAllBytes(path);

            // BOM あり → UTF-8
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(true);

            // BOM なし: 妥当な UTF-8 として読めるか厳密判定（ASCII も妥当 UTF-8）
            try
            {
                var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
                strict.GetString(bytes);
                return utf8NoBom;
            }
            catch (DecoderFallbackException)
            {
                // UTF-8 として不正 → 既存の Shift_JIS ファイルとみなす
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(932);
            }
        }
        catch
        {
            return utf8NoBom;
        }
    }

    /// <summary>
    /// 任意ファイルをエンコーディング自動判定で読み込む（リストア用）。
    /// 読み込みは StreamReader（File.ReadAllText）経由のため BOM は自動的に除去される。
    /// </summary>
    public static string ReadTextDetect(string path)
        => File.ReadAllText(path, DetectEncoding(path));

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

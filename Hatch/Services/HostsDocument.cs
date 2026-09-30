using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Hatch.Models;

namespace Hatch.Services;

/// <summary>
/// hosts ファイルのテキストとエントリ一覧を相互変換する純関数群（I/O なし）。
/// </summary>
public static class HostsDocument
{
    /// <summary>Hatch が書き込み時に先頭へ付けるヘッダー行。読み込み時は捨てて再生成する。</summary>
    public const string Header = "# Managed by Hatch";

    private const string NewLine = "\r\n";

    // IPv4 は各オクテット 0-255 に限定（999.x.x.x 等の不正値を弾く）。IPv6 は緩め。
    private const string Ipv4Octet = @"(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)";
    private const string IpPattern =
        Ipv4Octet + @"(?:\." + Ipv4Octet + @"){3}|[0-9a-fA-F]*:[0-9a-fA-F:]+";
    private static readonly Regex EntryPattern =
        new(@"^(?<disabled>#\s*)?(?<ip>" + IpPattern + @")\s+(?<hosts>.+)$");
    private static readonly Regex IpOnlyPattern = new(@"^(?:" + IpPattern + @")$");

    private static readonly Regex HostnamePattern = new(@"^[A-Za-z0-9._-]+$");

    private static readonly char[] Whitespace = { ' ', '\t' };

    // ==================== Parse ====================

    public static IReadOnlyList<HostEntry> Parse(string text)
    {
        var entries = new List<HostEntry>();

        foreach (var line in SplitLines(text))
        {
            // ブランク行
            if (string.IsNullOrWhiteSpace(line))
            {
                entries.Add(RawLine(line));
                continue;
            }

            var trimmed = line.TrimStart();

            // "# Managed by Hatch" は保持しない（書き込み時に再生成する）
            if (trimmed.StartsWith(Header, StringComparison.OrdinalIgnoreCase))
                continue;

            var match = EntryPattern.Match(trimmed);
            if (!match.Success)
            {
                // 純粋なコメント行など
                entries.Add(RawLine(line));
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
            var hostnames = hostsPart.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);

            // ホスト名が 1 つもない行（"10.0.0.1 # memo" 等）はエントリにできないので原文のまま残す
            if (hostnames.Length == 0)
            {
                entries.Add(RawLine(line));
                continue;
            }

            // "# 192.168.1.1 is the router" のような IP を含む説明コメントを無効エントリに化けさせない
            if (disabled && !LooksLikeDisabledEntry(ip, hostnames))
            {
                entries.Add(RawLine(line));
                continue;
            }

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
    /// "#" で始まる行を無効エントリとみなしてよいか。
    /// IP が実際に解釈でき、全トークンがホスト名の文字種だけでできていることが前提。
    /// そのうえでトークンが 2 つ以上ある場合は、各トークンがドットを含む（または localhost）ときだけ
    /// エントリとみなす（"is the router" のような英文は単語がホスト名の文字種を満たしてしまうため）。
    /// </summary>
    private static bool LooksLikeDisabledEntry(string ip, string[] tokens)
    {
        if (!IsValidIpAddress(ip)) return false;
        if (tokens.Length == 0 || !tokens.All(IsValidHostname)) return false;
        if (tokens.Length == 1) return true;
        return tokens.All(t => t.Contains('.')
                               || t.Equals("localhost", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>File.ReadAllLines と同じ規則で行に分割する（末尾改行の後に空行を作らない）。</summary>
    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
            yield return line;
    }

    private static HostEntry RawLine(string line) => new() { IsRawLine = true, RawText = line };

    /// <summary>
    /// localhost 等のシステム既定エントリかどうかを判定する（削除・無効化の保護対象）。
    /// </summary>
    public static bool IsSystemEntry(string ip, string hostname)
        => (ip == "127.0.0.1" || ip == "::1")
           && hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    // ==================== Render ====================

    public static string Render(IEnumerable<HostEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append(NewLine);
        sb.Append(NewLine);

        foreach (var entry in entries)
        {
            if (entry.IsRawLine)
            {
                sb.Append(entry.RawText).Append(NewLine);
                continue;
            }

            var line = entry.IsEnabled
                ? $"{entry.IpAddress} {entry.Hostname}"
                : $"# {entry.IpAddress} {entry.Hostname}";

            if (!string.IsNullOrWhiteSpace(entry.Comment))
                line += $" # {entry.Comment}";

            sb.Append(line).Append(NewLine);
        }

        return sb.ToString();
    }

    // ==================== Validation ====================

    /// <summary>
    /// IPv4 / IPv6 として解釈でき、かつ Parse が IP として読み戻せる表記か。
    /// IPAddress.TryParse は "1"（= 0.0.0.1）やゾーン付き IPv6 も受け付けるが、
    /// それらを書くと次回の読み込みでエントリとして認識されないため弾く。
    /// </summary>
    public static bool IsValidIpAddress(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        if (!IPAddress.TryParse(ip, out var address)) return false;
        if (address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            return false;
        return IpOnlyPattern.IsMatch(ip);
    }

    /// <summary>ホスト名が英数字と . _ - だけでできているか。</summary>
    public static bool IsValidHostname(string? hostname)
        => !string.IsNullOrEmpty(hostname) && HostnamePattern.IsMatch(hostname);

    /// <summary>コメントが hosts の行を壊さないか（"#" を含むと次回の読み込みで分割がずれる）。</summary>
    public static bool IsValidComment(string? comment)
        => comment == null || !comment.Contains('#');
}

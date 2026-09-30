using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Hatch.Models;

namespace Hatch.Services;

/// <summary>
/// hosts ファイルの I/O（エンコーディング判定・読み書き・DNS フラッシュ）。
/// hosts への書き込みはすべて <see cref="WriteHostsText"/> を通り、
/// 自動バックアップ → 一時ファイル経由の原子的置換 → 読み戻し検証 の順で行う。
/// </summary>
public class HostsFileService
{
    /// <summary>自動バックアップの保持世代数。</summary>
    public const int BackupGenerations = 20;

    private const string BackupPrefix = "hosts.";
    private const string BackupExtension = ".txt";
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    public static string DefaultHostsPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                     "drivers", "etc", "hosts");

    public static string DefaultBackupDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "Hatch", "backups");

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public string HostsPath { get; }
    public string BackupDirectory { get; }

    /// <summary>バックアップ名の時刻（テストで差し替える）。</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>置換直後・読み戻し前に呼ばれるフック（テストで書込後の改変を模擬する）。</summary>
    internal Action? AfterReplaceForTest { get; set; }

    public HostsFileService() : this(DefaultHostsPath, DefaultBackupDirectory) { }

    public HostsFileService(string hostsPath, string backupDirectory)
    {
        HostsPath = hostsPath;
        BackupDirectory = backupDirectory;
    }

    // ==================== 読み込み ====================

    public List<HostEntry> ReadHostsFile() => HostsDocument.Parse(ReadHostsFileRaw()).ToList();

    public string ReadHostsFileRaw()
    {
        if (!File.Exists(HostsPath))
            return string.Empty;

        return File.ReadAllText(HostsPath, DetectEncoding(HostsPath));
    }

    // ==================== 書き込み（唯一の経路） ====================

    public void WriteHostsFile(IEnumerable<HostEntry> entries) => WriteHostsText(HostsDocument.Render(entries));

    /// <summary>
    /// hosts を書き換える唯一の経路。
    /// 1. 現在の hosts をバックアップフォルダへ退避（直近 <see cref="BackupGenerations"/> 世代を保持）
    /// 2. 同じフォルダの一時ファイルに UTF-8(BOM なし) で書き、File.Move(overwrite) で置換
    /// 3. 読み戻して Parse→Render の結果が一致するか検証。不一致ならバックアップへ戻して例外
    /// </summary>
    public void WriteHostsText(string content)
    {
        var backupPath = CreateBackup();

        ReplaceAtomically(Utf8NoBom.GetBytes(content));
        AfterReplaceForTest?.Invoke();

        var expected = Normalize(HostsDocument.Render(HostsDocument.Parse(content)));
        var actual = Normalize(HostsDocument.Render(HostsDocument.Parse(ReadHostsFileRaw())));
        if (expected == actual)
            return;

        if (backupPath != null)
        {
            ReplaceAtomically(File.ReadAllBytes(backupPath));
            throw new HostsWriteVerificationException(
                "hosts ファイルへの書き込み後、読み戻した内容が書いた内容と一致しませんでした。\n" +
                $"書き込み前の状態（{Path.GetFileName(backupPath)}）に戻しました。\n" +
                "他のソフト（セキュリティソフト等）が hosts を書き換えていないか確認してください。");
        }

        throw new HostsWriteVerificationException(
            "hosts ファイルへの書き込み後、読み戻した内容が書いた内容と一致しませんでした。\n" +
            "書き込み前の hosts が存在しなかったため、元に戻せていません。");
    }

    /// <summary>最新の自動バックアップを hosts へ書き戻す（書き戻し自体も同じ経路でバックアップされる）。</summary>
    public void RestoreBackup(string backupPath) => WriteHostsText(ReadTextDetect(backupPath));

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private void ReplaceAtomically(byte[] bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(HostsPath))!;
        var tempPath = Path.Combine(directory, $"hosts.hatch-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tempPath, HostsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    // ==================== 自動バックアップ ====================

    /// <summary>
    /// 現在の hosts をバックアップフォルダへコピーし、そのパスを返す（hosts が無ければ null）。
    /// 保持世代数を超えた古いバックアップは削除する。
    /// </summary>
    internal string? CreateBackup()
    {
        if (!File.Exists(HostsPath))
            return null;

        Directory.CreateDirectory(BackupDirectory);

        var stamp = Clock().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var path = Path.Combine(BackupDirectory, $"{BackupPrefix}{stamp}{BackupExtension}");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(BackupDirectory, $"{BackupPrefix}{stamp}-{n}{BackupExtension}");

        File.Copy(HostsPath, path);
        PruneBackups();
        return path;
    }

    /// <summary>自動バックアップを新しい順に返す。</summary>
    public IReadOnlyList<string> ListBackups()
    {
        if (!Directory.Exists(BackupDirectory))
            return Array.Empty<string>();

        return Directory.GetFiles(BackupDirectory, BackupPrefix + "*" + BackupExtension)
            .Select(p => (Path: p, Key: ParseBackupKey(Path.GetFileName(p))))
            .Where(x => x.Key != null)
            .OrderByDescending(x => x.Key!.Value.Stamp, StringComparer.Ordinal)
            .ThenByDescending(x => x.Key!.Value.Sequence)
            .Select(x => x.Path)
            .ToList();
    }

    public string? GetLatestBackup() => ListBackups().FirstOrDefault();

    /// <summary>バックアップ名から時刻を読む（一覧表示用）。</summary>
    public static DateTime? GetBackupTime(string backupPath)
    {
        var key = ParseBackupKey(Path.GetFileName(backupPath));
        if (key == null) return null;
        return DateTime.ParseExact(key.Value.Stamp, TimestampFormat, CultureInfo.InvariantCulture);
    }

    private void PruneBackups()
    {
        foreach (var old in ListBackups().Skip(BackupGenerations))
        {
            try { File.Delete(old); } catch { }
        }
    }

    /// <summary>"hosts.yyyyMMdd-HHmmss[-n].txt" を (時刻文字列, 連番) に分解する。該当しなければ null。</summary>
    private static (string Stamp, int Sequence)? ParseBackupKey(string fileName)
    {
        if (fileName.Length <= BackupPrefix.Length + BackupExtension.Length
            || !fileName.StartsWith(BackupPrefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(BackupExtension, StringComparison.OrdinalIgnoreCase))
            return null;

        var body = fileName[BackupPrefix.Length..^BackupExtension.Length];
        if (body.Length < TimestampFormat.Length)
            return null;

        var stamp = body[..TimestampFormat.Length];
        if (!DateTime.TryParseExact(stamp, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return null;

        var rest = body[TimestampFormat.Length..];
        if (rest.Length == 0)
            return (stamp, 1);
        if (rest[0] == '-' && int.TryParse(rest[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var seq))
            return (stamp, seq);
        return null;
    }

    // ==================== エンコーディング ====================

    /// <summary>
    /// ファイルのエンコーディングを内容ベースで検出する。
    /// BOM あり、またはバイト列が妥当な UTF-8 なら UTF-8（ASCII も妥当 UTF-8 なので無害）。
    /// UTF-8 として不正な場合のみ CP932(Shift_JIS) にフォールバックする。
    /// Hatch は常に UTF-8(BOM なし) で書き込むため、この判定で読み書きが往復一致する。
    /// </summary>
    internal static Encoding DetectEncoding(string path)
    {
        if (!File.Exists(path)) return Utf8NoBom;
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
                return Utf8NoBom;
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
            return Utf8NoBom;
        }
    }

    /// <summary>
    /// 任意ファイルをエンコーディング自動判定で読み込む（リストア用）。
    /// 読み込みは StreamReader（File.ReadAllText）経由のため BOM は自動的に除去される。
    /// </summary>
    public static string ReadTextDetect(string path)
        => File.ReadAllText(path, DetectEncoding(path));

    // ==================== その他 ====================

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

/// <summary>書き込み後の読み戻し検証に失敗したことを表す。</summary>
public class HostsWriteVerificationException : IOException
{
    public HostsWriteVerificationException(string message) : base(message) { }
}

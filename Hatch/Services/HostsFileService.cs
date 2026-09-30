using System.Diagnostics;
using System.IO;
using System.Text;
using Hatch.Models;

namespace Hatch.Services;

public class HostsFileService
{
    private static readonly string HostsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                     "drivers", "etc", "hosts");

    public List<HostEntry> ReadHostsFile() => HostsDocument.Parse(ReadHostsFileRaw()).ToList();

    public void WriteHostsFile(IEnumerable<HostEntry> entries) => WriteHostsFileRaw(HostsDocument.Render(entries));

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

    internal static Encoding DetectEncoding(string path)
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

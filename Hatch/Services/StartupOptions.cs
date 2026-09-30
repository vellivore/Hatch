using System.IO;

namespace Hatch.Services;

/// <summary>
/// 開発・動作確認用のコマンドライン指定。
///   --hosts &lt;path&gt;   実際の hosts の代わりにこのファイルを読み書きする（バックアップは backups\dev\ へ）
///   --no-elevate      自己昇格しない（--hosts と一緒のときだけ有効）
///   --config &lt;path&gt;  設定ファイルの場所を固定する
/// </summary>
public sealed class StartupOptions
{
    public string? HostsPath { get; init; }
    public string? ConfigPath { get; init; }
    public bool NoElevateRequested { get; init; }

    /// <summary>昇格を省くか。実際の hosts を書く可能性がある場合（--hosts なし）は無視する。</summary>
    public bool SkipElevation => NoElevateRequested && HostsPath != null;

    public static StartupOptions Current { get; set; } = new();

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? hosts = null, config = null;
        var noElevate = false;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--hosts" when i + 1 < args.Count:
                    hosts = Path.GetFullPath(args[++i]);
                    break;
                case "--config" when i + 1 < args.Count:
                    config = Path.GetFullPath(args[++i]);
                    break;
                case "--no-elevate":
                    noElevate = true;
                    break;
            }
        }

        return new StartupOptions { HostsPath = hosts, ConfigPath = config, NoElevateRequested = noElevate };
    }

    /// <summary>昇格して再起動するときに渡す引数（パスは解決済みの絶対パス）。</summary>
    public IReadOnlyList<string> ToArgs()
    {
        var args = new List<string>();
        if (HostsPath != null) { args.Add("--hosts"); args.Add(HostsPath); }
        if (ConfigPath != null) { args.Add("--config"); args.Add(ConfigPath); }
        if (NoElevateRequested) args.Add("--no-elevate");
        return args;
    }

    public HostsFileService CreateHostsService()
        => HostsPath == null
            ? new HostsFileService()
            : new HostsFileService(HostsPath, Path.Combine(HostsFileService.DefaultBackupDirectory, "dev"));

    public SettingsService CreateSettingsService()
        => ConfigPath == null ? new SettingsService() : new SettingsService(ConfigPath);
}

using System.Diagnostics;
using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Hatch;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private static Thread? _listenerThread;
    private static volatile bool _stopping;
    private const string MutexName = "Hatch_SingleInstance_Mutex";
    private const string EventName = "Hatch_ShowWindow_Event";

    protected override void OnStartup(StartupEventArgs e)
    {
        // 1. 単一インスタンス確認を昇格より先に行う。
        //    既に起動中なら UAC を出さずに既存インスタンスへ表示要求だけ送って終了する。
        if (!TryAcquireSingleInstance())
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        // 2. hosts 書込には管理者権限が必須。非昇格で起動された場合は
        //    UAC で自身を昇格起動し直し、このインスタンスは終了する。
        //    （単一ファイル publish ではマニフェストの requireAdministrator が
        //      効かない場合があるため、コードで確実に昇格させる）
        if (!IsRunningAsAdmin())
        {
            // 昇格後のインスタンスがミューテックスを取れるよう、先に手放す
            ReleaseSingleInstance();
            if (TryRelaunchAsAdmin())
            {
                Shutdown();
                return;
            }
            // ユーザーが UAC を拒否 → 読み取り専用で続行（保存時に従来の警告）
            if (!TryAcquireSingleInstance())
            {
                SignalExistingInstance();
                Shutdown();
                return;
            }
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"エラーが発生しました:\n\n{args.Exception}", "Hatch",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // 他インスタンスからの表示要求を待つイベント
        _showEvent = CreateShowEvent();
        _listenerThread = new Thread(ListenForShowRequests)
        {
            IsBackground = true,
            Name = "Hatch_ShowWindowListener"
        };
        _listenerThread.Start();

        base.OnStartup(e);
    }

    private void ListenForShowRequests()
    {
        var evt = _showEvent;
        if (evt == null) return;
        try
        {
            while (!_stopping)
            {
                evt.WaitOne();
                if (_stopping) break;

                var dispatcher = Dispatcher;
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) break;
                dispatcher.BeginInvoke(() =>
                {
                    if (_stopping || MainWindow == null) return;
                    MainWindow.Show();
                    MainWindow.WindowState = WindowState.Normal;
                    MainWindow.Activate();
                });
            }
        }
        catch (ObjectDisposedException)
        {
            // 終了処理と競合した場合は黙って抜ける
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 停止フラグを先に立ててから起こす（リスナーは起床後にフラグを見て抜ける）
        _stopping = true;
        try { _showEvent?.Set(); } catch (ObjectDisposedException) { }
        _listenerThread?.Join(1000);
        _showEvent?.Dispose();
        _showEvent = null;
        ReleaseSingleInstance();
        base.OnExit(e);
    }

    /// <summary>
    /// 単一インスタンス用ミューテックスを取得する。取得できれば true。
    /// 既に別インスタンス（昇格済みを含む）が保持していれば false。
    /// </summary>
    private static bool TryAcquireSingleInstance()
    {
        try
        {
            var mutex = MutexAcl.Create(true, MutexName, out bool createdNew, CreateMutexSecurity());
            if (!createdNew)
            {
                mutex.Dispose();
                return false;
            }
            _mutex = mutex;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // 他インスタンスが作成したミューテックスを開く権限がない場合も「起動中」とみなす
            return false;
        }
    }

    private static void ReleaseSingleInstance()
    {
        if (_mutex == null) return;
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
        _mutex = null;
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var evt = EventWaitHandle.OpenExisting(EventName);
            evt.Set();
        }
        catch
        {
            // 既存インスタンスがまだイベントを作っていない等。致命的ではない
        }
    }

    /// <summary>
    /// 表示要求イベントを作成する。昇格済みインスタンスが作ったイベントを
    /// 非昇格の 2 回目起動からシグナルできるよう、現在ユーザーにもアクセスを許可する。
    /// </summary>
    private static EventWaitHandle CreateShowEvent()
    {
        var security = new EventWaitHandleSecurity();
        foreach (var sid in AllowedSids())
        {
            security.AddAccessRule(new EventWaitHandleAccessRule(
                sid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        }
        return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, EventName, out _, security);
    }

    private static MutexSecurity CreateMutexSecurity()
    {
        var security = new MutexSecurity();
        foreach (var sid in AllowedSids())
        {
            security.AddAccessRule(new MutexAccessRule(
                sid, MutexRights.FullControl, AccessControlType.Allow));
        }
        return security;
    }

    private static List<SecurityIdentifier> AllowedSids()
    {
        var sids = new List<SecurityIdentifier>();
        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (identity.User != null)
                sids.Add(identity.User);
        }
        sids.Add(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        sids.Add(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        return sids;
    }

    /// <summary>現在のプロセスが管理者として実行されているか。</summary>
    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// 自身を管理者権限で再起動する（UAC ダイアログ表示）。
    /// 成功すれば true（呼び出し側は現インスタンスを終了する）。
    /// UAC 拒否・失敗時は false。
    /// </summary>
    private static bool TryRelaunchAsAdmin()
    {
        // 単一ファイル publish では Environment.ProcessPath が apphost(.exe) を指す
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return false;

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
        };

        try
        {
            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ERROR_CANCELLED (1223) 等: ユーザーが UAC を拒否
            return false;
        }
    }
}

/// <summary>
/// bool を反転する。IsSystem=true → IsEnabled=false でチェックボックスを無効化。
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>
/// グループ名からハッシュベースで背景色を生成する。
/// ダークテーマに合う低彩度・低明度の色を返す。
/// </summary>
public class GroupColorConverter : IValueConverter
{
    private static readonly Color[] Palette = new[]
    {
        Color.FromArgb(40, 0x4F, 0xA3, 0xD6), // 青
        Color.FromArgb(40, 0x6B, 0xB3, 0x6B), // 緑
        Color.FromArgb(40, 0xD6, 0x8F, 0x4F), // オレンジ
        Color.FromArgb(40, 0xB3, 0x6B, 0xB3), // 紫
        Color.FromArgb(40, 0xD6, 0xD6, 0x4F), // 黄
        Color.FromArgb(40, 0xD6, 0x4F, 0x4F), // 赤
        Color.FromArgb(40, 0x4F, 0xD6, 0xC0), // ティール
        Color.FromArgb(40, 0xD6, 0x6B, 0xA3), // ピンク
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string groupName || string.IsNullOrEmpty(groupName))
            return System.Windows.Media.Brushes.Transparent;

        // ルートグループで色を統一（"業務/案件A/環境A" → "業務" でハッシュ）
        var root = groupName.Split('/')[0];
        var color = Palette[StableHash(root) % (uint)Palette.Length];
        return new SolidColorBrush(color);
    }

    /// <summary>
    /// FNV-1a (32bit) を UTF-16 コード単位に適用した決定的ハッシュ。
    /// string.GetHashCode() はプロセスごとにランダム化されるため、色が起動ごとに変わってしまう。
    /// </summary>
    internal static uint StableHash(string text)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= prime;
        }
        return hash;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// グループ名をツリー風インデント表示用に変換する。
/// "業務/案件A/環境A" → "    環境A"（末端のみ表示、階層分インデント）
/// フィルター用: フルパスも保持。
/// </summary>
public class GroupDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrEmpty(name) || name == "すべて")
            return value ?? "";

        var parts = name.Split('/');
        var indent = new string('\u2003', parts.Length - 1); // em space for indent
        return $"{indent}{parts[^1]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// プリセット名の表示用。内部の番兵 "なし" を「すべて無効」と表示する。
/// </summary>
public class PresetDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string name ? Hatch.Services.PresetLogic.DisplayName(name) : value ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

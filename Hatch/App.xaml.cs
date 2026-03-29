using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Hatch;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private const string MutexName = "Hatch_SingleInstance_Mutex";
    private const string EventName = "Hatch_ShowWindow_Event";

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"エラーが発生しました:\n\n{args.Exception}", "Hatch",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _mutex = new Mutex(true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            // 既に起動中 → イベントをシグナルして既存インスタンスのウィンドウを表示させる
            try
            {
                var evt = EventWaitHandle.OpenExisting(EventName);
                evt.Set();
                evt.Dispose();
            }
            catch { }
            Shutdown();
            return;
        }

        // 他インスタンスからの表示要求を待つイベント
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                Dispatcher.Invoke(() =>
                {
                    MainWindow?.Show();
                    MainWindow!.WindowState = WindowState.Normal;
                    MainWindow.Activate();
                });
            }
        })
        {
            IsBackground = true,
            Name = "Hatch_ShowWindowListener"
        };
        thread.Start();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Set(); // リスナースレッドを解放
        _showEvent?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
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
/// IsSystem=true のとき Opacity=0.45 でグレーアウト表示。
/// </summary>
public class SystemEntryOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool isSystem && isSystem ? 0.45 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
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
        var hash = Math.Abs(root.GetHashCode());
        var color = Palette[hash % Palette.Length];
        return new SolidColorBrush(color);
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

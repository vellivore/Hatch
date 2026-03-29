using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Data;

namespace Hatch;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private const string MutexName = "Hatch_SingleInstance_Mutex";
    private const string EventName = "Hatch_ShowWindow_Event";

    protected override void OnStartup(StartupEventArgs e)
    {
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

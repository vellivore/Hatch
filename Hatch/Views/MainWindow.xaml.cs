using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Hatch.Services;
using Hatch.ViewModels;
using WinForms = System.Windows.Forms;

namespace Hatch.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private WinForms.NotifyIcon? _notifyIcon;

    // Column header → sort property mapping
    private static readonly Dictionary<string, string> ColumnMap = new()
    {
        { "有効", "IsEnabled" },
        { "グループ", "GroupName" },
        { "IPアドレス", "IpAddress" },
        { "ホスト名", "Hostname" },
    };

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(ColumnHeader_Click));

        Loaded += (_, _) =>
        {
            var s = ViewModel.Settings;
            if (s.WindowLeft is double left && s.WindowTop is double top && IsOnVirtualScreen(left, top))
            {
                Left = left;
                Top = top;
            }
            Width = s.WindowWidth;
            Height = s.WindowHeight;

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "";
            TitleText.Text = StartupOptions.Current.HostsPath == null
                ? $"Hatch {versionStr}"
                : $"Hatch {versionStr} [dev: {StartupOptions.Current.HostsPath}]";

            InitializeNotifyIcon();
        };
    }

    /// <summary>
    /// 保存済みの左上座標が現在の仮想スクリーン（全モニタの外接矩形）内にあるか。
    /// モニタ構成が変わって画面外に出てしまう復元を防ぐ。
    /// </summary>
    private static bool IsOnVirtualScreen(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top)) return false;
        return left >= SystemParameters.VirtualScreenLeft
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && top >= SystemParameters.VirtualScreenTop
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
    }

    /// <summary>
    /// ウィンドウ位置を保存する。最小化中（トレイ格納を含む）は Left/Top が -32000 になるため
    /// RestoreBounds（通常表示時の位置）を使う。
    /// </summary>
    private void SaveWindowSettings()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (bounds.IsEmpty)
        {
            ViewModel.SaveSettings(null, null, Width, Height);
            return;
        }
        ViewModel.SaveSettings(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }

    // ==================== キーボード ====================

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            TryApply();
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ReloadWithConfirm();
            e.Handled = true;
        }
    }

    // ==================== 並べ替え ====================

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header) return;
        if (header.Role == GridViewColumnHeaderRole.Padding) return;
        if (header.Column == null) return;

        // グループ列はフィルター用ComboBoxなのでソートしない
        if (header.Column.Header is System.Windows.Controls.ComboBox) return;

        var headerText = (header.Column.Header?.ToString() ?? "").TrimEnd(' ', '^', 'v');
        if (!ColumnMap.TryGetValue(headerText, out var sortProperty)) return;

        ViewModel.SortByColumn(sortProperty);
        UpdateSortIndicators();
    }

    private void UpdateSortIndicators()
    {
        var gridView = EntryListView.View as GridView;
        if (gridView == null) return;

        foreach (var col in gridView.Columns)
        {
            var text = (col.Header?.ToString() ?? "").TrimEnd(' ', '^', 'v');
            if (!ColumnMap.TryGetValue(text, out var prop)) continue;

            if (prop == ViewModel.SortColumn)
                col.Header = $"{text} {(ViewModel.SortAscending ? "^" : "v")}";
            else
                col.Header = text;
        }
    }

    // ==================== 保存・最新化 ====================

    private void ApplyButton_Click(object sender, RoutedEventArgs e) => TryApply();

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => ReloadWithConfirm();

    /// <summary>一覧を hosts に保存する。権限がない・失敗したときはメッセージを出して false。</summary>
    private bool TryApply()
    {
        if (!ViewModel.CanWrite)
        {
            MessageBox.Show(
                "hostsファイルへの書き込み権限がありません。\n管理者として実行してください。",
                "Hatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            ViewModel.Apply();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"書き込みエラー: {ex.Message}", "Hatch",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void ReloadWithConfirm()
    {
        if (ViewModel.IsDirty)
        {
            var result = MessageBox.Show(
                "未保存の変更があります。破棄して最新化しますか？",
                "Hatch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;
        }
        ViewModel.Reload();
    }

    // ==================== プリセット切替 ====================

    /// <summary>
    /// ComboBox で選ばれたときだけ切り替える。VM 側の値の変化（バインド経由の選択更新）は
    /// VM の値と一致するので無視される。
    /// </summary>
    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetComboBox.SelectedItem is not string selected) return;
        if (selected == ViewModel.SelectedPresetName) return;

        // ドロップダウンが閉じてから確認ダイアログを出す
        Dispatcher.BeginInvoke(() => SwitchPresetFromWindow(selected));
    }

    private void SwitchPresetFromWindow(string presetName)
    {
        try
        {
            if (presetName == ViewModel.SelectedPresetName) return;

            var result = ViewModel.SwitchPreset(presetName);
            if (result == PresetSwitchResult.NeedsConfirmation)
            {
                var dialog = new ConfirmSwitchDialog(PresetLogic.DisplayName(presetName)) { Owner = this };
                dialog.ShowDialog();
                switch (dialog.Choice)
                {
                    case SwitchConfirmChoice.ApplyAndSwitch:
                        if (!TryApply()) return;
                        result = ViewModel.SwitchPreset(presetName, force: true);
                        break;
                    case SwitchConfirmChoice.DiscardAndSwitch:
                        ViewModel.Reload();
                        result = ViewModel.SwitchPreset(presetName, force: true);
                        break;
                    default:
                        return;
                }
            }

            if (result == PresetSwitchResult.Failed)
            {
                MessageBox.Show(ViewModel.StatusText, "Hatch", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            // 取り消し・失敗時は ComboBox を VM の現在値に戻す（成功時も同じ値に揃うだけ）
            SyncPresetComboBox();
        }
    }

    private void SyncPresetComboBox()
        => BindingOperations.GetBindingExpression(PresetComboBox, Selector.SelectedItemProperty)?.UpdateTarget();

    /// <summary>トレイからの切替。未保存の変更があるときは確認できないので拒否してバルーンで知らせる。</summary>
    private void SwitchPresetFromTray(string presetName)
    {
        if (ViewModel.IsDirty)
        {
            _notifyIcon?.ShowBalloonTip(
                5000, "Hatch",
                "一覧に未保存の変更があるため、プリセットを切り替えませんでした。\n" +
                "ウィンドウを開いて適用するか、最新化で破棄してから切り替えてください。",
                WinForms.ToolTipIcon.Warning);
            return;
        }

        var result = ViewModel.SwitchPreset(presetName);
        if (result == PresetSwitchResult.Failed)
        {
            _notifyIcon?.ShowBalloonTip(5000, "Hatch", ViewModel.StatusText, WinForms.ToolTipIcon.Error);
        }
        SyncPresetComboBox();
    }

    // ==================== タスクトレイ ====================

    private void InitializeNotifyIcon()
    {
        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = TrayTooltip(),
        };

        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
        RebuildTrayMenu();

        // プリセット変更時にトレイメニューとツールチップを更新
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedPresetName)
                || e.PropertyName == nameof(MainViewModel.PresetNames))
            {
                RebuildTrayMenu();
                if (_notifyIcon != null)
                    _notifyIcon.Text = TrayTooltip();
            }
        };
    }

    private string TrayTooltip()
    {
        // NotifyIcon.Text は 127 文字まで
        var text = $"Hatch - {ViewModel.ActivePresetDisplayName}";
        return text.Length > 127 ? text[..127] : text;
    }

    private void RebuildTrayMenu()
    {
        if (_notifyIcon == null) return;

        var menu = new WinForms.ContextMenuStrip();

        // プリセット一覧
        foreach (var name in ViewModel.PresetNames)
        {
            if (name == PresetLogic.NoneName) continue;

            var item = new WinForms.ToolStripMenuItem(name)
            {
                Checked = name == ViewModel.SelectedPresetName,
            };
            var presetName = name; // capture for lambda
            item.Click += (_, _) => Dispatcher.Invoke(() => SwitchPresetFromTray(presetName));
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
            menu.Items.Add(new WinForms.ToolStripSeparator());

        // "すべて無効"
        var noneItem = new WinForms.ToolStripMenuItem(PresetLogic.NoneDisplayName)
        {
            Checked = ViewModel.SelectedPresetName == PresetLogic.NoneName,
        };
        noneItem.Click += (_, _) => Dispatcher.Invoke(() => SwitchPresetFromTray(PresetLogic.NoneName));
        menu.Items.Add(noneItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        // "直前の状態に戻す"（最新の自動バックアップを書き戻す）
        var undoItem = new WinForms.ToolStripMenuItem("直前の状態に戻す")
        {
            Enabled = ViewModel.LatestAutoBackup != null,
        };
        undoItem.Click += (_, _) => Dispatcher.Invoke(RestoreLatestBackup);
        menu.Items.Add(undoItem);
        // 開くたびに有効/無効を最新にする（バックアップは書き込みのたびに増えるため）
        menu.Opening += (_, _) => undoItem.Enabled = ViewModel.LatestAutoBackup != null;

        menu.Items.Add(new WinForms.ToolStripSeparator());

        // "表示"
        var showItem = new WinForms.ToolStripMenuItem("表示");
        showItem.Click += (_, _) => Dispatcher.Invoke(ShowWindow);
        menu.Items.Add(showItem);

        // "終了"
        var exitItem = new WinForms.ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            SaveWindowSettings();
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            Application.Current.Shutdown();
        });
        menu.Items.Add(exitItem);

        var old = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = menu;
        old?.Dispose();
    }

    private void RestoreLatestBackup()
    {
        if (!ViewModel.CanWrite)
        {
            MessageBox.Show("hostsファイルへの書き込み権限がありません。\n管理者として実行してください。",
                "Hatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var latest = ViewModel.LatestAutoBackup;
        if (latest == null)
        {
            MessageBox.Show("自動バックアップがまだありません。", "Hatch",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var time = HostsFileService.GetBackupTime(latest);
        var message = $"hosts を {time:yyyy/MM/dd HH:mm:ss} の状態に戻しますか？";
        if (ViewModel.IsDirty)
            message += "\n\n一覧の未保存の変更は破棄されます。";
        if (MessageBox.Show(message, "Hatch", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            ViewModel.RestoreLatestBackup();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"復元エラー: {ex.Message}", "Hatch", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon != null) return icon;
            }
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    // ==================== タイトルバー ====================

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // ✕ ボタンはトレイに格納（終了はトレイメニューから）
        SaveWindowSettings();
        Hide();
    }

    // ==================== エントリ操作 ====================

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EntryDialog(ViewModel.Groups) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            ViewModel.AddEntry(dialog.Result);
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        EditSelectedEntry();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = ViewModel.SelectedEntry;
        if (entry == null || entry.IsSystem || entry.IsRawLine)
            return;

        var result = MessageBox.Show(
            $"{entry.Hostname} を削除しますか？",
            "Hatch", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            ViewModel.DeleteEntry(entry);
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 列ヘッダーのダブルクリックは無視（ソート操作）
        var hitElement = e.OriginalSource as DependencyObject;
        while (hitElement != null)
        {
            if (hitElement is GridViewColumnHeader) return;
            hitElement = System.Windows.Media.VisualTreeHelper.GetParent(hitElement);
        }
        EditSelectedEntry();
    }

    private void EditSelectedEntry()
    {
        if (ViewModel.SelectedEntry == null || ViewModel.SelectedEntry.IsSystem)
            return;

        var dialog = new EntryDialog(ViewModel.Groups, ViewModel.SelectedEntry) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            ViewModel.UpdateEntry(ViewModel.SelectedEntry, dialog.Result);
        }
    }

    // ==================== プリセット保存・管理 ====================

    private void ManageButton_Click(object sender, RoutedEventArgs e)
    {
        var currentGroups = ViewModel.Groups.Where(g => g != "すべて").ToList();
        var dialog = new ManageDialog(currentGroups, ViewModel.Presets)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.UpdatePresets(dialog.ResultPresets, dialog.ResultRenames);
        }
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NameDialog() { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            if (dialog.ResultName == PresetLogic.NoneName || dialog.ResultName == PresetLogic.NoneDisplayName)
            {
                MessageBox.Show($"「{dialog.ResultName}」はプリセット名に使えません。", "Hatch",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ViewModel.SaveCurrentStateAsPreset(dialog.ResultName);
        }
    }

    // ==================== テキスト編集 ====================

    private void TextEditorButton_Click(object sender, RoutedEventArgs e)
    {
        // テキスト編集は hosts の現在の内容を開き、保存すると一覧を読み直すため未保存の変更は失われる
        if (ViewModel.IsDirty
            && !ConfirmDialog.Ask(this, "未保存の変更があります。テキスト編集を開くと破棄されます。", "続行"))
            return;

        var content = ViewModel.ReadHostsRaw();
        var dialog = new TextEditorDialog(content) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Saved)
        {
            try
            {
                ViewModel.WriteHostsRaw(dialog.ResultText);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"書き込みエラー: {ex.Message}", "Hatch",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ==================== バックアップ / リストア ====================

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"hosts_backup_{timestamp}.txt",
            Filter = "テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            Title = "hostsファイルのバックアップ",
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                ViewModel.BackupHostsFile(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"バックアップエラー: {ex.Message}", "Hatch",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            Title = "hostsファイルのリストア",
        };

        if (dialog.ShowDialog() != true) return;

        var result = MessageBox.Show(
            "リストアすると現在のhostsファイルが上書きされます。続行しますか？",
            "Hatch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            ViewModel.RestoreHostsFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"リストアエラー: {ex.Message}", "Hatch",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveWindowSettings();
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        base.OnClosed(e);
    }
}

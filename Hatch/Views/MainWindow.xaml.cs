using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hatch.Models;
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
            if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop))
            {
                Left = s.WindowLeft;
                Top = s.WindowTop;
            }
            Width = s.WindowWidth;
            Height = s.WindowHeight;

            InitializeNotifyIcon();
        };
    }

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

    // --- System Tray ---
    private void InitializeNotifyIcon()
    {
        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = $"Hatch - {ViewModel.ActivePresetDisplayName}",
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
                    _notifyIcon.Text = $"Hatch - {ViewModel.ActivePresetDisplayName}";
            }
        };
    }

    private void RebuildTrayMenu()
    {
        if (_notifyIcon == null) return;

        var menu = new WinForms.ContextMenuStrip();

        // プリセット一覧
        foreach (var name in ViewModel.PresetNames)
        {
            if (name == "なし") continue;

            var item = new WinForms.ToolStripMenuItem(name)
            {
                Checked = name == ViewModel.SelectedPresetName,
            };
            var presetName = name; // capture for lambda
            item.Click += (_, _) =>
            {
                Dispatcher.Invoke(() => ViewModel.SwitchPreset(presetName));
            };
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
            menu.Items.Add(new WinForms.ToolStripSeparator());

        // "なし"
        var noneItem = new WinForms.ToolStripMenuItem("なし")
        {
            Checked = ViewModel.SelectedPresetName == "なし",
        };
        noneItem.Click += (_, _) =>
        {
            Dispatcher.Invoke(() => ViewModel.SwitchPreset("なし"));
        };
        menu.Items.Add(noneItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        // "表示"
        var showItem = new WinForms.ToolStripMenuItem("表示");
        showItem.Click += (_, _) => Dispatcher.Invoke(ShowWindow);
        menu.Items.Add(showItem);

        // "終了"
        var exitItem = new WinForms.ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            ViewModel.SaveSettings(Left, Top, Width, Height);
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            Application.Current.Shutdown();
        });
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = menu;
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

    // --- Title Bar ---
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // ✕ ボタンはトレイに格納（終了はトレイメニューから）
        ViewModel.SaveSettings(Left, Top, Width, Height);
        Hide();
    }

    // --- Entry CRUD ---
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

    private void ManageButton_Click(object sender, RoutedEventArgs e)
    {
        var currentGroups = ViewModel.Groups.Where(g => g != "すべて").ToList();
        var dialog = new ManageDialog(currentGroups, ViewModel.Presets)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.UpdatePresets(dialog.ResultPresets);
        }
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NameDialog() { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.SaveCurrentStateAsPreset(dialog.ResultName);
        }
    }

    private void CheckBox_Click(object sender, RoutedEventArgs e)
    {
        // CheckBox の TwoWay バインディングで IsEnabled は既に更新済み
        if (sender is CheckBox cb && cb.DataContext is HostEntry entry)
        {
            // IsEnabled は既にバインディングで変更済みなので何もしない
        }
    }

    // --- Text Editor ---
    private void TextEditorButton_Click(object sender, RoutedEventArgs e)
    {
        var content = ViewModel.ReadHostsRaw();
        var dialog = new TextEditorDialog(content) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Saved)
        {
            ViewModel.WriteHostsRaw(dialog.ResultText);
        }
    }

    // --- Backup / Restore ---
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
        ViewModel.SaveSettings(Left, Top, Width, Height);
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        base.OnClosed(e);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hatch.Models;

namespace Hatch.Views;

public partial class EntryDialog : Window
{
    public HostEntry? Result { get; private set; }

    private readonly HostEntry? _existing;
    private readonly List<string> _allGroups;
    private bool _suppressSelectionChanged;

    public EntryDialog(IEnumerable<string> groups, HostEntry? existing = null)
    {
        InitializeComponent();

        _existing = existing;
        _allGroups = groups.Where(g => g != "すべて").ToList();

        // 大区分の候補を設定
        RebuildGroup1();

        if (existing != null)
        {
            DialogTitle.Text = "エントリの編集";
            Title = "エントリの編集";
            IpTextBox.Text = existing.IpAddress;
            HostnameTextBox.Text = existing.Hostname;
            CommentTextBox.Text = existing.Comment;

            // 既存のグループ名を分解してセット
            if (!string.IsNullOrEmpty(existing.GroupName))
            {
                var parts = existing.GroupName.Split('/', 3);
                _suppressSelectionChanged = true;
                if (parts.Length >= 1) Group1ComboBox.Text = parts[0];
                RebuildGroup2();
                if (parts.Length >= 2) Group2ComboBox.Text = parts[1];
                RebuildGroup3();
                if (parts.Length >= 3) Group3ComboBox.Text = parts[2];
                _suppressSelectionChanged = false;
            }
        }
        else
        {
            IpTextBox.Text = "127.0.0.1";
        }

        AddButtons();
        IpTextBox.Focus();
    }

    private void RebuildGroup1()
    {
        var items = _allGroups
            .Select(g => g.Split('/')[0])
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        Group1ComboBox.Items.Clear();
        Group1ComboBox.Items.Add("");
        foreach (var item in items)
            Group1ComboBox.Items.Add(item);
    }

    private void RebuildGroup2()
    {
        var g1 = Group1ComboBox.Text?.Trim() ?? "";
        Group2ComboBox.Items.Clear();
        Group2ComboBox.Items.Add("");
        Group2ComboBox.Text = "";

        if (string.IsNullOrEmpty(g1)) return;

        var items = _allGroups
            .Where(g => g.StartsWith(g1 + "/"))
            .Select(g =>
            {
                var rest = g[(g1.Length + 1)..];
                return rest.Split('/')[0];
            })
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        foreach (var item in items)
            Group2ComboBox.Items.Add(item);
    }

    private void RebuildGroup3()
    {
        var g1 = Group1ComboBox.Text?.Trim() ?? "";
        var g2 = Group2ComboBox.Text?.Trim() ?? "";
        Group3ComboBox.Items.Clear();
        Group3ComboBox.Items.Add("");
        Group3ComboBox.Text = "";

        if (string.IsNullOrEmpty(g1) || string.IsNullOrEmpty(g2)) return;

        var prefix = $"{g1}/{g2}/";
        var items = _allGroups
            .Where(g => g.StartsWith(prefix))
            .Select(g => g[prefix.Length..].Split('/')[0])
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        foreach (var item in items)
            Group3ComboBox.Items.Add(item);
    }

    private void Group1_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        RebuildGroup2();
        RebuildGroup3();
    }

    private void Group2_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        RebuildGroup3();
    }

    private string BuildGroupName()
    {
        var g1 = Group1ComboBox.Text?.Trim() ?? "";
        var g2 = Group2ComboBox.Text?.Trim() ?? "";
        var g3 = Group3ComboBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(g1)) return "";
        if (string.IsNullOrEmpty(g2)) return g1;
        if (string.IsNullOrEmpty(g3)) return $"{g1}/{g2}";
        return $"{g1}/{g2}/{g3}";
    }

    private void AddButtons()
    {
        var panel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var okButton = new System.Windows.Controls.Button
        {
            Content = "OK",
            Width = 80,
            Height = 30,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D4")),
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D4")),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            FontSize = 13,
        };
        okButton.Click += OK_Click;

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "キャンセル",
            Width = 80,
            Height = 30,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3F3F46")),
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E0E0E0")),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3F3F46")),
            Cursor = Cursors.Hand,
            FontSize = 13,
        };
        cancelButton.Click += Cancel_Click;

        panel.Children.Add(okButton);
        panel.Children.Add(cancelButton);

        var grid = (System.Windows.Controls.Grid)((System.Windows.Controls.Border)Content).Child;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        System.Windows.Controls.Grid.SetRow(panel, grid.RowDefinitions.Count - 1);
        grid.Children.Add(panel);
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        var ip = IpTextBox.Text.Trim();
        var hostname = HostnameTextBox.Text.Trim();

        if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(hostname))
        {
            MessageBox.Show("IPアドレスとホスト名は必須です。", "Hatch",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var groupName = BuildGroupName();

        Result = new HostEntry
        {
            IpAddress = ip,
            Hostname = hostname,
            Comment = CommentTextBox.Text.Trim(),
            GroupName = string.IsNullOrEmpty(groupName) ? null : groupName,
            IsEnabled = _existing?.IsEnabled ?? true,
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }
}

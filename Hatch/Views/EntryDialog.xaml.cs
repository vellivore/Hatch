using System.Windows;
using System.Windows.Input;
using Hatch.Models;

namespace Hatch.Views;

public partial class EntryDialog : Window
{
    public HostEntry? Result { get; private set; }

    private readonly HostEntry? _existing;

    public EntryDialog(IEnumerable<string> groups, HostEntry? existing = null)
    {
        InitializeComponent();

        _existing = existing;

        // グループドロップダウンに選択肢を追加
        GroupComboBox.Items.Add("");
        foreach (var g in groups.Where(g => g != "すべて"))
            GroupComboBox.Items.Add(g);

        if (existing != null)
        {
            DialogTitle.Text = "エントリの編集";
            Title = "エントリの編集";
            IpTextBox.Text = existing.IpAddress;
            HostnameTextBox.Text = existing.Hostname;
            CommentTextBox.Text = existing.Comment;
            GroupComboBox.Text = existing.GroupName ?? "";
        }
        else
        {
            IpTextBox.Text = "127.0.0.1";
        }

        // ボタンを追加（コードビハインドで生成してGrid末尾に配置）
        AddButtons();

        IpTextBox.Focus();
    }

    private void AddButtons()
    {
        var panel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 0),
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

        // Grid の最後に追加
        var grid = (System.Windows.Controls.Grid)((System.Windows.Controls.Border)Content).Child;
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
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

        Result = new HostEntry
        {
            IpAddress = ip,
            Hostname = hostname,
            Comment = CommentTextBox.Text.Trim(),
            GroupName = string.IsNullOrWhiteSpace(GroupComboBox.Text) ? null : GroupComboBox.Text.Trim(),
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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hatch.Views;

public partial class GroupDialog : Window
{
    private readonly List<string> _groups;
    private readonly Action<List<string>> _onSave;
    private readonly Action<string, string>? _onRename;
    private bool _suppressTextChanged;

    public GroupDialog(List<string> groups, Action<List<string>> onSave, Action<string, string>? onRename = null)
    {
        InitializeComponent();
        _groups = new List<string>(groups);
        _onSave = onSave;
        _onRename = onRename;
        RefreshList();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void RefreshList()
    {
        _suppressTextChanged = true;
        GroupListBox.Items.Clear();
        foreach (var g in _groups)
            GroupListBox.Items.Add(g);
        _suppressTextChanged = false;
    }

    private void GroupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupListBox.SelectedItem is string selected)
        {
            DetailPanel.IsEnabled = true;
            _suppressTextChanged = true;
            GroupNameTextBox.Text = selected;
            _suppressTextChanged = false;
        }
        else
        {
            DetailPanel.IsEnabled = false;
            _suppressTextChanged = true;
            GroupNameTextBox.Text = "";
            _suppressTextChanged = false;
        }
    }

    private void GroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged) return;
        if (GroupListBox.SelectedItem is not string oldName) return;

        var newName = GroupNameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(newName) || newName == oldName) return;
        if (_groups.Contains(newName)) return; // 重複回避

        var idx = _groups.IndexOf(oldName);
        if (idx < 0) return;

        _groups[idx] = newName;
        _onRename?.Invoke(oldName, newName);

        _suppressTextChanged = true;
        GroupListBox.Items[idx] = newName;
        GroupListBox.SelectedIndex = idx;
        _suppressTextChanged = false;

        _onSave(_groups);
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var baseName = "新しいグループ";
        var name = baseName;
        int counter = 2;
        while (_groups.Contains(name))
        {
            name = $"{baseName} {counter}";
            counter++;
        }

        _groups.Add(name);
        RefreshList();
        GroupListBox.SelectedIndex = _groups.Count - 1;
        _onSave(_groups);

        // 名前を選択状態にして即編集可能に
        GroupNameTextBox.Focus();
        GroupNameTextBox.SelectAll();
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupListBox.SelectedItem is not string selected) return;

        var result = MessageBox.Show(
            $"グループ「{selected}」を削除しますか？\n所属するエントリのグループは空になります。",
            "Hatch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _groups.Remove(selected);
        RefreshList();
        _onSave(_groups);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

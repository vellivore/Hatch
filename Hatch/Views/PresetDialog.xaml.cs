using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hatch.Models;

namespace Hatch.Views;

public partial class PresetDialog : Window
{
    private readonly ObservableCollection<Preset> _presets;
    private readonly List<string> _allGroups;
    private Preset? _selectedPreset;
    private bool _suppressNameUpdate;

    /// <summary>
    /// 編集結果のプリセット一覧（呼び出し元が取得する）
    /// </summary>
    public List<Preset> ResultPresets => _presets.ToList();

    public PresetDialog(IEnumerable<Preset> presets, IEnumerable<string> allGroups)
    {
        InitializeComponent();

        _allGroups = allGroups.Where(g => g != "すべて").ToList();
        _presets = new ObservableCollection<Preset>(presets.Select(p => p.Clone()));

        RefreshPresetList();

        if (_presets.Count > 0)
            PresetListBox.SelectedIndex = 0;
    }

    private void RefreshPresetList()
    {
        _suppressNameUpdate = true;
        PresetListBox.Items.Clear();
        foreach (var p in _presets)
            PresetListBox.Items.Add(p.Name);
        _suppressNameUpdate = false;
    }

    private void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = PresetListBox.SelectedIndex;
        if (idx < 0 || idx >= _presets.Count)
        {
            _selectedPreset = null;
            DetailPanel.IsEnabled = false;
            PresetNameTextBox.Text = "";
            GroupCheckBoxPanel.Children.Clear();
            return;
        }

        _selectedPreset = _presets[idx];
        DetailPanel.IsEnabled = true;

        _suppressNameUpdate = true;
        PresetNameTextBox.Text = _selectedPreset.Name;
        _suppressNameUpdate = false;

        RebuildGroupCheckBoxes();
    }

    private void RebuildGroupCheckBoxes()
    {
        GroupCheckBoxPanel.Children.Clear();

        if (_allGroups.Count == 0)
        {
            NoGroupsText.Visibility = Visibility.Visible;
            return;
        }
        NoGroupsText.Visibility = Visibility.Collapsed;

        foreach (var group in _allGroups)
        {
            var cb = new CheckBox
            {
                Content = group,
                IsChecked = _selectedPreset?.EnabledGroups.Contains(group) ?? false,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E0E0E0")),
                FontSize = 13,
                Margin = new Thickness(4, 4, 4, 4),
                Tag = group,
            };
            cb.Checked += GroupCheckBox_Changed;
            cb.Unchecked += GroupCheckBox_Changed;
            GroupCheckBoxPanel.Children.Add(cb);
        }
    }

    private void GroupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null || sender is not CheckBox cb || cb.Tag is not string group)
            return;

        if (cb.IsChecked == true)
        {
            if (!_selectedPreset.EnabledGroups.Contains(group))
                _selectedPreset.EnabledGroups.Add(group);
        }
        else
        {
            _selectedPreset.EnabledGroups.Remove(group);
        }
    }

    private void PresetNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressNameUpdate || _selectedPreset == null)
            return;

        _selectedPreset.Name = PresetNameTextBox.Text.Trim();

        // リスト表示を更新
        var idx = PresetListBox.SelectedIndex;
        if (idx >= 0 && idx < PresetListBox.Items.Count)
        {
            _suppressNameUpdate = true;
            PresetListBox.Items[idx] = _selectedPreset.Name;
            PresetListBox.SelectedIndex = idx;
            _suppressNameUpdate = false;
        }
    }

    private void AddPreset_Click(object sender, RoutedEventArgs e)
    {
        var newPreset = new Preset { Name = $"プリセット{_presets.Count + 1}" };
        _presets.Add(newPreset);
        PresetListBox.Items.Add(newPreset.Name);
        PresetListBox.SelectedIndex = _presets.Count - 1;
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        var idx = PresetListBox.SelectedIndex;
        if (idx < 0 || idx >= _presets.Count) return;

        var result = MessageBox.Show(
            $"プリセット「{_presets[idx].Name}」を削除しますか？",
            "Hatch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _presets.RemoveAt(idx);
        PresetListBox.Items.RemoveAt(idx);

        if (_presets.Count > 0)
            PresetListBox.SelectedIndex = Math.Min(idx, _presets.Count - 1);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }
}

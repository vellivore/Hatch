using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hatch.Models;

namespace Hatch.Views;

public partial class ManageDialog : Window
{
    private readonly ObservableCollection<Preset> _presets;
    private readonly List<string> _groups;
    private Preset? _selectedPreset;
    private bool _suppressPresetNameUpdate;

    public List<Preset> ResultPresets => _presets.ToList();

    public ManageDialog(List<string> groups, IEnumerable<Preset> presets)
    {
        InitializeComponent();

        _groups = new List<string>(groups);
        _presets = new ObservableCollection<Preset>(presets.Select(p => p.Clone()));

        RefreshPresetList();

        if (_presets.Count > 0)
            PresetListBox.SelectedIndex = 0;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // ==================== プリセット ====================

    private void RefreshPresetList()
    {
        _suppressPresetNameUpdate = true;
        PresetListBox.Items.Clear();
        foreach (var p in _presets)
            PresetListBox.Items.Add(p.Name);
        _suppressPresetNameUpdate = false;
    }

    private void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = PresetListBox.SelectedIndex;
        if (idx < 0 || idx >= _presets.Count)
        {
            _selectedPreset = null;
            PresetDetailPanel.IsEnabled = false;
            PresetNameTextBox.Text = "";
            GroupCheckBoxPanel.Children.Clear();
            return;
        }

        _selectedPreset = _presets[idx];
        PresetDetailPanel.IsEnabled = true;

        _suppressPresetNameUpdate = true;
        PresetNameTextBox.Text = _selectedPreset.Name;
        _suppressPresetNameUpdate = false;

        RebuildGroupCheckBoxes();
    }

    private void RebuildGroupCheckBoxes()
    {
        GroupCheckBoxPanel.Children.Clear();

        if (_groups.Count == 0)
        {
            NoGroupsText.Visibility = Visibility.Visible;
            return;
        }
        NoGroupsText.Visibility = Visibility.Collapsed;

        foreach (var group in _groups)
        {
            var label = new TextBlock
            {
                Text = group,
                Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#E0E0E0")),
                FontSize = 13,
            };
            var cb = new CheckBox
            {
                Content = label,
                IsChecked = _selectedPreset?.EnabledGroups.Contains(group) ?? false,
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
        if (_suppressPresetNameUpdate || _selectedPreset == null) return;

        _selectedPreset.Name = PresetNameTextBox.Text.Trim();

        var idx = PresetListBox.SelectedIndex;
        if (idx >= 0 && idx < PresetListBox.Items.Count)
        {
            _suppressPresetNameUpdate = true;
            PresetListBox.Items[idx] = _selectedPreset.Name;
            PresetListBox.SelectedIndex = idx;
            _suppressPresetNameUpdate = false;
        }
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        var idx = PresetListBox.SelectedIndex;
        if (idx < 0 || idx >= _presets.Count) return;

        var result = MessageBox.Show(
            $"プリセット「{_presets[idx].Name}」を削除しますか？",
            "Hatch", MessageBoxButton.YesNo, MessageBoxImage.Question);

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
}

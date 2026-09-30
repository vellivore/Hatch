using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.Views;

public partial class ManageDialog : Window
{
    private readonly ObservableCollection<Preset> _presets;
    private readonly List<string> _groups;
    private Preset? _selectedPreset;
    private bool _suppressPresetNameUpdate;

    // 編集用コピー → 開いた時点の名前（名前変更の追跡用）
    private readonly Dictionary<Preset, string> _originalNames = new();

    public List<Preset> ResultPresets => _presets.ToList();

    /// <summary>名前が変わったプリセットの 旧名 → 新名。</summary>
    public IReadOnlyDictionary<string, string> ResultRenames =>
        _presets.Where(p => _originalNames.TryGetValue(p, out var orig) && orig != p.Name)
                .ToDictionary(p => _originalNames[p], p => p.Name);

    public ManageDialog(List<string> groups, IEnumerable<Preset> presets)
    {
        InitializeComponent();

        _groups = new List<string>(groups);
        _presets = new ObservableCollection<Preset>(presets.Select(p => p.Clone()));
        foreach (var p in _presets)
            _originalNames[p] = p.Name;

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

    private bool _suppressGroupCheck;

    /// <summary>
    /// グループのチェック一覧を階層順・深さに応じたインデントで作る。
    /// プリセットは前方一致なので、親をチェックすると子孫は自動的に含まれる（淡色・操作不可で表示）。
    /// </summary>
    private void RebuildGroupCheckBoxes()
    {
        GroupCheckBoxPanel.Children.Clear();

        if (_groups.Count == 0)
        {
            NoGroupsText.Visibility = Visibility.Visible;
            return;
        }
        NoGroupsText.Visibility = Visibility.Collapsed;

        var foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"));
        foreach (var group in PresetLogic.OrderAsTree(_groups))
        {
            var label = new TextBlock
            {
                Text = group.Split('/')[^1],
                Foreground = foreground,
                FontSize = 13,
                ToolTip = group,
            };
            var cb = new CheckBox
            {
                Content = label,
                Margin = new Thickness(4 + PresetLogic.Depth(group) * 20, 4, 4, 4),
                Tag = group,
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(cb, "GroupCheck:" + group);
            cb.Checked += GroupCheckBox_Changed;
            cb.Unchecked += GroupCheckBox_Changed;
            GroupCheckBoxPanel.Children.Add(cb);
        }

        RefreshGroupCheckStates();
    }

    /// <summary>選択中プリセットの内容をチェック状態に反映する（親に含まれる子は淡色で自動チェック）。</summary>
    private void RefreshGroupCheckStates()
    {
        if (_selectedPreset == null) return;

        _suppressGroupCheck = true;
        try
        {
            foreach (var cb in GroupCheckBoxPanel.Children.OfType<CheckBox>())
            {
                if (cb.Tag is not string group) continue;

                var explicitlyOn = _selectedPreset.EnabledGroups.Contains(group);
                var implied = _selectedPreset.EnabledGroups.Any(p => p != group && PresetLogic.IsSelfOrDescendant(group, p));

                cb.IsChecked = explicitlyOn || implied;
                cb.IsEnabled = !implied;
                cb.Opacity = implied ? 0.45 : 1.0;
                cb.ToolTip = implied ? "親グループに含まれています" : null;
            }
        }
        finally
        {
            _suppressGroupCheck = false;
        }
    }

    private void GroupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressGroupCheck || _selectedPreset == null || sender is not CheckBox cb || cb.Tag is not string group)
            return;

        if (cb.IsChecked == true)
        {
            if (!_selectedPreset.EnabledGroups.Contains(group))
                _selectedPreset.EnabledGroups.Add(group);

            // 子孫は親に含まれるので個別指定を外す（プリセットの中身を最小に保つ）
            foreach (var descendant in _selectedPreset.EnabledGroups
                         .Where(g => g != group && PresetLogic.IsSelfOrDescendant(g, group)).ToList())
            {
                _selectedPreset.EnabledGroups.Remove(descendant);
            }
        }
        else
        {
            _selectedPreset.EnabledGroups.Remove(group);
        }

        RefreshGroupCheckStates();
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

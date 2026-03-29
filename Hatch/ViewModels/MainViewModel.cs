using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HostsFileService _hostsService = new();
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings = new();

    [ObservableProperty]
    private ObservableCollection<HostEntry> _entries = new();

    [ObservableProperty]
    private ObservableCollection<HostEntry> _filteredEntries = new();

    [ObservableProperty]
    private HostEntry? _selectedEntry;

    [ObservableProperty]
    private ObservableCollection<string> _groups = new();

    [ObservableProperty]
    private string _selectedGroup = "すべて";

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _canWrite;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private ObservableCollection<Preset> _presets = new();

    [ObservableProperty]
    private ObservableCollection<string> _presetNames = new();

    [ObservableProperty]
    private string _selectedPresetName = "なし";

    public MainViewModel()
    {
        LoadSettings();
        ReloadEntries();
        CanWrite = _hostsService.CanWriteHostsFile();
    }

    private void LoadSettings()
    {
        _settings = _settingsService.Load();
        LoadPresets();
    }

    private void LoadPresets()
    {
        Presets.Clear();
        foreach (var dto in _settings.Presets)
        {
            Presets.Add(new Preset
            {
                Name = dto.Name,
                EnabledGroups = new ObservableCollection<string>(dto.EnabledGroups),
            });
        }
        RebuildPresetNames();

        // アクティブプリセットを復元
        SelectedPresetName = !string.IsNullOrEmpty(_settings.ActivePresetName)
            && Presets.Any(p => p.Name == _settings.ActivePresetName)
            ? _settings.ActivePresetName
            : "なし";
    }

    private void SavePresets()
    {
        _settings.Presets = Presets.Select(p => new PresetDto
        {
            Name = p.Name,
            EnabledGroups = p.EnabledGroups.ToList(),
        }).ToList();
        _settings.ActivePresetName = SelectedPresetName == "なし" ? null : SelectedPresetName;
    }

    private void RebuildPresetNames()
    {
        PresetNames.Clear();
        PresetNames.Add("なし");
        foreach (var p in Presets)
            PresetNames.Add(p.Name);
    }

    public void UpdatePresets(List<Preset> newPresets)
    {
        Presets = new ObservableCollection<Preset>(newPresets);
        RebuildPresetNames();

        // 現在のアクティブプリセットが削除されていた場合は「なし」に戻す
        if (SelectedPresetName != "なし" && !Presets.Any(p => p.Name == SelectedPresetName))
        {
            SelectedPresetName = "なし";
        }

        SavePresets();
        _settingsService.Save(_settings);
    }

    public void SaveCurrentStateAsPreset(string name)
    {
        // 現在有効なエントリのグループ名を収集
        var enabledGroups = new HashSet<string>();
        foreach (var entry in Entries)
        {
            if (!entry.IsRawLine && entry.IsEnabled && !string.IsNullOrEmpty(entry.GroupName))
                enabledGroups.Add(entry.GroupName);
        }

        // 同名プリセットがあれば上書き
        var existing = Presets.FirstOrDefault(p => p.Name == name);
        if (existing != null)
        {
            existing.EnabledGroups = new ObservableCollection<string>(enabledGroups);
        }
        else
        {
            Presets.Add(new Preset
            {
                Name = name,
                EnabledGroups = new ObservableCollection<string>(enabledGroups),
            });
        }

        RebuildPresetNames();
        SelectedPresetName = name;
        SavePresets();
        _settingsService.Save(_settings);
        StatusText = $"プリセット「{name}」を保存しました";
    }

    partial void OnSelectedPresetNameChanged(string value)
    {
        ApplyPreset(value);
    }

    private void ApplyPreset(string presetName)
    {
        // エントリ未読み込み時はスキップ（起動時の初期化順序）
        if (Entries.Count == 0) return;

        if (presetName == "なし")
        {
            // 「なし」を選択 = グループ付きエントリをすべて無効化
            foreach (var entry in Entries)
            {
                if (!entry.IsRawLine && !entry.IsSystem && !string.IsNullOrEmpty(entry.GroupName))
                    entry.IsEnabled = false;
            }
        }
        else
        {
            var preset = Presets.FirstOrDefault(p => p.Name == presetName);
            if (preset == null) return;

            foreach (var entry in Entries)
            {
                if (entry.IsRawLine || entry.IsSystem || string.IsNullOrEmpty(entry.GroupName))
                    continue;

                entry.IsEnabled = preset.EnabledGroups.Contains(entry.GroupName);
            }
        }

        // hosts ファイルに書き込み + DNS フラッシュ
        if (CanWrite)
        {
            try
            {
                _hostsService.WriteHostsFile(Entries.ToList());
                _hostsService.FlushDns();
                SaveGroupAssignments();
                SavePresets();
                _settingsService.Save(_settings);
                IsDirty = false;
            }
            catch (Exception ex)
            {
                StatusText = $"プリセット適用エラー: {ex.Message}";
                return;
            }
        }
        else
        {
            IsDirty = true;
        }

        ApplyFilter();
        UpdateStatus();
    }

    public AppSettings Settings => _settings;

    public void SaveSettings(double left, double top, double width, double height)
    {
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        SaveGroupAssignments();
        SavePresets();
        _settingsService.Save(_settings);
    }

    private void SaveGroupAssignments()
    {
        _settings.GroupAssignments.Clear();
        foreach (var entry in Entries)
        {
            if (entry.IsRawLine || entry.IsSystem || string.IsNullOrEmpty(entry.GroupName))
                continue;

            if (!_settings.GroupAssignments.ContainsKey(entry.GroupName))
                _settings.GroupAssignments[entry.GroupName] = new List<string>();

            var key = $"{entry.IpAddress}|{entry.Hostname}";
            if (!_settings.GroupAssignments[entry.GroupName].Contains(key))
                _settings.GroupAssignments[entry.GroupName].Add(key);
        }
    }

    private void ApplyGroupAssignments()
    {
        foreach (var (group, keys) in _settings.GroupAssignments)
        {
            foreach (var key in keys)
            {
                var parts = key.Split('|', 2);
                if (parts.Length != 2) continue;

                var entry = Entries.FirstOrDefault(e =>
                    !e.IsRawLine && e.IpAddress == parts[0] && e.Hostname == parts[1]);
                if (entry != null)
                    entry.GroupName = group;
            }
        }
    }

    private void SaveGroupList()
    {
        _settings.Groups = Groups.Where(g => g != "すべて").ToList();
    }

    public void RenameGroup(string oldName, string newName)
    {
        // エントリのグループ名を更新
        foreach (var entry in Entries)
        {
            if (entry.GroupName == oldName)
                entry.GroupName = newName;
        }

        // プリセット内のグループ名も更新
        foreach (var preset in Presets)
        {
            var idx = preset.EnabledGroups.IndexOf(oldName);
            if (idx >= 0)
                preset.EnabledGroups[idx] = newName;
        }
    }

    public void UpdateGroups(List<string> updatedGroups)
    {
        var oldGroups = Groups.Where(g => g != "すべて").ToList();

        // 削除されたグループに属するエントリのグループを空にする
        foreach (var entry in Entries)
        {
            if (!string.IsNullOrEmpty(entry.GroupName) && !updatedGroups.Contains(entry.GroupName))
                entry.GroupName = null;
        }

        // 名前変更: 旧グループ名→新グループ名のマッピングは GroupDialog 側で処理済み
        // ここでは新しいグループ一覧でリビルドするだけ
        RebuildGroups();

        // 新規追加されたグループを Groups に反映（エントリがなくても表示するため）
        foreach (var g in updatedGroups)
        {
            if (!Groups.Contains(g))
                Groups.Add(g);
        }

        // グループ一覧を設定ファイルに即時保存
        SaveGroupList();
        _settingsService.Save(_settings);

        IsDirty = true;
        UpdateStatus();
    }

    [RelayCommand]
    private void Reload()
    {
        if (IsDirty)
        {
            var result = MessageBox.Show(
                "未保存の変更があります。破棄して最新化しますか？",
                "Hatch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;
        }
        ReloadEntries();
    }

    private void ReloadEntries()
    {
        try
        {
            var entries = _hostsService.ReadHostsFile();
            Entries = new ObservableCollection<HostEntry>(entries);
            ApplyGroupAssignments();
            RebuildGroups();
            ApplyFilter();
            UpdateStatus();
            IsDirty = false;
        }
        catch (Exception ex)
        {
            StatusText = $"読み込みエラー: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Apply()
    {
        if (!CanWrite)
        {
            MessageBox.Show(
                "hostsファイルへの書き込み権限がありません。\n管理者として実行してください。",
                "Hatch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            _hostsService.WriteHostsFile(Entries.ToList());
            _hostsService.FlushDns();
            SaveGroupAssignments();
            _settingsService.Save(_settings);
            IsDirty = false;
            StatusText = "hostsファイルに保存しました (DNS キャッシュをフラッシュ済み)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"書き込みエラー: {ex.Message}",
                "Hatch",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedEntry == null || SelectedEntry.IsSystem || SelectedEntry.IsRawLine)
            return;

        var result = MessageBox.Show(
            $"{SelectedEntry.Hostname} を削除しますか？",
            "Hatch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            Entries.Remove(SelectedEntry);
            RebuildGroups();
            ApplyFilter();
            UpdateStatus();
            IsDirty = true;
        }
    }

    public void AddEntry(HostEntry entry)
    {
        // システムエントリの後に追加
        var insertIdx = 0;
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            if (!Entries[i].IsRawLine)
            {
                insertIdx = i + 1;
                break;
            }
        }
        Entries.Insert(insertIdx, entry);
        RebuildGroups();
        ApplyFilter();
        UpdateStatus();
        IsDirty = true;
    }

    public void UpdateEntry(HostEntry original, HostEntry updated)
    {
        var idx = Entries.IndexOf(original);
        if (idx < 0) return;

        original.IpAddress = updated.IpAddress;
        original.Hostname = updated.Hostname;
        original.Comment = updated.Comment;
        original.GroupName = updated.GroupName;
        RebuildGroups();
        ApplyFilter();
        IsDirty = true;
    }

    public void ToggleEntry(HostEntry entry)
    {
        if (entry.IsSystem || entry.IsRawLine) return;
        entry.IsEnabled = !entry.IsEnabled;
        UpdateStatus();
        IsDirty = true;
    }

    private void RebuildGroups()
    {
        var groupSet = new SortedSet<string>();

        // 設定ファイルに保存されたグループを復元
        foreach (var g in _settings.Groups)
            groupSet.Add(g);

        // エントリに割り当てられたグループも追加
        foreach (var e in Entries)
        {
            if (!string.IsNullOrEmpty(e.GroupName))
                groupSet.Add(e.GroupName);
        }

        // 親階層も自動追加（業務/案件A/環境A → 業務, 業務/案件A も追加）
        var withParents = new SortedSet<string>(groupSet);
        foreach (var g in groupSet)
        {
            var parts = g.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                withParents.Add(string.Join("/", parts.Take(i)));
            }
        }

        Groups.Clear();
        Groups.Add("すべて");
        foreach (var g in withParents)
            Groups.Add(g);

        if (!Groups.Contains(SelectedGroup))
            SelectedGroup = "すべて";
    }

    partial void OnSelectedGroupChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (SelectedGroup == "すべて")
        {
            FilteredEntries = new ObservableCollection<HostEntry>(
                Entries.Where(e => !e.IsRawLine));
        }
        else
        {
            // 親階層を選んだ場合は配下も表示（前方一致）
            var prefix = SelectedGroup + "/";
            FilteredEntries = new ObservableCollection<HostEntry>(
                Entries.Where(e => !e.IsRawLine &&
                    (e.GroupName == SelectedGroup ||
                     (e.GroupName != null && e.GroupName.StartsWith(prefix)))));
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var total = Entries.Count(e => !e.IsRawLine);
        var enabled = Entries.Count(e => !e.IsRawLine && e.IsEnabled);
        var groupText = SelectedGroup == "すべて" ? "" : $" | グループ: {SelectedGroup}";
        var presetText = SelectedPresetName == "なし" ? "" : $" | プリセット: {SelectedPresetName}";
        var writeText = CanWrite ? "" : " | 読み取り専用";
        StatusText = $"エントリ: {enabled}/{total}{groupText}{presetText}{writeText}";
    }

    // --- Text Editor ---
    public string ReadHostsRaw() => _hostsService.ReadHostsFileRaw();

    public void WriteHostsRaw(string content)
    {
        _hostsService.WriteHostsFileRaw(content);
        _hostsService.FlushDns();
        ReloadEntries();
        StatusText = "テキスト編集からhostsファイルを保存しました (DNS キャッシュをフラッシュ済み)";
    }

    // --- Backup / Restore ---
    public void BackupHostsFile(string path)
    {
        var content = _hostsService.ReadHostsFileRaw();
        System.IO.File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        StatusText = $"バックアップを保存しました: {System.IO.Path.GetFileName(path)}";
    }

    public void RestoreHostsFile(string path)
    {
        var content = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
        _hostsService.WriteHostsFileRaw(content);
        _hostsService.FlushDns();
        ReloadEntries();
        StatusText = "リストアが完了しました (DNS キャッシュをフラッシュ済み)";
    }

    // --- Tray support ---
    public string ActivePresetDisplayName =>
        SelectedPresetName == "なし" ? "プリセットなし" : SelectedPresetName;

    public void SwitchPreset(string presetName)
    {
        SelectedPresetName = presetName;
    }
}

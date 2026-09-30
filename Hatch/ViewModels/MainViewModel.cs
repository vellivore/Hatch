using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.ViewModels;

/// <summary>プリセット切替の結果。</summary>
public enum PresetSwitchResult
{
    /// <summary>切り替えた（書込権限がある場合は hosts に保存済み）。</summary>
    Switched,
    /// <summary>未保存の変更があるため切り替えていない。呼び出し側で確認して force で呼び直す。</summary>
    NeedsConfirmation,
    /// <summary>プリセットが見つからない、または hosts への書き込みに失敗した（状態は元のまま）。</summary>
    Failed,
}

/// <summary>
/// 確認ダイアログは持たない（View 側の責務）。hosts への書き込みは
/// <see cref="Apply"/> と <see cref="SwitchPreset"/>、テキスト編集・リストア系の明示メソッドだけが行う。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private const string AllGroups = "すべて";

    private readonly HostsFileService _hostsService = StartupOptions.Current.CreateHostsService();
    private readonly SettingsService _settingsService = StartupOptions.Current.CreateSettingsService();
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
    private string _selectedGroup = AllGroups;

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

    /// <summary>
    /// 現在のプリセット（番兵 "なし" = すべて無効）。変更しても hosts には書かない。
    /// 切り替えは <see cref="SwitchPreset"/> を使う。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePresetDisplayName))]
    private string _selectedPresetName = PresetLogic.NoneName;

    // 一括変更中（プリセット適用など）はエントリ単位の変更通知で状態を再計算しない
    private bool _bulkUpdate;

    // 直近の hosts 読み込みが失敗した（一覧が空のまま書くと hosts を消してしまうので書き込みを拒否する）
    private bool _loadFailed;

    public MainViewModel()
    {
        OnEntriesChanged(null, Entries);
        LoadSettings();
        CanWrite = _hostsService.CanWriteHostsFile();
        ReloadEntries();
    }

    public AppSettings Settings => _settings;

    public string SettingsPath => _settingsService.SettingsPath;

    // ==================== 設定・プリセット ====================

    private void LoadSettings()
    {
        _settings = _settingsService.Load();
        Presets = new ObservableCollection<Preset>(_settings.Presets.Select(p => p.Clone()));

        // アクティブプリセットを復元（起動時は hosts に書かない）
        var active = !string.IsNullOrEmpty(_settings.ActivePresetName)
            && Presets.Any(p => p.Name == _settings.ActivePresetName)
            ? _settings.ActivePresetName!
            : PresetLogic.NoneName;
        RebuildPresetNames(active);
    }

    private void SavePresetsToSettings()
    {
        _settings.Presets = Presets.Select(p => p.Clone()).ToList();
        _settings.ActivePresetName = SelectedPresetName == PresetLogic.NoneName ? null : SelectedPresetName;
    }

    private void SaveSettingsFile()
    {
        SavePresetsToSettings();
        _settingsService.Save(_settings);
    }

    /// <summary>
    /// ComboBox の項目を作り直し、選択を <paramref name="selected"/> に合わせる。
    /// （ComboBox は OneWay バインドなので、Clear() で選択が外れても VM の値は null にならない）
    /// </summary>
    private void RebuildPresetNames(string selected)
    {
        PresetNames.Clear();
        PresetNames.Add(PresetLogic.NoneName);
        foreach (var p in Presets)
            PresetNames.Add(p.Name);

        SelectedPresetName = PresetNames.Contains(selected) ? selected : PresetLogic.NoneName;
        // Clear() で ComboBox の選択が外れるため、値が変わっていなくても通知して選択を戻させる
        OnPropertyChanged(nameof(SelectedPresetName));
        OnPropertyChanged(nameof(PresetNames));
    }

    /// <summary>
    /// プリセット管理ダイアログの結果を反映する。有効プリセットの名前変更には追従する。
    /// 有効プリセットが削除された場合は選択を「すべて無効」に戻すが、hosts には書かない
    /// （書き込みは明示的な切替・適用だけが行う）。削除されたら true。
    /// </summary>
    public bool UpdatePresets(List<Preset> newPresets, IReadOnlyDictionary<string, string> renames)
    {
        var previous = string.IsNullOrEmpty(SelectedPresetName) ? PresetLogic.NoneName : SelectedPresetName;
        var target = previous != PresetLogic.NoneName && renames.TryGetValue(previous, out var renamed)
            ? renamed
            : previous;

        Presets = new ObservableCollection<Preset>(newPresets);
        var activeDeleted = target != PresetLogic.NoneName && !Presets.Any(p => p.Name == target);
        RebuildPresetNames(activeDeleted ? PresetLogic.NoneName : target);
        SaveSettingsFile();

        if (activeDeleted)
            StatusText = $"有効だったプリセット「{previous}」を削除しました（hosts は変更していません）";
        else
            UpdateStatus();
        return activeDeleted;
    }

    /// <summary>
    /// 現在の有効/無効状態をプリセットとして保存する（前方一致で重複するグループは親にまとめる）。
    /// 現在の状態そのものなので、選択を合わせるだけで hosts には書かない。
    /// </summary>
    public void SaveCurrentStateAsPreset(string name)
    {
        var enabledGroups = PresetLogic.CaptureEnabledGroups(Entries);

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

        RebuildPresetNames(name);
        SaveSettingsFile();
        StatusText = $"プリセット「{name}」を保存しました";
    }

    /// <summary>
    /// プリセットを切り替える。プリセットで hosts に書き込むのはこのメソッドだけ。
    /// 未保存の変更があり <paramref name="force"/> でなければ何もせず NeedsConfirmation を返す。
    /// 書込権限がない場合はエントリだけ切り替えて未保存状態にする。
    /// 書き込みに失敗した場合はエントリの状態を元に戻して Failed を返す（エラーは StatusText）。
    /// </summary>
    public PresetSwitchResult SwitchPreset(string presetName, bool force = false)
    {
        if (IsDirty && !force)
            return PresetSwitchResult.NeedsConfirmation;

        IReadOnlyCollection<string>? groups = null;
        if (presetName != PresetLogic.NoneName)
        {
            var preset = Presets.FirstOrDefault(p => p.Name == presetName);
            if (preset == null)
            {
                StatusText = $"プリセット「{presetName}」が見つかりません";
                return PresetSwitchResult.Failed;
            }
            groups = preset.EnabledGroups.ToList();
        }

        var snapshot = Entries.Where(e => !e.IsRawLine).ToDictionary(e => e, e => e.IsEnabled);
        var wasDirty = IsDirty;

        _bulkUpdate = true;
        try
        {
            PresetLogic.Apply(Entries, groups);
        }
        finally
        {
            _bulkUpdate = false;
        }

        if (CanWrite)
        {
            try
            {
                WriteEntriesToHosts();
            }
            catch (Exception ex)
            {
                _bulkUpdate = true;
                try
                {
                    foreach (var (entry, enabled) in snapshot)
                        entry.IsEnabled = enabled;
                }
                finally
                {
                    _bulkUpdate = false;
                }
                IsDirty = wasDirty;
                ApplyFilter();
                StatusText = $"プリセット適用エラー: {ex.Message}";
                return PresetSwitchResult.Failed;
            }
        }
        else
        {
            IsDirty = true;
        }

        SelectedPresetName = presetName;
        SaveSettingsFile();
        ApplyFilter();
        return PresetSwitchResult.Switched;
    }

    /// <summary>トレイ・ツールチップ用の表示名（番兵は「すべて無効」）。</summary>
    public string ActivePresetDisplayName => PresetLogic.DisplayName(SelectedPresetName);

    // ==================== 旧設定からの移行 ====================

    /// <summary>移行待ちの旧形式グループ割り当てがあるか。</summary>
    public bool HasPendingMigration => _settings.GroupAssignments is { Count: > 0 };

    /// <summary>
    /// v1.x の GroupAssignments（"IP|ホスト名"）を、タグのないエントリに当てはめる。当てはめた件数を返す。
    /// 設定からは消さない（hosts への書き込みが成功した時点で消す）。
    /// </summary>
    private int ApplyLegacyGroupAssignments()
        => HasPendingMigration ? LegacyGroupMigration.Apply(Entries, _settings.GroupAssignments) : 0;

    /// <summary>エントリ（タグ付き）を hosts に書けたので、旧形式の割り当てを設定から消す。</summary>
    private void CompleteMigration()
    {
        if (_settings.GroupAssignments == null) return;
        _settings.GroupAssignments = null;
        SaveSettingsFile();
    }

    // ==================== hosts の読み書き ====================

    /// <summary>一覧のエントリを hosts に書き、DNS をフラッシュする（移行も完了させる）。</summary>
    private void WriteEntriesToHosts()
    {
        if (_loadFailed)
            throw new InvalidOperationException("hosts の読み込みに失敗しているため書き込みません。最新化してから操作してください。");
        _hostsService.WriteHostsFile(Entries.ToList());
        _hostsService.FlushDns();
        IsDirty = false;
        CompleteMigration();
    }

    /// <summary>hosts を読み直す（未保存の変更は破棄される。確認は View 側）。</summary>
    public void Reload() => ReloadEntries();

    private void ReloadEntries()
    {
        try
        {
            var entries = _hostsService.ReadHostsFile();
            Entries = new ObservableCollection<HostEntry>(entries);
            _loadFailed = false;
            var migrated = ApplyLegacyGroupAssignments();
            RebuildGroups();
            ApplyFilter();
            IsDirty = migrated > 0;
            if (migrated > 0)
                StatusText = "旧設定からグループを移行しました。適用で hosts に保存されます";
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            StatusText = $"読み込みエラー: {ex.Message}";
        }
    }

    /// <summary>
    /// 一覧を hosts に保存する。書込権限の確認とエラー表示は View 側（失敗時は例外）。
    /// </summary>
    public void Apply()
    {
        if (!CanWrite)
            throw new InvalidOperationException("hostsファイルへの書き込み権限がありません。");

        WriteEntriesToHosts();
        SaveSettingsFile();
        UpdateStatus();
        StatusText = "hostsファイルに保存しました (DNS キャッシュをフラッシュ済み)";
    }

    // ==================== エントリ操作 ====================

    public void DeleteEntry(HostEntry entry)
    {
        if (entry.IsSystem || entry.IsRawLine) return;
        if (!Entries.Remove(entry)) return;
        RebuildGroups();
        ApplyFilter();
        IsDirty = true;
    }

    public void AddEntry(HostEntry entry)
    {
        // 最後のエントリの後に追加（末尾のコメント・空行より前）
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

    // --- エントリの変更追跡 ---
    partial void OnEntriesChanged(ObservableCollection<HostEntry>? oldValue, ObservableCollection<HostEntry> newValue)
    {
        if (oldValue != null)
        {
            oldValue.CollectionChanged -= Entries_CollectionChanged;
            foreach (var entry in oldValue)
                entry.PropertyChanged -= Entry_PropertyChanged;
        }
        newValue.CollectionChanged += Entries_CollectionChanged;
        foreach (var entry in newValue)
            entry.PropertyChanged += Entry_PropertyChanged;
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (HostEntry entry in e.OldItems)
                entry.PropertyChanged -= Entry_PropertyChanged;
        if (e.NewItems != null)
            foreach (HostEntry entry in e.NewItems)
                entry.PropertyChanged += Entry_PropertyChanged;
    }

    /// <summary>
    /// 一覧のチェックボックスは TwoWay バインドで IsEnabled を直接書き換えるため、
    /// ここで未保存フラグと競合表示を更新する。
    /// </summary>
    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_bulkUpdate) return;
        if (e.PropertyName == nameof(HostEntry.IsEnabled))
        {
            IsDirty = true;
            UpdateStatus();
        }
    }

    // ==================== グループ・フィルタ・並べ替え ====================

    private void RebuildGroups()
    {
        var groupSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in Entries)
        {
            if (string.IsNullOrEmpty(e.GroupName)) continue;

            // 親階層も自動追加（業務/案件A/環境A → 業務, 業務/案件A も追加）
            var parts = e.GroupName.Split('/');
            for (var i = 1; i <= parts.Length; i++)
                groupSet.Add(string.Join("/", parts.Take(i)));
        }

        Groups.Clear();
        Groups.Add(AllGroups);
        foreach (var g in PresetLogic.OrderAsTree(groupSet))
            Groups.Add(g);

        if (!Groups.Contains(SelectedGroup))
            SelectedGroup = AllGroups;
    }

    partial void OnSelectedGroupChanged(string value)
    {
        ApplyFilter();
    }

    private string _sortColumn = "";
    private bool _sortAscending = true;

    public string SortColumn => _sortColumn;
    public bool SortAscending => _sortAscending;

    public void SortByColumn(string column)
    {
        if (_sortColumn == column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }
        ApplyFilter();
    }

    private IEnumerable<HostEntry> ApplySortOrder(IEnumerable<HostEntry> items)
    {
        return _sortColumn switch
        {
            "IsEnabled" => _sortAscending
                ? items.OrderByDescending(e => e.IsEnabled)
                : items.OrderBy(e => e.IsEnabled),
            "GroupName" => _sortAscending
                ? items.OrderBy(e => e.GroupName ?? "", StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(e => e.GroupName ?? "", StringComparer.OrdinalIgnoreCase),
            "IpAddress" => _sortAscending
                ? items.OrderBy(e => e.IpAddress, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(e => e.IpAddress, StringComparer.OrdinalIgnoreCase),
            "Hostname" => _sortAscending
                ? items.OrderBy(e => e.Hostname, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(e => e.Hostname, StringComparer.OrdinalIgnoreCase),
            _ => items
        };
    }

    private void ApplyFilter()
    {
        IEnumerable<HostEntry> filtered;
        if (SelectedGroup == AllGroups || string.IsNullOrEmpty(SelectedGroup))
        {
            filtered = Entries.Where(e => !e.IsRawLine);
        }
        else
        {
            filtered = Entries.Where(e => !e.IsRawLine
                && e.GroupName != null && PresetLogic.IsSelfOrDescendant(e.GroupName, SelectedGroup));
        }

        if (!string.IsNullOrEmpty(_sortColumn))
            filtered = ApplySortOrder(filtered);

        FilteredEntries = new ObservableCollection<HostEntry>(filtered);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var conflicts = RecomputeConflicts();
        var total = Entries.Count(e => !e.IsRawLine);
        var enabled = Entries.Count(e => !e.IsRawLine && e.IsEnabled);
        var groupText = SelectedGroup == AllGroups ? "" : $" | グループ: {SelectedGroup}";
        var presetText = SelectedPresetName == PresetLogic.NoneName ? "" : $" | プリセット: {SelectedPresetName}";
        var writeText = CanWrite ? "" : " | 読み取り専用";
        var conflictText = conflicts > 0 ? $" | 競合: {conflicts}" : "";
        StatusText = $"エントリ: {enabled}/{total}{groupText}{presetText}{writeText}{conflictText}";
    }

    /// <summary>
    /// 同じホスト名が複数行で有効になっている行に HasConflict を立て、競合しているホスト名の数を返す。
    /// IPv4 と IPv6 は別の問い合わせ（A / AAAA）に効くため、アドレス種別ごとに数える。
    /// </summary>
    private int RecomputeConflicts()
    {
        var conflicted = Entries
            .Where(e => !e.IsRawLine && e.IsEnabled)
            .GroupBy(ConflictKey, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Entries)
        {
            if (entry.IsRawLine) continue;
            entry.HasConflict = entry.IsEnabled && conflicted.Contains(ConflictKey(entry));
        }

        return conflicted.Select(k => k[(k.IndexOf('|') + 1)..])
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Count();
    }

    private static string ConflictKey(HostEntry entry)
    {
        var family = entry.IpAddress.Contains(':') ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        return $"{family}|{entry.Hostname}";
    }

    // ==================== ウィンドウ設定 ====================

    public void SaveSettings(double? left, double? top, double width, double height)
    {
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        SaveSettingsFile();
    }

    // ==================== テキスト編集・バックアップ ====================

    public string ReadHostsRaw() => _hostsService.ReadHostsFileRaw();

    public void WriteHostsRaw(string content)
    {
        _hostsService.WriteHostsText(content);
        _hostsService.FlushDns();
        ReloadEntries();
        StatusText = "テキスト編集からhostsファイルを保存しました (DNS キャッシュをフラッシュ済み)";
    }

    public void BackupHostsFile(string path)
    {
        var content = _hostsService.ReadHostsFileRaw();
        // hosts と同じ UTF-8(BOM なし) で保存（Encoding.UTF8 は BOM を付与するため使わない）
        System.IO.File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        StatusText = $"バックアップを保存しました: {System.IO.Path.GetFileName(path)}";
    }

    public void RestoreHostsFile(string path)
    {
        // 外部ツール作成の Shift_JIS バックアップ等も自動判定で読み込む
        var content = HostsFileService.ReadTextDetect(path);
        _hostsService.WriteHostsText(content);
        _hostsService.FlushDns();
        ReloadEntries();
        StatusText = "リストアが完了しました (DNS キャッシュをフラッシュ済み)";
    }

    public string BackupDirectory => _hostsService.BackupDirectory;

    /// <summary>最新の自動バックアップのパス（なければ null）。</summary>
    public string? LatestAutoBackup => _hostsService.GetLatestBackup();

    /// <summary>
    /// 最新の自動バックアップを hosts へ書き戻し、DNS をフラッシュして再読込する。
    /// 書き戻す直前の状態も自動バックアップされるので、もう一度実行すれば元に戻せる。
    /// </summary>
    public void RestoreLatestBackup()
    {
        var latest = _hostsService.GetLatestBackup()
            ?? throw new InvalidOperationException("自動バックアップがまだありません。");
        _hostsService.RestoreBackup(latest);
        _hostsService.FlushDns();
        ReloadEntries();
        var time = HostsFileService.GetBackupTime(latest);
        StatusText = $"直前の状態に戻しました（{time:yyyy/MM/dd HH:mm:ss} の hosts） (DNS キャッシュをフラッシュ済み)";
    }
}

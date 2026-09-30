using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hatch.Models;

/// <summary>
/// プリセット（有効にするグループの集合）。設定 JSON にもこの型をそのまま保存する
/// （プロパティ名 Name / EnabledGroups は旧 PresetDto と互換）。
/// </summary>
public partial class Preset : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private ObservableCollection<string> _enabledGroups = new();

    public Preset Clone()
    {
        return new Preset
        {
            Name = Name,
            EnabledGroups = new ObservableCollection<string>(EnabledGroups),
        };
    }
}

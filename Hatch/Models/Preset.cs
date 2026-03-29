using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hatch.Models;

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

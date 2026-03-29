using CommunityToolkit.Mvvm.ComponentModel;

namespace Hatch.Models;

public partial class HostEntry : ObservableObject
{
    [ObservableProperty]
    private string _ipAddress = string.Empty;

    [ObservableProperty]
    private string _hostname = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _comment = string.Empty;

    [ObservableProperty]
    private string? _groupName;

    /// <summary>
    /// システムデフォルトエントリ（localhost等）はtrue。編集・削除不可。
    /// </summary>
    [ObservableProperty]
    private bool _isSystem;

    /// <summary>
    /// 純粋なコメント行やブランク行を保持するためのフラグ
    /// </summary>
    [ObservableProperty]
    private bool _isRawLine;

    /// <summary>
    /// IsRawLine=true の場合、元の行テキストをそのまま保持
    /// </summary>
    [ObservableProperty]
    private string _rawText = string.Empty;

    public HostEntry Clone()
    {
        return new HostEntry
        {
            IpAddress = IpAddress,
            Hostname = Hostname,
            IsEnabled = IsEnabled,
            Comment = Comment,
            GroupName = GroupName,
            IsSystem = IsSystem,
            IsRawLine = IsRawLine,
            RawText = RawText,
        };
    }
}

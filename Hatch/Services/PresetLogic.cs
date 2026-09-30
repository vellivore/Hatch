using Hatch.Models;

namespace Hatch.Services;

/// <summary>
/// プリセットとグループの対応を決める純関数群。
/// グループは "/" 区切りの階層で、プリセットに含めたグループはその子孫もすべて含む（前方一致）。
/// </summary>
public static class PresetLogic
{
    /// <summary>「プリセットなし」を表す内部の番兵値（設定ファイルには null で保存する）。</summary>
    public const string NoneName = "なし";

    /// <summary>番兵値の表示名。選ぶとグループ付きエントリがすべて無効になる。</summary>
    public const string NoneDisplayName = "すべて無効";

    public static string DisplayName(string? presetName)
        => string.IsNullOrEmpty(presetName) || presetName == NoneName ? NoneDisplayName : presetName;

    /// <summary><paramref name="group"/> が <paramref name="ancestor"/> 自身かその子孫か。</summary>
    public static bool IsSelfOrDescendant(string group, string ancestor)
        => group == ancestor || group.StartsWith(ancestor + "/", StringComparison.Ordinal);

    /// <summary>プリセットのグループ一覧が <paramref name="group"/> を含むか（前方一致）。</summary>
    public static bool Includes(IEnumerable<string> enabledGroups, string? group)
        => !string.IsNullOrEmpty(group) && enabledGroups.Any(p => IsSelfOrDescendant(group, p));

    /// <summary>
    /// プリセットをエントリに当てはめる。グループのない行・システム行・raw 行は変えない。
    /// <paramref name="enabledGroups"/> が null なら「すべて無効」。
    /// </summary>
    public static void Apply(IEnumerable<HostEntry> entries, IReadOnlyCollection<string>? enabledGroups)
    {
        foreach (var entry in entries)
        {
            if (entry.IsRawLine || entry.IsSystem || string.IsNullOrEmpty(entry.GroupName))
                continue;
            entry.IsEnabled = enabledGroups != null && Includes(enabledGroups, entry.GroupName);
        }
    }

    /// <summary>
    /// 現在の状態からプリセットのグループ一覧を作る。有効なエントリを持つグループを集め、
    /// 他の要素の子孫として既に含まれるもの（前方一致で重複するもの）を除く。
    /// </summary>
    public static List<string> CaptureEnabledGroups(IEnumerable<HostEntry> entries)
    {
        var enabled = entries
            .Where(e => !e.IsRawLine && e.IsEnabled && !string.IsNullOrEmpty(e.GroupName))
            .Select(e => e.GroupName!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return OrderAsTree(enabled.Where(g => !enabled.Any(p => p != g && IsSelfOrDescendant(g, p))));
    }

    /// <summary>階層の深さ（"業務" = 0, "業務/案件A" = 1）。</summary>
    public static int Depth(string group) => group.Count(c => c == '/');

    /// <summary>親の直後に子が並ぶ順（区切りごとに序数比較）に並べる。</summary>
    public static List<string> OrderAsTree(IEnumerable<string> groups)
        => groups.Distinct(StringComparer.Ordinal)
                 .OrderBy(g => g, TreeOrderComparer.Instance)
                 .ToList();

    private sealed class TreeOrderComparer : IComparer<string>
    {
        public static readonly TreeOrderComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var a = (x ?? "").Split('/');
            var b = (y ?? "").Split('/');
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                var c = string.CompareOrdinal(a[i], b[i]);
                if (c != 0) return c;
            }
            return a.Length.CompareTo(b.Length);
        }
    }
}

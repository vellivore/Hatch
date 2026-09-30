using Hatch.Models;

namespace Hatch.Services;

/// <summary>
/// v1.x の設定ファイルにあった GroupAssignments（グループ名 → "IP|ホスト名" の一覧）を
/// hosts のエントリへ当てはめる。v2 ではグループを hosts の行末タグに持つ。
/// </summary>
public static class LegacyGroupMigration
{
    /// <summary>
    /// タグのない（GroupName が null の）エントリにだけ旧割り当てを適用し、適用した件数を返す。
    /// タグで既にグループが付いているエントリは hosts 側を正とし、上書きしない。
    /// </summary>
    public static int Apply(IEnumerable<HostEntry> entries, IReadOnlyDictionary<string, List<string>>? assignments)
    {
        if (assignments == null || assignments.Count == 0) return 0;

        var targets = entries.Where(e => !e.IsRawLine).ToList();
        var applied = 0;
        foreach (var (group, keys) in assignments)
        {
            if (string.IsNullOrEmpty(group) || !HostsDocument.IsValidGroupName(group)) continue;
            foreach (var key in keys)
            {
                var parts = key.Split('|', 2);
                if (parts.Length != 2) continue;

                foreach (var entry in targets.Where(e =>
                             e.GroupName == null && e.IpAddress == parts[0] && e.Hostname == parts[1]))
                {
                    entry.GroupName = group;
                    applied++;
                }
            }
        }
        return applied;
    }
}

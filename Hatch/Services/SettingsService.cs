using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hatch.Services;

public class SettingsService
{
    private static readonly string SettingsPath =
        Path.Combine(AppContext.BaseDirectory, "hatch-config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}

public class AppSettings
{
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 900;
    public double WindowHeight { get; set; } = 560;

    /// <summary>
    /// 登録済みグループ名の一覧（エントリ紐付けがなくても永続化）
    /// </summary>
    public List<string> Groups { get; set; } = new();

    /// <summary>
    /// グループ名 → エントリのホスト名リスト（hostsファイルにはグループ情報がないため設定で保持）
    /// </summary>
    public Dictionary<string, List<string>> GroupAssignments { get; set; } = new();

    /// <summary>
    /// プリセット一覧
    /// </summary>
    public List<PresetDto> Presets { get; set; } = new();

    /// <summary>
    /// 現在アクティブなプリセット名（null = なし）
    /// </summary>
    public string? ActivePresetName { get; set; }
}

/// <summary>
/// JSON シリアライズ用の Preset DTO
/// </summary>
public class PresetDto
{
    public string Name { get; set; } = "";
    public List<string> EnabledGroups { get; set; } = new();
}

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hatch.Models;

namespace Hatch.Services;

public class SettingsService
{
    public const string FileName = "hatch-config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // 日本語のグループ名・プリセット名をエスケープせず、そのまま読める形で保存する
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    /// <summary>設定ファイルのパス。</summary>
    public string SettingsPath { get; }

    public SettingsService() : this(ResolveDefaultPath(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)))
    {
    }

    public SettingsService(string settingsPath)
    {
        SettingsPath = settingsPath;
    }

    /// <summary>
    /// 設定ファイルの置き場所を決める。exe と同じフォルダに hatch-config.json があればそれを使い
    /// （既存のポータブル運用を壊さない）、なければ %LOCALAPPDATA%\Hatch\hatch-config.json。
    /// </summary>
    public static string ResolveDefaultPath(string exeDirectory, string localAppData)
    {
        var portable = Path.Combine(exeDirectory, FileName);
        if (File.Exists(portable))
            return portable;
        return Path.Combine(localAppData, "Hatch", FileName);
    }

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
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}

public class AppSettings
{
    /// <summary>ウィンドウ左上。未保存なら null（v1.3.x の "NaN" も null として読む）。</summary>
    [JsonConverter(typeof(LenientNullableDoubleConverter))]
    public double? WindowLeft { get; set; }

    [JsonConverter(typeof(LenientNullableDoubleConverter))]
    public double? WindowTop { get; set; }

    public double WindowWidth { get; set; } = 900;
    public double WindowHeight { get; set; } = 560;

    /// <summary>
    /// v1.x の旧形式: グループ名 → "IP|ホスト名" の一覧。v2 ではグループを hosts の行末タグに持つため、
    /// 起動時に一度だけエントリへ移行し、hosts への書き込みが成功したら null にして消す。
    /// </summary>
    public Dictionary<string, List<string>>? GroupAssignments { get; set; }

    /// <summary>
    /// プリセット一覧
    /// </summary>
    public List<Preset> Presets { get; set; } = new();

    /// <summary>
    /// 現在アクティブなプリセット名（null = なし）
    /// </summary>
    public string? ActivePresetName { get; set; }
}

/// <summary>
/// double? を読み書きする。数値文字列は数値として、"NaN" など数値でない文字列は null として読む
/// （v1.3.x は未設定の座標を "NaN" で保存していた）。書き込みは通常の数値か null。
/// </summary>
public class LenientNullableDoubleConverter : JsonConverter<double?>
{
    public override bool HandleNull => true;

    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.GetDouble();
            case JsonTokenType.String:
                var text = reader.GetString();
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    && double.IsFinite(value))
                    return value;
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is double d && double.IsFinite(d))
            writer.WriteNumberValue(d);
        else
            writer.WriteNullValue();
    }
}

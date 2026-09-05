using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnythingLLMReviewTranslator;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AppSettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    public string FilePath { get; }

    public AppSettings LoadOrCreate()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = AppSettings.CreateDefault();
            Save(defaults);
            return defaults;
        }

        try
        {
            using var stream = File.OpenRead(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(stream, Options) ?? AppSettings.CreateDefault();
            return settings.Normalize();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Không thể đọc file cấu hình '{FilePath}'.", ex);
        }
    }

    public void Save(AppSettings settings)
    {
        settings = settings.Normalize();
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(FilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

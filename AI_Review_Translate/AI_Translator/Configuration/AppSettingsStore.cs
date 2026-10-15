using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI_Translator.Configuration;

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
            stream.Close();

            var hasPlainTextKeys = HasPlainTextSecrets(settings);
            UnprotectSecrets(settings);
            settings.Normalize();

            // Tự động mã hóa các key còn để bản rõ trong file
            if (hasPlainTextKeys)
            {
                try
                {
                    Save(settings);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return settings;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Không thể đọc file cấu hình '{FilePath}'.", ex);
        }
    }

    public void Save(AppSettings settings)
    {
        settings = settings.Normalize();

        // Mã hóa trên bản sao để object đang dùng trong app vẫn giữ key bản rõ
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Options), Options)!;
        ProtectSecrets(copy);

        var json = JsonSerializer.Serialize(copy, Options);
        File.WriteAllText(FilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool HasPlainTextSecrets(AppSettings settings)
    {
        var gemini = settings.Gemini;
        if (gemini is null)
        {
            return false;
        }

        return (!string.IsNullOrWhiteSpace(gemini.ApiKey) && !SecretProtector.IsProtected(gemini.ApiKey))
            || (gemini.ApiKeys?.Any(k => !string.IsNullOrWhiteSpace(k) && !SecretProtector.IsProtected(k)) ?? false);
    }

    private static void ProtectSecrets(AppSettings settings)
    {
        settings.Gemini.ApiKey = SecretProtector.Protect(settings.Gemini.ApiKey);
        settings.Gemini.ApiKeys = settings.Gemini.ApiKeys.Select(SecretProtector.Protect).ToList();
    }

    private static void UnprotectSecrets(AppSettings settings)
    {
        if (settings.Gemini is null)
        {
            return;
        }

        settings.Gemini.ApiKey = SecretProtector.Unprotect(settings.Gemini.ApiKey);
        settings.Gemini.ApiKeys = settings.Gemini.ApiKeys?.Select(SecretProtector.Unprotect).ToList() ?? new List<string>();
    }
}

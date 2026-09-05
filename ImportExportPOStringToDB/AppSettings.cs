using System.Text.Json;

namespace ImportPOStringToDB;

public sealed class AppSettings
{
    public string ConnectionString { get; set; } =
        @"Server=(localdb)\MSSQLLocalDB;Database=ImportPOStringToDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    public bool SkipHeaderEntry { get; set; } = true;

    public string? SourcePath { get; private set; }

    public static AppSettings Load(string? explicitPath = null)
    {
        foreach (var candidate in GetCandidatePaths(explicitPath))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var json = File.ReadAllText(candidate);
            var settings = JsonSerializer.Deserialize<AppSettings>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new AppSettings();

            if (string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                throw new InvalidOperationException($"ConnectionString bị thiếu trong file: {candidate}");
            }

            settings.SourcePath = candidate;
            return settings;
        }

        throw new FileNotFoundException(
            "Không tìm thấy appsettings.json. Hãy đặt file này cạnh exe hoặc ở thư mục gốc của project.",
            "appsettings.json");
    }

    private static IEnumerable<string> GetCandidatePaths(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return NormalizePath(explicitPath);
        }

        var envPath = Environment.GetEnvironmentVariable("IMPORT_PO_APPSETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            yield return NormalizePath(envPath);
        }

        yield return NormalizePath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "appsettings.json"));
        yield return NormalizePath(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        yield return NormalizePath(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"));
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}

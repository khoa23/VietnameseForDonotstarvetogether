namespace PoTranslationsWeb.Models;

public sealed class PoTranslationRow
{
    public int Id { get; init; }

    public string AllText { get; init; } = string.Empty;

    public string? MsgCtxt { get; init; }

    public string MsgId { get; init; } = string.Empty;

    public string MsgStr { get; init; } = string.Empty;

    public string? SuggestedTranslation { get; set; }

    public double? Rating { get; set; }

    public string SourceFilePath { get; init; } = string.Empty;

    public DateTime ImportedAtUtc { get; init; }

    public bool TranslationLocked { get; init; }
}

namespace ImportPOStringToDB.Models;

public sealed class PoTranslation
{
    public int Id { get; set; }

    public string AllText { get; set; } = string.Empty;

    public string? MsgCtxt { get; set; }

    public string MsgId { get; set; } = string.Empty;

    public string MsgStr { get; set; } = string.Empty;

    public string? SuggestedTranslation { get; set; }

    public double? Rating { get; set; }

    public bool TranslationLocked { get; set; }

    public string SourceFilePath { get; set; } = string.Empty;

    public DateTime ImportedAtUtc { get; set; }

    public DateTime? LastUpdated { get; set; }
}

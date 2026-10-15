namespace AI_Translator.Models;

public sealed class EscapeRepairResult
{
    public bool Selected { get; set; }
    public long RowId { get; init; }
    public string MsgId { get; init; } = string.Empty;
    public string OriginalTranslation { get; init; } = string.Empty;
    public string RepairedTranslation { get; init; } = string.Empty;
    public string Issues { get; init; } = string.Empty;
}

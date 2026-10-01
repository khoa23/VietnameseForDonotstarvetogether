namespace AnythingLLMReviewTranslator;

/// <summary>
/// Kết quả kiểm tra từ điển cho một cặp (dòng dữ liệu, từ trong từ điển).
/// Mỗi instance đại diện cho một từ tiếng Anh tìm thấy trong MsgId
/// và trạng thái khớp với SuggestedTranslation.
/// </summary>
public sealed class GlossaryCheckResult
{
    public bool Selected { get; set; }
    public long RowId { get; init; }
    public string? MsgId { get; init; }
    public string? MsgStr { get; init; }
    public string? SuggestedTranslation { get; set; }
    public string EnglishTerm { get; init; } = "";
    public string ExpectedVietnamese { get; init; } = "";
    public string CommonWords { get; init; } = "";
    public bool IsMatch { get; set; }
    public string MatchStatus => IsMatch ? "✅ Khớp" : "❌ Chưa khớp";
}

public sealed class GlossaryCheckRow
{
    public long Id { get; init; }
    public string? MsgId { get; init; }
    public string? MsgStr { get; init; }
    public string? SuggestedTranslation { get; init; }
}

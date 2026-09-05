namespace AnythingLLMReviewTranslator;

public sealed record ProcessorProgress(
    long? RowId,
    int Completed,
    int Total,
    string Message,
    string? SuggestedTranslation = null,
    decimal? Rating = null,
    string? Status = null,
    string? Error = null,
    string? RawResponse = null,
    bool IsSkipped = false,
    bool IsSummary = false);

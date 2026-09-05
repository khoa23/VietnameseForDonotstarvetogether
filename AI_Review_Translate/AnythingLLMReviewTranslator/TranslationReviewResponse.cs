namespace AnythingLLMReviewTranslator;

public sealed record TranslationReviewResponse(
    string SuggestedTranslation,
    decimal? Rating,
    string RawResponse);

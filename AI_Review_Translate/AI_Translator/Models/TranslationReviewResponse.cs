namespace AI_Translator.Models;

public sealed record TranslationReviewResponse(
    string SuggestedTranslation,
    decimal? Rating,
    string RawResponse);

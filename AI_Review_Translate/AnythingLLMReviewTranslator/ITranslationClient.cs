namespace AnythingLLMReviewTranslator;

public interface ITranslationClient : IDisposable
{
    event Action<AnythingLlmApiResponse>? ResponseReceived;

    Task<TranslationReviewResponse> ReviewTranslationAsync(ReviewRowViewModel row, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<long, TranslationReviewResponse>> ReviewTranslationsAsync(
        IReadOnlyList<ReviewRowViewModel> rows,
        CancellationToken cancellationToken)
    {
        return Task.FromException<IReadOnlyDictionary<long, TranslationReviewResponse>>(
            new NotSupportedException("Batch translation is not supported by this client."));
    }
}

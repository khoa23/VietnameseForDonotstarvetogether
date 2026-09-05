namespace PoTranslationsWeb.Models;

public sealed class PoTranslationSearchResult
{
    public IReadOnlyList<PoTranslationRow> Items { get; init; } = Array.Empty<PoTranslationRow>();

    public long TotalCount { get; init; }
}

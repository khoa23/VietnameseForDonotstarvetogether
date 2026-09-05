namespace AnythingLLMReviewTranslator;

internal static class SqlIdentifier
{
    public static string QuotePath(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("SQL identifier cannot be empty.", nameof(identifier));
        }

        var parts = identifier
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(QuoteSegment);

        return string.Join(".", parts);
    }

    public static string QuoteSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new ArgumentException("SQL identifier segment cannot be empty.", nameof(segment));
        }

        var trimmed = segment.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            trimmed = trimmed[1..^1];
        }

        return "[" + trimmed.Replace("]", "]]") + "]";
    }
}

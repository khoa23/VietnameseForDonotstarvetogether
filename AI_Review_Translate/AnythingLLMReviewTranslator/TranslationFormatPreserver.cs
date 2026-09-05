namespace AnythingLLMReviewTranslator;

internal static class TranslationFormatPreserver
{
    /// <summary>
    /// Áp dụng wrapper escaped-quote (\") từ source lên suggestion nếu source có dùng chúng.
    /// Ngoài ra, luôn strip dấu ngoặc kép thường (") mà AI hay bọc ngoài kết quả
    /// khi source ban đầu không có regular outer quotes.
    /// </summary>
    public static string PreserveOuterEscapedQuotes(string? source, string suggestion)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(suggestion))
        {
            return suggestion;
        }

        var sourceTrimmed = source.Trim();
        var hasLeadingEscapedQuote = sourceTrimmed.StartsWith("\\\"", StringComparison.Ordinal);
        var hasTrailingEscapedQuote = sourceTrimmed.EndsWith("\\\"", StringComparison.Ordinal);

        if (hasLeadingEscapedQuote || hasTrailingEscapedQuote)
        {
            // Source dùng escaped-quote wrapper → strip mọi dạng quote ngoài rồi áp lại đúng format.
            var translatedContent = RemoveOuterQuoteTokens(suggestion.Trim());
            return $"{(hasLeadingEscapedQuote ? "\\\"" : string.Empty)}{translatedContent}{(hasTrailingEscapedQuote ? "\\\"" : string.Empty)}";
        }

        // Source KHÔNG có escaped-quote wrapper.
        // Nếu source cũng không bắt đầu/kết thúc bằng regular quote, thì AI không
        // được phép bọc suggestion trong "..." → strip regular outer quotes nếu có.
        var sourceHasLeadingRegularQuote = sourceTrimmed.StartsWith('"');
        var sourceHasTrailingRegularQuote = sourceTrimmed.EndsWith('"');

        if (!sourceHasLeadingRegularQuote && !sourceHasTrailingRegularQuote)
        {
            suggestion = StripRegularOuterQuotes(suggestion.Trim());
        }

        return StripUnwantedEllipsis(sourceTrimmed, suggestion);
    }

    /// <summary>
    /// Gọt bỏ dấu ba chấm (...) vô tội vạ ở đầu/cuối câu mà AI tự chèn vào
    /// nếu văn bản gốc không hề có dấu ba chấm ở vị trí tương ứng.
    /// </summary>
    private static string StripUnwantedEllipsis(string source, string suggestion)
    {
        var trimmedSuggestion = suggestion.Trim();

        // Gọt ở đầu câu
        if (trimmedSuggestion.StartsWith("...") || trimmedSuggestion.StartsWith("…"))
        {
            if (!source.StartsWith("...") && !source.StartsWith("…"))
            {
                trimmedSuggestion = trimmedSuggestion.StartsWith("...") 
                    ? trimmedSuggestion[3..].TrimStart() 
                    : trimmedSuggestion[1..].TrimStart();
            }
        }

        // Gọt ở cuối câu
        if (trimmedSuggestion.EndsWith("...") || trimmedSuggestion.EndsWith("…"))
        {
            if (!source.EndsWith("...") && !source.EndsWith("…"))
            {
                trimmedSuggestion = trimmedSuggestion.EndsWith("...") 
                    ? trimmedSuggestion[..^3].TrimEnd() 
                    : trimmedSuggestion[..^1].TrimEnd();
            }
        }

        return trimmedSuggestion;
    }

    /// <summary>Strip dấu " ở đầu/cuối nếu có (không strip escaped \").</summary>
    private static string StripRegularOuterQuotes(string value)
    {
        if (value.Length >= 2 && value.StartsWith('"') && !value.StartsWith("\\\"", StringComparison.Ordinal))
        {
            value = value[1..];
        }

        if (value.Length >= 1 && value.EndsWith('"') && !value.EndsWith("\\\"", StringComparison.Ordinal))
        {
            value = value[..^1];
        }

        return value;
    }

    private static string RemoveOuterQuoteTokens(string value)
    {
        if (value.StartsWith("\\\"", StringComparison.Ordinal))
        {
            value = value[2..];
        }
        else if (value.StartsWith('"'))
        {
            value = value[1..];
        }

        if (value.EndsWith("\\\"", StringComparison.Ordinal))
        {
            value = value[..^2];
        }
        else if (value.EndsWith('"'))
        {
            value = value[..^1];
        }

        return value;
    }
}

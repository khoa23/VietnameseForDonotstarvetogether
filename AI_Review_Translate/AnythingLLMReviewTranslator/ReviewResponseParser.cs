using System.Globalization;
using System.Text.Json;

namespace AnythingLLMReviewTranslator;

public static class ReviewResponseParser
{
    public static TranslationReviewResponse ParseReviewResponse(string body, string providerName = "LLM")
    {
        var trimmed = body.Trim();

        if (TryParseReviewResponseObject(trimmed, text => text, out var parsed))
        {
            return parsed;
        }

        var fenced = StripCodeFences(trimmed);
        if (!string.Equals(fenced, trimmed, StringComparison.Ordinal))
        {
            if (TryParseReviewResponseObject(fenced, _ => trimmed, out parsed))
            {
                return parsed;
            }
        }

        var candidate = ExtractJsonCandidate(trimmed);
        if (!string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate, trimmed, StringComparison.Ordinal))
        {
            if (TryParseReviewResponseObject(candidate, _ => trimmed, out parsed))
            {
                return parsed;
            }
        }

        throw new InvalidOperationException($"Không thể đọc phản hồi từ {providerName}. Raw: {TrimForLog(body, 3000)}");
    }

    private static bool TryParseReviewResponseObject(string text, Func<string, string> rawResponseSelector, out TranslationReviewResponse response)
    {
        response = default!;
        if (!TryParseJsonObject(text, out var root))
        {
            return false;
        }

        var error = GetString(root, "error");
        var textResponse = GetString(root, "textResponse");
        if (!string.IsNullOrWhiteSpace(error) && string.IsNullOrWhiteSpace(textResponse))
        {
            throw new InvalidOperationException(error);
        }

        if (!string.IsNullOrWhiteSpace(textResponse))
        {
            var nestedText = textResponse.Trim();
            if (TryParseReviewResponseObject(nestedText, _ => rawResponseSelector(text), out var nested))
            {
                response = nested;
                return true;
            }

            var nestedCandidate = ExtractJsonCandidate(nestedText);
            if (!string.IsNullOrWhiteSpace(nestedCandidate) &&
                !string.Equals(nestedCandidate, nestedText, StringComparison.Ordinal) &&
                TryParseReviewResponseObject(nestedCandidate, _ => rawResponseSelector(text), out nested))
            {
                response = nested;
                return true;
            }

            if (TryRecoverMalformedReviewResponse(nestedText, rawResponseSelector(text), out nested))
            {
                response = nested;
                return true;
            }

            throw new InvalidOperationException(
                "Phản hồi trả về textResponse không đúng định dạng JSON yêu cầu. " +
                "Kỳ vọng: {\"suggestedTranslation\":\"...\",\"rating\":8.5}. " +
                $"Raw textResponse: {TrimForLog(nestedText, 3000)}");
        }

        var suggestion =
            GetString(root, "suggestedTranslation") ??
            GetString(root, "suggested_translation") ??
            GetString(root, "translation") ??
            GetString(root, "answer") ??
            GetString(root, "text");

        var finalRating = GetDecimal(root, "rating") ?? GetDecimal(root, "score");

        if (!string.IsNullOrWhiteSpace(suggestion) && finalRating.HasValue)
        {
            response = new TranslationReviewResponse(suggestion.Trim(), finalRating, rawResponseSelector(text));
            return true;
        }

        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            throw new InvalidOperationException(
                "Phản hồi có suggestedTranslation nhưng thiếu đánh giá rating (0-10). " +
                $"Raw: {TrimForLog(text, 3000)}");
        }

        return false;
    }

    private static bool TryRecoverMalformedReviewResponse(string text, string rawResponse, out TranslationReviewResponse response)
    {
        response = default!;

        const string prefix = "{\"suggestedTranslation\":\"";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var ratingMarkers = new[] { ",\"rating\":", ",\\\"rating\\\":" };
        var markerIndex = -1;
        var markerLength = 0;
        foreach (var marker in ratingMarkers)
        {
            var index = text.LastIndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                markerIndex = index;
                markerLength = marker.Length;
                break;
            }
        }

        if (markerIndex < 0)
        {
            return false;
        }

        try
        {
            var encodedSuggestion = text[prefix.Length..markerIndex];
            var ratingText = text[(markerIndex + markerLength)..].Trim().TrimEnd('}').Trim();
            if (!decimal.TryParse(ratingText, NumberStyles.Number, CultureInfo.InvariantCulture, out var rating))
            {
                return false;
            }

            var suggestion = JsonSerializer.Deserialize<string>("\"" + encodedSuggestion + "\"");
            if (string.IsNullOrWhiteSpace(suggestion))
            {
                return false;
            }

            response = new TranslationReviewResponse(suggestion.Trim(), rating, rawResponse);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseJsonObject(string text, out JsonElement root)
    {
        root = default;
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    private static decimal? GetDecimal(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (TryParseDecimal(text, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = System.Text.RegularExpressions.Regex.Match(text, @"-?\d+(\.\d+)?");
        var candidate = match.Success ? match.Value : text.Trim();
        return decimal.TryParse(candidate, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine < 0)
        {
            return trimmed;
        }

        var endFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (endFence <= firstNewLine)
        {
            return trimmed;
        }

        return trimmed[(firstNewLine + 1)..endFence].Trim();
    }

    private static string? ExtractJsonCandidate(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text[start..(end + 1)].Trim();
    }

    internal static string TrimForLog(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..maxLength] + "...";
    }
}

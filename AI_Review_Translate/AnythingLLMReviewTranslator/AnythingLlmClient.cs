using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AnythingLLMReviewTranslator;

public sealed class AnythingLlmClient : ITranslationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AnythingLlmSettings _settings;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public event Action<AnythingLlmApiResponse>? ResponseReceived;

    public AnythingLlmClient(AnythingLlmSettings settings)
    {
        _settings = settings;
        var baseUri = NormalizeBaseUri(settings.BaseUrl);

        _httpClient = new HttpClient
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds)
        };

        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }
    }

    public async Task<string> TestWorkspaceAsync(string workspaceSlug, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();

        if (string.IsNullOrWhiteSpace(workspaceSlug))
        {
            throw new InvalidOperationException("Workspace slug is empty.");
        }

        var requestPath = $"workspace/{Uri.EscapeDataString(workspaceSlug)}";
        using var response = await _httpClient.GetAsync(requestPath, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        NotifyResponseReceived(response.RequestMessage?.RequestUri, body);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildHttpErrorMessage(response.StatusCode, body, response.RequestMessage?.RequestUri, requestPath, workspaceSlug));
        }

        return body;
    }

    public async Task<TranslationReviewResponse> ReviewTranslationAsync(ReviewRowViewModel row, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();

        if (string.IsNullOrWhiteSpace(_settings.WorkspaceSlug))
        {
            throw new InvalidOperationException("Workspace slug is empty.");
        }

        var promptMessage = PromptFormatter.Apply(_settings.PromptTemplate, row);
        var payload = new
        {
            message = promptMessage,
            mode = _settings.Mode,
            sessionId = BuildSessionId(row)
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var requestPath = $"workspace/{Uri.EscapeDataString(_settings.WorkspaceSlug)}/chat";
        using var response = await _httpClient.PostAsync(requestPath, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        NotifyResponseReceived(response.RequestMessage?.RequestUri, body, promptMessage);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildHttpErrorMessage(response.StatusCode, body, response.RequestMessage?.RequestUri, requestPath, _settings.WorkspaceSlug));
        }

        return ReviewResponseParser.ParseReviewResponse(body, "AnythingLLM");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    private string BuildSessionId(ReviewRowViewModel row)
    {
        return $"{_settings.SessionPrefix}-{row.Id}";
    }

    private static Uri NormalizeBaseUri(string baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            trimmed = "http://localhost:3001";
        }

        if (trimmed.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed + "/", UriKind.Absolute);
        }

        if (trimmed.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed + "/v1/", UriKind.Absolute);
        }

        return new Uri(trimmed + "/api/v1/", UriKind.Absolute);
    }

    private string BuildHttpErrorMessage(
        System.Net.HttpStatusCode statusCode,
        string body,
        Uri? requestUri,
        string requestPath,
        string workspaceSlug)
    {
        var cleanBody = TrimForLog(body, 2000);
        var absoluteUri = requestUri?.ToString() ?? $"{_httpClient.BaseAddress}{requestPath}";

        if (statusCode == System.Net.HttpStatusCode.NotFound)
        {
            return
                $"AnythingLLM trả về 404 tại {absoluteUri}. " +
                $"Thường là sai BaseUrl, sai WorkspaceSlug ('{workspaceSlug}'), hoặc instance chưa expose API dưới /api/v1. " +
                $"Nội dung: {cleanBody}";
        }

        return $"AnythingLLM trả về HTTP {(int)statusCode} ({statusCode}) tại {absoluteUri}. Nội dung: {cleanBody}";
    }

    private static TranslationReviewResponse ParseReviewResponse(string body)
    {
        var trimmed = body.Trim();

        if (TryParseReviewResponseObject(trimmed, out var parsed))
        {
            return parsed;
        }

        var fenced = StripCodeFences(trimmed);
        if (!string.Equals(fenced, trimmed, StringComparison.Ordinal))
        {
            if (TryParseReviewResponseObject(fenced, out parsed))
            {
                return parsed;
            }
        }

        var candidate = ExtractJsonCandidate(trimmed);
        if (!string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate, trimmed, StringComparison.Ordinal))
        {
            if (TryParseReviewResponseObject(candidate, out parsed))
            {
                return parsed;
            }
        }

        throw new InvalidOperationException($"Không thể đọc phản hồi từ AnythingLLM. Raw: {TrimForLog(body, 3000)}");
    }

    private static bool TryParseReviewResponseObject(string text, out TranslationReviewResponse response)
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
            if (TryParseReviewResponseObject(nestedText, out var nested))
            {
                response = nested with { RawResponse = text };
                return true;
            }

            var nestedCandidate = ExtractJsonCandidate(nestedText);
            if (!string.IsNullOrWhiteSpace(nestedCandidate) &&
                !string.Equals(nestedCandidate, nestedText, StringComparison.Ordinal) &&
                TryParseReviewResponseObject(nestedCandidate, out nested))
            {
                response = nested with { RawResponse = text };
                return true;
            }

            if (TryRecoverMalformedReviewResponse(nestedText, out nested))
            {
                response = nested with { RawResponse = text };
                return true;
            }

            throw new InvalidOperationException(
                "AnythingLLM returned textResponse that is not the required JSON object. " +
                "Expected: {\"suggestedTranslation\":\"...\",\"rating\":8.5}. " +
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
            response = new TranslationReviewResponse(suggestion.Trim(), finalRating, text);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            throw new InvalidOperationException(
                "AnythingLLM response has suggestedTranslation but is missing a numeric rating. " +
                $"Raw: {TrimForLog(text, 3000)}");
        }

        return false;
    }

    private static bool TryRecoverMalformedReviewResponse(string text, out TranslationReviewResponse response)
    {
        response = default!;

        const string prefix = "{\"suggestedTranslation\":\"";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Local models may escape the quotes around the rating field, too.
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

            response = new TranslationReviewResponse(suggestion.Trim(), rating, text);
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

    private static string TrimForLog(string? value, int maxLength)
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

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AnythingLlmClient));
        }
    }

    private void NotifyResponseReceived(Uri? requestUri, string body, string? requestPayload = null)
    {
        ResponseReceived?.Invoke(new AnythingLlmApiResponse(requestUri?.ToString() ?? string.Empty, requestPayload, body));
    }
}

public sealed record AnythingLlmApiResponse(string RequestUrl, string? RequestPayload, string Body);

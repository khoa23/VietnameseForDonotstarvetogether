using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AnythingLLMReviewTranslator;

/// <summary>
/// HTTP client hỗ trợ hai chế độ:
///  - Google Gemini API (detected by "generativelanguage.googleapis.com" trong BaseUrl)
///  - OpenAI-compatible API: OpenRouter, DeepSeek, Groq, v.v. (mọi URL khác)
/// </summary>
public sealed class GeminiClient : ITranslationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly GeminiSettings _geminiSettings;
    private readonly string _promptTemplate;
    private readonly HttpClient _httpClient;
    private readonly bool _isGoogleGemini;
    private readonly List<string> _apiKeys;
    private readonly List<string> _models;
    private readonly Dictionary<string, GeminiModelRateConfig> _modelRateMap;
    private readonly Dictionary<string, ModelRateTracker> _rateTrackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxCredentialAttempts;
    private int _apiKeyIndex;
    private int _modelIndex;
    private bool _disposed;

    public event Action<AnythingLlmApiResponse>? ResponseReceived;

    public GeminiClient(GeminiSettings geminiSettings, string promptTemplate)
    {
        _geminiSettings = geminiSettings;
        _promptTemplate = promptTemplate;
        _apiKeys = geminiSettings.ApiKeys.Count > 0
            ? geminiSettings.ApiKeys.ToList()
            : (string.IsNullOrWhiteSpace(geminiSettings.ApiKey) ? new List<string>() : new List<string> { geminiSettings.ApiKey });
        _models = geminiSettings.Models.Count > 0
            ? geminiSettings.Models.ToList()
            : (string.IsNullOrWhiteSpace(geminiSettings.Model) ? new List<string>() : new List<string> { geminiSettings.Model });
        _modelRateMap = geminiSettings.ModelConfigs
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _apiKeyIndex = 0;
        _modelIndex = 0;
        _maxCredentialAttempts = Math.Max(1, _apiKeys.Count * Math.Max(1, _models.Count));

        var baseUrl = (geminiSettings.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "https://openrouter.ai/api/v1";
        }

        _isGoogleGemini = baseUrl.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(geminiSettings.RequestTimeoutSeconds)
        };

        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!_isGoogleGemini && !string.IsNullOrWhiteSpace(GetCurrentApiKey()))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GetCurrentApiKey());
        }
    }

    public async Task<string> TestApiAsync(CancellationToken cancellationToken)
    {
        EnsureNotDisposed();

        var effectiveKey = GetCurrentApiKey();
        if (string.IsNullOrWhiteSpace(effectiveKey))
        {
            throw new InvalidOperationException("API Key chưa được cài đặt.");
        }

        var model = GetModel();

        if (_isGoogleGemini)
        {
            return await TestGeminiAsync(model, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return await TestOpenAiCompatibleAsync(model, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TranslationReviewResponse> ReviewTranslationAsync(ReviewRowViewModel row, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();

        if (string.IsNullOrWhiteSpace(GetCurrentApiKey()))
        {
            throw new InvalidOperationException("API Key chưa được cài đặt.");
        }

        var model = GetModel();
        var promptMessage = PromptFormatter.Apply(_promptTemplate, row);

        string body;
        Uri? requestUri;

        if (_isGoogleGemini)
        {
            (body, requestUri) = await SendGeminiRequestAsync(model, promptMessage, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            (body, requestUri) = await SendOpenAiRequestAsync(model, promptMessage, cancellationToken).ConfigureAwait(false);
        }

        NotifyResponseReceived(requestUri, body, promptMessage);

        var providerName = _isGoogleGemini ? "Gemini" : "OpenRouter/OpenAI";
        var extractedText = _isGoogleGemini
            ? ExtractGeminiText(body)
            : ExtractOpenAiText(body);

        return ReviewResponseParser.ParseReviewResponse(extractedText, providerName);
    }

    public async Task<IReadOnlyDictionary<long, TranslationReviewResponse>> ReviewTranslationsAsync(
        IReadOnlyList<ReviewRowViewModel> rows,
        CancellationToken cancellationToken)
    {
        EnsureNotDisposed();

        if (rows.Count == 0)
        {
            return new Dictionary<long, TranslationReviewResponse>();
        }

        if (!_isGoogleGemini)
        {
            var fallback = new Dictionary<long, TranslationReviewResponse>();
            foreach (var row in rows)
            {
                fallback[row.Id] = await ReviewTranslationAsync(row, cancellationToken).ConfigureAwait(false);
            }

            return fallback;
        }

        var model = GetModel();
        var batchPrompt = BuildBatchPrompt(rows);
        var (body, _) = await SendGeminiRequestAsync(model, batchPrompt, cancellationToken).ConfigureAwait(false);
        var extractedText = ExtractGeminiText(body);
        return ParseBatchGeminiResponse(extractedText);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }

    // ─── Google Gemini ────────────────────────────────────────────────────────

    private sealed class ModelRateTracker
    {
        public DateTime MinuteStartUtc { get; set; } = DateTime.MinValue;
        public int MinuteCount { get; set; }
        public DateTime DayStartUtc { get; set; } = DateTime.MinValue;
        public int DayCount { get; set; }
    }

    private async Task<string> TestGeminiAsync(string model, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _maxCredentialAttempts; attempt++)
        {
            try
            {
                await WaitForModelRateLimitAsync(model, cancellationToken).ConfigureAwait(false);

                var key = GetCurrentApiKey();
                var requestPath = $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(key)}";

                var payload = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = "Reply with only valid JSON: {\"status\":\"ok\"}" } } }
                    },
                    generationConfig = new { responseMimeType = "application/json" }
                };

                var json = JsonSerializer.Serialize(payload, JsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(requestPath, content, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                NotifyResponseReceived(response.RequestMessage?.RequestUri, body, json);

                if (!response.IsSuccessStatusCode)
                {
                    ThrowApiError(response.StatusCode, body, model);
                }

                return body;
            }
            catch (RateLimitExceededException) when (TryRotateCredential() && attempt < _maxCredentialAttempts - 1)
            {
                model = GetCurrentModel();
                continue;
            }
        }

        throw new InvalidOperationException("Gemini rate limit retry exhausted.");
    }

    private async Task<(string body, Uri? requestUri)> SendGeminiRequestAsync(string model, string promptMessage, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _maxCredentialAttempts; attempt++)
        {
            try
            {
                await WaitForModelRateLimitAsync(model, cancellationToken).ConfigureAwait(false);

                var key = GetCurrentApiKey();
                var requestPath = $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(key)}";

                var payload = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = promptMessage } } }
                    },
                    generationConfig = new { responseMimeType = "application/json" }
                };

                var json = JsonSerializer.Serialize(payload, JsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(requestPath, content, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    ThrowApiError(response.StatusCode, body, model);
                }

                return (body, response.RequestMessage?.RequestUri);
            }
            catch (RateLimitExceededException) when (TryRotateCredential() && attempt < _maxCredentialAttempts - 1)
            {
                model = GetCurrentModel();
                continue;
            }
        }

        throw new InvalidOperationException("Gemini rate limit retry exhausted.");
    }

    private static string ExtractGeminiText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorElement))
        {
            var message = errorElement.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : errorElement.GetRawText();
            throw new InvalidOperationException($"Gemini API error: {message}");
        }

        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Gemini API không trả về candidate nào (có thể do bộ lọc an toàn).");
        }

        var firstCandidate = candidates[0];
        if (!firstCandidate.TryGetProperty("content", out var contentEl) ||
            !contentEl.TryGetProperty("parts", out var partsEl) ||
            partsEl.ValueKind != JsonValueKind.Array ||
            partsEl.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Nội dung phản hồi Gemini rỗng.");
        }

        if (!partsEl[0].TryGetProperty("text", out var textEl))
        {
            throw new InvalidOperationException("Thiếu trường 'text' trong phản hồi Gemini.");
        }

        return textEl.GetString() ?? string.Empty;
    }

    private static string BuildBatchPrompt(IReadOnlyList<ReviewRowViewModel> rows)
    {
        var lines = new List<string>
        {
            "Bạn là chuyên gia hiệu đính và dịch thuật tiếng Việt cho game Don't Starve Together.",
            "Nhiệm vụ:",
            "1. Chấm điểm bản dịch hiện tại (MsgStr) so với MsgId theo thang rating (0.0 - 10.0).",
            "2. Đề xuất bản dịch tiếng Việt tối ưu nhất cho suggestedTranslation.",
            "3. Giữ nguyên tuyệt đối tất cả các ký tự đặc biệt, dấu gạch chéo ngược (\\), dấu ngoặc kép (\"), escaped quotes (\\\"), ký tự xuống dòng (\\n, \\r), tab (\\t), placeholder (%s, {0}, {name}), mã thẻ định dạng.",
            "Ví dụ:",
            "Input: \\\"Not yet mid-summer\\\", you say? Well my friend, the early bird gets the worm!",
            "Output suggestedTranslation: \\\"Chưa đến giữa hè\\\", bạn nói? Bạn ơi, con chim sớm sẽ có sâu!",
            string.Empty,
            "Hãy xử lý tất cả các mục dưới đây và trả về duy nhất một JSON array.",
            "Mỗi phần tử phải có đúng 3 trường: id, suggestedTranslation, rating.",
            "Trả về CHỈ JSON hợp lệ, không bọc markdown, không giải thích thừa.",
            "Ví dụ mẫu: [{\"id\":101,\"suggestedTranslation\":\"\\\"Chưa đến giữa hè\\\", bạn nói?...\",\"rating\":8.5}]",
            string.Empty
        };

        foreach (var row in rows)
        {
            var promptText = PromptFormatter.Apply("Id: {{Id}}\nMsgCtxt: {{MsgCtxt}}\nMsgId: {{MsgId}}\nMsgStr: {{MsgStr}}", row);
            lines.Add($"Item:\n{promptText}");
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IReadOnlyDictionary<long, TranslationReviewResponse> ParseBatchGeminiResponse(string responseText)
    {
        var trimmed = responseText.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new Dictionary<long, TranslationReviewResponse>();
        }

        var result = new Dictionary<long, TranslationReviewResponse>();
        var extracted = ExtractJsonObjects(trimmed);
        if (extracted.Count == 0)
        {
            throw new InvalidOperationException($"Không thể đọc batch response từ Gemini. Raw: {ReviewResponseParser.TrimForLog(responseText, 3000)}");
        }

        foreach (var obj in extracted)
        {
            try
            {
                using var doc = JsonDocument.Parse(obj);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var item = doc.RootElement;
                if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var id = idEl.GetInt64();
                var suggestion = item.TryGetProperty("suggestedTranslation", out var suggestionEl) && suggestionEl.ValueKind == JsonValueKind.String
                    ? suggestionEl.GetString()
                    : item.TryGetProperty("translation", out var translationEl) && translationEl.ValueKind == JsonValueKind.String
                        ? translationEl.GetString()
                        : string.Empty;

                var rating = item.TryGetProperty("rating", out var ratingEl)
                    ? ratingEl.ValueKind == JsonValueKind.Number && ratingEl.TryGetDecimal(out var value)
                        ? value
                        : decimal.TryParse(ratingEl.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                            ? parsed
                            : (decimal?)null
                    : (decimal?)null;

                if (string.IsNullOrWhiteSpace(suggestion) || !rating.HasValue)
                {
                    continue;
                }

                result[id] = new TranslationReviewResponse(suggestion.Trim(), rating, obj);
            }
            catch (JsonException)
            {
                // ignore malformed objects and keep the valid ones
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException($"Không thể đọc batch response từ Gemini. Raw: {ReviewResponseParser.TrimForLog(responseText, 3000)}");
        }

        return result;
    }

    private static IReadOnlyList<string> ExtractJsonObjects(string text)
    {
        var items = new List<string>();
        var start = text.IndexOf('{', StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = FindMatchingBrace(text, start);
            if (end <= start)
            {
                break;
            }

            var candidate = text[start..(end + 1)].Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                items.Add(candidate);
            }

            start = text.IndexOf('{', start + 1);
        }

        return items;
    }

    private static int FindMatchingBrace(string text, int startIndex)
    {
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                    break;
            }
        }

        return -1;
    }

    // ─── OpenAI-compatible (OpenRouter, DeepSeek, Groq, …) ──────────────────

    private async Task<string> TestOpenAiCompatibleAsync(string model, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "user", content = "Reply with only valid JSON: {\"status\":\"ok\"}" }
            }
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("chat/completions", content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        NotifyResponseReceived(response.RequestMessage?.RequestUri, body, json);

        if (!response.IsSuccessStatusCode)
        {
            ThrowApiError(response.StatusCode, body, model);
        }

        return body;
    }

    private async Task<(string body, Uri? requestUri)> SendOpenAiRequestAsync(string model, string promptMessage, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "user", content = promptMessage }
            }
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("chat/completions", content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            ThrowApiError(response.StatusCode, body, model);
        }

        return (body, response.RequestMessage?.RequestUri);
    }

    private static string ExtractOpenAiText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorEl))
        {
            var message = errorEl.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : errorEl.GetRawText();
            throw new InvalidOperationException($"API error: {message}");
        }

        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("API không trả về choices nào trong response.");
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var messageEl) ||
            !messageEl.TryGetProperty("content", out var contentEl))
        {
            throw new InvalidOperationException("Thiếu trường 'message.content' trong phản hồi API.");
        }

        return contentEl.GetString() ?? string.Empty;
    }

    // ─── Shared helpers ───────────────────────────────────────────────────────

    private string GetModel()
    {
        if (_models.Count > 0)
        {
            return _models[_modelIndex];
        }

        return string.IsNullOrWhiteSpace(_geminiSettings.Model)
            ? (_isGoogleGemini ? "gemini-2.5-flash" : "qwen/qwen3.8-max")
            : _geminiSettings.Model.Trim();
    }

    private GeminiModelRateConfig? GetModelRateConfig(string modelName)
    {
        if (_modelRateMap.Count == 0)
        {
            return null;
        }

        return _modelRateMap.TryGetValue(modelName, out var config) ? config : null;
    }

    private async Task WaitForModelRateLimitAsync(string modelName, CancellationToken cancellationToken)
    {
        var config = GetModelRateConfig(modelName);
        if (config is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var tracker = _rateTrackers.TryGetValue(modelName, out var existing) ? existing : new ModelRateTracker();
        var delay = TimeSpan.Zero;

        lock (tracker)
        {
            if (tracker.MinuteStartUtc == DateTime.MinValue || now - tracker.MinuteStartUtc >= TimeSpan.FromMinutes(1))
            {
                tracker.MinuteStartUtc = now;
                tracker.MinuteCount = 0;
            }

            if (tracker.DayStartUtc == DateTime.MinValue || now.Date != tracker.DayStartUtc.Date)
            {
                tracker.DayStartUtc = now;
                tracker.DayCount = 0;
            }

            if (config.Rpm > 0 && tracker.MinuteCount >= config.Rpm)
            {
                var nextMinute = tracker.MinuteStartUtc.AddMinutes(1);
                delay = TimeSpan.FromTicks(Math.Max(0, (nextMinute - now).Ticks));
            }
            else if (config.Rpd > 0 && tracker.DayCount >= config.Rpd)
            {
                var nextDayUtc = now.Date.AddDays(1).Date;
                var nextDay = new DateTime(nextDayUtc.Year, nextDayUtc.Month, nextDayUtc.Day, 0, 0, 0, DateTimeKind.Utc);
                delay = TimeSpan.FromTicks(Math.Max(0, (nextDay - now).Ticks));
            }

            if (delay == TimeSpan.Zero)
            {
                tracker.MinuteCount++;
                tracker.DayCount++;
            }
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            lock (tracker)
            {
                tracker.MinuteStartUtc = DateTime.UtcNow;
                tracker.MinuteCount = 0;
                tracker.DayStartUtc = DateTime.UtcNow;
                tracker.DayCount = 0;
                tracker.MinuteCount++;
                tracker.DayCount++;
            }
        }

        _rateTrackers[modelName] = tracker;
    }

    private string GetCurrentApiKey()
    {
        if (_apiKeys.Count > 0)
        {
            return _apiKeys[_apiKeyIndex];
        }

        return _geminiSettings.ApiKey;
    }

    private string GetCurrentModel()
    {
        if (_models.Count > 0)
        {
            return _models[_modelIndex];
        }

        return _geminiSettings.Model;
    }

    private bool TryRotateCredential()
    {
        if (_models.Count > 1)
        {
            _modelIndex = (_modelIndex + 1) % _models.Count;
            if (!_isGoogleGemini && _httpClient.DefaultRequestHeaders.Authorization is not null)
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", GetCurrentApiKey());
            }

            return true;
        }

        if (_apiKeys.Count > 1)
        {
            _apiKeyIndex = (_apiKeyIndex + 1) % _apiKeys.Count;
            _modelIndex = 0;

            if (!_isGoogleGemini && _httpClient.DefaultRequestHeaders.Authorization is not null)
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", GetCurrentApiKey());
            }

            return true;
        }

        return false;
    }

    private void ThrowApiError(System.Net.HttpStatusCode statusCode, string body, string model)
    {
        var cleanBody = body.Length > 1500 ? body[..1500] + "..." : body;
        var errorMsg = $"API trả về lỗi HTTP {(int)statusCode} ({statusCode}) cho model '{model}'. Phản hồi: {cleanBody}";

        if (statusCode == System.Net.HttpStatusCode.TooManyRequests ||
            body.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("rate_limit", StringComparison.OrdinalIgnoreCase))
        {
            var retryAfter = ParseRetryDelay(body);
            throw new RateLimitExceededException(errorMsg, retryAfter);
        }

        throw new InvalidOperationException(errorMsg);
    }

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GeminiClient));
    }

    private void NotifyResponseReceived(Uri? requestUri, string body, string? requestPayload = null)
    {
        var urlString = requestUri?.ToString() ?? string.Empty;
        if (!string.IsNullOrEmpty(_geminiSettings.ApiKey) && urlString.Contains(_geminiSettings.ApiKey))
        {
            urlString = urlString.Replace(_geminiSettings.ApiKey, "***API_KEY***");
        }

        ResponseReceived?.Invoke(new AnythingLlmApiResponse(urlString, requestPayload, body));
    }

    private static TimeSpan? ParseRetryDelay(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errorEl) &&
                errorEl.TryGetProperty("details", out var detailsEl) &&
                detailsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in detailsEl.EnumerateArray())
                {
                    if (detail.TryGetProperty("retryDelay", out var delayEl))
                    {
                        var delayStr = delayEl.GetString()?.TrimEnd('s');
                        if (double.TryParse(delayStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var sec))
                        {
                            return TimeSpan.FromSeconds(Math.Ceiling(sec));
                        }
                    }
                }
            }
        }
        catch { /* fallback to regex */ }

        var match = System.Text.RegularExpressions.Regex.Match(body, @"retry(?:ing)?\s+(?:in\s+)?(\d+(?:\.\d+)?)s", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(Math.Ceiling(seconds));
        }

        return null;
    }
}

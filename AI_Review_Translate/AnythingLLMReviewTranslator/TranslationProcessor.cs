namespace AnythingLLMReviewTranslator;

public sealed class TranslationProcessor
{
    private readonly MssqlTranslationRepository _repository;
    private readonly ITranslationClient _client;
    private readonly ProcessingSettings _processingSettings;
    private readonly GlossaryDictionary? _glossary;

    public TranslationProcessor(
        MssqlTranslationRepository repository,
        ITranslationClient client,
        ProcessingSettings processingSettings,
        GlossaryDictionary? glossary = null)
    {
        _repository = repository;
        _client = client;
        _processingSettings = processingSettings;
        _glossary = glossary;
    }

    public async Task ProcessAsync(
        IReadOnlyList<ReviewRowViewModel> rows,
        IProgress<ProcessorProgress> progress,
        CancellationToken cancellationToken)
    {
        var total = rows.Count;
        var completed = 0;
        var skipped = 0;
        var succeeded = 0;
        var failed = 0;
        var rowsToProcess = new List<ReviewRowViewModel>(rows.Count);

        if (_client is GeminiClient && rowsToProcess.Count == 0 && rows.Count > 1)
        {
            // no-op, handled below after filtering
        }

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_processingSettings.RespectTranslationLocked && row.TranslationLocked == true)
            {
                Interlocked.Increment(ref skipped);
                var currentCompleted = Interlocked.Increment(ref completed);
                progress.Report(new ProcessorProgress(
                    row.Id,
                    currentCompleted,
                    total,
                    $"Skipped Id={row.Id}: TranslationLocked = 1.",
                    Status: "Skipped",
                    IsSkipped: true));
                continue;
            }

            if (_processingSettings.SkipIfSuggestedExists && !string.IsNullOrWhiteSpace(row.SuggestedTranslation))
            {
                Interlocked.Increment(ref skipped);
                var currentCompleted = Interlocked.Increment(ref completed);
                progress.Report(new ProcessorProgress(
                    row.Id,
                    currentCompleted,
                    total,
                    $"Skipped Id={row.Id}: SuggestedTranslation already exists.",
                    Status: "Skipped",
                    IsSkipped: true));
                continue;
            }

            rowsToProcess.Add(row);
        }

        SemaphoreSlim? rateLimiter = _processingSettings.RequestsPerMinute > 0
            ? new SemaphoreSlim(1, 1)
            : null;
        DateTime lastRequestTime = DateTime.MinValue;

        async Task WaitForRequestSlotAsync(CancellationToken token)
        {
            if (rateLimiter is null)
            {
                return;
            }

            await rateLimiter.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var minIntervalMs = (int)Math.Ceiling(60000.0 / _processingSettings.RequestsPerMinute);
                if (lastRequestTime != DateTime.MinValue)
                {
                    var elapsedMs = (DateTime.UtcNow - lastRequestTime).TotalMilliseconds;
                    var delayMs = (int)Math.Ceiling(minIntervalMs - elapsedMs);
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, token).ConfigureAwait(false);
                    }
                }

                lastRequestTime = DateTime.UtcNow;
            }
            finally
            {
                rateLimiter.Release();
            }
        }

        var maxDegreeOfParallelism = _processingSettings.MaxConcurrentRequests;

        if (_client is GeminiClient && rowsToProcess.Count >= 2)
        {
            const int batchSize = 300;
            for (var i = 0; i < rowsToProcess.Count; i += batchSize)
            {
                var batch = rowsToProcess.Skip(i).Take(batchSize).ToList();
                var fromIndex = i + 1;
                var toIndex = Math.Min(i + batch.Count, rowsToProcess.Count);

                progress.Report(new ProcessorProgress(
                    null,
                    completed,
                    total,
                    $"Đang gửi batch {batch.Count} dòng lên Gemini (từ dòng {fromIndex} đến {toIndex})...",
                    Status: "SendingBatch"));

                await WaitForRequestSlotAsync(cancellationToken).ConfigureAwait(false);
                var results = await _client.ReviewTranslationsAsync(batch, cancellationToken).ConfigureAwait(false);

                progress.Report(new ProcessorProgress(
                    null,
                    completed,
                    total,
                    $"Đã nhận kết quả batch từ Gemini: {results.Count}/{batch.Count} dòng thành công.",
                    Status: "BatchReceived"));

                var missingRows = new List<ReviewRowViewModel>();
                foreach (var row in batch)
                {
                    if (!results.TryGetValue(row.Id, out var reviewResponse))
                    {
                        missingRows.Add(row);
                        continue;
                    }

                    row.SuggestedTranslation = reviewResponse.SuggestedTranslation;
                    row.Rating = reviewResponse.Rating;
                    row.Status = "Reviewed";
                    row.Error = null;

                    var currentDone = Interlocked.Increment(ref completed);
                    progress.Report(new ProcessorProgress(
                        row.Id,
                        currentDone,
                        total,
                        $"[{row.Id}] Đã dịch thành công bằng batch Gemini.",
                        Status: "Reviewed",
                        SuggestedTranslation: reviewResponse.SuggestedTranslation,
                        Rating: reviewResponse.Rating,
                        RawResponse: reviewResponse.RawResponse));

                    await _repository.UpdateTranslationAsync(
                        row.Id,
                        reviewResponse.SuggestedTranslation,
                        reviewResponse.Rating,
                        cancellationToken).ConfigureAwait(false);
                    succeeded++;
                }

                var fallbackOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = maxDegreeOfParallelism
                };

                await Parallel.ForEachAsync(missingRows, fallbackOptions, async (row, token) =>
                {
                    try
                    {
                        await WaitForRequestSlotAsync(token).ConfigureAwait(false);
                        var reviewResponse = await _client.ReviewTranslationAsync(row, token).ConfigureAwait(false);
                        row.SuggestedTranslation = reviewResponse.SuggestedTranslation;
                        row.Rating = reviewResponse.Rating;
                        row.Status = "Reviewed";
                        row.Error = null;

                        await _repository.UpdateTranslationAsync(
                            row.Id,
                            reviewResponse.SuggestedTranslation,
                            reviewResponse.Rating,
                            token).ConfigureAwait(false);

                        Interlocked.Increment(ref succeeded);
                        var currentDone = Interlocked.Increment(ref completed);
                        progress.Report(new ProcessorProgress(
                            row.Id,
                            currentDone,
                            total,
                            $"[{row.Id}] Fallback single-row OK.",
                            Status: "Reviewed",
                            SuggestedTranslation: reviewResponse.SuggestedTranslation,
                            Rating: reviewResponse.Rating,
                            RawResponse: reviewResponse.RawResponse));
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception rowEx)
                    {
                        Interlocked.Increment(ref failed);
                        var currentFailed = Interlocked.Increment(ref completed);
                        progress.Report(new ProcessorProgress(
                            row.Id,
                            currentFailed,
                            total,
                            $"[{row.Id}] Fallback single-row lỗi: {rowEx.Message}",
                            Status: "Failed",
                            Error: rowEx.Message));
                    }
                }).ConfigureAwait(false);
            }

            progress.Report(new ProcessorProgress(
                null,
                completed,
                total,
                $"Completed. Saved: {succeeded}, skipped: {skipped}, failed: {failed}.",
                IsSummary: true));
            return;
        }

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        };

        await Parallel.ForEachAsync(rowsToProcess, parallelOptions, async (row, token) =>
        {
            TranslationReviewResponse? reviewResponse = null;
            Exception? lastError = null;

            for (var attempt = 0; attempt <= _processingSettings.MaxRetries; attempt++)
            {
                await WaitForRequestSlotAsync(token).ConfigureAwait(false);

                try
                {
                    var attemptLabel = attempt > 0 ? $" (lần thử {attempt + 1})" : "";
                    var matchedTerms = _glossary?.FindMatches(row.MsgId) ?? Array.Empty<GlossaryEntry>();
                    var dictLabel = matchedTerms.Count > 0 ? $" (kèm {matchedTerms.Count} từ điển)" : "";
                    progress.Report(new ProcessorProgress(
                        row.Id, completed, total,
                        $"[{row.Id}] Đang gửi câu hỏi lên AI{attemptLabel}{dictLabel}...",
                        Status: "Sending"));

                    if (matchedTerms.Count > 0 && attempt == 0)
                    {
                        var termsPreview = string.Join(", ", matchedTerms.Take(5).Select(t => $"\"{t.English}\"->\"{t.Vietnamese}\""));
                        if (matchedTerms.Count > 5)
                        {
                            termsPreview += $" (+{matchedTerms.Count - 5} từ khác)";
                        }
                        progress.Report(new ProcessorProgress(
                            row.Id, completed, total,
                            $"[{row.Id}] 📖 Từ điển áp dụng: {termsPreview}",
                            Status: "Dictionary"));
                    }

                    reviewResponse = await _client.ReviewTranslationAsync(row, token).ConfigureAwait(false);

                    progress.Report(new ProcessorProgress(
                        row.Id, completed, total,
                        $"[{row.Id}] Nhận được phản hồi từ AI. Raw (500 ký tự đầu): {TrimLog(reviewResponse.RawResponse, 500)}",
                        Status: "Received",
                        RawResponse: reviewResponse.RawResponse));

                    progress.Report(new ProcessorProgress(
                        row.Id, completed, total,
                        $"[{row.Id}] Response raw cuối cùng nhận được: {TrimLog(reviewResponse.RawResponse, 1000)}",
                        Status: "Received",
                        RawResponse: reviewResponse.RawResponse));

                    lastError = null;
                    break;
                }
                catch (OperationCanceledException)
                {
                    // User bấm Stop → không skip, rethrow để dừng hẳn.
                    throw;
                }
                catch (RateLimitExceededException rateLimitEx)
                {
                    lastError = rateLimitEx;
                    var waitDelay = rateLimitEx.RetryAfter ?? TimeSpan.FromSeconds(30);
                    var waitMs = Math.Max(1000, (int)Math.Ceiling(waitDelay.TotalMilliseconds));
                    progress.Report(new ProcessorProgress(
                        row.Id,
                        completed,
                        total,
                        $"[Rate Limit 429] Bị dính giới hạn API. Tự động chờ {(waitMs / 1000.0):F0}s trước khi thử lại...",
                        Status: "RateLimited"));

                    await Task.Delay(waitMs, token).ConfigureAwait(false);
                    attempt--;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    progress.Report(new ProcessorProgress(
                        row.Id, completed, total,
                        $"[{row.Id}] Lỗi khi gọi AI (attempt {attempt + 1}/{_processingSettings.MaxRetries + 1}): {ex.Message}",
                        Status: "Error",
                        Error: ex.Message));
                    if (attempt < _processingSettings.MaxRetries && _processingSettings.RetryDelayMs > 0)
                    {
                        var delayMs = Math.Max(0, _processingSettings.RetryDelayMs);
                        await Task.Delay(delayMs, token).ConfigureAwait(false);
                    }
                }
            }

            if (reviewResponse is null)
            {
                Interlocked.Increment(ref failed);
                var currentCompleted = Interlocked.Increment(ref completed);
                progress.Report(new ProcessorProgress(
                    row.Id,
                    currentCompleted,
                    total,
                    $"Translation failed for Id={row.Id}.",
                    Status: "Error",
                    Error: lastError?.Message ?? "Unknown error"));
                return;
            }

            try
            {
                progress.Report(new ProcessorProgress(
                    row.Id, completed, total,
                    $"[{row.Id}] Parse OK. suggestedTranslation={TrimLog(reviewResponse.SuggestedTranslation, 120)}, rating={reviewResponse.Rating}. Đang áp dụng format preserver...",
                    Status: "Parsing"));

                progress.Report(new ProcessorProgress(
                    row.Id, completed, total,
                    $"[{row.Id}] Raw response trước khi save: {TrimLog(reviewResponse.RawResponse, 1200)}",
                    Status: "Parsing",
                    RawResponse: reviewResponse.RawResponse));

                reviewResponse = reviewResponse with
                {
                    SuggestedTranslation = TranslationFormatPreserver.PreserveOuterEscapedQuotes(
                        string.IsNullOrWhiteSpace(row.MsgStr) ? row.MsgId : row.MsgStr,
                        reviewResponse.SuggestedTranslation)
                };

                progress.Report(new ProcessorProgress(
                    row.Id, completed, total,
                    $"[{row.Id}] Sau format preserver: {TrimLog(reviewResponse.SuggestedTranslation, 120)}. Đang validate...",
                    Status: "Validating"));

                ValidateReviewResponse(row, reviewResponse);

                progress.Report(new ProcessorProgress(
                    row.Id, completed, total,
                    $"[{row.Id}] Validate OK. Đang lưu vào DB... suggestedTranslation={TrimLog(reviewResponse.SuggestedTranslation, 120)}, rating={reviewResponse.Rating}",
                    Status: "Saving"));

                await _repository.UpdateTranslationAsync(row.Id, reviewResponse.SuggestedTranslation, reviewResponse.Rating, token).ConfigureAwait(false);

                Interlocked.Increment(ref succeeded);
                var currentCompleted = Interlocked.Increment(ref completed);
                progress.Report(new ProcessorProgress(
                    row.Id,
                    currentCompleted,
                    total,
                    $"[{row.Id}] ✅ Đã lưu DB thành công. Translation={TrimLog(reviewResponse.SuggestedTranslation, 80)}, Rating={reviewResponse.Rating}",
                    SuggestedTranslation: reviewResponse.SuggestedTranslation,
                    Rating: reviewResponse.Rating,
                    Status: "Saved",
                    RawResponse: reviewResponse.RawResponse));
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                var currentCompleted = Interlocked.Increment(ref completed);
                progress.Report(new ProcessorProgress(
                    row.Id,
                    currentCompleted,
                    total,
                    $"[{row.Id}] ❌ Lỗi sau khi nhận AI response: {ex.Message}",
                    SuggestedTranslation: reviewResponse.SuggestedTranslation,
                    Rating: reviewResponse.Rating,
                    Status: "DB error",
                    Error: ex.ToString(),
                    RawResponse: reviewResponse.RawResponse));
            }

            if (_processingSettings.DelayBetweenRequestsMs > 0)
            {
                await Task.Delay(_processingSettings.DelayBetweenRequestsMs, token).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        progress.Report(new ProcessorProgress(
            null,
            Volatile.Read(ref completed),
            total,
            $"Completed. Saved: {Volatile.Read(ref succeeded)}, skipped: {Volatile.Read(ref skipped)}, failed: {Volatile.Read(ref failed)}.",
            IsSummary: true));
    }

    private static void ValidateReviewResponse(ReviewRowViewModel row, TranslationReviewResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.SuggestedTranslation))
        {
            throw new InvalidOperationException("AnythingLLM returned an empty suggestedTranslation.");
        }

        if (!response.Rating.HasValue || response.Rating < 0m || response.Rating > 10m)
        {
            throw new InvalidOperationException(
                $"AnythingLLM returned an invalid rating for Id={row.Id}. Rating must be between 0 and 10.");
        }
    }

    private static string TrimLog(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(empty)";
        var v = value.Trim();
        return v.Length <= maxLength ? v : v[..maxLength] + "...";
    }
}

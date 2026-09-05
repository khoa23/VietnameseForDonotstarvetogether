using System.Collections.Concurrent;
using System.Text;
using ImportPOStringToDB.Data;
using ImportPOStringToDB.Models;
using Microsoft.EntityFrameworkCore;

namespace ImportPOStringToDB.Services;

public class PoEntry
{
    public string Comments { get; set; } = string.Empty;
    public string? MsgCtxt { get; set; }
    public string MsgId { get; set; } = string.Empty;
    public string MsgStr { get; set; } = string.Empty;
    
    // Keep raw format for msgctxt, msgid, and msgstr to avoid reformatting
    public string MsgCtxtRaw { get; set; } = string.Empty;
    public string MsgIdRaw { get; set; } = string.Empty;
    public string MsgStrRaw { get; set; } = string.Empty;
}

public class UpdateTranslationResult
{
    public int TotalCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int NotFoundCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public static class UpdateTranslationService
{
    public static async Task<UpdateTranslationResult> UpdateTranslationsFromDbAsync(
        string connectionString,
        string poFilePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. Parse PO file into entries
                progress?.Report("Đang đọc file PO...");
                var entries = ParsePoFile(poFilePath);
                
                if (entries.Count == 0)
                {
                    return new UpdateTranslationResult
                    {
                        Message = "File PO không chứa dữ liệu hoặc trống."
                    };
                }

                // Header entry (msgid is empty) does not get updated
                var hasHeader = entries.Count > 0 && string.IsNullOrEmpty(entries[0].MsgId);
                var dataEntries = hasHeader ? entries.Skip(1).ToList() : entries;
                var totalCount = dataEntries.Count;

                if (totalCount == 0)
                {
                    return new UpdateTranslationResult
                    {
                        Message = "File PO chỉ có header, không chứa dữ liệu bản dịch."
                    };
                }

                progress?.Report($"Tìm thấy {totalCount} mục trong file PO.");

                // 2. Get all data from database
                progress?.Report("Đang lấy dữ liệu từ database...");
                var dbTranslations = new Dictionary<string, string?>(StringComparer.Ordinal);

                using (var db = new ImportPoDbContext(connectionString))
                {
                    var allDbItems = await db.PoTranslations
                        .AsNoTracking()
                        .Select(x => new { x.MsgCtxt, x.MsgId, x.SuggestedTranslation })
                        .ToListAsync(cancellationToken);

                    foreach (var item in allDbItems)
                    {
                        var key = BuildKey(item.MsgCtxt, item.MsgId);
                        if (!dbTranslations.ContainsKey(key))
                        {
                            dbTranslations[key] = item.SuggestedTranslation;
                        }
                    }
                }

                progress?.Report($"Đã tải {dbTranslations.Count} bản ghi từ database.");

                // 3. Update entries - multi-threaded
                var updatedEntries = new ConcurrentBag<(int index, PoEntry entry)>();
                var updatedCount = 0;
                var notFoundCount = 0;
                var skippedCount = 0;
                var lockObj = new object();

                progress?.Report("Đang tìm bản dịch đề xuất...");

                Parallel.ForEach(
                    dataEntries.Select((e, i) => (index: i, entry: e)),
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    },
                    (item) =>
                    {
                        if (string.IsNullOrEmpty(item.entry.MsgId))
                        {
                            updatedEntries.Add((item.index, item.entry));
                            return;
                        }

                        var key = BuildKey(item.entry.MsgCtxt, item.entry.MsgId);

                        if (dbTranslations.TryGetValue(key, out var suggestedTranslation))
                        {
                            if (!string.IsNullOrEmpty(suggestedTranslation))
                            {
                                var updatedEntry = new PoEntry
                                {
                                    Comments = item.entry.Comments,
                                    MsgCtxt = item.entry.MsgCtxt,
                                    MsgId = item.entry.MsgId,
                                    MsgStr = suggestedTranslation,
                                    MsgCtxtRaw = item.entry.MsgCtxtRaw,
                                    MsgIdRaw = item.entry.MsgIdRaw,
                                    MsgStrRaw = string.Empty
                                };
                                updatedEntries.Add((item.index, updatedEntry));
                                lock (lockObj)
                                {
                                    updatedCount++;
                                }
                            }
                            else
                            {
                                updatedEntries.Add((item.index, item.entry));
                                lock (lockObj)
                                {
                                    skippedCount++;
                                }
                            }
                        }
                        else
                        {
                            updatedEntries.Add((item.index, item.entry));
                            lock (lockObj)
                            {
                                notFoundCount++;
                            }
                        }
                    });

                progress?.Report($"Tìm được {updatedCount} bản dịch cần cập nhật.");

                // 4. Reorder updated entries back to original order
                var sortedEntries = updatedEntries
                    .OrderBy(x => x.index)
                    .Select(x => x.entry)
                    .ToList();

                // Reconstruct entries list with header if present
                var finalEntries = new List<PoEntry>();
                if (hasHeader)
                {
                    finalEntries.Add(entries[0]);
                }
                finalEntries.AddRange(sortedEntries);

                // 5. Write back to file
                progress?.Report("Đang ghi file PO...");
                await WritePoFileAsync(poFilePath, finalEntries, cancellationToken);

                progress?.Report("Hoàn tất!");

                return new UpdateTranslationResult
                {
                    TotalCount = totalCount,
                    UpdatedCount = updatedCount,
                    SkippedCount = skippedCount,
                    NotFoundCount = notFoundCount,
                    Message = $"Cập nhật thành công {updatedCount}/{totalCount} mục."
                };
            }
            catch (OperationCanceledException)
            {
                return new UpdateTranslationResult
                {
                    Message = "Thao tác bị hủy."
                };
            }
            catch (Exception ex)
            {
                return new UpdateTranslationResult
                {
                    Message = $"Lỗi: {ex.Message}"
                };
            }
        }, cancellationToken);
    }

    public static List<PoEntry> ParsePoFile(string filePath)
    {
        var entries = new List<PoEntry>();
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        
        var currentComments = new StringBuilder();
        var currentMsgCtxt = new StringBuilder();
        var currentMsgCtxtRaw = new StringBuilder();
        var currentMsgId = new StringBuilder();
        var currentMsgIdRaw = new StringBuilder();
        var currentMsgStr = new StringBuilder();
        var currentMsgStrRaw = new StringBuilder();
        var currentState = "none"; // none, comments, msgctxt, msgid, msgstr
        var hasParsedMsgId = false;
        var hasParsedMsgStr = false;

        void SaveCurrentEntry()
        {
            if (hasParsedMsgId && hasParsedMsgStr)
            {
                entries.Add(new PoEntry
                {
                    Comments = currentComments.ToString().TrimEnd(),
                    MsgCtxt = currentMsgCtxt.Length > 0 ? DecodePoString(currentMsgCtxt.ToString()) : null,
                    MsgId = DecodePoString(currentMsgId.ToString()),
                    MsgStr = DecodePoString(currentMsgStr.ToString()),
                    MsgCtxtRaw = currentMsgCtxtRaw.ToString().TrimEnd(),
                    MsgIdRaw = currentMsgIdRaw.ToString().TrimEnd(),
                    MsgStrRaw = currentMsgStrRaw.ToString().TrimEnd()
                });
            }

            currentComments.Clear();
            currentMsgCtxt.Clear();
            currentMsgCtxtRaw.Clear();
            currentMsgId.Clear();
            currentMsgIdRaw.Clear();
            currentMsgStr.Clear();
            currentMsgStrRaw.Clear();
            currentState = "none";
            hasParsedMsgId = false;
            hasParsedMsgStr = false;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Empty line marks end of entry
            if (string.IsNullOrWhiteSpace(line))
            {
                SaveCurrentEntry();
                continue;
            }

            // Comment line
            if (trimmed.StartsWith("#"))
            {
                if (hasParsedMsgStr)
                {
                    SaveCurrentEntry();
                }

                currentState = "comments";
                if (currentComments.Length > 0)
                    currentComments.AppendLine();
                currentComments.Append(line);
                continue;
            }

            // msgctxt
            if (trimmed.StartsWith("msgctxt"))
            {
                if (hasParsedMsgStr)
                {
                    SaveCurrentEntry();
                }

                currentState = "msgctxt";
                if (currentMsgCtxtRaw.Length > 0)
                    currentMsgCtxtRaw.AppendLine();
                currentMsgCtxtRaw.Append(line);
                
                var value = trimmed.Substring("msgctxt".Length).TrimStart();
                if (value.StartsWith("\""))
                {
                    currentMsgCtxt.Append(ExtractPoValue(value));
                }
                continue;
            }

            // msgid
            if (trimmed.StartsWith("msgid"))
            {
                if (hasParsedMsgStr)
                {
                    SaveCurrentEntry();
                }

                currentState = "msgid";
                hasParsedMsgId = true;
                if (currentMsgIdRaw.Length > 0)
                    currentMsgIdRaw.AppendLine();
                currentMsgIdRaw.Append(line);
                
                var value = trimmed.Substring("msgid".Length).TrimStart();
                if (value.StartsWith("\""))
                {
                    currentMsgId.Append(ExtractPoValue(value));
                }
                continue;
            }

            // msgstr
            if (trimmed.StartsWith("msgstr"))
            {
                currentState = "msgstr";
                hasParsedMsgStr = true;
                if (currentMsgStrRaw.Length > 0)
                    currentMsgStrRaw.AppendLine();
                currentMsgStrRaw.Append(line);

                var value = trimmed.Substring("msgstr".Length).TrimStart();
                if (value.StartsWith("\""))
                {
                    currentMsgStr.Append(ExtractPoValue(value));
                }
                continue;
            }

            // Continuation line
            if (trimmed.StartsWith("\"") && currentState != "comments" && currentState != "none")
            {
                var value = ExtractPoValue(trimmed);
                switch (currentState)
                {
                    case "msgctxt":
                        currentMsgCtxtRaw.AppendLine();
                        currentMsgCtxtRaw.Append(line);
                        currentMsgCtxt.Append(value);
                        break;
                    case "msgid":
                        currentMsgIdRaw.AppendLine();
                        currentMsgIdRaw.Append(line);
                        currentMsgId.Append(value);
                        break;
                    case "msgstr":
                        currentMsgStrRaw.AppendLine();
                        currentMsgStrRaw.Append(line);
                        currentMsgStr.Append(value);
                        break;
                }
            }
        }

        // Handle last entry if file doesn't end with blank line
        SaveCurrentEntry();

        return entries;
    }

    private static string ExtractPoValue(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("\""))
            return string.Empty;

        var lastQuote = trimmed.LastIndexOf("\"");
        if (lastQuote <= 0)
            return string.Empty;

        return trimmed.Substring(1, lastQuote - 1);
    }

    private static string DecodePoString(string encoded)
    {
        return encoded
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t")
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\");
    }

    private static async Task WritePoFileAsync(
        string filePath,
        List<PoEntry> entries,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = new StringBuilder();

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var isHeaderEntry = string.IsNullOrEmpty(entry.MsgId);

                // Write comments
                if (!string.IsNullOrEmpty(entry.Comments))
                {
                    content.AppendLine(entry.Comments);
                }

                // Only write msgctxt for non-header entries
                if (!isHeaderEntry && !string.IsNullOrEmpty(entry.MsgCtxtRaw))
                {
                    content.AppendLine(entry.MsgCtxtRaw);
                }

                // Write msgid - use raw format to preserve original formatting
                if (!string.IsNullOrEmpty(entry.MsgIdRaw))
                {
                    content.AppendLine(entry.MsgIdRaw);
                }
                else
                {
                    content.AppendLine($"msgid {EncodePoString(entry.MsgId)}");
                }

                // Write msgstr
                if (isHeaderEntry && !string.IsNullOrEmpty(entry.MsgStrRaw))
                {
                    content.AppendLine(entry.MsgStrRaw);
                }
                else
                {
                    content.AppendLine($"msgstr {EncodePoString(entry.MsgStr)}");
                }

                // Empty line between entries (except after last)
                if (i < entries.Count - 1)
                {
                    content.AppendLine();
                }
            }

            File.WriteAllText(filePath, content.ToString(), Encoding.UTF8);
        }, cancellationToken);
    }

    private static string EncodePoString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");

        // If single line and short enough, return as is
        if (!escaped.Contains("\\n") && escaped.Length < 80)
        {
            return $"\"{escaped}\"";
        }

        // For longer strings or multiline, use format: "..."
        // PO format allows multiline strings
        var lines = escaped.Split(new[] { "\\n" }, StringSplitOptions.None);
        if (lines.Length == 1)
        {
            return $"\"{escaped}\"";
        }

        // Multiline format
        var result = new StringBuilder("\"\"");
        for (int i = 0; i < lines.Length; i++)
        {
            result.AppendLine();
            result.Append("\"");
            result.Append(lines[i]);
            if (i < lines.Length - 1)
            {
                result.Append("\\n");
            }
            result.Append("\"");
        }

        return result.ToString();
    }

    private static string BuildKey(string? msgCtxt, string msgId)
    {
        return $"{NormalizeString(msgCtxt)}\u001f{NormalizeString(msgId)}";
    }

    private static string NormalizeString(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }
}

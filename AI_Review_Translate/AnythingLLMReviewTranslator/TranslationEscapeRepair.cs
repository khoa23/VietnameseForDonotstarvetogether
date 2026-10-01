using System.Text;

namespace AnythingLLMReviewTranslator;

public sealed class EscapeRepairResult
{
    public bool Selected { get; set; }
    public long RowId { get; init; }
    public string MsgId { get; init; } = string.Empty;
    public string OriginalTranslation { get; init; } = string.Empty;
    public string RepairedTranslation { get; init; } = string.Empty;
    public string Issues { get; init; } = string.Empty;
}

public static class TranslationEscapeRepair
{
    public static string Repair(string msgId, string translation)
    {
        var escapeQuotes = msgId.Contains("\\\"", StringComparison.Ordinal);
        var escapeNewlines = msgId.Contains("\\n", StringComparison.Ordinal);
        if (!escapeQuotes && !escapeNewlines)
        {
            return translation;
        }

        var repaired = new StringBuilder(translation.Length);
        for (var index = 0; index < translation.Length; index++)
        {
            var current = translation[index];
            if (escapeNewlines && (current == '\r' || current == '\n'))
            {
                if (current == '\r' && index + 1 < translation.Length && translation[index + 1] == '\n')
                {
                    index++;
                }
                repaired.Append("\\n");
                continue;
            }

            if (escapeQuotes && current == '"' && !IsEscaped(translation, index))
            {
                repaired.Append('\\');
            }

            repaired.Append(current);
        }

        return repaired.ToString();
    }

    public static string DescribeIssues(string msgId, string translation)
    {
        var issues = new List<string>();
        if (msgId.Contains("\\\"", StringComparison.Ordinal) && HasUnescapedQuote(translation))
        {
            issues.Add("Thiếu \\");
        }
        if (msgId.Contains("\\n", StringComparison.Ordinal) && HasLiteralNewline(translation))
        {
            issues.Add("Thiếu \\n");
        }
        return string.Join(", ", issues);
    }

    private static bool HasUnescapedQuote(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '"' && !IsEscaped(value, index))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasLiteralNewline(string value) => value.Contains('\n') || value.Contains('\r');

    private static bool IsEscaped(string value, int index)
    {
        var backslashCount = 0;
        for (var preceding = index - 1; preceding >= 0 && value[preceding] == '\\'; preceding--)
        {
            backslashCount++;
        }
        return backslashCount % 2 != 0;
    }
}
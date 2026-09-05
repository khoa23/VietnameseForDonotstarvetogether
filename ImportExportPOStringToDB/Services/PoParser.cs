using System.Text;
using ImportPOStringToDB.Models;

namespace ImportPOStringToDB.Services;

public static class PoParser
{
    public static List<PoTranslation> Parse(string filePath, bool skipHeaderEntry)
    {
        var results = new List<PoTranslation>();
        var builder = new EntryBuilder(skipHeaderEntry);

        foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                builder.Flush(results);
                continue;
            }

            builder.AppendLine(line);
        }

        builder.Flush(results);
        return results;
    }

    private enum CurrentField
    {
        None,
        MsgCtxt,
        MsgId,
        MsgStr
    }

    private sealed class EntryBuilder
    {
        private readonly bool _skipHeaderEntry;
        private readonly StringBuilder _allText = new();
        private readonly StringBuilder _msgCtxt = new();
        private readonly StringBuilder _msgId = new();
        private readonly StringBuilder _msgStr = new();
        private CurrentField _currentField = CurrentField.None;
        private bool _hasData;
        private bool _firstRawLine = true;

        public EntryBuilder(bool skipHeaderEntry)
        {
            _skipHeaderEntry = skipHeaderEntry;
        }

        public void AppendLine(string line)
        {
            AppendRawLine(line);
            _hasData = true;

            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
            {
                return;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                return;
            }

            if (TryAppendKeyword(trimmed, "msgctxt", CurrentField.MsgCtxt, _msgCtxt))
            {
                return;
            }

            if (TryAppendKeyword(trimmed, "msgid", CurrentField.MsgId, _msgId))
            {
                return;
            }

            if (TryAppendKeyword(trimmed, "msgstr", CurrentField.MsgStr, _msgStr))
            {
                return;
            }

            if (trimmed.StartsWith("\"", StringComparison.Ordinal) && _currentField != CurrentField.None)
            {
                AppendContinuation(trimmed);
            }
        }

        public void Flush(List<PoTranslation> results)
        {
            if (!_hasData)
            {
                Reset();
                return;
            }

            var msgId = _msgId.ToString();
            var msgStr = _msgStr.ToString();
            var msgCtxt = _msgCtxt.Length == 0 ? null : _msgCtxt.ToString();

            var isHeader = _skipHeaderEntry && string.IsNullOrEmpty(msgId);
            if (!isHeader)
            {
                results.Add(new PoTranslation
                {
                    AllText = _allText.ToString(),
                    MsgCtxt = msgCtxt,
                    MsgId = msgId,
                    MsgStr = msgStr,
                    SuggestedTranslation = null,
                    Rating = null
                });
            }

            Reset();
        }

        private bool TryAppendKeyword(string line, string keyword, CurrentField field, StringBuilder builder)
        {
            if (!StartsWithToken(line, keyword))
            {
                return false;
            }

            _currentField = field;
            AppendDecoded(builder, ExtractPoString(line.Substring(keyword.Length)));
            return true;
        }

        private void AppendContinuation(string line)
        {
            switch (_currentField)
            {
                case CurrentField.MsgCtxt:
                    AppendDecoded(_msgCtxt, ExtractPoString(line));
                    break;
                case CurrentField.MsgId:
                    AppendDecoded(_msgId, ExtractPoString(line));
                    break;
                case CurrentField.MsgStr:
                    AppendDecoded(_msgStr, ExtractPoString(line));
                    break;
            }
        }

        private void AppendRawLine(string line)
        {
            if (!_firstRawLine)
            {
                _allText.AppendLine();
            }

            _allText.Append(line);
            _firstRawLine = false;
        }

        private void Reset()
        {
            _allText.Clear();
            _msgCtxt.Clear();
            _msgId.Clear();
            _msgStr.Clear();
            _currentField = CurrentField.None;
            _hasData = false;
            _firstRawLine = true;
        }
    }

    private static bool StartsWithToken(string line, string token)
    {
        if (!line.StartsWith(token, StringComparison.Ordinal))
        {
            return false;
        }

        if (line.Length == token.Length)
        {
            return true;
        }

        var next = line[token.Length];
        return char.IsWhiteSpace(next) || next == '"';
    }

    private static void AppendDecoded(StringBuilder builder, string value)
    {
        if (value.Length > 0)
        {
            builder.Append(value);
        }
    }

        private static string ExtractPoString(string value)
        {
            var trimmed = value.TrimStart();
            if (trimmed.Length == 0)
            {
            return string.Empty;
        }

        if (trimmed[0] != '"')
        {
            return trimmed;
        }

        if (trimmed.Length >= 2 && trimmed[^1] == '"')
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }
            else
            {
                trimmed = trimmed.Substring(1);
            }

            return trimmed;
        }

        private static string UnescapePoText(string text)
        {
            if (text.IndexOf('\\') < 0)
            {
            return text;
        }

        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != '\\')
            {
                builder.Append(ch);
                continue;
            }

            if (i == text.Length - 1)
            {
                builder.Append('\\');
                break;
            }

            var next = text[++i];
            switch (next)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'a':
                    builder.Append('\a');
                    break;
                case 'v':
                    builder.Append('\v');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case '"':
                    builder.Append('"');
                    break;
                case 'x':
                    builder.Append(ParseHexEscape(text, ref i));
                    break;
                default:
                    if (next is >= '0' and <= '7')
                    {
                        builder.Append(ParseOctalEscape(text, ref i, next));
                    }
                    else
                    {
                        builder.Append(next);
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    private static char ParseHexEscape(string text, ref int index)
    {
        var value = 0;
        var digits = 0;

        while (index + 1 < text.Length && digits < 2)
        {
            var candidate = text[index + 1];
            if (!Uri.IsHexDigit(candidate))
            {
                break;
            }

            value = (value * 16) + Convert.ToInt32(candidate.ToString(), 16);
            index++;
            digits++;
        }

        return (char)value;
    }

    private static char ParseOctalEscape(string text, ref int index, char firstDigit)
    {
        var value = firstDigit - '0';
        var digits = 1;

        while (index + 1 < text.Length && digits < 3)
        {
            var candidate = text[index + 1];
            if (candidate < '0' || candidate > '7')
            {
                break;
            }

            value = (value * 8) + (candidate - '0');
            index++;
            digits++;
        }

        return (char)value;
    }
}

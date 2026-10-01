using System.Text;
using System.Text.RegularExpressions;

namespace AnythingLLMReviewTranslator;

public sealed record GlossaryEntry(string English, string Vietnamese);

public sealed class GlossaryDictionary
{
    private readonly List<GlossaryItem> _items;
    private readonly Dictionary<string, List<GlossaryItem>> _itemsByFirstWord = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _items.Count;
    public IReadOnlyList<GlossaryItem> Items => _items;

    public GlossaryDictionary(IEnumerable<GlossaryItem> items)
    {
        _items = items
            .OrderByDescending(x => x.English.Length)
            .ToList();

        foreach (var item in _items)
        {
            if (!_itemsByFirstWord.TryGetValue(item.FirstWord, out var list))
            {
                list = new List<GlossaryItem>();
                _itemsByFirstWord[item.FirstWord] = list;
            }
            list.Add(item);
        }
    }

    public static GlossaryDictionary LoadFromCsv(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File từ điển không tồn tại: {filePath}", filePath);
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var rawList = new List<(string English, string Vietnamese)>();
        var isFirstLine = true;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cols = ParseCsvLine(line);
            if (cols.Count < 2)
            {
                continue;
            }

            var en = cols[0].Trim();
            var vi = cols[1].Trim();

            if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(vi))
            {
                continue;
            }

            // Bỏ qua nếu cột tiếng Anh giống hệt cột tiếng Việt (chưa dịch hoặc không phải cặp dịch thuật)
            if (string.Equals(en, vi, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Bỏ qua các từ quá ngắn (< 2 ký tự như "a", "I") để tránh khớp nhầm
            if (en.Length < 2)
            {
                continue;
            }

            if (isFirstLine)
            {
                isFirstLine = false;
                if (IsHeader(en, vi))
                {
                    continue;
                }
            }

            rawList.Add((en, vi));
        }

        // Nhóm các từ tiếng Anh trùng lặp, gộp nghĩa tiếng Việt bằng dấu gạch chéo
        var groupedItems = rawList
            .GroupBy(x => x.English, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var combinedVi = string.Join(" / ", g.Select(x => x.Vietnamese).Distinct(StringComparer.OrdinalIgnoreCase));
                return new GlossaryItem(g.Key, combinedVi);
            })
            .OrderByDescending(x => x.English.Length)
            .ToList();

        return new GlossaryDictionary(groupedItems);
    }

    public IReadOnlyList<GlossaryEntry> FindMatches(string? text, int maxMatches = int.MaxValue)
    {
        if (string.IsNullOrWhiteSpace(text) || _items.Count == 0)
        {
            return Array.Empty<GlossaryEntry>();
        }

        var matches = new List<GlossaryEntry>();
        var seenEnglish = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _items)
        {
            if (seenEnglish.Contains(item.English))
            {
                continue;
            }

            if (item.MatchRegex.IsMatch(text))
            {
                matches.Add(new GlossaryEntry(item.English, item.Vietnamese));
                seenEnglish.Add(item.English);
                if (maxMatches > 0 && matches.Count >= maxMatches)
                {
                    break;
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Chuẩn hóa msgid có khoảng trắng trước sau (" msgid "), thay thế các dấu câu bằng khoảng trắng,
    /// và loại trừ toàn bộ nội dung trong ngoặc nhọn { } hoặc {{ }} (các biến placeholder như {plant}, {{name}}).
    /// </summary>
    public static string NormalizeMsgIdWithSpaces(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return " ";
        }

        // Loại trừ nội dung trong ngoặc nhọn { } và {{ }}
        while (text.Contains('{') && text.Contains('}'))
        {
            var replaced = Regex.Replace(text, @"\{+[^{}]*\}+", " ");
            if (replaced == text)
            {
                break;
            }
            text = replaced;
        }

        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            // Giữ lại chữ cái, số, dấu gạch nối và dấu nháy đơn trong từ
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '\'')
            {
                chars[i] = ' ';
            }
        }
        return " " + new string(chars) + " ";
    }

    public static string FindCommonWords(string? msgId, string? msgStr)
    {
        if (string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(msgStr))
        {
            return string.Empty;
        }

        var msgStrWords = Regex.Matches(msgStr, @"[\p{L}\p{N}]+(?:[-'][\p{L}\p{N}]+)*")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commonWords = Regex.Matches(msgId, @"[\p{L}\p{N}]+(?:[-'][\p{L}\p{N}]+)*")
            .Select(match => match.Value)
            .Where(word => msgStrWords.Contains(word) && seenWords.Add(word));

        return string.Join(" | ", commonWords);
    }

    /// <summary>
    /// Quét 1 dòng: tìm các từ trong từ điển khớp với MsgId (theo chuẩn ' msgid ')
    /// và kiểm tra xem bản dịch có chứa nghĩa tiếng Việt tương ứng không (LIKE N'%từ%').
    /// </summary>
    public List<GlossaryCheckResult> ScanRow(long rowId, string? msgId, string? msgStr, string? targetTranslation, int maxMatchesPerRow = int.MaxValue)
    {
        if (string.IsNullOrWhiteSpace(msgId) || _items.Count == 0)
        {
            return new List<GlossaryCheckResult>();
        }

        var paddedMsgId = NormalizeMsgIdWithSpaces(msgId);
        var words = paddedMsgId.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return new List<GlossaryCheckResult>();
        }

        var wordSet = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
        var candidateItems = new List<GlossaryItem>();
        var seenCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in wordSet)
        {
            if (_itemsByFirstWord.TryGetValue(word, out var itemsForWord))
            {
                foreach (var item in itemsForWord)
                {
                    if (seenCandidates.Add(item.English))
                    {
                        candidateItems.Add(item);
                    }
                }
            }
        }

        if (candidateItems.Count == 0)
        {
            return new List<GlossaryCheckResult>();
        }

        candidateItems.Sort((a, b) => b.English.Length.CompareTo(a.English.Length));

        var matchedItems = new List<(GlossaryItem Item, bool IsMatch)>();
        var matchedEnglish = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in candidateItems)
        {
            if (matchedEnglish.Contains(item.English))
            {
                continue;
            }

            // Dùng biên từ để không coi "Log" là một phần của "logged".
            if (item.MatchRegex.IsMatch(msgId))
            {
                matchedEnglish.Add(item.English);

                // So khớp tiếng Việt theo kiểu LIKE N'%từ trong từ điển%'
                bool isMatch = false;
                if (!string.IsNullOrWhiteSpace(targetTranslation))
                {
                    foreach (var viPart in item.VietnameseParts)
                    {
                        if (targetTranslation.IndexOf(viPart, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isMatch = true;
                            break;
                        }
                    }
                }

                matchedItems.Add((item, isMatch));

                if (maxMatchesPerRow > 0 && matchedItems.Count >= maxMatchesPerRow)
                {
                    break;
                }
            }
        }

        if (matchedItems.Count == 0)
        {
            return new List<GlossaryCheckResult>();
        }

        return new List<GlossaryCheckResult>
        {
            new GlossaryCheckResult
            {
                RowId = rowId,
                MsgId = msgId,
                MsgStr = msgStr,
                SuggestedTranslation = targetTranslation,
                EnglishTerm = string.Join(" | ", matchedItems.Select(match => match.Item.English)),
                ExpectedVietnamese = string.Join(" | ", matchedItems.Select(match => match.Item.Vietnamese)),
                CommonWords = FindCommonWords(msgId, msgStr),
                IsMatch = matchedItems.All(match => match.IsMatch)
            }
        };
    }

    private static bool IsHeader(string en, string vi)
    {
        var enLower = en.ToLowerInvariant();
        var viLower = vi.ToLowerInvariant();
        bool isEnCol = enLower is "en" or "english" or "eng" or "source" or "tiếng anh" or "tieng anh" or "từ tiếng anh" or "msgid" or "key";
        bool isViCol = viLower is "vi" or "vietnamese" or "vie" or "target" or "tiếng việt" or "tieng viet" or "dịch" or "nghĩa" or "bản dịch" or "msgstr" or "value";
        return isEnCol || (enLower == "english" && viLower == "vietnamese");
    }

    private static List<string> ParseCsvLine(string line)
    {
        char delimiter = ',';
        if (line.Contains('\t'))
        {
            delimiter = '\t';
        }
        else if (!line.Contains(',') && line.Contains(';'))
        {
            delimiter = ';';
        }

        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                fields.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        fields.Add(sb.ToString().Trim());
        return fields;
    }
}

public sealed class GlossaryItem
{
    public string English { get; }
    public string Vietnamese { get; }
    public Regex MatchRegex { get; }
    public string PaddedEnglish { get; }
    public string FirstWord { get; }
    public IReadOnlyList<string> VietnameseParts { get; }

    public GlossaryItem(string english, string vietnamese)
    {
        English = english.Trim();
        Vietnamese = vietnamese.Trim();

        var leftBoundary = English.Length > 0 && char.IsLetterOrDigit(English[0]) ? @"(?<!\w)" : "";
        var rightBoundary = English.Length > 0 && char.IsLetterOrDigit(English[^1]) ? @"(?!\w)" : "";
        MatchRegex = new Regex(leftBoundary + Regex.Escape(English) + rightBoundary, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        PaddedEnglish = " " + English + " ";
        var words = English.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        FirstWord = words.Length > 0 ? words[0].Trim('-', '\'') : English;

        VietnameseParts = Vietnamese
            .Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

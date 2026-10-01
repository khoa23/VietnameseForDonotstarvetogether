using System.Globalization;

namespace AnythingLLMReviewTranslator;

internal static class PromptFormatter
{
    private const string FormatPreservationInstruction =
        "QUY TẮC BẢO TOÀN ĐỊNH DẠNG (BẮT BUỘC):\n" +
        "- Giữ nguyên tuyệt đối tất cả các ký tự đặc biệt, dấu gạch chéo ngược (\\), dấu ngoặc kép (\"), escaped quotes (\\\"), " +
        "ký tự xuống dòng (\\n, \\r), tab (\\t), placeholder (%s, {0}, {name}), mã màu, thẻ định dạng.\n" +
        "- Ví dụ minh họa:\n" +
        "  + Input: \\\"Not yet mid-summer\\\", you say? Well my friend, the early bird gets the worm!\n" +
        "  + Output suggestedTranslation: \\\"Chưa đến giữa hè\\\", bạn nói? Bạn ơi, con chim sớm sẽ có sâu!";

    private const string TranslationRequiredInstruction =
        "QUAN TRỌNG VỀ BẢN DỊCH & CHẤM ĐIỂM (BẮT BUỘC TUÂN THỦ 100%):\n" +
        "- Trường \"suggestedTranslation\" PHẢI LUÔN LUÔN LÀ BẢN DỊCH TIẾNG VIỆT tự nhiên, chuẩn văn phong game Don't Starve Together.\n" +
        "- TUYỆT ĐỐI KHÔNG ĐƯỢC trả về văn bản tiếng Anh trong suggestedTranslation dưới bất kỳ hình thức nào.\n" +
        "- Nếu MsgStr hiện tại chưa được dịch, đang rỗng hoặc đang là tiếng Anh → Hãy chấm rating thấp (0.0 - 1.0).";

    public static string Apply(string template, ReviewRowViewModel row)
    {
        return Apply(template, row, (IReadOnlyList<GlossaryEntry>?)null);
    }

    public static string Apply(string template, ReviewRowViewModel row, GlossaryDictionary? dictionary)
    {
        var matched = dictionary?.FindMatches(row.MsgId) ?? Array.Empty<GlossaryEntry>();
        return Apply(template, row, matched);
    }

    public static string Apply(string template, ReviewRowViewModel row, IReadOnlyList<GlossaryEntry>? matchedGlossary)
    {
        var result = template ?? string.Empty;

        var formattedGlossary = matchedGlossary is { Count: > 0 } ? FormatGlossary(matchedGlossary) : string.Empty;
        var hasGlossaryPlaceholder = result.Contains("{{Dictionary}}", StringComparison.OrdinalIgnoreCase) ||
                                     result.Contains("{{Glossary}}", StringComparison.OrdinalIgnoreCase);

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["{{Id}}"] = row.Id.ToString(CultureInfo.InvariantCulture),
            ["{{AllText}}"] = row.AllText,
            ["{{MsgCtxt}}"] = row.MsgCtxt,
            ["{{MsgId}}"] = row.MsgId,
            ["{{MsgStr}}"] = row.MsgStr,
            ["{{SuggestedTranslation}}"] = row.SuggestedTranslation,
            ["{{Rating}}"] = row.Rating?.ToString(CultureInfo.InvariantCulture),
            ["{{SourceFilePath}}"] = row.SourceFilePath,
            ["{{ImportedAtUtc}}"] = row.ImportedAtUtc?.ToString("o", CultureInfo.InvariantCulture),
            ["{{TranslationLocked}}"] = row.TranslationLocked?.ToString(),
            ["{{Dictionary}}"] = formattedGlossary,
            ["{{Glossary}}"] = formattedGlossary
        };

        foreach (var pair in values)
        {
            result = result.Replace(pair.Key, pair.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        var extraInstructions = new System.Text.StringBuilder();
        extraInstructions.AppendLine(FormatPreservationInstruction);
        extraInstructions.AppendLine();
        extraInstructions.AppendLine(TranslationRequiredInstruction);

        if (!hasGlossaryPlaceholder && !string.IsNullOrEmpty(formattedGlossary))
        {
            extraInstructions.AppendLine();
            extraInstructions.Append(formattedGlossary);
        }

        return result.TrimEnd() + Environment.NewLine + Environment.NewLine + extraInstructions.ToString().TrimEnd();
    }

    private static string FormatGlossary(IReadOnlyList<GlossaryEntry> entries)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("DANH MỤC THUẬT NGỮ THAM KHẢO (Khi dịch MsgId sang tiếng Việt, BẮT BUỘC ưu tiên sử dụng các nghĩa tiếng Việt này):");
        foreach (var entry in entries)
        {
            sb.AppendLine($"- Từ tiếng Anh \"{entry.English}\" => dịch sang tiếng Việt là \"{entry.Vietnamese}\"");
        }
        sb.AppendLine("LƯU Ý BẮT BUỘC: Bản dịch suggestedTranslation phải hoàn toàn bằng tiếng Việt.");
        return sb.ToString().TrimEnd();
    }
}

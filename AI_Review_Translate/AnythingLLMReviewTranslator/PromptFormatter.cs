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
        "QUAN TRỌNG VỀ CHẤM ĐIỂM & DỊCH:\n" +
        "- MsgStr hiện tại chưa được dịch hoặc đang trống → Hãy chấm rating thấp (0.0 - 1.0).\n" +
        "- Bạn BẮT BUỘC phải dịch MsgId sang tiếng Việt tự nhiên cho suggestedTranslation. TUYỆT ĐỐI không được trả về văn bản tiếng Anh.";

    public static string Apply(string template, ReviewRowViewModel row)
    {
        var result = template ?? string.Empty;

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
            ["{{TranslationLocked}}"] = row.TranslationLocked?.ToString()
        };

        foreach (var pair in values)
        {
            result = result.Replace(pair.Key, pair.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        var extraInstructions = new System.Text.StringBuilder();
        extraInstructions.AppendLine(FormatPreservationInstruction);

        // Phát hiện MsgStr chưa được dịch: rỗng, hoặc trùng với MsgId (case-insensitive, sau khi trim).
        var msgStr = (row.MsgStr ?? string.Empty).Trim().Trim('"').Trim('\\', '"');
        var msgId = (row.MsgId ?? string.Empty).Trim().Trim('"').Trim('\\', '"');
        var msgStrIsUntranslated =
            string.IsNullOrWhiteSpace(row.MsgStr) ||
            string.Equals(msgStr, msgId, StringComparison.OrdinalIgnoreCase);

        if (msgStrIsUntranslated)
        {
            extraInstructions.AppendLine();
            extraInstructions.Append(TranslationRequiredInstruction);
        }

        return result.TrimEnd() + Environment.NewLine + Environment.NewLine + extraInstructions.ToString().TrimEnd();
    }
}

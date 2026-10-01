using System.Text;
using ImportPOStringToDB.Services;
using Xunit;

namespace ImportPOStringToDB.Tests.Services;

public class PoParserTests
{
    [Fact]
    public void Parse_EmptyMsgStr_ShouldIncludeEntry()
    {
        // Arrange
        var poContent = "msgid \"Hello\"\nmsgstr \"\"\n";
        var filePath = "temp_empty_msgstr.po";
        File.WriteAllText(filePath, poContent, Encoding.UTF8);

        try
        {
            // Act
            var entries = PoParser.Parse(filePath, false);

            // Assert
            Assert.Single(entries);
            Assert.Equal("Hello", entries[0].MsgId);
            Assert.Equal("", entries[0].MsgStr);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void UpdateTranslationService_ParsePoFile_EmptyMsgStr_ShouldIncludeEntry()
    {
        // Arrange
        var poContent = @"msgid """"
msgstr """"
""Language: vi\n""

msgctxt ""ACTION""
msgid ""Hello""
msgstr """"
";
        var filePath = "temp_update_empty_msgstr.po";
        File.WriteAllText(filePath, poContent, Encoding.UTF8);

        try
        {
            // Act
            var entries = UpdateTranslationService.ParsePoFile(filePath);

            // Assert
            Assert.Equal(2, entries.Count);
            Assert.Equal("", entries[0].MsgId);
            Assert.Equal("ACTION", entries[1].MsgCtxt);
            Assert.Equal("Hello", entries[1].MsgId);
            Assert.Equal("", entries[1].MsgStr);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void PoTranslation_LastUpdated_And_TranslationLocked_PropertiesWorkCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var item = new ImportPOStringToDB.Models.PoTranslation
        {
            MsgId = "test_msgid",
            MsgStr = "bản dịch thử",
            TranslationLocked = true,
            LastUpdated = now
        };

        // Assert
        Assert.Equal("test_msgid", item.MsgId);
        Assert.Equal("bản dịch thử", item.MsgStr);
        Assert.True(item.TranslationLocked);
        Assert.Equal(now, item.LastUpdated);
    }

    [Fact]
    public void OverwriteItemModel_PropertyChanged_FiresOnToggle()
    {
        // Arrange
        var model = new OverwriteItemModel
        {
            ShouldOverwrite = true,
            ExistingInDb = new ImportPOStringToDB.Models.PoTranslation { MsgId = "id1", MsgStr = "cũ" },
            NewFromPo = new ImportPOStringToDB.Models.PoTranslation { MsgId = "id1", MsgStr = "mới" }
        };

        bool eventFired = false;
        model.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(OverwriteItemModel.ShouldOverwrite))
                eventFired = true;
        };

        // Act
        model.ShouldOverwrite = false;

        // Assert
        Assert.False(model.ShouldOverwrite);
        Assert.True(eventFired);
        Assert.Equal("id1", model.MsgId);
        Assert.Equal("cũ", model.OldMsgStr);
        Assert.Equal("mới", model.NewMsgStr);
    }

    [Fact]
    public void PoTranslation_RatingZero_CanBeAssigned()
    {
        // Arrange
        var item = new ImportPOStringToDB.Models.PoTranslation
        {
            MsgId = "test_item",
            Rating = 0.0,
            TranslationLocked = true
        };

        // Assert
        Assert.Equal(0.0, item.Rating);
        Assert.True(item.TranslationLocked);
    }

    [Fact]
    public void OverwriteConfirmForm_RowsContainBoundData()
    {
        // Arrange
        var list = new List<OverwriteItemModel>
        {
            new OverwriteItemModel
            {
                ShouldOverwrite = true,
                ExistingInDb = new ImportPOStringToDB.Models.PoTranslation { MsgId = "ENGLISH_TEXT", MsgStr = "VIETNAMESE_OLD" },
                NewFromPo = new ImportPOStringToDB.Models.PoTranslation { MsgId = "ENGLISH_TEXT", MsgStr = "VIETNAMESE_NEW" },
                MsgId = "ENGLISH_TEXT",
                OldMsgStr = "VIETNAMESE_OLD",
                NewMsgStr = "VIETNAMESE_NEW"
            }
        };

        // Act
        using var form = new OverwriteConfirmForm(list);

        // Assert
        Assert.Single(form.SelectedItemsToOverwrite);
        Assert.Equal("ENGLISH_TEXT", list[0].MsgId);
        Assert.Equal("VIETNAMESE_OLD", list[0].OldMsgStr);
        Assert.Equal("VIETNAMESE_NEW", list[0].NewMsgStr);
    }

    [Fact]
    public void UpdateTranslationService_BuildKey_ShouldUseMsgCtxtAndMsgIdTogether()
    {
        var buildKeyMethod = typeof(UpdateTranslationService).GetMethod("BuildKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(buildKeyMethod);

        var sameKey1 = buildKeyMethod!.Invoke(null, new object?[] { "CONTEXT_A", "Hello" });
        var sameKey2 = buildKeyMethod.Invoke(null, new object?[] { "CONTEXT_A", "Hello" });
        var differentContextKey = buildKeyMethod.Invoke(null, new object?[] { "CONTEXT_B", "Hello" });
        var differentIdKey = buildKeyMethod.Invoke(null, new object?[] { "CONTEXT_A", "Goodbye" });

        Assert.Equal(sameKey1, sameKey2);
        Assert.NotEqual(sameKey1, differentContextKey);
        Assert.NotEqual(sameKey1, differentIdKey);
    }

    [Fact]
    public void UpdateTranslationService_NormalizeString_ShouldHandleBackslashesInMsgId()
    {
        var normalizeMethod = typeof(UpdateTranslationService)
            .GetMethod("NormalizeString", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(normalizeMethod);

        var raw = @"C:\Users\Test\folder";
        var escaped = "C:\\\\Users\\\\Test\\\\folder";

        var normalizedRaw = normalizeMethod!.Invoke(null, new object?[] { raw });
        var normalizedEscaped = normalizeMethod.Invoke(null, new object?[] { escaped });

        Assert.Equal(normalizedRaw, normalizedEscaped);
        Assert.Equal(@"C:\Users\Test\folder", normalizedRaw);
    }

    [Fact]
    public void UpdateTranslationService_EncodePoString_ShouldKeepSingleLineWithoutAddingEscapes()
    {
        // Arrange
        var input = "A new version is available.\nPlease update Don't Starve Together.";

        // Act
        var encoded = UpdateTranslationService.EncodePoString(input);

        // Assert
        Assert.Equal("\"A new version is available.\nPlease update Don't Starve Together.\"", encoded);
        Assert.Contains("\n", encoded);
        Assert.DoesNotContain("\\n", encoded);
    }

    [Fact]
    public void UpdateTranslationService_EncodePoString_WithEscapedInput_ShouldNotAddMoreEscapes()
    {
        // Arrange
        var input = @"A new version is available.\nPlease update Don't Starve Together.";

        // Act
        var encoded = UpdateTranslationService.EncodePoString(input);

        // Assert
        Assert.Equal("\"A new version is available.\\nPlease update Don't Starve Together.\"", encoded);
        Assert.DoesNotContain("\\\\n", encoded);
    }

    [Fact]
    public void UpdateTranslationService_EncodePoString_ShouldNotAddBackslashes()
    {
        var input = @"C:\mods\translation";

        var encoded = UpdateTranslationService.EncodePoString(input);

        Assert.Equal(@"""C:\mods\translation""", encoded);
    }

    [Fact]
    public async Task UpdateTranslationService_WritePoFileAsync_ShouldOutputSingleLineMsgIdAndMsgStr()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_po_{Guid.NewGuid():N}.po");
        try
        {
            var initialContent = @"msgid """"
msgstr """"
""Application: Dont' Starve\n""
""POT Version: 2.0\n""

msgctxt ""STRINGS.UI.UPDATE""
msgid ""A new version is available.\nPlease update Don't Starve Together.""
msgstr ""Có phiên bản mới.\nVui lòng cập nhật Don't Starve Together.""
";
            File.WriteAllText(tempFile, initialContent, Encoding.UTF8);

            var entries = UpdateTranslationService.ParsePoFile(tempFile);
            Assert.Equal(2, entries.Count);

            // Act
            await UpdateTranslationService.WritePoFileAsync(tempFile, entries);

            // Assert
            var outputLines = File.ReadAllLines(tempFile, Encoding.UTF8);

            // Ensure msgid and msgstr lines are on single lines
            Assert.Contains(outputLines, l => l == @"msgid ""A new version is available.\nPlease update Don't Starve Together.""");
            Assert.Contains(outputLines, l => l == @"msgstr ""Có phiên bản mới.\nVui lòng cập nhật Don't Starve Together.""");

            // Ensure no broken multiline msgid "" exists for non-header entry
            var nonHeaderMsgIdEmptyCount = outputLines.Count(l => l == @"msgid """"");
            Assert.Equal(1, nonHeaderMsgIdEmptyCount); // only header
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UpdateTranslationService_WritePoFileAsync_OnlyModifiesMsgStr_PreservesAllOtherPartsExactly()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_preserve_{Guid.NewGuid():N}.po");
        try
        {
            var initialContent = @"msgid """"
msgstr """"
""Project-Id-Version: DST Vietnamese\n""
""Language: vi\n""

#. STRINGS.ACTIONS.FEED.GENERIC
#: scripts/actions.lua:123
#, fuzzy
msgctxt ""STRINGS.ACTIONS.FEED.GENERIC""
msgid ""Feed \t special %s""
msgstr ""Cho an cu""

#. UNCHANGED_ENTRY
msgctxt ""STRINGS.ACTIONS.SLEEP""
msgid ""Sleep now""
msgstr ""Di ngu""
";
            File.WriteAllText(tempFile, initialContent, Encoding.UTF8);

            var entries = UpdateTranslationService.ParsePoFile(tempFile);
            Assert.Equal(3, entries.Count); // header + 2 entries

            // Simulate updating ONLY the first data entry's translation
            entries[1] = new PoEntry
            {
                Comments = entries[1].Comments,
                MsgCtxt = entries[1].MsgCtxt,
                MsgId = entries[1].MsgId,
                MsgStr = "Cho ăn đặc biệt %s\nvà lớn nhanh", // New translation with newline
                MsgCtxtRaw = entries[1].MsgCtxtRaw,
                MsgIdRaw = entries[1].MsgIdRaw,
                MsgStrRaw = string.Empty // signals updated
            };
            // entries[2] remains untouched (MsgStrRaw is NOT empty)

            // Act
            await UpdateTranslationService.WritePoFileAsync(tempFile, entries);

            // Assert
            var output = File.ReadAllText(tempFile, Encoding.UTF8);
            var outputLines = File.ReadAllLines(tempFile, Encoding.UTF8);

            // 1. Header is preserved exactly
            Assert.Contains(@"""Project-Id-Version: DST Vietnamese\n""", output);
            Assert.Contains(@"""Language: vi\n""", output);

            // 2. Comments, flags, references are preserved exactly
            Assert.Contains("#. STRINGS.ACTIONS.FEED.GENERIC", output);
            Assert.Contains("#: scripts/actions.lua:123", output);
            Assert.Contains("#, fuzzy", output);

            // 3. msgctxt is preserved exactly
            Assert.Contains(@"msgctxt ""STRINGS.ACTIONS.FEED.GENERIC""", output);

            // 4. msgid is preserved exactly as raw
            Assert.Contains(@"msgid ""Feed \t special %s""", output);

            // 5. ONLY msgstr of entry 1 was updated, and formatted on a single line with escaped newline
            Assert.Contains(@"msgstr ""Cho ăn đặc biệt %s\nvà lớn nhanh""", output);
            Assert.DoesNotContain("Cho an cu", output);

            // 6. Entry 2 (unchanged entry) was preserved 100%
            Assert.Contains("#. UNCHANGED_ENTRY", output);
            Assert.Contains(@"msgctxt ""STRINGS.ACTIONS.SLEEP""", output);
            Assert.Contains(@"msgid ""Sleep now""", output);
            Assert.Contains(@"msgstr ""Di ngu""", output);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}

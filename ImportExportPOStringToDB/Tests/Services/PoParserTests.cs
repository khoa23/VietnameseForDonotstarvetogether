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
}

using ImportPOStringToDB.Data;
using ImportPOStringToDB.Models;
using Microsoft.EntityFrameworkCore;

namespace ImportPOStringToDB.Services;

public class ChangedItemComparison
{
    public PoTranslation ExistingInDb { get; set; } = null!;
    public PoTranslation NewFromPo { get; set; } = null!;
}

public class ImportComparisonResult
{
    public int NewCount { get; set; }
    public int UnchangedCount { get; set; }
    public int ChangedCount { get; set; }
    public List<PoTranslation> NewItems { get; set; } = new();
    public List<ChangedItemComparison> ChangedItems { get; set; } = new();
}

public static class PoImportService
{
    public static async Task<ImportComparisonResult> CompareAsync(
        string connectionString,
        IEnumerable<PoTranslation> rows,
        string sourceFilePath,
        bool includeLocked = false,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedSourceFilePath = Path.GetFullPath(sourceFilePath);
            var nowUtc = DateTime.UtcNow;
            var materialized = rows
                .Select(row => new PoTranslation
                {
                    AllText = row.AllText,
                    MsgCtxt = row.MsgCtxt,
                    MsgId = row.MsgId,
                    MsgStr = row.MsgStr,
                    SuggestedTranslation = row.SuggestedTranslation,
                    Rating = row.Rating,
                    TranslationLocked = row.TranslationLocked,
                    SourceFilePath = normalizedSourceFilePath,
                    ImportedAtUtc = nowUtc,
                    LastUpdated = nowUtc
                })
                .ToList();

            using var db = new ImportPoDbContext(connectionString);
            db.Database.EnsureCreated();
            EnsureDatabaseColumnsExist(db);

            var existingList = db.PoTranslations.AsNoTracking().ToList();

            var existingMap = new Dictionary<string, PoTranslation>(StringComparer.Ordinal);
            foreach (var item in existingList)
            {
                var key = BuildKey(item.MsgCtxt, item.MsgId);
                existingMap[key] = item;
            }

            var result = new ImportComparisonResult();

            foreach (var item in materialized)
            {
                var key = BuildKey(item.MsgCtxt, item.MsgId);
                if (existingMap.TryGetValue(key, out var existing))
                {
                    // If not including locked items, skip if translation is locked or Rating is 0 in DB
                    if (!includeLocked && (existing.TranslationLocked || (existing.Rating.HasValue && existing.Rating.Value == 0)))
                    {
                        result.UnchangedCount++;
                        continue;
                    }

                    bool isMsgStrDifferent = NormalizeString(existing.MsgStr) != NormalizeString(item.MsgStr);

                    if (isMsgStrDifferent)
                    {
                        item.Id = existing.Id;
                        result.ChangedItems.Add(new ChangedItemComparison
                        {
                            ExistingInDb = existing,
                            NewFromPo = item
                        });
                        result.ChangedCount++;
                    }
                    else
                    {
                        result.UnchangedCount++;
                    }
                }
                else
                {
                    result.NewItems.Add(item);
                    result.NewCount++;
                }
            }

            return result;
        }, cancellationToken);
    }

    private static string BuildKey(string? msgCtxt, string msgId)
    {
        return $"{NormalizeString(msgCtxt)}\u001f{NormalizeString(msgId)}";
    }

    private static string NormalizeLineEndings(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
    }

    private static string NormalizeString(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static double NormalizeDouble(double? val)
    {
        return val ?? 0.0;
    }

    public static async Task<int> ExecuteImportAsync(
        string connectionString,
        List<PoTranslation> newItems,
        List<PoTranslation> changedItemsToUpdate,
        List<PoTranslation>? itemsToLock = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var db = new ImportPoDbContext(connectionString);
            EnsureDatabaseColumnsExist(db);
            using var transaction = db.Database.BeginTransaction();
            var nowUtc = DateTime.UtcNow;

            if (newItems.Count > 0)
            {
                foreach (var item in newItems)
                {
                    item.LastUpdated = nowUtc;
                }
                db.PoTranslations.AddRange(newItems);
            }

            if (changedItemsToUpdate.Count > 0)
            {
                foreach (var item in changedItemsToUpdate)
                {
                    var entity = db.PoTranslations.Find(item.Id);
                    if (entity != null)
                    {
                        entity.MsgCtxt = item.MsgCtxt;
                        entity.MsgId = item.MsgId;
                        entity.MsgStr = item.MsgStr;
                        entity.SuggestedTranslation = item.SuggestedTranslation;
                        entity.Rating = item.Rating;
                        entity.TranslationLocked = item.TranslationLocked;
                        entity.SourceFilePath = item.SourceFilePath;
                        entity.ImportedAtUtc = item.ImportedAtUtc;
                        entity.LastUpdated = nowUtc;
                    }
                }
            }

            if (itemsToLock != null && itemsToLock.Count > 0)
            {
                foreach (var item in itemsToLock)
                {
                    var entity = db.PoTranslations.Find(item.Id);
                    if (entity != null)
                    {
                        entity.TranslationLocked = true;
                        entity.Rating = 0; // Set rating = 0 as requested
                        entity.LastUpdated = nowUtc;
                    }
                }
            }

            var affected = db.SaveChanges();
            transaction.Commit();
            return affected;
        }, cancellationToken);
    }

    private static void EnsureDatabaseColumnsExist(ImportPoDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.PoTranslations', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.PoTranslations', N'TranslationLocked') IS NULL
    BEGIN
        IF COL_LENGTH(N'dbo.PoTranslations', N'Khóa bản dịch') IS NOT NULL
        BEGIN
            EXEC sp_rename N'[dbo].[PoTranslations].[Khóa bản dịch]', N'TranslationLocked', 'COLUMN';
        END
        ELSE
        BEGIN
            ALTER TABLE [dbo].[PoTranslations]
            ADD [TranslationLocked] bit NOT NULL CONSTRAINT [DF_PoTranslations_TranslationLocked] DEFAULT (0);
        END
    END

    IF COL_LENGTH(N'dbo.PoTranslations', N'LastUpdated') IS NULL
    BEGIN
        ALTER TABLE [dbo].[PoTranslations]
        ADD [LastUpdated] datetime2 NULL;
    END
END";

        db.Database.ExecuteSqlRaw(sql);
    }
}

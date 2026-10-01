using System.Globalization;
using Microsoft.Data.SqlClient;

namespace AnythingLLMReviewTranslator;

public sealed class MssqlTranslationRepository
{
    private readonly SqlServerSettings _settings;
    private readonly ProcessingSettings _processingSettings;
    private readonly int _commandTimeoutSeconds;

    public MssqlTranslationRepository(SqlServerSettings settings, ProcessingSettings processingSettings, int commandTimeoutSeconds = 120)
    {
        _settings = settings;
        _processingSettings = processingSettings;
        _commandTimeoutSeconds = Math.Max(30, commandTimeoutSeconds);
    }

    public async Task<List<ReviewRowViewModel>> LoadRowsAsync(CancellationToken cancellationToken)
    {
        var rows = new List<ReviewRowViewModel>();

        using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandTimeout = _commandTimeoutSeconds;
        command.CommandText = BuildSelectSql();

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new ReviewRowViewModel
            {
                Id = GetLong(reader, _settings.KeyColumn) ?? throw new InvalidOperationException($"Không tìm thấy hoặc không đọc được cột khóa '{_settings.KeyColumn}'."),
                AllText = GetString(reader, _settings.AllTextColumn),
                MsgCtxt = GetString(reader, _settings.MsgCtxtColumn),
                MsgId = GetString(reader, _settings.MsgIdColumn),
                MsgStr = GetString(reader, _settings.MsgStrColumn),
                SuggestedTranslation = GetString(reader, _settings.SuggestedTranslationColumn),
                Rating = GetDecimal(reader, _settings.RatingColumn),
                SourceFilePath = GetString(reader, _settings.SourceFilePathColumn),
                ImportedAtUtc = GetDateTime(reader, _settings.ImportedAtUtcColumn),
                TranslationLocked = GetBool(reader, _settings.TranslationLockedColumn),
                Status = "Loaded"
            };

            rows.Add(row);
            if (_processingSettings.MaxRows > 0 && rows.Count >= _processingSettings.MaxRows)
            {
                break;
            }
        }

        return rows;
    }

    public async Task<List<GlossaryCheckRow>> LoadAllRowsForGlossaryCheckAsync(
        bool excludeLocked = true,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<GlossaryCheckRow>();

        using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandTimeout = Math.Max(_commandTimeoutSeconds, 300);

        var selectColumns = new List<string>();
        AddSelectColumn(selectColumns, _settings.KeyColumn);
        AddSelectColumn(selectColumns, _settings.MsgIdColumn);
        AddSelectColumn(selectColumns, _settings.MsgStrColumn);
        AddSelectColumn(selectColumns, _settings.SuggestedTranslationColumn);

        var whereClauses = new List<string>();
        if (excludeLocked && !string.IsNullOrWhiteSpace(_settings.TranslationLockedColumn))
        {
            whereClauses.Add($"ISNULL({SqlIdentifier.QuotePath(_settings.TranslationLockedColumn)}, 0) <> 1");
        }

        var whereClause = whereClauses.Count > 0 ? " WHERE " + string.Join(" AND ", whereClauses) : string.Empty;
        command.CommandText = $"SELECT {string.Join(", ", selectColumns)} FROM {SqlIdentifier.QuotePath(_settings.SourceTable)}{whereClause} ORDER BY {SqlIdentifier.QuotePath(_settings.KeyColumn)}";

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        int count = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new GlossaryCheckRow
            {
                Id = GetLong(reader, _settings.KeyColumn) ?? 0,
                MsgId = GetString(reader, _settings.MsgIdColumn),
                MsgStr = GetString(reader, _settings.MsgStrColumn),
                SuggestedTranslation = GetString(reader, _settings.SuggestedTranslationColumn)
            });

            count++;
            if (progress is not null && count % 5000 == 0)
            {
                progress.Report(count);
            }
        }

        progress?.Report(count);
        return rows;
    }

    public async Task UpdateTranslationAsync(long id, string? suggestedTranslation, decimal? rating, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.TargetTable))
        {
            throw new InvalidOperationException("TargetTable is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_settings.SuggestedTranslationColumn))
        {
            throw new InvalidOperationException("SuggestedTranslationColumn is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_settings.RatingColumn))
        {
            throw new InvalidOperationException("RatingColumn is not configured.");
        }

        using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandTimeout = _commandTimeoutSeconds;
        command.CommandText =
            $"UPDATE {SqlIdentifier.QuotePath(_settings.TargetTable)} " +
            $"SET {SqlIdentifier.QuotePath(_settings.SuggestedTranslationColumn)} = @SuggestedTranslation, " +
            $"{SqlIdentifier.QuotePath(_settings.RatingColumn)} = @Rating, " +
            "[LastUpdated] = SYSUTCDATETIME() " +
            $"WHERE {SqlIdentifier.QuotePath(_settings.KeyColumn)} = @Id";

        command.Parameters.Add("@SuggestedTranslation", System.Data.SqlDbType.NVarChar, -1).Value =
            (object?)suggestedTranslation ?? DBNull.Value;

        var ratingParameter = command.Parameters.Add("@Rating", System.Data.SqlDbType.Decimal);
        ratingParameter.Precision = 18;
        ratingParameter.Scale = 4;
        ratingParameter.Value = (object?)rating ?? DBNull.Value;

        command.Parameters.Add("@Id", System.Data.SqlDbType.BigInt).Value = id;

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected == 0)
        {
            throw new InvalidOperationException($"Không cập nhật được dòng có Id = {id}.");
        }
    }

    public async Task ClearTranslationsAsync(IEnumerable<long> ids, CancellationToken cancellationToken)
    {
        var distinctIds = ids.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.TargetTable) ||
            string.IsNullOrWhiteSpace(_settings.SuggestedTranslationColumn) ||
            string.IsNullOrWhiteSpace(_settings.RatingColumn))
        {
            throw new InvalidOperationException("TargetTable, SuggestedTranslationColumn và RatingColumn phải được cấu hình.");
        }

        using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var batch in distinctIds.Chunk(1000))
        {
            using var command = connection.CreateCommand();
            command.CommandTimeout = _commandTimeoutSeconds;
            var parameterNames = new List<string>();
            for (var i = 0; i < batch.Length; i++)
            {
                var parameterName = $"@Id{i}";
                parameterNames.Add(parameterName);
                command.Parameters.Add(parameterName, System.Data.SqlDbType.BigInt).Value = batch[i];
            }

            command.CommandText =
                $"UPDATE {SqlIdentifier.QuotePath(_settings.TargetTable)} " +
                $"SET {SqlIdentifier.QuotePath(_settings.SuggestedTranslationColumn)} = NULL, " +
                $"{SqlIdentifier.QuotePath(_settings.RatingColumn)} = NULL " +
                $"WHERE {SqlIdentifier.QuotePath(_settings.KeyColumn)} IN ({string.Join(", ", parameterNames)})";

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task UpdateSuggestedTranslationsAsync(
        IReadOnlyDictionary<long, string> translations,
        CancellationToken cancellationToken)
    {
        if (translations.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.TargetTable) ||
            string.IsNullOrWhiteSpace(_settings.SuggestedTranslationColumn))
        {
            throw new InvalidOperationException("TargetTable và SuggestedTranslationColumn phải được cấu hình.");
        }

        using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _commandTimeoutSeconds;
        command.CommandText =
            $"UPDATE {SqlIdentifier.QuotePath(_settings.TargetTable)} " +
            $"SET {SqlIdentifier.QuotePath(_settings.SuggestedTranslationColumn)} = @SuggestedTranslation, " +
            "[LastUpdated] = SYSUTCDATETIME() " +
            $"WHERE {SqlIdentifier.QuotePath(_settings.KeyColumn)} = @Id";
        command.Parameters.Add("@SuggestedTranslation", System.Data.SqlDbType.NVarChar, -1);
        command.Parameters.Add("@Id", System.Data.SqlDbType.BigInt);

        foreach (var (id, translation) in translations)
        {
            command.Parameters["@SuggestedTranslation"].Value = translation;
            command.Parameters["@Id"].Value = id;
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                throw new InvalidOperationException($"Không cập nhật được dòng có Id = {id}.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private string BuildSelectSql()
    {
        if (!string.IsNullOrWhiteSpace(_settings.SourceQuery))
        {
            return _settings.SourceQuery;
        }

        var selectColumns = new List<string>();
        AddSelectColumn(selectColumns, _settings.KeyColumn);
        AddSelectColumn(selectColumns, _settings.AllTextColumn);
        AddSelectColumn(selectColumns, _settings.MsgCtxtColumn);
        AddSelectColumn(selectColumns, _settings.MsgIdColumn);
        AddSelectColumn(selectColumns, _settings.MsgStrColumn);
        AddSelectColumn(selectColumns, _settings.SuggestedTranslationColumn);
        AddSelectColumn(selectColumns, _settings.RatingColumn);
        AddSelectColumn(selectColumns, _settings.SourceFilePathColumn);
        AddSelectColumn(selectColumns, _settings.ImportedAtUtcColumn);
        AddSelectColumn(selectColumns, _settings.TranslationLockedColumn);

        var whereClauses = new List<string>();
        if (_processingSettings.SkipIfSuggestedExists && !string.IsNullOrWhiteSpace(_settings.SuggestedTranslationColumn))
        {
            whereClauses.Add(
                $"NULLIF(LTRIM(RTRIM(COALESCE(CAST({SqlIdentifier.QuotePath(_settings.SuggestedTranslationColumn)} AS nvarchar(max)), ''))), '') IS NULL");
        }

        if (_processingSettings.RespectTranslationLocked && !string.IsNullOrWhiteSpace(_settings.TranslationLockedColumn))
        {
            whereClauses.Add($"ISNULL({SqlIdentifier.QuotePath(_settings.TranslationLockedColumn)}, 0) = 0");
        }

        var whereClause = whereClauses.Count > 0 ? " WHERE " + string.Join(" AND ", whereClauses) : string.Empty;
        var orderByClause = $" ORDER BY {SqlIdentifier.QuotePath(_settings.KeyColumn)}";

        return $"SELECT {string.Join(", ", selectColumns)} FROM {SqlIdentifier.QuotePath(_settings.SourceTable)}{whereClause}{orderByClause}";
    }

    private static void AddSelectColumn(ICollection<string> selectColumns, string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return;
        }

        var quoted = SqlIdentifier.QuotePath(columnName);
        selectColumns.Add($"{quoted} AS {quoted}");
    }

    private static string? GetString(SqlDataReader reader, string columnName)
    {
        if (!TryGetOrdinal(reader, columnName, out var ordinal) || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static decimal? GetDecimal(SqlDataReader reader, string columnName)
    {
        if (!TryGetOrdinal(reader, columnName, out var ordinal) || reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is decimal decimalValue)
        {
            return decimalValue;
        }

        if (value is IConvertible)
        {
            try
            {
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                // fall through
            }
        }

        if (decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static long? GetLong(SqlDataReader reader, string columnName)
    {
        if (!TryGetOrdinal(reader, columnName, out var ordinal) || reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is long longValue)
        {
            return longValue;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (value is IConvertible)
        {
            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                // fall through
            }
        }

        if (long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? GetDateTime(SqlDataReader reader, string columnName)
    {
        if (!TryGetOrdinal(reader, columnName, out var ordinal) || reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is DateTime dateTime)
        {
            return dateTime;
        }

        if (value is IConvertible)
        {
            try
            {
                return Convert.ToDateTime(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                // fall through
            }
        }

        if (DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? GetBool(SqlDataReader reader, string columnName)
    {
        if (!TryGetOrdinal(reader, columnName, out var ordinal) || reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is bool boolValue)
        {
            return boolValue;
        }

        if (value is byte or short or int or long or decimal or double or float)
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture) != 0m;
        }

        if (value is string stringValue)
        {
            if (bool.TryParse(stringValue, out var parsedBool))
            {
                return parsedBool;
            }

            if (decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDecimal))
            {
                return parsedDecimal != 0m;
            }
        }

        try
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetOrdinal(SqlDataReader reader, string columnName, out int ordinal)
    {
        try
        {
            ordinal = reader.GetOrdinal(columnName);
            return true;
        }
        catch (IndexOutOfRangeException)
        {
            ordinal = -1;
            return false;
        }
    }
}

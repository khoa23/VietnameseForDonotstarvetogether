using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using PoTranslationsWeb.Models;

namespace PoTranslationsWeb.Data;

public sealed class PoTranslationRepository
{
    private const string TableName = "[dbo].[PoTranslations]";

    private readonly string _connectionString;

    public PoTranslationRepository(string? connectionString)
    {
        _connectionString = connectionString?.Trim() ?? string.Empty;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public async Task<long> CountAsync(string? search, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 60;
        command.CommandText = $"""
SELECT COUNT_BIG(1)
FROM {TableName}
{BuildSearchWhereClause(search)}
""";

        AddSearchParameter(command, search);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is null or DBNull ? 0L : Convert.ToInt64(scalar);
    }

    public async Task<IReadOnlyList<PoTranslationRow>> GetPageAsync(
        string? search,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var rows = new List<PoTranslationRow>(pageSize);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 60;
command.CommandText = $"""
SELECT
    [Id],
    [AllText],
    [MsgCtxt],
    [MsgId],
    [MsgStr],
    [SuggestedTranslation],
    [Rating],
    [SourceFilePath],
    [ImportedAtUtc],
    [TranslationLocked]
FROM {TableName}
{BuildSearchWhereClause(search)}
ORDER BY LEN(COALESCE([AllText], N'')) ASC, [AllText] ASC, [Id] ASC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
"""; 

        AddSearchParameter(command, search);
        command.Parameters.Add(new SqlParameter("@offset", SqlDbType.Int) { Value = (pageNumber - 1) * pageSize });
        command.Parameters.Add(new SqlParameter("@pageSize", SqlDbType.Int) { Value = pageSize });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var idOrdinal = reader.GetOrdinal("Id");
        var allTextOrdinal = reader.GetOrdinal("AllText");
        var msgCtxtOrdinal = reader.GetOrdinal("MsgCtxt");
        var msgIdOrdinal = reader.GetOrdinal("MsgId");
        var msgStrOrdinal = reader.GetOrdinal("MsgStr");
        var suggestedTranslationOrdinal = reader.GetOrdinal("SuggestedTranslation");
        var ratingOrdinal = reader.GetOrdinal("Rating");
        var sourceFilePathOrdinal = reader.GetOrdinal("SourceFilePath");
        var importedAtUtcOrdinal = reader.GetOrdinal("ImportedAtUtc");
        var translationLockedOrdinal = reader.GetOrdinal("TranslationLocked");

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new PoTranslationRow
            {
                Id = reader.GetInt32(idOrdinal),
                AllText = reader.IsDBNull(allTextOrdinal) ? string.Empty : reader.GetString(allTextOrdinal),
                MsgCtxt = reader.IsDBNull(msgCtxtOrdinal) ? null : reader.GetString(msgCtxtOrdinal),
                MsgId = reader.IsDBNull(msgIdOrdinal) ? string.Empty : reader.GetString(msgIdOrdinal),
                MsgStr = reader.IsDBNull(msgStrOrdinal) ? string.Empty : reader.GetString(msgStrOrdinal),
                SuggestedTranslation = reader.IsDBNull(suggestedTranslationOrdinal) ? null : reader.GetString(suggestedTranslationOrdinal),
                Rating = reader.IsDBNull(ratingOrdinal) ? null : reader.GetDouble(ratingOrdinal),
                SourceFilePath = reader.IsDBNull(sourceFilePathOrdinal) ? string.Empty : reader.GetString(sourceFilePathOrdinal),
                ImportedAtUtc = reader.GetDateTime(importedAtUtcOrdinal),
                TranslationLocked = reader.GetBoolean(translationLockedOrdinal),
            });
        }

        return rows;
    }

    public async Task<bool> UpdateAsync(
        int id,
        string? suggestedTranslation,
        double? rating,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 60;
        command.CommandText = $"""
UPDATE {TableName}
SET
    [SuggestedTranslation] = @suggestedTranslation,
    [Rating] = @rating
WHERE [Id] = @id;
""";

        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
        command.Parameters.Add(new SqlParameter("@suggestedTranslation", SqlDbType.NVarChar, -1)
        {
            Value = string.IsNullOrWhiteSpace(suggestedTranslation) ? DBNull.Value : suggestedTranslation
        });
        command.Parameters.Add(new SqlParameter("@rating", SqlDbType.Float)
        {
            Value = rating.HasValue ? rating.Value : DBNull.Value
        });

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool?> ToggleLockAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 60;
        command.CommandText = $"""
UPDATE {TableName}
SET [TranslationLocked] = CASE WHEN [TranslationLocked] = 1 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END
OUTPUT INSERTED.[TranslationLocked]
WHERE [Id] = @id;
""";

        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        if (scalar is null or DBNull)
        {
            return null;
        }

        return Convert.ToBoolean(scalar);
    }

    private static string BuildSearchWhereClause(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return string.Empty;
        }

        return """
WHERE
    CONVERT(nvarchar(50), [Id]) LIKE @like ESCAPE '\'
    OR COALESCE([AllText], N'') LIKE @like ESCAPE '\'
    OR COALESCE([MsgCtxt], N'') LIKE @like ESCAPE '\'
    OR COALESCE([MsgId], N'') LIKE @like ESCAPE '\'
    OR COALESCE([MsgStr], N'') LIKE @like ESCAPE '\'
    OR COALESCE([SuggestedTranslation], N'') LIKE @like ESCAPE '\'
    OR CONVERT(nvarchar(50), [Rating]) LIKE @like ESCAPE '\'
    OR COALESCE([SourceFilePath], N'') LIKE @like ESCAPE '\'
    OR CONVERT(nvarchar(50), [ImportedAtUtc], 126) LIKE @like ESCAPE '\'
    OR CONVERT(nvarchar(10), [TranslationLocked]) LIKE @like ESCAPE '\'
""";
    }

    private static void AddSearchParameter(SqlCommand command, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return;
        }

        command.Parameters.Add(new SqlParameter("@like", SqlDbType.NVarChar, 4000)
        {
            Value = $"%{EscapeLikePattern(search.Trim())}%"
        });
    }

    private static string EscapeLikePattern(string value)
    {
        var builder = new StringBuilder(value.Length * 2);

        foreach (var character in value)
        {
            if (character is '\\' or '%' or '_' or '[')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private void EnsureConfigured()
    {
        if (IsConfigured)
        {
            return;
        }

        throw new InvalidOperationException(
            "Chưa cấu hình connection string PoTranslationsDb. Hãy điền ConnectionStrings:PoTranslationsDb trong appsettings.json hoặc set env var ConnectionStrings__PoTranslationsDb.");
    }
}

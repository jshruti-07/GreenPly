using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pim.Catalog.Application.AssetLibrary;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// The ingest write path.
///
/// Dapper/Npgsql rather than EF, following <c>ProductAssetStore</c>: there is no
/// Asset aggregate in Domain, assets are reference data written directly, and
/// inventing an aggregate for one insert would be ceremony rather than design.
///
/// The asset row and every rendition are written in ONE transaction. An asset
/// whose renditions half-committed would be a row pointing at derivatives that
/// do not exist, and the screen would serve 404s for images the library says
/// are there.
/// </summary>
public sealed class AssetIngestStore(CatalogDbContext dbContext) : IAssetIngestStore
{
    private readonly CatalogDbContext _dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public async Task<Guid?> FindByChecksumAsync(string checksum, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checksum);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        return await DapperGateway.QuerySingleOrDefaultOnAsync<Guid?>(
            connection,
            """
            SELECT public_id FROM catalog.asset
            WHERE checksum = @checksum AND status = 'active'
            ORDER BY id
            LIMIT 1
            """,
            new { checksum },
            cancellationToken);
    }

    public async Task<Guid> InsertAsync(AssetIngestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            return await InsertCoreAsync(connection, transaction, request, cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // Unique violation: another request stored these exact bytes first.
            await transaction.RollbackAsync(cancellationToken);

            var winner = await FindByStorageKeyAsync(
                connection, request.StorageProvider, request.StorageKey, cancellationToken);

            return winner ?? throw new InvalidOperationException(
                "An asset insert conflicted on a unique key, but no existing row could be found for it.",
                ex);
        }
    }

    private static async Task<Guid> InsertCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetIngestRequest request,
        CancellationToken cancellationToken)
    {
        const string insertAsset = """
            INSERT INTO catalog.asset
                (storage_provider, storage_key, file_name, mime_type, file_size,
                 asset_type, checksum, width, height, usage_rights, rights_expiry)
            VALUES (@provider, @key, @fileName, @mimeType, @fileSize,
                    @assetType, @checksum, @width, @height, @usageRights, @rightsExpiry)
            RETURNING id, public_id
            """;

        await using var command = new NpgsqlCommand(insertAsset, connection, transaction);
        command.Parameters.AddWithValue("provider", request.StorageProvider);
        command.Parameters.AddWithValue("key", request.StorageKey);
        command.Parameters.AddWithValue("fileName", request.FileName);
        command.Parameters.AddWithValue("mimeType", (object?)request.MimeType ?? DBNull.Value);
        command.Parameters.AddWithValue("fileSize", request.FileSizeBytes);
        command.Parameters.AddWithValue("assetType", request.AssetType);
        command.Parameters.AddWithValue("checksum", request.Checksum);
        command.Parameters.AddWithValue("width", (object?)request.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("height", (object?)request.Height ?? DBNull.Value);
        command.Parameters.AddWithValue("usageRights", (object?)request.UsageRights ?? DBNull.Value);
        command.Parameters.AddWithValue("rightsExpiry", (object?)request.RightsExpiry ?? DBNull.Value);

        long assetId;
        Guid publicId;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Inserting an asset returned no row.");
            }

            assetId = reader.GetInt64(0);
            publicId = reader.GetGuid(1);
        }

        foreach (var rendition in request.Renditions)
        {
            const string insertRendition = """
                INSERT INTO catalog.asset_rendition (asset_id, role, uri, width, height, format)
                VALUES (@assetId, @role, @uri, @width, @height, @format)
                ON CONFLICT (asset_id, role) DO UPDATE
                  SET uri = EXCLUDED.uri, width = EXCLUDED.width,
                      height = EXCLUDED.height, format = EXCLUDED.format
                """;

            await using var renditionCommand = new NpgsqlCommand(insertRendition, connection, transaction);
            renditionCommand.Parameters.AddWithValue("assetId", assetId);
            renditionCommand.Parameters.AddWithValue("role", rendition.Role);
            renditionCommand.Parameters.AddWithValue("uri", rendition.Uri);
            renditionCommand.Parameters.AddWithValue("width", (object?)rendition.Width ?? DBNull.Value);
            renditionCommand.Parameters.AddWithValue("height", (object?)rendition.Height ?? DBNull.Value);
            renditionCommand.Parameters.AddWithValue("format", (object?)rendition.Format ?? DBNull.Value);

            await renditionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.Title) || !string.IsNullOrWhiteSpace(request.AltText))
        {
            // Written against the locale the uploader is working in, matching
            // the rule R-18 established for asset titles: read and write move
            // together, and neither is pinned to the base locale.
            const string insertText = """
                INSERT INTO catalog.asset_tr (asset_id, locale_code, title, alt_text)
                VALUES (@assetId, @locale, @title, @altText)
                ON CONFLICT (asset_id, locale_code) DO UPDATE
                  SET title = COALESCE(EXCLUDED.title, catalog.asset_tr.title),
                      alt_text = COALESCE(EXCLUDED.alt_text, catalog.asset_tr.alt_text)
                """;

            await using var textCommand = new NpgsqlCommand(insertText, connection, transaction);
            textCommand.Parameters.AddWithValue("assetId", assetId);
            textCommand.Parameters.AddWithValue("locale", request.LocaleCode);
            textCommand.Parameters.AddWithValue("title", (object?)request.Title ?? DBNull.Value);
            textCommand.Parameters.AddWithValue("altText", (object?)request.AltText ?? DBNull.Value);

            await textCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return publicId;
    }

    /// <summary>
    /// Two uploads of the same file at the same moment.
    ///
    /// The checksum lookup in the handler is a read, so two requests can both
    /// miss it and both try to insert. They cannot both succeed: keys are
    /// content-addressed, so both produce the same storage_key and the second
    /// hits the existing unique constraint on (storage_provider, storage_key).
    /// That is the right outcome — one row, one copy of the bytes — but only if
    /// the loser returns the winner's asset instead of a 500.
    /// </summary>
    private static async Task<Guid?> FindByStorageKeyAsync(
        NpgsqlConnection connection, string provider, string key, CancellationToken cancellationToken)
    {
        return await DapperGateway.QuerySingleOrDefaultOnAsync<Guid?>(
            connection,
            """
            SELECT public_id FROM catalog.asset
            WHERE storage_provider = @provider AND storage_key = @key
            LIMIT 1
            """,
            new { provider, key },
            cancellationToken);
    }

    /// <summary>
    /// Where one rendition's bytes are, for serving.
    ///
    /// Falls back to the original when the requested role does not exist, and
    /// says which it returned via the URI — a caller asking for a thumb of a
    /// BIM file should get the file, not a 404, because there is no thumbnail
    /// of a Revit family and never will be.
    /// </summary>
    public async Task<AssetBinaryLocation?> FindBinaryAsync(
        Guid publicId, string renditionRole, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        // Column order matches BinaryRow exactly. R-17.
        const string sql = """
            SELECT a.storage_provider,
                   COALESCE(want.uri, fallback.uri, a.storage_key) AS uri,
                   -- 'jpg' is a file extension; 'image/jpeg' is the media type.
                   -- Serving image/jpg makes some clients download rather than
                   -- render, which looks like a broken thumbnail.
                   CASE WHEN want.uri IS NULL OR want.format IS NULL THEN a.mime_type
                        WHEN want.format = 'jpg' THEN 'image/jpeg'
                        ELSE 'image/' || want.format
                   END                                             AS mime_type,
                   a.file_name
            FROM catalog.asset a
            LEFT JOIN catalog.asset_rendition want
                   ON want.asset_id = a.id AND want.role = @role
            LEFT JOIN catalog.asset_rendition fallback
                   ON fallback.asset_id = a.id AND fallback.role = 'original'
            WHERE a.public_id = @publicId
            """;

        return await DapperGateway.QuerySingleOrDefaultOnAsync<BinaryRow>(
            connection, sql, new { publicId, role = renditionRole }, cancellationToken)
            is { } row
            ? new AssetBinaryLocation(row.StorageProvider, row.Uri, row.MimeType, row.FileName)
            : null;
    }

    private sealed record BinaryRow(string StorageProvider, string Uri, string? MimeType, string? FileName);
}

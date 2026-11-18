using Npgsql;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Products.Assets;
using Pim.Catalog.Domain.Assets;
using Pim.Catalog.Infrastructure.Persistence;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Digital assets and documents linked to a product.
///
/// Binaries never touch the database. An asset row records where the file lives
/// (provider + key); for the seeded client catalogue that is their own Magento
/// server, which is why the payload flags each asset as external — the UI has
/// to be able to say "this image is hosted by the client" when it fails to load
/// rather than implying the record is broken.
/// </summary>
public sealed class ProductAssetStore(CatalogDbContext dbContext) : IProductAssetStore
{
    public Task<ProductAssetsDto?> GetAsync(
        string productCode,
        DisplayLocale locale,
        CancellationToken cancellationToken) =>
        ProductWriteScope.ReadAsync(
            dbContext,
            connection => ReadAsync(connection, productCode, locale, cancellationToken),
            cancellationToken);

    public Task<Result<ProductAssetsDto>> AddAsync(
        AddProductAssetCommand command,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ProductWriteScope.ExecuteAsync(
            dbContext,
            command.Code,
            command.RowVersion,
            async scope =>
            {
                var key = command.StorageKey.Trim();
                var provider = AssetRules.InferProvider(key);
                var fileName = AssetRules.FileNameFrom(key);
                var assetType = command.AssetType ?? AssetRules.InferType(fileName);

                var pairing = AssetRules.Validate(assetType, command.Role);
                if (pairing.IsFailure)
                {
                    return pairing.Error;
                }

                // MAIN is singular by design; the partial unique index enforces
                // it too, but a clear message beats a constraint violation.
                if (AssetRole.IsSingular(command.Role))
                {
                    var existing = await ExistingMainAsync(
                        scope.Connection, scope.Transaction, scope.ProductId, cancellationToken);

                    if (existing is not null)
                    {
                        return AssetErrors.MainAlreadySet(existing);
                    }
                }

                // Re-registering a key reuses the asset. The same photo is
                // routinely shared across a series, and a second row for the
                // same binary would fragment rights and versioning.
                var assetId = await UpsertAssetAsync(
                    scope.Connection, scope.Transaction, provider, key, fileName,
                    assetType, cancellationToken);

                await WriteTranslationsAsync(
                    scope.Connection, scope.Transaction, assetId,
                    command.Title, command.AltText, locale, cancellationToken);

                const string linkSql = """
                    INSERT INTO catalog.asset_link
                        (product_id, asset_id, role, is_primary, sort_order)
                    SELECT @productId, @assetId, @role,
                           -- The first asset in a role becomes its primary, so a
                           -- role is never left without one.
                           NOT EXISTS (
                             SELECT 1 FROM catalog.asset_link
                             WHERE product_id = @productId AND role = @role AND is_primary),
                           COALESCE((
                             SELECT max(sort_order) + 1 FROM catalog.asset_link
                             WHERE product_id = @productId AND role = @role), 0)
                    ON CONFLICT (product_id, asset_id, role) DO NOTHING
                    """;

                await using var link = new NpgsqlCommand(linkSql, scope.Connection, scope.Transaction);
                link.Parameters.AddWithValue("productId", scope.ProductId);
                link.Parameters.AddWithValue("assetId", assetId);
                link.Parameters.AddWithValue("role", command.Role);

                var affected = await link.ExecuteNonQueryAsync(cancellationToken);
                return affected == 0 ? AssetErrors.AlreadyLinked(command.Role) : null;
            },
            connection => ReadAsync(connection, command.Code, locale, cancellationToken),
            cancellationToken);
    }

    public Task<Result<ProductAssetsDto>> UpdateAsync(
        UpdateProductAssetCommand command,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ProductWriteScope.ExecuteAsync(
            dbContext,
            command.Code,
            command.RowVersion,
            async scope =>
            {
                var current = await LoadLinkAsync(
                    scope.Connection, scope.Transaction, scope.ProductId, command.LinkId, cancellationToken);

                if (current is null)
                {
                    return AssetErrors.NotFound(command.LinkId);
                }

                var pairing = AssetRules.Validate(current.Value.AssetType, command.Role);
                if (pairing.IsFailure)
                {
                    return pairing.Error;
                }

                const string sql = """
                    UPDATE catalog.asset_link
                    SET role = @role, sort_order = @sortOrder
                    WHERE id = @id AND product_id = @productId
                    """;

                await using (var update = new NpgsqlCommand(sql, scope.Connection, scope.Transaction))
                {
                    update.Parameters.AddWithValue("id", command.LinkId);
                    update.Parameters.AddWithValue("productId", scope.ProductId);
                    update.Parameters.AddWithValue("role", command.Role);
                    update.Parameters.AddWithValue("sortOrder", command.SortOrder);
                    await update.ExecuteNonQueryAsync(cancellationToken);
                }

                await WriteTranslationsAsync(
                    scope.Connection, scope.Transaction, current.Value.AssetId,
                    command.Title, command.AltText, locale, cancellationToken);

                return null;
            },
            connection => ReadAsync(connection, command.Code, locale, cancellationToken),
            cancellationToken);
    }

    public Task<Result<ProductAssetsDto>> SetPrimaryAsync(
        SetPrimaryAssetCommand command,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ProductWriteScope.ExecuteAsync(
            dbContext,
            command.Code,
            command.RowVersion,
            async scope =>
            {
                var current = await LoadLinkAsync(
                    scope.Connection, scope.Transaction, scope.ProductId, command.LinkId, cancellationToken);

                if (current is null)
                {
                    return AssetErrors.NotFound(command.LinkId);
                }

                // Demote first: the partial unique index would reject two
                // primaries in the same role, and doing it in one transaction
                // means no window where the role has none.
                const string demoteSql = """
                    UPDATE catalog.asset_link SET is_primary = false
                    WHERE product_id = @productId AND role = @role AND is_primary AND id <> @id
                    """;

                await using (var demote = new NpgsqlCommand(demoteSql, scope.Connection, scope.Transaction))
                {
                    demote.Parameters.AddWithValue("productId", scope.ProductId);
                    demote.Parameters.AddWithValue("role", current.Value.Role);
                    demote.Parameters.AddWithValue("id", command.LinkId);
                    await demote.ExecuteNonQueryAsync(cancellationToken);
                }

                const string promoteSql =
                    "UPDATE catalog.asset_link SET is_primary = true WHERE id = @id";

                await using var promote = new NpgsqlCommand(promoteSql, scope.Connection, scope.Transaction);
                promote.Parameters.AddWithValue("id", command.LinkId);
                await promote.ExecuteNonQueryAsync(cancellationToken);

                return null;
            },
            connection => ReadAsync(connection, command.Code, locale, cancellationToken),
            cancellationToken);
    }

    public Task<Result<ProductAssetsDto>> RemoveAsync(
        RemoveProductAssetCommand command,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ProductWriteScope.ExecuteAsync(
            dbContext,
            command.Code,
            command.RowVersion,
            async scope =>
            {
                var current = await LoadLinkAsync(
                    scope.Connection, scope.Transaction, scope.ProductId, command.LinkId, cancellationToken);

                if (current is null)
                {
                    return AssetErrors.NotFound(command.LinkId);
                }

                // Only the LINK goes. The asset row survives because the same
                // file is shared across a series; deleting it would strip the
                // image from other products without warning.
                const string sql =
                    "DELETE FROM catalog.asset_link WHERE id = @id AND product_id = @productId";

                await using (var delete = new NpgsqlCommand(sql, scope.Connection, scope.Transaction))
                {
                    delete.Parameters.AddWithValue("id", command.LinkId);
                    delete.Parameters.AddWithValue("productId", scope.ProductId);
                    await delete.ExecuteNonQueryAsync(cancellationToken);
                }

                // Removing the primary would leave the role with none, so the
                // next asset in order is promoted.
                if (current.Value.IsPrimary)
                {
                    const string promoteSql = """
                        UPDATE catalog.asset_link SET is_primary = true
                        WHERE id = (
                          SELECT id FROM catalog.asset_link
                          WHERE product_id = @productId AND role = @role
                          ORDER BY sort_order, id LIMIT 1)
                        """;

                    await using var promote = new NpgsqlCommand(promoteSql, scope.Connection, scope.Transaction);
                    promote.Parameters.AddWithValue("productId", scope.ProductId);
                    promote.Parameters.AddWithValue("role", current.Value.Role);
                    await promote.ExecuteNonQueryAsync(cancellationToken);
                }

                return null;
            },
            connection => ReadAsync(connection, command.Code, locale, cancellationToken),
            cancellationToken);
    }

    private static async Task<string?> ExistingMainAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(a.file_name, a.storage_key)
            FROM catalog.asset_link l
            JOIN catalog.asset a ON a.id = l.asset_id
            WHERE l.product_id = @productId AND l.role = 'MAIN'
            LIMIT 1
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("productId", productId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<long> UpsertAssetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string provider,
        string key,
        string fileName,
        string assetType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO catalog.asset
                (storage_provider, storage_key, file_name, mime_type, asset_type)
            VALUES (@provider, @key, @fileName, @mimeType, @assetType)
            ON CONFLICT (storage_provider, storage_key) DO UPDATE
              SET file_name = COALESCE(catalog.asset.file_name, EXCLUDED.file_name)
            RETURNING id
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("fileName", fileName);
        command.Parameters.AddWithValue(
            "mimeType", (object?)AssetRules.InferMimeType(fileName) ?? DBNull.Value);
        command.Parameters.AddWithValue("assetType", assetType);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task WriteTranslationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long assetId,
        string? title,
        string? altText,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(altText))
        {
            return;
        }

        // Written against the locale the steward is looking at, which is the
        // same one ReadAsync renders. Previously this always wrote the base
        // locale, so an English or French user edited a title and silently
        // updated the German row (R-18).
        //
        // The locale is validated before it reaches here — an unconfigured one
        // is rejected by DisplayLocaleResolver — so this cannot create rows for
        // a language the catalogue does not have.
        const string sql = """
            INSERT INTO catalog.asset_tr (asset_id, locale_code, title, alt_text)
            VALUES (@assetId, @locale, @title, @altText)
            ON CONFLICT (asset_id, locale_code) DO UPDATE
              SET title = COALESCE(EXCLUDED.title, catalog.asset_tr.title),
                  alt_text = COALESCE(EXCLUDED.alt_text, catalog.asset_tr.alt_text)
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("assetId", assetId);
        command.Parameters.AddWithValue("locale", locale.Code);
        command.Parameters.AddWithValue(
            "title", string.IsNullOrWhiteSpace(title) ? DBNull.Value : title.Trim());
        command.Parameters.AddWithValue(
            "altText", string.IsNullOrWhiteSpace(altText) ? DBNull.Value : altText.Trim());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(long AssetId, string Role, string AssetType, bool IsPrimary)?> LoadLinkAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        long linkId,
        CancellationToken cancellationToken)
    {
        // product_id is in the predicate so a link id belonging to another
        // product 404s rather than being edited from the wrong screen.
        const string sql = """
            SELECT l.asset_id, l.role, a.asset_type, l.is_primary
            FROM catalog.asset_link l
            JOIN catalog.asset a ON a.id = l.asset_id
            WHERE l.id = @id AND l.product_id = @productId
            """;

        var row = await DapperGateway.QuerySingleOrDefaultOnAsync<LinkRow>(
            connection, sql, new { id = linkId, productId }, cancellationToken, transaction);

        return row is null ? null : (row.AssetId, row.Role, row.AssetType, row.IsPrimary);
    }

    private sealed record LinkRow(long AssetId, string Role, string AssetType, bool IsPrimary);

    private sealed record ProductHeaderRow(long Id, int RowVersion, string BaseLocale);

    /// <summary>
    /// One asset link as projected for the tab.
    ///
    /// Both <c>asset_link.id</c> and <c>asset.id</c> appear in this projection.
    /// Under positional reads they were GetInt64(0) and GetInt64(1) — two
    /// identically-typed columns distinguished only by the order they happened
    /// to be written in. They are aliased now, so swapping them is a compile
    /// error rather than a silent identity mix-up.
    /// </summary>
    private sealed record AssetRow(
        long LinkId,
        long AssetId,
        string Role,
        string AssetType,
        string StorageProvider,
        string? FileName,
        string? MimeType,
        long? FileSize,
        string Url,
        string? Title,
        string? AltText,
        bool IsPrimary,
        int SortOrder);

    private static async Task<ProductAssetsDto?> ReadAsync(
        NpgsqlConnection connection,
        string productCode,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        const string productSql = """
            SELECT id, row_version, base_locale FROM catalog.product WHERE product_code = @code
            """;

        var header = await DapperGateway.QuerySingleOrDefaultOnAsync<ProductHeaderRow>(
            connection, productSql, new { code = productCode }, cancellationToken);

        if (header is null)
        {
            return null;
        }

        var (productId, rowVersion, baseLocale) = (header.Id, header.RowVersion, header.BaseLocale);

        // The web rendition wins over the raw key when one exists, so the
        // client requests the right size without knowing renditions exist.
        const string sql = """
            SELECT l.id AS link_id, a.id AS asset_id, l.role, a.asset_type,
                   a.storage_provider, a.file_name, a.mime_type, a.file_size,
                   COALESCE(r.uri, a.storage_key) AS url,
                   COALESCE(tr_d.title, tr_b.title)       AS title,
                   COALESCE(tr_d.alt_text, tr_b.alt_text)  AS alt_text,
                   l.is_primary, l.sort_order
            FROM catalog.asset_link l
            JOIN catalog.asset a ON a.id = l.asset_id
            LEFT JOIN catalog.asset_rendition r ON r.asset_id = a.id AND r.role = 'web'
            LEFT JOIN catalog.asset_tr tr_d ON tr_d.asset_id = a.id AND tr_d.locale_code = @displayLocale
            LEFT JOIN catalog.asset_tr tr_b ON tr_b.asset_id = a.id AND tr_b.locale_code = @baseLocale
            WHERE l.product_id = @productId AND a.status = 'active'
            ORDER BY l.is_primary DESC, l.sort_order, l.id
            """;

        // R-18, closed in Sprint 19. Titles now read in the display locale and
        // fall back to the base — and, crucially, the WRITE below moves with
        // them. Sprint 18 left this on base precisely because migrating the read
        // alone would have let a steward edit a title in English, write it to
        // German, and watch it vanish on the next load. Read and write move
        // together or not at all; this is the "together".
        var rows = await DapperGateway.QueryOnAsync<AssetRow>(
            connection,
            sql,
            new { productId, displayLocale = locale.Code, baseLocale },
            cancellationToken);

        var media = new List<ProductAssetDto>();
        var documents = new List<ProductAssetDto>();

        foreach (var row in rows)
        {
            var dto = new ProductAssetDto(
                row.LinkId,
                row.AssetId,
                row.Role,
                row.AssetType,
                row.StorageProvider,
                row.FileName,
                row.MimeType,
                row.FileSize,
                row.Url,
                row.Title,
                row.AltText,
                row.IsPrimary,
                row.SortOrder,
                // Externally hosted: the binary is on someone else's server, so
                // the UI must be prepared for it not to load.
                !string.Equals(row.StorageProvider, "local", StringComparison.Ordinal));

            if (AssetType.IsVisual(row.AssetType))
            {
                media.Add(dto);
            }
            else
            {
                documents.Add(dto);
            }
        }

        return new ProductAssetsDto(productCode, rowVersion, media, documents);
    }
}

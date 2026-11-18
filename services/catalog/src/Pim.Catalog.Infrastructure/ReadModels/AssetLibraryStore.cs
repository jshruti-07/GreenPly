using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pim.Catalog.Application.AssetLibrary;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Infrastructure.Persistence;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// The Digital Assets library.
///
/// One thing here is worth stating plainly, because it changed with this sprint.
/// `asset_rendition` holds derivatives — thumb, web, print — and this build
/// GENERATES NONE. It cannot: the service has never stored a byte, and every
/// `storage_key` points into the client's Magento MAM. Until an ingest path
/// exists (Sprint 26), the only rendition that truthfully exists is the
/// original.
///
/// The seed used to insert a row claiming a 'web' rendition per image whose URI
/// was the original file and whose format was hard-coded 'jpg'. Two read models
/// join that row, so the API had been reporting a web rendition for 1,969
/// images and serving full-size originals. Nothing distinguished them. That row
/// is now honestly labelled 'original', and every response says WHICH rendition
/// it served — because a client that cannot tell them apart will put a 4 MB
/// original behind a storefront thumbnail.
///
/// R-17 applies: each SELECT sits above the positional record it fills.
/// </summary>
public sealed class AssetLibraryStore(CatalogDbContext dbContext) : IAssetLibraryStore
{
    private readonly CatalogDbContext _dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <summary>Rights expiring inside this window count as "expiring soon".</summary>
    private const int RightsWarningDays = 90;

    private static readonly IReadOnlyList<AssetOmittedPanelDto> OmittedPanels =
    [
        // Upload and rendition generation were listed here until Sprint 26 and
        // are now gone, because they exist. A "not shown yet" panel that keeps
        // claiming a shipped capability is missing is the same defect as one
        // that hides a missing capability — it is just pointing the other way.
        new("Channel delivery",
            "There is no channel service and no channel tables in this schema, so there "
            + "is nowhere for an asset to be delivered TO. The outbox and its broker can "
            + "carry the events the day a consumer exists."),
        new("Approval state per asset",
            "v4.2 shows assets as Approved or In Review. `asset` carries active/archived "
            + "and no review state, so a review column here would be invented."),
    ];

    public async Task<AssetLibrarySummaryDto> GetSummaryAsync(
        DisplayLocale locale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locale);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        // Column order matches SummaryRow exactly. R-17.
        const string sql = """
            WITH active AS (SELECT * FROM catalog.asset WHERE status = 'active')
            SELECT (SELECT count(*)::int FROM active)                              AS total_assets,
                   (SELECT COALESCE(sum(file_size), 0)::bigint FROM active)        AS total_bytes,
                   (SELECT count(*)::int FROM active a
                     WHERE NOT EXISTS (SELECT 1 FROM catalog.asset_link l
                                        WHERE l.asset_id = a.id))                  AS orphan_count,
                   -- A product with no MAIN image. Asked of the product, not of
                   -- the asset, because the gap is the product's.
                   (SELECT count(*)::int FROM catalog.product p
                     WHERE NOT EXISTS (
                         SELECT 1 FROM catalog.asset_link l
                         JOIN catalog.asset a2 ON a2.id = l.asset_id AND a2.status = 'active'
                         WHERE l.product_id = p.id AND l.role = 'MAIN'))           AS products_without_main_image,
                   (SELECT count(*)::int FROM active
                     WHERE rights_expiry IS NOT NULL
                       AND rights_expiry >= CURRENT_DATE
                       AND rights_expiry < CURRENT_DATE + @warningDays)            AS rights_expiring_soon,
                   (SELECT count(*)::int FROM active
                     WHERE rights_expiry IS NOT NULL
                       AND rights_expiry < CURRENT_DATE)                           AS rights_expired
            """;

        var summary = await DapperGateway.QuerySingleOrDefaultOnAsync<SummaryRow>(
                          connection, sql, new { warningDays = RightsWarningDays }, cancellationToken)
                      ?? throw new InvalidOperationException(
                          "Asset summary query returned no row; it must always return exactly one.");

        // Column order matches TypeRow exactly. R-17.
        const string byTypeSql = """
            SELECT asset_type,
                   count(*)::int                     AS count,
                   COALESCE(sum(file_size), 0)::bigint AS total_bytes
            FROM catalog.asset
            WHERE status = 'active'
            GROUP BY asset_type
            ORDER BY count(*) DESC, asset_type
            """;

        var byType = await DapperGateway.QueryOnAsync<TypeRow>(
            connection, byTypeSql, parameters: null, cancellationToken);

        return new AssetLibrarySummaryDto(
            summary.TotalAssets,
            summary.TotalBytes,
            [.. byType.Select(t => new AssetTypeSummaryDto(t.AssetType, t.Count, t.TotalBytes))],
            summary.OrphanCount,
            summary.ProductsWithoutMainImage,
            summary.RightsExpiringSoon,
            summary.RightsExpired,
            OmittedPanels);
    }

    private sealed record SummaryRow(
        int TotalAssets,
        long TotalBytes,
        int OrphanCount,
        int ProductsWithoutMainImage,
        int RightsExpiringSoon,
        int RightsExpired);

    private sealed record TypeRow(string AssetType, int Count, long TotalBytes);

    public async Task<IReadOnlyList<string>> GetKnownTypesAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        return await DapperGateway.QueryOnAsync<string>(
            connection,
            "SELECT DISTINCT asset_type FROM catalog.asset WHERE status = 'active' ORDER BY asset_type",
            parameters: null,
            cancellationToken);
    }

    public async Task<AssetPageDto> GetAssetsAsync(
        string? type, string usage, string? search, int page, int pageSize,
        DisplayLocale locale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locale);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        var conditions = new List<string> { "a.status = 'active'" };
        if (type is not null) conditions.Add("a.asset_type = @type");
        if (usage == "orphan") conditions.Add("NOT EXISTS (SELECT 1 FROM catalog.asset_link l WHERE l.asset_id = a.id)");
        if (usage == "linked") conditions.Add("EXISTS (SELECT 1 FROM catalog.asset_link l WHERE l.asset_id = a.id)");
        if (search is not null)
        {
            // File name, or the title in ANY language. Matching is
            // locale-agnostic, rendering is not — Sprint 17's rule. Searching
            // only the displayed title would make an asset findable or not
            // depending on a view setting.
            conditions.Add("""
                (a.file_name ILIKE @search
                 OR EXISTS (SELECT 1 FROM catalog.asset_tr t
                             WHERE t.asset_id = a.id AND t.title ILIKE @search))
                """);
        }

        var where = "WHERE " + string.Join("\n              AND ", conditions);

        var parameters = new
        {
            type,
            search = search is null ? null : $"%{search}%",
            displayLocale = locale.Code,
            limit = pageSize,
            offset = (page - 1) * pageSize,
        };

        var totalCount = await DapperGateway.QuerySingleOrDefaultOnAsync<int?>(
            connection,
            $"SELECT count(*)::int FROM catalog.asset a {where}",
            parameters, cancellationToken) ?? 0;

        // Column order matches ListRow exactly. R-17.
        //
        // The rendition preference is explicit and ordered — web, then original,
        // then the raw key — and the chosen role travels with the URL. That is
        // the whole fix: the caller is told what it is looking at.
        var sql = $"""
            SELECT a.public_id,
                   a.file_name,
                   COALESCE(tr.title, a.file_name)      AS title,
                   tr.locale_code                       AS title_locale,
                   a.asset_type,
                   a.mime_type,
                   a.file_size                          AS file_size_bytes,
                   a.width,
                   a.height,
                   a.storage_provider,
                   a.status,
                   COALESCE(best.role, 'original')      AS served_rendition,
                   COALESCE(best.uri, a.storage_key)    AS url,
                   (SELECT count(*)::int FROM catalog.asset_link l WHERE l.asset_id = a.id) AS link_count,
                   a.usage_rights,
                   a.rights_expiry
            FROM catalog.asset a
            LEFT JOIN LATERAL (
                SELECT r.role, r.uri
                FROM catalog.asset_rendition r
                WHERE r.asset_id = a.id AND r.role IN ('web', 'original')
                ORDER BY (r.role = 'web') DESC
                LIMIT 1
            ) best ON true
            LEFT JOIN LATERAL (
                SELECT t.title, t.locale_code
                FROM catalog.asset_tr t
                WHERE t.asset_id = a.id
                  AND t.locale_code IN (@displayLocale, (SELECT code FROM catalog.locale WHERE is_base LIMIT 1))
                ORDER BY (t.locale_code = @displayLocale) DESC
                LIMIT 1
            ) tr ON true
            {where}
            ORDER BY a.file_name NULLS LAST, a.id
            LIMIT @limit OFFSET @offset
            """;

        var rows = await DapperGateway.QueryOnAsync<ListRow>(
            connection, sql, parameters, cancellationToken);

        var items = rows
            .Select(r => new AssetListItemDto(
                r.PublicId, r.FileName, r.Title, r.TitleLocale, r.AssetType, r.MimeType,
                r.FileSizeBytes, r.Width, r.Height, r.StorageProvider, r.Status,
                r.ServedRendition, r.Url, r.LinkCount, r.UsageRights, r.RightsExpiry))
            .ToList();

        return new AssetPageDto(page, pageSize, totalCount, items);
    }

    private sealed record ListRow(
        Guid PublicId,
        string? FileName,
        string? Title,
        string? TitleLocale,
        string AssetType,
        string? MimeType,
        long? FileSizeBytes,
        int? Width,
        int? Height,
        string StorageProvider,
        string Status,
        string ServedRendition,
        string Url,
        int LinkCount,
        string? UsageRights,
        DateOnly? RightsExpiry);

    public async Task<AssetDetailDto?> GetDetailAsync(
        Guid publicId, DisplayLocale locale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locale);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        var parameters = new { publicId, displayLocale = locale.Code };

        // Column order matches DetailRow exactly. R-17.
        const string headSql = """
            SELECT a.public_id,
                   a.file_name,
                   COALESCE(tr.title, a.file_name) AS title,
                   tr.locale_code                  AS title_locale,
                   a.asset_type,
                   a.mime_type,
                   a.file_size                     AS file_size_bytes,
                   a.width,
                   a.height,
                   a.storage_provider,
                   a.storage_key,
                   a.checksum,
                   a.status,
                   a.usage_rights,
                   a.rights_expiry,
                   a.locale                        AS locale_code,
                   a.created_at                    AS created_at_utc
            FROM catalog.asset a
            LEFT JOIN LATERAL (
                SELECT t.title, t.locale_code
                FROM catalog.asset_tr t
                WHERE t.asset_id = a.id
                  AND t.locale_code IN (@displayLocale, (SELECT code FROM catalog.locale WHERE is_base LIMIT 1))
                ORDER BY (t.locale_code = @displayLocale) DESC
                LIMIT 1
            ) tr ON true
            WHERE a.public_id = @publicId
            """;

        var head = await DapperGateway.QuerySingleOrDefaultOnAsync<DetailRow>(
            connection, headSql, parameters, cancellationToken);

        if (head is null)
        {
            return null;
        }

        // Column order matches RenditionRow exactly. R-17.
        const string renditionSql = """
            SELECT r.role, r.uri, r.width, r.height, r.format
            FROM catalog.asset_rendition r
            JOIN catalog.asset a ON a.id = r.asset_id
            WHERE a.public_id = @publicId
            ORDER BY r.role
            """;

        var renditions = await DapperGateway.QueryOnAsync<RenditionRow>(
            connection, renditionSql, parameters, cancellationToken);

        // One row per configured locale, translated or not. The absent ones are
        // the point — alt text missing in a language is an accessibility gap on
        // that language's storefront, and it is invisible unless stated.
        const string textSql = """
            SELECT l.code AS locale_code, l.name AS locale_name, l.is_base,
                   t.title, t.alt_text, t.caption
            FROM catalog.locale l
            LEFT JOIN catalog.asset_tr t
                   ON t.locale_code = l.code
                  AND t.asset_id = (SELECT id FROM catalog.asset WHERE public_id = @publicId)
            WHERE l.is_active
            ORDER BY l.is_base DESC, l.code
            """;

        var texts = await DapperGateway.QueryOnAsync<TextRow>(
            connection, textSql, parameters, cancellationToken);

        // Column order matches UsageRow exactly. R-17.
        const string usageSql = """
            SELECT p.product_code,
                   COALESCE(ptr.name, p.product_code) AS product_name,
                   ptr.locale_code                    AS product_name_locale,
                   l.role,
                   l.is_primary
            FROM catalog.asset_link l
            JOIN catalog.asset a  ON a.id = l.asset_id
            JOIN catalog.product p ON p.id = l.product_id
            LEFT JOIN LATERAL (
                SELECT t.name, t.locale_code
                FROM catalog.product_tr t
                WHERE t.product_id = p.id
                  AND t.locale_code IN (@displayLocale, p.base_locale)
                ORDER BY (t.locale_code = @displayLocale) DESC
                LIMIT 1
            ) ptr ON true
            WHERE a.public_id = @publicId
            ORDER BY l.is_primary DESC, p.product_code
            """;

        var usage = await DapperGateway.QueryOnAsync<UsageRow>(
            connection, usageSql, parameters, cancellationToken);

        return new AssetDetailDto(
            head.PublicId, head.FileName, head.Title, head.TitleLocale, head.AssetType,
            head.MimeType, head.FileSizeBytes, head.Width, head.Height,
            head.StorageProvider, head.StorageKey, head.Checksum, head.Status,
            head.UsageRights, head.RightsExpiry, head.LocaleCode, head.CreatedAtUtc,
            [.. renditions.Select(r => new AssetRenditionDto(r.Role, r.Uri, r.Width, r.Height, r.Format))],
            [.. texts.Select(t => new AssetTextDto(t.LocaleCode, t.LocaleName, t.IsBase, t.Title, t.AltText, t.Caption))],
            [.. usage.Select(u => new AssetUsageDto(u.ProductCode, u.ProductName, u.ProductNameLocale, u.Role, u.IsPrimary))]);
    }

    private sealed record DetailRow(
        Guid PublicId,
        string? FileName,
        string? Title,
        string? TitleLocale,
        string AssetType,
        string? MimeType,
        long? FileSizeBytes,
        int? Width,
        int? Height,
        string StorageProvider,
        string StorageKey,
        string? Checksum,
        string Status,
        string? UsageRights,
        DateOnly? RightsExpiry,
        string? LocaleCode,
        DateTimeOffset CreatedAtUtc);

    private sealed record RenditionRow(string Role, string Uri, int? Width, int? Height, string? Format);

    private sealed record TextRow(
        string LocaleCode, string LocaleName, bool IsBase,
        string? Title, string? AltText, string? Caption);

    private sealed record UsageRow(
        string ProductCode, string ProductName, string? ProductNameLocale,
        string Role, bool IsPrimary);
}

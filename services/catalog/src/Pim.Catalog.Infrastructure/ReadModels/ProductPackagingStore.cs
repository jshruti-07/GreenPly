using Npgsql;
using Pim.Catalog.Application.Products.Logistics;
using Pim.Catalog.Domain.Packaging;
using Pim.Catalog.Infrastructure.Persistence;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Packaging hierarchy per SKU.
///
/// Uniqueness of GTIN and SSCC is enforced by partial unique indexes in the
/// schema; this store checks first only so the client gets a 409 with a usable
/// message instead of a raw constraint violation. The index remains the
/// authority — the check here is a race away from being stale.
/// </summary>
public sealed class ProductPackagingStore(CatalogDbContext dbContext) : IProductPackagingStore
{
    public Task<ProductPackagingDto?> GetAsync(string productCode, CancellationToken cancellationToken) =>
        ProductWriteScope.ReadAsync(
            dbContext,
            connection => ReadAsync(connection, productCode, cancellationToken),
            cancellationToken);

    public Task<Result<ProductPackagingDto>> AddAsync(
        AddPackagingUnitCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ProductWriteScope.ExecuteAsync(
            dbContext,
            command.Code,
            command.RowVersion,
            async scope =>
            {
                var skuId = await ResolveSkuAsync(
                    scope.Connection, scope.Transaction, scope.ProductId, command.SkuCode, cancellationToken);

                if (skuId is null)
                {
                    return PackagingErrors.SkuNotFound(command.SkuCode);
                }

                var existing = await LoadLevelsAsync(
                    scope.Connection, scope.Transaction, skuId.Value, cancellationToken);

                if (existing.ContainsKey(command.Level))
                {
                    return PackagingErrors.DuplicateLevel(command.Level);
                }

                long? parentId = null;
                if (!string.IsNullOrWhiteSpace(command.ParentLevel))
                {
                    if (!PackagingLevel.IsKnown(command.ParentLevel))
                    {
                        return PackagingErrors.UnknownLevel(command.ParentLevel);
                    }

                    // The parent is the CONTAINING level, so it must be larger.
                    if (!PackagingLevel.CanContain(command.ParentLevel, command.Level))
                    {
                        return PackagingErrors.InvalidContainment(command.Level, command.ParentLevel);
                    }

                    if (!existing.TryGetValue(command.ParentLevel, out var resolved))
                    {
                        return PackagingErrors.UnknownLevel(command.ParentLevel);
                    }

                    parentId = resolved;
                }

                var validation = ValidateFields(
                    command.Gtin, command.Sscc, command.GrossWeight, command.NetWeight);

                if (validation is not null)
                {
                    return validation;
                }

                var conflict = await CheckIdentifiersAsync(
                    scope.Connection, scope.Transaction, command.Gtin, command.Sscc, null, cancellationToken);

                if (conflict is not null)
                {
                    return conflict;
                }

                // Only one base unit per SKU: clear any previous holder first,
                // otherwise the partial unique index rejects the insert.
                if (command.IsBaseUnit)
                {
                    await ClearBaseUnitAsync(scope.Connection, scope.Transaction, skuId.Value, null, cancellationToken);
                }

                const string sql = """
                    INSERT INTO catalog.packaging_unit
                        (sku_id, parent_id, level, contains_qty, gtin, sscc,
                         gross_weight, net_weight, length_mm, width_mm, height_mm,
                         is_base_unit, dangerous_goods, un_class)
                    VALUES (@skuId, @parentId, @level, @qty, @gtin, @sscc,
                            @gross, @net, @length, @width, @height,
                            @isBase, @dangerous, @unClass)
                    """;

                await using var insert = new NpgsqlCommand(sql, scope.Connection, scope.Transaction);
                insert.Parameters.AddWithValue("skuId", skuId.Value);
                insert.Parameters.AddWithValue("parentId", (object?)parentId ?? DBNull.Value);
                insert.Parameters.AddWithValue("level", command.Level);
                insert.Parameters.AddWithValue("qty", command.ContainsQty);
                AddNullableText(insert, "gtin", command.Gtin);
                AddNullableText(insert, "sscc", command.Sscc);
                AddNullableNumber(insert, "gross", command.GrossWeight);
                AddNullableNumber(insert, "net", command.NetWeight);
                AddNullableNumber(insert, "length", command.LengthMm);
                AddNullableNumber(insert, "width", command.WidthMm);
                AddNullableNumber(insert, "height", command.HeightMm);
                insert.Parameters.AddWithValue("isBase", command.IsBaseUnit);
                insert.Parameters.AddWithValue("dangerous", command.DangerousGoods);
                AddNullableText(insert, "unClass", command.UnClass);

                await insert.ExecuteNonQueryAsync(cancellationToken);
                return null;
            },
            connection => ReadAsync(connection, command.Code, cancellationToken),
            cancellationToken);
    }

    public Task<Result<ProductPackagingDto>> UpdateAsync(
        UpdatePackagingUnitCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ProductWriteScope.ExecuteAsync(
            dbContext,
            command.Code,
            command.RowVersion,
            async scope =>
            {
                var skuId = await ResolveOwningSkuAsync(
                    scope.Connection, scope.Transaction, scope.ProductId, command.UnitId, cancellationToken);

                if (skuId is null)
                {
                    return PackagingErrors.NotFound(command.UnitId);
                }

                var validation = ValidateFields(
                    command.Gtin, command.Sscc, command.GrossWeight, command.NetWeight);

                if (validation is not null)
                {
                    return validation;
                }

                var conflict = await CheckIdentifiersAsync(
                    scope.Connection, scope.Transaction, command.Gtin, command.Sscc,
                    command.UnitId, cancellationToken);

                if (conflict is not null)
                {
                    return conflict;
                }

                if (command.IsBaseUnit)
                {
                    await ClearBaseUnitAsync(
                        scope.Connection, scope.Transaction, skuId.Value, command.UnitId, cancellationToken);
                }

                const string sql = """
                    UPDATE catalog.packaging_unit
                    SET contains_qty = @qty, gtin = @gtin, sscc = @sscc,
                        gross_weight = @gross, net_weight = @net,
                        length_mm = @length, width_mm = @width, height_mm = @height,
                        is_base_unit = @isBase, dangerous_goods = @dangerous, un_class = @unClass
                    WHERE id = @id
                    """;

                await using var update = new NpgsqlCommand(sql, scope.Connection, scope.Transaction);
                update.Parameters.AddWithValue("id", command.UnitId);
                update.Parameters.AddWithValue("qty", command.ContainsQty);
                AddNullableText(update, "gtin", command.Gtin);
                AddNullableText(update, "sscc", command.Sscc);
                AddNullableNumber(update, "gross", command.GrossWeight);
                AddNullableNumber(update, "net", command.NetWeight);
                AddNullableNumber(update, "length", command.LengthMm);
                AddNullableNumber(update, "width", command.WidthMm);
                AddNullableNumber(update, "height", command.HeightMm);
                update.Parameters.AddWithValue("isBase", command.IsBaseUnit);
                update.Parameters.AddWithValue("dangerous", command.DangerousGoods);
                AddNullableText(update, "unClass", command.UnClass);

                var affected = await update.ExecuteNonQueryAsync(cancellationToken);
                return affected == 0 ? PackagingErrors.NotFound(command.UnitId) : null;
            },
            connection => ReadAsync(connection, command.Code, cancellationToken),
            cancellationToken);
    }

    public Task<Result<ProductPackagingDto>> RemoveAsync(
        RemovePackagingUnitCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ProductWriteScope.ExecuteAsync(
            dbContext,
            command.Code,
            command.RowVersion,
            async scope =>
            {
                // Scoped to this product's SKUs so an id from another product
                // 404s rather than deleting someone else's packaging.
                const string sql = """
                    DELETE FROM catalog.packaging_unit pu
                    USING catalog.sku s
                    WHERE pu.sku_id = s.id
                      AND pu.id = @id
                      AND s.product_id = @productId
                    """;

                await using var delete = new NpgsqlCommand(sql, scope.Connection, scope.Transaction);
                delete.Parameters.AddWithValue("id", command.UnitId);
                delete.Parameters.AddWithValue("productId", scope.ProductId);

                var affected = await delete.ExecuteNonQueryAsync(cancellationToken);
                return affected == 0 ? PackagingErrors.NotFound(command.UnitId) : null;
            },
            connection => ReadAsync(connection, command.Code, cancellationToken),
            cancellationToken);
    }

    private static Error? ValidateFields(string? gtin, string? sscc, decimal? gross, decimal? net)
    {
        if (!string.IsNullOrWhiteSpace(gtin) && !Gs1.IsWellFormedGtin(gtin.Trim()))
        {
            return PackagingErrors.InvalidGtin;
        }

        if (!string.IsNullOrWhiteSpace(sscc) && !Gs1.IsWellFormedSscc(sscc.Trim()))
        {
            return PackagingErrors.InvalidSscc;
        }

        return gross is not null && net is not null && gross < net
            ? PackagingErrors.GrossBelowNet
            : null;
    }

    private static async Task<Error?> CheckIdentifiersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string? gtin,
        string? sscc,
        long? excludingId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(gtin) &&
            await IdentifierExistsAsync(connection, transaction, "gtin", gtin.Trim(), excludingId, cancellationToken))
        {
            return PackagingErrors.GtinInUse(gtin.Trim());
        }

        if (!string.IsNullOrWhiteSpace(sscc) &&
            await IdentifierExistsAsync(connection, transaction, "sscc", sscc.Trim(), excludingId, cancellationToken))
        {
            return PackagingErrors.SsccInUse(sscc.Trim());
        }

        return null;
    }

    private static async Task<bool> IdentifierExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string column,
        string value,
        long? excludingId,
        CancellationToken cancellationToken)
    {
        // The column name is chosen from two compile-time literals at the only
        // call site above; no client input reaches this string.
        var sql = $"""
            SELECT 1 FROM catalog.packaging_unit
            WHERE {column} = @value AND (@excludingId::bigint IS NULL OR id <> @excludingId)
            LIMIT 1
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("value", value);
        command.Parameters.AddWithValue("excludingId", (object?)excludingId ?? DBNull.Value);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task ClearBaseUnitAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long skuId,
        long? excludingId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE catalog.packaging_unit
            SET is_base_unit = false
            WHERE sku_id = @skuId AND is_base_unit
              AND (@excludingId::bigint IS NULL OR id <> @excludingId)
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("skuId", skuId);
        command.Parameters.AddWithValue("excludingId", (object?)excludingId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long?> ResolveSkuAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        string skuCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id FROM catalog.sku WHERE product_id = @productId AND sku_code = @code
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("productId", productId);
        command.Parameters.AddWithValue("code", skuCode);

        return await command.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
    }

    private static async Task<long?> ResolveOwningSkuAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        long unitId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.id
            FROM catalog.packaging_unit pu
            JOIN catalog.sku s ON s.id = pu.sku_id
            WHERE pu.id = @id AND s.product_id = @productId
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", unitId);
        command.Parameters.AddWithValue("productId", productId);

        return await command.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
    }

    private static async Task<Dictionary<string, long>> LoadLevelsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long skuId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT level, id FROM catalog.packaging_unit WHERE sku_id = @skuId";

        var levels = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var row in await DapperGateway.QueryOnAsync<LevelRow>(
                     connection, sql, new { skuId }, cancellationToken, transaction))
        {
            levels[row.Level] = row.Id;
        }

        return levels;
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(
            name, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim());

    private static void AddNullableNumber(NpgsqlCommand command, string name, decimal? value) =>
        command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);

    private static async Task<ProductPackagingDto?> ReadAsync(
        NpgsqlConnection connection,
        string productCode,
        CancellationToken cancellationToken)
    {
        var (productId, rowVersion) = await ProductWriteScope.ResolveProductAsync(
            connection, productCode, cancellationToken);

        if (productId is null)
        {
            return null;
        }

        // Every SKU is returned, including those with no packaging yet, so the
        // tab shows which variants still need logistics data.
        const string sql = """
            SELECT s.sku_code,
                   pu.id,
                   pu.level,
                   parent.level AS parent_level,
                   pu.contains_qty,
                   pu.gtin, pu.sscc, pu.gross_weight, pu.net_weight,
                   pu.length_mm, pu.width_mm, pu.height_mm,
                   pu.is_base_unit, pu.dangerous_goods, pu.un_class
            FROM catalog.sku s
            LEFT JOIN catalog.packaging_unit pu ON pu.sku_id = s.id
            LEFT JOIN catalog.packaging_unit parent ON parent.id = pu.parent_id
            WHERE s.product_id = @productId
            ORDER BY s.sort_order, s.sku_code, pu.id
            """;

        var rows = await DapperGateway.QueryOnAsync<PackagingRow>(
            connection, sql, new { productId = productId.Value }, cancellationToken);

        var bySku = new Dictionary<string, List<PackagingUnitDto>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var row in rows)
        {
            if (!bySku.TryGetValue(row.SkuCode, out var units))
            {
                units = [];
                bySku[row.SkuCode] = units;
                order.Add(row.SkuCode);
            }

            // The LEFT JOIN yields one all-null packaging row for a SKU with no
            // logistics data yet; the SKU still has to appear in the list.
            if (row.Id is null)
            {
                continue;
            }

            units.Add(new PackagingUnitDto(
                row.Id.Value,
                row.Level!,
                row.ParentLevel,
                row.ContainsQty!.Value,
                row.Gtin,
                row.Sscc,
                row.GrossWeight,
                row.NetWeight,
                row.LengthMm,
                row.WidthMm,
                row.HeightMm,
                row.IsBaseUnit!.Value,
                row.DangerousGoods!.Value,
                row.UnClass));
        }

        // Innermost first, so the table reads Each -> Carton -> Pallet.
        var skus = order
            .Select(code => new SkuPackagingDto(
                code,
                [.. bySku[code].OrderBy(u => PackagingLevel.RankOf(u.Level))]))
            .ToList();

        return new ProductPackagingDto(productCode, rowVersion, skus);
    }

    private sealed record LevelRow(string Level, long Id);

    /// <summary>
    /// One packaging row, or a SKU that has none.
    ///
    /// Every packaging column is nullable here even where the table forbids
    /// nulls: this is the right side of a LEFT JOIN, so a SKU without
    /// logistics data produces a row of nulls that must materialise rather
    /// than throw.
    /// </summary>
    private sealed record PackagingRow(
        string SkuCode,
        long? Id,
        string? Level,
        string? ParentLevel,
        decimal? ContainsQty,
        string? Gtin,
        string? Sscc,
        decimal? GrossWeight,
        decimal? NetWeight,
        decimal? LengthMm,
        decimal? WidthMm,
        decimal? HeightMm,
        bool? IsBaseUnit,
        bool? DangerousGoods,
        string? UnClass);
}

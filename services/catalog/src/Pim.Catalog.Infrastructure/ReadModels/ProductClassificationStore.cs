using Npgsql;
using Pim.Catalog.Application.Products.Classifications;
using Pim.Catalog.Domain.Classifications;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Infrastructure.Persistence;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Reads a product's parallel classifications and writes mappings and feature
/// values back.
///
/// Every scheme is returned whether or not the product is mapped into it, so
/// the tab shows the full set of standards the business has to satisfy and an
/// unmapped scheme is visibly a gap rather than an absent row.
///
/// Feature completeness is computed in SQL from the class's declared features
/// against the product's stored values. It is never persisted: a stored count
/// would go stale the moment a class gained a feature.
/// </summary>
public sealed class ProductClassificationStore(CatalogDbContext dbContext) : IProductClassificationStore
{
    public Task<ProductClassificationsDto?> GetAsync(
        string productCode,
        CancellationToken cancellationToken) =>
        ProductWriteScope.ReadAsync(
            dbContext,
            connection => ReadAsync(connection, null, productCode, cancellationToken),
            cancellationToken);

    public Task<Result<ProductClassificationsDto>> AssignClassAsync(
        string productCode,
        string schemeCode,
        string? classCode,
        int expectedRowVersion,
        CancellationToken cancellationToken) =>
        InTransactionAsync(
            productCode,
            expectedRowVersion,
            async (connection, transaction, productId) =>
            {
                var schemeId = await ResolveSchemeAsync(
                    connection, transaction, schemeCode, cancellationToken);

                if (schemeId is null)
                {
                    return ClassificationErrors.SchemeNotFound(schemeCode);
                }

                long? classId = null;
                if (!string.IsNullOrWhiteSpace(classCode))
                {
                    classId = await ResolveClassAsync(
                        connection, transaction, schemeId.Value, classCode, cancellationToken);

                    if (classId is null)
                    {
                        return ClassificationErrors.ClassNotFound(schemeCode, classCode);
                    }
                }

                await UpsertMappingAsync(
                    connection, transaction, productId, schemeId.Value, classId, cancellationToken);

                // Feature values belong to a class. Moving to a different class
                // (or clearing the mapping) leaves values that the new class does
                // not declare, which would then be invisible in the UI but still
                // exported. Drop them here rather than leak them.
                await DeleteOrphanedValuesAsync(
                    connection, transaction, productId, schemeId.Value, classId, cancellationToken);

                return null;
            },
            cancellationToken);

    public Task<Result<ProductClassificationsDto>> SaveFeaturesAsync(
        string productCode,
        string schemeCode,
        IReadOnlyList<ClassificationFeatureInput> values,
        int expectedRowVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);

        return InTransactionAsync(
            productCode,
            expectedRowVersion,
            async (connection, transaction, productId) =>
            {
                var schemeId = await ResolveSchemeAsync(
                    connection, transaction, schemeCode, cancellationToken);

                if (schemeId is null)
                {
                    return ClassificationErrors.SchemeNotFound(schemeCode);
                }

                var definitions = await LoadAssignedFeaturesAsync(
                    connection, transaction, productId, schemeId.Value, cancellationToken);

                if (definitions.Count == 0)
                {
                    return ClassificationErrors.NoClassAssigned(schemeCode);
                }

                // Validate everything before writing anything: a form save is
                // all-or-nothing, so one bad field cannot half-apply the rest.
                var writes = new List<(FeatureDefinition Definition, ParsedFeatureValue Parsed)>();
                foreach (var input in values)
                {
                    if (!definitions.TryGetValue(input.FeatureCode, out var definition))
                    {
                        return ClassificationErrors.UnknownFeature(input.FeatureCode);
                    }

                    var parsed = ClassificationFeatureValueParser.Parse(
                        definition.Code, definition.DataType, input.Value);

                    if (parsed.IsFailure)
                    {
                        return parsed.Error;
                    }

                    writes.Add((definition, parsed.Value));
                }

                foreach (var (definition, parsed) in writes)
                {
                    await WriteFeatureValueAsync(
                        connection, transaction, productId, definition, parsed, cancellationToken);
                }

                await RefreshMappedStatusAsync(
                    connection, transaction, productId, schemeId.Value, cancellationToken);

                return null;
            },
            cancellationToken);
    }

    private sealed record FeatureDefinition(long FeatureId, string Code, string DataType, string? Unit);

    /// <summary>
    /// Delegates the lock/compare/bump protocol to the shared scope so all five
    /// product-scoped stores cannot drift apart on the part that matters most.
    /// </summary>
    private Task<Result<ProductClassificationsDto>> InTransactionAsync(
        string productCode,
        int expectedRowVersion,
        Func<NpgsqlConnection, NpgsqlTransaction, long, Task<Error?>> action,
        CancellationToken cancellationToken) =>
        ProductWriteScope.ExecuteAsync(
            dbContext,
            productCode,
            expectedRowVersion,
            scope => action(scope.Connection, scope.Transaction, scope.ProductId),
            connection => ReadAsync(connection, null, productCode, cancellationToken),
            cancellationToken);

    private static async Task<long?> ResolveSchemeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string schemeCode,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT id FROM catalog.classification_scheme WHERE code = @code";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("code", schemeCode);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long id ? id : null;
    }

    private static async Task<long?> ResolveClassAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long schemeId,
        string classCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id FROM catalog.classification_class
            WHERE scheme_id = @schemeId AND code = @code
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("schemeId", schemeId);
        command.Parameters.AddWithValue("code", classCode);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long id ? id : null;
    }

    private static async Task UpsertMappingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        long schemeId,
        long? classId,
        CancellationToken cancellationToken)
    {
        // mapped_status is derived here rather than accepted from the client:
        // a class with no assignment is unmapped, and one with an assignment
        // starts 'partial' until RefreshMappedStatus proves otherwise.
        const string sql = """
            INSERT INTO catalog.product_classification
                (product_id, scheme_id, class_id, is_primary, mapped_status)
            VALUES (@productId, @schemeId, @classId, false,
                    CASE WHEN @classId::bigint IS NULL THEN 'unmapped' ELSE 'partial' END)
            ON CONFLICT (product_id, scheme_id) DO UPDATE
            SET class_id = EXCLUDED.class_id,
                mapped_status = EXCLUDED.mapped_status
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("productId", productId);
        command.Parameters.AddWithValue("schemeId", schemeId);
        command.Parameters.AddWithValue("classId", (object?)classId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteOrphanedValuesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        long schemeId,
        long? classId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM catalog.product_classification_value v
            USING catalog.classification_feature f
            WHERE v.feature_id = f.id
              AND v.product_id = @productId
              AND f.scheme_id = @schemeId
              AND (@classId::bigint IS NULL OR f.class_id IS DISTINCT FROM @classId)
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("productId", productId);
        command.Parameters.AddWithValue("schemeId", schemeId);
        command.Parameters.AddWithValue("classId", (object?)classId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, FeatureDefinition>> LoadAssignedFeaturesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        long schemeId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT f.id AS feature_id, f.code, f.data_type, f.unit
            FROM catalog.product_classification pc
            JOIN catalog.classification_feature f ON f.class_id = pc.class_id
            WHERE pc.product_id = @productId AND pc.scheme_id = @schemeId
            """;

        var rows = await DapperGateway.QueryOnAsync<FeatureDefinition>(
            connection, sql, new { productId, schemeId }, cancellationToken, transaction);

        var definitions = new Dictionary<string, FeatureDefinition>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            definitions[row.Code] = row;
        }

        return definitions;
    }

    private static async Task WriteFeatureValueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        FeatureDefinition definition,
        ParsedFeatureValue parsed,
        CancellationToken cancellationToken)
    {
        if (parsed.IsEmpty)
        {
            const string deleteSql = """
                DELETE FROM catalog.product_classification_value
                WHERE product_id = @productId AND feature_id = @featureId
                """;

            await using var delete = new NpgsqlCommand(deleteSql, connection, transaction);
            delete.Parameters.AddWithValue("productId", productId);
            delete.Parameters.AddWithValue("featureId", definition.FeatureId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        // The primary key is a plain (product_id, feature_id) pair, so unlike
        // attribute_value this can use a real upsert.
        const string upsertSql = """
            INSERT INTO catalog.product_classification_value
                (product_id, feature_id, value_text, value_number, value_bool, value_unit, status)
            VALUES (@productId, @featureId, @text, @number, @bool, @unit, 'approved')
            ON CONFLICT (product_id, feature_id) DO UPDATE
            SET value_text = EXCLUDED.value_text,
                value_number = EXCLUDED.value_number,
                value_bool = EXCLUDED.value_bool,
                value_unit = EXCLUDED.value_unit,
                status = EXCLUDED.status
            """;

        await using var command = new NpgsqlCommand(upsertSql, connection, transaction);
        command.Parameters.AddWithValue("productId", productId);
        command.Parameters.AddWithValue("featureId", definition.FeatureId);
        command.Parameters.AddWithValue("text", (object?)parsed.Text ?? DBNull.Value);
        command.Parameters.AddWithValue("number", (object?)parsed.Number ?? DBNull.Value);
        command.Parameters.AddWithValue("bool", (object?)parsed.Boolean ?? DBNull.Value);
        command.Parameters.AddWithValue("unit", (object?)definition.Unit ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// A mapping is 'mapped' only when every REQUIRED feature of its class has
    /// a value. Optional gaps are normal and must not block an export.
    /// </summary>
    private static async Task RefreshMappedStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        long schemeId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE catalog.product_classification pc
            SET mapped_status = CASE
                WHEN pc.class_id IS NULL THEN 'unmapped'
                WHEN NOT EXISTS (
                    SELECT 1 FROM catalog.classification_feature f
                    LEFT JOIN catalog.product_classification_value v
                           ON v.feature_id = f.id AND v.product_id = pc.product_id
                    WHERE f.class_id = pc.class_id AND f.is_required AND v.product_id IS NULL
                ) THEN 'mapped'
                ELSE 'partial'
            END
            WHERE pc.product_id = @productId AND pc.scheme_id = @schemeId
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("productId", productId);
        command.Parameters.AddWithValue("schemeId", schemeId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task BumpRowVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE catalog.product
            SET row_version = row_version + 1, updated_at = now()
            WHERE id = @productId
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("productId", productId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record SchemeRow(
        long SchemeId,
        string Code,
        string Name,
        string Version,
        string? Purpose,
        bool IsPrimary,
        string MappedStatus,
        long? ClassId,
        string? ClassCode,
        string? ClassLabel);

    private static async Task<ProductClassificationsDto?> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string productCode,
        CancellationToken cancellationToken)
    {
        const string productSql = """
            SELECT id, row_version FROM catalog.product WHERE product_code = @code
            """;

        var header = await DapperGateway.QuerySingleOrDefaultOnAsync<ProductVersionRow>(
            connection, productSql, new { code = productCode }, cancellationToken, transaction);

        if (header is null)
        {
            return null;
        }

        var (productId, rowVersion) = (header.Id, header.RowVersion);

        // LEFT JOIN from scheme, not from the mapping: every configured scheme
        // appears whether or not this product is mapped into it.
        const string schemeSql = """
            SELECT s.id AS scheme_id, s.code, s.name, s.version, s.purpose,
                   COALESCE(pc.is_primary, false)             AS is_primary,
                   COALESCE(pc.mapped_status, 'unmapped')     AS mapped_status,
                   pc.class_id,
                   c.code  AS class_code,
                   c.label AS class_label
            FROM catalog.classification_scheme s
            LEFT JOIN catalog.product_classification pc
                   ON pc.scheme_id = s.id AND pc.product_id = @productId
            LEFT JOIN catalog.classification_class c ON c.id = pc.class_id
            WHERE s.status = 'active'
            ORDER BY s.sort_order, s.code
            """;

        var schemes = await DapperGateway.QueryOnAsync<SchemeRow>(
            connection, schemeSql, new { productId }, cancellationToken, transaction);

        var classes = await LoadClassesAsync(connection, transaction, cancellationToken);
        var features = await LoadFeaturesAsync(connection, transaction, productId, cancellationToken);

        var result = schemes.Select(s =>
        {
            var schemeFeatures = s.ClassId is { } classId && features.TryGetValue(classId, out var list)
                ? list
                : [];

            return new ProductClassificationDto(
                s.Code,
                s.Name,
                s.Version,
                s.Purpose,
                s.IsPrimary,
                s.MappedStatus,
                s.ClassCode,
                s.ClassLabel,
                schemeFeatures.Count,
                schemeFeatures.Count(f => f.Value is not null),
                classes.TryGetValue(s.SchemeId, out var available) ? available : [],
                schemeFeatures);
        }).ToList();

        return new ProductClassificationsDto(productCode, rowVersion, result);
    }

    private static async Task<Dictionary<long, List<ClassificationClassDto>>> LoadClassesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT scheme_id, code, label
            FROM catalog.classification_class
            ORDER BY scheme_id, code
            """;

        var rows = await DapperGateway.QueryOnAsync<ClassRow>(
            connection, sql, parameters: null, cancellationToken, transaction);

        var map = new Dictionary<long, List<ClassificationClassDto>>();
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.SchemeId, out var list))
            {
                list = [];
                map[row.SchemeId] = list;
            }

            list.Add(new ClassificationClassDto(row.Code, row.Label));
        }

        return map;
    }

    private static async Task<Dictionary<long, List<ClassificationFeatureDto>>> LoadFeaturesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        long productId,
        CancellationToken cancellationToken)
    {
        // The value is rendered to canonical string form in SQL so the client
        // sees one representation per data type. Same trailing-zero rule as the
        // attribute reader: strip only from the fraction, never the integer.
        const string sql = """
            SELECT f.class_id, f.code, f.name, f.data_type, f.unit, f.is_required,
                   CASE
                     WHEN v.product_id IS NULL THEN NULL
                     WHEN f.data_type = 'logical' THEN
                       CASE WHEN v.value_bool THEN 'true' ELSE 'false' END
                     WHEN f.data_type = 'numeric' THEN
                       CASE
                         WHEN v.value_number = trunc(v.value_number)
                           THEN trunc(v.value_number)::bigint::text
                         ELSE rtrim(rtrim(v.value_number::text, '0'), '.')
                       END
                     ELSE v.value_text
                   END AS value
            FROM catalog.classification_feature f
            LEFT JOIN catalog.product_classification_value v
                   ON v.feature_id = f.id AND v.product_id = @productId
            WHERE f.class_id IS NOT NULL
            ORDER BY f.class_id, f.sort_order, f.code
            """;

        var rows = await DapperGateway.QueryOnAsync<FeatureRow>(
            connection, sql, new { productId }, cancellationToken, transaction);

        var map = new Dictionary<long, List<ClassificationFeatureDto>>();
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.ClassId, out var list))
            {
                list = [];
                map[row.ClassId] = list;
            }

            list.Add(new ClassificationFeatureDto(
                row.Code, row.Name, row.DataType, row.Unit, row.IsRequired, row.Value));
        }

        return map;
    }

    private sealed record ProductVersionRow(long Id, int RowVersion);

    private sealed record ClassRow(long SchemeId, string Code, string? Label);

    /// <summary>
    /// A feature and this product's value for it. Value is rendered to its
    /// canonical string in SQL, so the same number reads identically here and
    /// on the Attributes tab.
    /// </summary>
    private sealed record FeatureRow(
        long ClassId,
        string Code,
        string? Name,
        string DataType,
        string? Unit,
        bool IsRequired,
        string? Value);
}

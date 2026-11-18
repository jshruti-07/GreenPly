using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Pim.Catalog.Application.Attributes;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Attributes;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Infrastructure.Persistence;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Reads the attribute template for a product and writes values back.
///
/// The template is the product's attribute_set — the same denominator the
/// completeness rule uses — so what the form shows and what the grid scores
/// can never disagree.
///
/// Values are written as 'approved'. Attribute-level review (draft -> approved)
/// arrives with the workflow engine; until then a steward's entry is the
/// authoritative value, and the draft state is still honoured on read because
/// seeded and imported data can carry it.
/// </summary>
public sealed class ProductAttributeStore(CatalogDbContext dbContext) : IProductAttributeStore
{
    private const string WriteDataState = "approved";

    private sealed record AttributeRow(
        long AttributeId,
        string Code,
        string DataType,
        bool IsRequired,
        bool IsLocalizable,
        string? UnitSymbol,
        string GroupCode,
        string GroupName,
        int GroupSort,
        int AttributeSort,
        string? Value,
        string? DataState,
        string Label);

    public async Task<ProductAttributesDto?> GetAsync(
        string productCode,
        DisplayLocale displayLocale,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            return await ReadAsync(connection, productCode, displayLocale, cancellationToken);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<Result<ProductAttributesDto>> SaveAsync(
        string productCode,
        IReadOnlyList<AttributeValueInput> values,
        int expectedRowVersion,
        DisplayLocale displayLocale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var (productId, rowVersion) = await LoadProductAsync(connection, transaction, productCode, cancellationToken);
            if (productId is null)
            {
                return Result.Failure<ProductAttributesDto>(ProductErrors.NotFound(productCode));
            }

            if (rowVersion != expectedRowVersion)
            {
                return Result.Failure<ProductAttributesDto>(ProductErrors.ConcurrentModification);
            }

            var definitions = await LoadDefinitionsAsync(connection, transaction, productId.Value, cancellationToken);

            // Validate EVERY value before writing any of them: a form save is
            // all-or-nothing, so a typo in one field cannot leave the rest applied.
            var writes = new List<(AttributeDefinition Definition, ParsedAttributeValue Parsed)>();
            foreach (var input in values)
            {
                if (!definitions.TryGetValue(input.AttributeCode, out var definition))
                {
                    return Result.Failure<ProductAttributesDto>(
                        AttributeErrors.UnknownAttribute(input.AttributeCode));
                }

                var parsed = AttributeValueParser.Parse(
                    definition.Code, definition.DataType, input.Value, definition.OptionCodes);

                if (parsed.IsFailure)
                {
                    return Result.Failure<ProductAttributesDto>(parsed.Error);
                }

                if (definition.IsRequired && parsed.Value.IsEmpty)
                {
                    return Result.Failure<ProductAttributesDto>(
                        AttributeErrors.RequiredValueMissing(definition.Code));
                }

                writes.Add((definition, parsed.Value));
            }

            foreach (var (definition, parsed) in writes)
            {
                await WriteValueAsync(connection, transaction, productId.Value, definition, parsed, cancellationToken);
            }

            await BumpRowVersionAsync(connection, transaction, productId.Value, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var refreshed = await ReadAsync(connection, productCode, displayLocale, cancellationToken);
            return refreshed is null
                ? Result.Failure<ProductAttributesDto>(ProductErrors.NotFound(productCode))
                : Result.Success(refreshed);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private sealed record AttributeDefinition(
        long AttributeId,
        string Code,
        string DataType,
        bool IsRequired,
        IReadOnlyCollection<string> OptionCodes);

    private static async Task<(long? ProductId, int RowVersion)> LoadProductAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string productCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, row_version FROM catalog.product WHERE product_code = @code FOR UPDATE
            """;

        var row = await DapperGateway.QuerySingleOrDefaultOnAsync<ProductVersionRow>(
            connection, sql, new { code = productCode }, cancellationToken, transaction);

        return row is null ? (null, 0) : (row.Id, row.RowVersion);
    }

    private static async Task<Dictionary<string, AttributeDefinition>> LoadDefinitionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT a.id AS attribute_id, a.code, a.data_type, sa.is_required,
                   COALESCE(
                     (SELECT string_agg(ao.code, ',' ORDER BY ao.sort_order)
                      FROM catalog.attribute_option ao WHERE ao.attribute_id = a.id), '')
                     AS option_codes_csv
            FROM catalog.product p
            JOIN catalog.attribute_set_attribute sa ON sa.set_id = p.attribute_set_id
            JOIN catalog.attribute a ON a.id = sa.attribute_id
            WHERE p.id = @productId
            """;

        var rows = await DapperGateway.QueryOnAsync<DefinitionRow>(
            connection, sql, new { productId }, cancellationToken, transaction);

        var definitions = new Dictionary<string, AttributeDefinition>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            definitions[row.Code] = new AttributeDefinition(
                row.AttributeId,
                row.Code,
                row.DataType,
                row.IsRequired,
                row.OptionCodesCsv.Length == 0 ? [] : row.OptionCodesCsv.Split(','));
        }

        return definitions;
    }

    private static async Task WriteValueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        AttributeDefinition definition,
        ParsedAttributeValue parsed,
        CancellationToken cancellationToken)
    {
        // Clearing removes the row entirely so completeness stops counting it.
        if (parsed.IsEmpty)
        {
            const string deleteSql = """
                DELETE FROM catalog.attribute_value
                WHERE product_id = @productId AND attribute_id = @attributeId
                """;

            await using var delete = new NpgsqlCommand(deleteSql, connection, transaction);
            delete.Parameters.AddWithValue("productId", productId);
            delete.Parameters.AddWithValue("attributeId", definition.AttributeId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        // Delete-then-insert rather than ON CONFLICT: the unique index is over
        // coalesce() expressions, which ON CONFLICT cannot target.
        const string upsertSql = """
            DELETE FROM catalog.attribute_value
            WHERE product_id = @productId AND attribute_id = @attributeId;

            INSERT INTO catalog.attribute_value
                (product_id, attribute_id, value_text, value_number, value_boolean,
                 value_date, value_datetime, value_option_id, data_state, source, updated_at)
            VALUES
                (@productId, @attributeId, @text, @number, @boolean,
                 @date, @datetime,
                 (SELECT id FROM catalog.attribute_option
                   WHERE attribute_id = @attributeId AND code = @optionCode),
                 @dataState, 'manual', now());
            """;

        await using var command = new NpgsqlCommand(upsertSql, connection, transaction);
        command.Parameters.AddWithValue("productId", productId);
        command.Parameters.AddWithValue("attributeId", definition.AttributeId);
        command.Parameters.AddWithValue("text", (object?)parsed.Text ?? DBNull.Value);
        command.Parameters.AddWithValue("number", (object?)parsed.Number ?? DBNull.Value);
        command.Parameters.AddWithValue("boolean", (object?)parsed.Boolean ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date)
        {
            Value = (object?)parsed.Date ?? DBNull.Value,
        });
        command.Parameters.AddWithValue("datetime", (object?)parsed.DateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("optionCode", (object?)parsed.OptionCode ?? DBNull.Value);
        command.Parameters.AddWithValue("dataState", WriteDataState);

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

    private static async Task<ProductAttributesDto?> ReadAsync(
        NpgsqlConnection connection,
        string productCode,
        DisplayLocale displayLocale,
        CancellationToken cancellationToken)
    {
        const string productSql = """
            SELECT id, base_locale, row_version FROM catalog.product WHERE product_code = @code
            """;

        var header = await DapperGateway.QuerySingleOrDefaultOnAsync<ProductLocaleVersionRow>(
            connection, productSql, new { code = productCode }, cancellationToken);

        if (header is null)
        {
            return null;
        }

        var (productId, baseLocale, rowVersion) =
            (header.Id, header.BaseLocale, header.RowVersion);

        // Value is rendered to its canonical string form in SQL so the client
        // receives one consistent representation per data type.
        const string attributesSql = """
            SELECT a.id AS attribute_id, a.code, a.data_type, sa.is_required,
                   a.is_localizable,
                   u.symbol AS unit_symbol,
                   COALESCE(ag.code, 'ungrouped')             AS group_code,
                   COALESCE(agtr_d.name, agtr_b.name, ag.code, 'Ungrouped') AS group_name,
                   COALESCE(ag.sort_order, 2147483647)        AS group_sort,
                   sa.sort_order AS attribute_sort,
                   CASE
                     WHEN av.id IS NULL THEN NULL
                     WHEN a.data_type = 'list' THEN ao.code
                     WHEN a.data_type = 'boolean' THEN
                       CASE WHEN av.value_boolean THEN 'true' ELSE 'false' END
                     WHEN a.data_type IN ('number','decimal','measure') THEN
                       -- Trailing zeros may only be stripped from the FRACTION.
                       -- Stripping them from the whole number turned 1000 into 1
                       -- (caught by smoke assertion, 2026-08-15).
                       CASE
                         WHEN av.value_number = trunc(av.value_number)
                           THEN trunc(av.value_number)::bigint::text
                         ELSE rtrim(rtrim(av.value_number::text, '0'), '.')
                       END
                     WHEN a.data_type = 'date' THEN to_char(av.value_date, 'YYYY-MM-DD')
                     WHEN a.data_type = 'datetime' THEN
                       to_char(av.value_datetime AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
                     ELSE av.value_text
                   END AS value,
                   av.data_state,
                   -- The steward reads a label, not a column name. Falling back
                   -- to the code is a visible defect, not a graceful default:
                   -- a German catalogue showed 'material_type' where the seed
                   -- had 'Materialart' all along, because this join was never
                   -- written (found in the browser pass, 2026-08-16).
                   COALESCE(atr_d.label, atr_b.label, a.code) AS label
            FROM catalog.product p
            JOIN catalog.attribute_set_attribute sa ON sa.set_id = p.attribute_set_id
            JOIN catalog.attribute a ON a.id = sa.attribute_id
            LEFT JOIN catalog.attribute_group ag ON ag.id = a.group_id
            -- Labels follow the reader's language, falling back to the base
            -- locale. Two plain joins rather than the grid's lateral: there is
            -- exactly one row per (entity, locale), so no ORDER BY / LIMIT is
            -- needed and each join is a primary-key hit.
            LEFT JOIN catalog.attribute_group_tr agtr_d
                   ON agtr_d.group_id = ag.id AND agtr_d.locale_code = @displayLocale
            LEFT JOIN catalog.attribute_group_tr agtr_b
                   ON agtr_b.group_id = ag.id AND agtr_b.locale_code = p.base_locale
            LEFT JOIN catalog.unit u ON u.id = a.unit_id
            LEFT JOIN catalog.attribute_value av
                   ON av.product_id = p.id AND av.attribute_id = a.id
                  -- A localizable attribute has one row per locale. Without
                  -- this predicate the join fans out and the attribute appears
                  -- once per language on a tab that edits one language.
                  --
                  -- Deliberately still base_locale, NOT @displayLocale. This is
                  -- the VALUE, and this tab edits it: following the selector
                  -- here would silently change which locale a save writes to,
                  -- and per-locale editing is the Localization tab's job
                  -- (ADR-009). Labels move with the reader; values do not.
                  AND (av.locale_code IS NULL OR av.locale_code = p.base_locale)
            LEFT JOIN catalog.attribute_option ao ON ao.id = av.value_option_id
            LEFT JOIN catalog.attribute_tr atr_d
                   ON atr_d.attribute_id = a.id AND atr_d.locale_code = @displayLocale
            LEFT JOIN catalog.attribute_tr atr_b
                   ON atr_b.attribute_id = a.id AND atr_b.locale_code = p.base_locale
            WHERE p.id = @productId
            ORDER BY ag.sort_order NULLS LAST, sa.sort_order, a.code
            """;

        // The "ungrouped" fallbacks moved into the SQL COALESCEs above, so
        // AttributeRow has no nullable group columns to defend against and the
        // default lives in one place rather than in both the query and the map.
        var rows = await DapperGateway.QueryOnAsync<AttributeRow>(
            connection, attributesSql, new { productId, displayLocale = displayLocale.Code }, cancellationToken);

        var options = await LoadOptionsAsync(
            connection, productId, displayLocale.Code, baseLocale, cancellationToken);

        var groups = rows
            .GroupBy(r => (r.GroupCode, r.GroupName, r.GroupSort))
            .OrderBy(g => g.Key.GroupSort)
            .ThenBy(g => g.Key.GroupCode, StringComparer.Ordinal)
            .Select(g => new ProductAttributeGroupDto(
                g.Key.GroupCode,
                g.Key.GroupName,
                [.. g.OrderBy(r => r.AttributeSort).Select(r => new ProductAttributeDto(
                    r.Code,
                    r.Label,
                    r.DataType,
                    r.IsRequired,
                    r.IsLocalizable,
                    AttributeDataType.IsEditable(r.DataType),
                    r.UnitSymbol,
                    r.Value,
                    r.DataState,
                    options.TryGetValue(r.AttributeId, out var opts) ? opts : []))]))
            .ToList();

        // Same rule as the grid: approved values for required attributes.
        var requiredRows = rows.Where(r => r.IsRequired).ToList();
        var requiredFilled = requiredRows.Count(r =>
            r.Value is not null && string.Equals(r.DataState, "approved", StringComparison.Ordinal));

        var completeness = requiredRows.Count == 0
            ? 0
            : (int)Math.Round(100.0 * requiredFilled / requiredRows.Count, MidpointRounding.AwayFromZero);

        return new ProductAttributesDto(
            productCode,
            baseLocale,
            completeness,
            requiredRows.Count,
            requiredFilled,
            rowVersion,
            groups);
    }

    private static async Task<Dictionary<long, List<AttributeOptionDto>>> LoadOptionsAsync(
        NpgsqlConnection connection,
        long productId,
        string displayLocale,
        string baseLocale,
        CancellationToken cancellationToken)
    {
        // Option labels are what the steward picks from in a dropdown, so they
        // follow the reader's language like every other label.
        const string sql = """
            SELECT ao.attribute_id, ao.code,
                   COALESCE(tr_d.label, tr_b.label, ao.code) AS label
            FROM catalog.product p
            JOIN catalog.attribute_set_attribute sa ON sa.set_id = p.attribute_set_id
            JOIN catalog.attribute_option ao ON ao.attribute_id = sa.attribute_id
            LEFT JOIN catalog.attribute_option_tr tr_d
                   ON tr_d.option_id = ao.id AND tr_d.locale_code = @displayLocale
            LEFT JOIN catalog.attribute_option_tr tr_b
                   ON tr_b.option_id = ao.id AND tr_b.locale_code = @baseLocale
            WHERE p.id = @productId
            ORDER BY ao.sort_order
            """;

        var rows = await DapperGateway.QueryOnAsync<OptionRow>(
            connection, sql, new { productId, displayLocale, baseLocale }, cancellationToken);

        var map = new Dictionary<long, List<AttributeOptionDto>>();
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.AttributeId, out var list))
            {
                list = [];
                map[row.AttributeId] = list;
            }

            list.Add(new AttributeOptionDto(row.Code, row.Label));
        }

        return map;
    }

    /// <summary>Formats a decimal the way the read SQL does, for tests.</summary>
    internal static string FormatNumber(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private sealed record ProductVersionRow(long Id, int RowVersion);

    private sealed record ProductLocaleVersionRow(long Id, string BaseLocale, int RowVersion);

    /// <summary>
    /// An attribute definition for the write path. Option codes arrive as a CSV
    /// from a correlated aggregate rather than a second query — the write needs
    /// them only to validate a list value against its allowed set.
    /// </summary>
    private sealed record DefinitionRow(
        long AttributeId,
        string Code,
        string DataType,
        bool IsRequired,
        string OptionCodesCsv);

    private sealed record OptionRow(long AttributeId, string Code, string Label);
}

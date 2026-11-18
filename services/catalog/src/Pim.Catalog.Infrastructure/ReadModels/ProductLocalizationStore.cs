using System.Globalization;
using Npgsql;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Products.Localization;
using Pim.Catalog.Domain.Localization;
using Pim.Catalog.Infrastructure.Persistence;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// The Localization tab: every configured locale side by side with the base.
///
/// Two storage shapes are edited through one endpoint. Product copy lives in
/// <c>product_tr</c> columns; localizable attribute values live in
/// <c>attribute_value</c> rows carrying a <c>locale_code</c>. The tab presents
/// them as one list because a translator does not care which table a sentence
/// is in.
/// </summary>
internal sealed class ProductLocalizationStore(CatalogDbContext dbContext) : IProductLocalizationStore
{
    private sealed record LocaleRow(
        string Code,
        string Name,
        bool IsBase,
        string Status,
        string? Source,
        DateTimeOffset? UpdatedAt,
        Dictionary<string, string?> Values);

    public Task<ProductLocalizationDto?> GetAsync(
        string productCode,
        DisplayLocale locale,
        CancellationToken cancellationToken) =>
        ProductWriteScope.ReadAsync(
            dbContext,
            connection => LoadAsync(connection, productCode, locale, cancellationToken),
            cancellationToken);

    public Task<Result<ProductLocalizationDto>> SaveAsync(
        UpdateProductLocalizationCommand command,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ProductWriteScope.ExecuteAsync(
            dbContext,
            command.Code,
            command.RowVersion,
            scope => ApplyAsync(scope, command, cancellationToken),
            connection => LoadAsync(connection, command.Code, locale, cancellationToken),
            cancellationToken);
    }

    // ---------------------------------------------------------------- read

    private static async Task<ProductLocalizationDto?> LoadAsync(
        NpgsqlConnection connection,
        string productCode,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        var header = await LoadHeaderAsync(connection, productCode, cancellationToken);
        if (header is null)
        {
            return null;
        }

        var (productId, rowVersion, baseLocale) = header.Value;

        // The LABELS of the localizable attributes follow the reader; the
        // values under them are the per-locale content this tab exists to edit
        // and are keyed by the row's own locale, not by the selector.
        var localizableAttributes = await LoadLocalizableAttributesAsync(
            connection, productId, baseLocale, locale, cancellationToken);

        var locales = await LoadLocaleRowsAsync(
            connection, productId, baseLocale, localizableAttributes, cancellationToken);

        var baseRow = locales.FirstOrDefault(l => l.IsBase);

        var dtos = locales.Select(locale =>
        {
            var fields = BuildFields(locale, baseRow, localizableAttributes);
            var filled = fields.Count(f => !string.IsNullOrWhiteSpace(f.Value));

            return new ProductLocaleDto(
                locale.Code,
                locale.Name,
                locale.IsBase,
                locale.Status,
                locale.Source,
                locale.UpdatedAt,
                fields.Count,
                filled,
                fields.Count == 0 ? 100 : (int)Math.Round(filled * 100.0 / fields.Count),
                fields);
        }).ToList();

        return new ProductLocalizationDto(
            productCode,
            baseLocale,
            rowVersion,
            // What the tab's header badge counts: locales a translator still
            // has work in. The base locale is never one of them.
            dtos.Count(l => !l.IsBase && !string.Equals(
                l.TranslationStatus, TranslationStatus.Approved, StringComparison.Ordinal)),
            dtos);
    }

    private static List<TranslationFieldDto> BuildFields(
        LocaleRow locale,
        LocaleRow? baseRow,
        IReadOnlyList<(string Code, string Label)> attributes)
    {
        var fields = new List<TranslationFieldDto>();

        foreach (var field in TranslationRules.ProductFields)
        {
            fields.Add(new TranslationFieldDto(
                field,
                TranslationRules.LabelFor(field),
                baseRow?.Values.GetValueOrDefault(field),
                locale.Values.GetValueOrDefault(field),
                TranslationRules.IsRequired(field),
                TranslationRules.IsMultiline(field),
                IsAttribute: false));
        }

        foreach (var (code, label) in attributes)
        {
            fields.Add(new TranslationFieldDto(
                code,
                label,
                baseRow?.Values.GetValueOrDefault(code),
                locale.Values.GetValueOrDefault(code),
                IsRequired: false,
                IsMultiline: false,
                IsAttribute: true));
        }

        return fields;
    }

    private static async Task<(long ProductId, int RowVersion, string BaseLocale)?> LoadHeaderAsync(
        NpgsqlConnection connection,
        string productCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, row_version, base_locale FROM catalog.product WHERE product_code = @code
            """;

        var row = await DapperGateway.QuerySingleOrDefaultOnAsync<ProductHeaderRow>(
            connection, sql, new { code = productCode }, cancellationToken);

        return row is null ? null : (row.Id, row.RowVersion, row.BaseLocale);
    }

    private sealed record ProductHeaderRow(long Id, int RowVersion, string BaseLocale);

    /// <summary>
    /// Localizable attributes in this product's set, labelled in the base
    /// locale. Only attributes flagged <c>is_localizable</c> appear: everything
    /// else has one value for all languages and belongs on the Attributes tab.
    /// </summary>
    private static async Task<List<(string Code, string Label)>> LoadLocalizableAttributesAsync(
        NpgsqlConnection connection,
        long productId,
        string baseLocale,
        DisplayLocale locale,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT a.code, COALESCE(atr_d.label, atr_b.label, a.code) AS label
            FROM catalog.product p
            JOIN catalog.attribute_set_attribute sa ON sa.set_id = p.attribute_set_id
            JOIN catalog.attribute a ON a.id = sa.attribute_id AND a.is_localizable
            LEFT JOIN catalog.attribute_tr atr_d
                   ON atr_d.attribute_id = a.id AND atr_d.locale_code = @displayLocale
            LEFT JOIN catalog.attribute_tr atr_b
                   ON atr_b.attribute_id = a.id AND atr_b.locale_code = @baseLocale
            WHERE p.id = @productId
            ORDER BY sa.sort_order, a.code
            """;

        var attributeRows = await DapperGateway.QueryOnAsync<AttributeLabelRow>(
            connection,
            sql,
            new { productId, baseLocale, displayLocale = locale.Code },
            cancellationToken);

        return attributeRows.Select(r => (r.Code, r.Label)).ToList();
    }

    private sealed record AttributeLabelRow(string Code, string Label);

    private static async Task<List<LocaleRow>> LoadLocaleRowsAsync(
        NpgsqlConnection connection,
        long productId,
        string baseLocale,
        List<(string Code, string Label)> attributes,
        CancellationToken cancellationToken)
    {
        // Every configured locale, whether or not the product has a row in it.
        // A locale with no translation at all is exactly what the tab must
        // show; an inner join would have hidden the untouched languages.
        const string localeSql = """
            SELECT l.code,
                   l.name           AS locale_name,
                   tr.name          AS product_name,
                   tr.short_desc, tr.long_desc, tr.marketing,
                   tr.seo_title, tr.seo_desc,
                   COALESCE(tr.translation_status, 'missing') AS status,
                   tr.source, tr.updated_at
            FROM catalog.locale l
            LEFT JOIN catalog.product_tr tr
                   ON tr.locale_code = l.code AND tr.product_id = @productId
            ORDER BY (l.code = @baseLocale) DESC, l.code
            """;

        var rows = new List<LocaleRow>();

        var localeRows = await DapperGateway.QueryOnAsync<LocaleQueryRow>(
            connection, localeSql, new { productId, baseLocale }, cancellationToken);

        foreach (var row in localeRows)
        {
            // The field keys are the API's field codes, not column names — the
            // tab addresses fields by code and TranslationRules owns the
            // mapping to columns.
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["name"] = row.ProductName,
                ["shortDesc"] = row.ShortDesc,
                ["longDesc"] = row.LongDesc,
                ["marketing"] = row.Marketing,
                ["seoTitle"] = row.SeoTitle,
                ["seoDesc"] = row.SeoDesc,
            };

            rows.Add(new LocaleRow(
                row.Code,
                row.LocaleName,
                string.Equals(row.Code, baseLocale, StringComparison.Ordinal),
                row.Status,
                row.Source,
                row.UpdatedAt,
                values));
        }

        if (attributes.Count == 0)
        {
            return rows;
        }

        const string valueSql = """
            SELECT av.locale_code, a.code, av.value_text
            FROM catalog.attribute_value av
            JOIN catalog.attribute a ON a.id = av.attribute_id AND a.is_localizable
            WHERE av.product_id = @productId AND av.locale_code IS NOT NULL
            """;

        var valueRows = await DapperGateway.QueryOnAsync<LocalizedValueRow>(
            connection, valueSql, new { productId }, cancellationToken);

        foreach (var value in valueRows)
        {
            var row = rows.FirstOrDefault(
                r => string.Equals(r.Code, value.LocaleCode, StringComparison.Ordinal));

            if (row is not null)
            {
                row.Values[value.Code] = value.ValueText;
            }
        }

        return rows;
    }

    // --------------------------------------------------------------- write

    private static async Task<Error?> ApplyAsync(
        ProductWriteScope.Scope scope,
        UpdateProductLocalizationCommand command,
        CancellationToken cancellationToken)
    {
        var baseLocale = await ReadBaseLocaleAsync(scope, cancellationToken);

        if (!await LocaleExistsAsync(scope, command.LocaleCode, cancellationToken))
        {
            return LocalizationErrors.UnknownLocale(command.LocaleCode);
        }

        // The master text is edited on General and Attributes. Allowing it here
        // would give two screens write access to the same column with different
        // validation, and let a translator quietly rewrite the source.
        if (string.Equals(command.LocaleCode, baseLocale, StringComparison.Ordinal))
        {
            return LocalizationErrors.BaseLocaleNotEditable(command.LocaleCode);
        }

        var productFields = command.Values
            .Where(v => TranslationRules.IsProductField(v.Field))
            .ToList();

        var attributeFields = command.Values
            .Where(v => !TranslationRules.IsProductField(v.Field))
            .ToList();

        foreach (var field in productFields.Where(f =>
                     TranslationRules.IsRequired(f.Field) && string.IsNullOrWhiteSpace(f.Value)))
        {
            return LocalizationErrors.RequiredFieldCleared(field.Field);
        }

        var error = await ApplyProductFieldsAsync(scope, command, productFields, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        foreach (var value in attributeFields)
        {
            error = await ApplyAttributeValueAsync(scope, command.LocaleCode, value, cancellationToken);
            if (error is not null)
            {
                return error;
            }
        }

        if (command.TranslationStatus is not null)
        {
            await SetStatusAsync(scope, command, cancellationToken);
        }

        return null;
    }

    private static async Task<Error?> ApplyProductFieldsAsync(
        ProductWriteScope.Scope scope,
        UpdateProductLocalizationCommand command,
        List<TranslationValueInput> fields,
        CancellationToken cancellationToken)
    {
        if (fields.Count == 0)
        {
            return null;
        }

        var exists = await TranslationRowExistsAsync(scope, command.LocaleCode, cancellationToken);

        // product_tr.name is NOT NULL, so a locale's first save has to carry
        // one. Saying so beats surfacing a constraint violation as a 500.
        if (!exists && !fields.Any(f =>
                string.Equals(f.Field, "name", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(f.Value)))
        {
            return LocalizationErrors.RequiredFieldCleared("name");
        }

        // Column names come from TranslationRules' fixed map, never from the
        // caller's string — the field name was whitelisted before reaching here.
        var assignments = fields
            .Select((f, i) => $"{TranslationRules.ColumnFor(f.Field)} = @v{i.ToString(CultureInfo.InvariantCulture)}")
            .ToList();

        string sql;
        if (exists)
        {
            sql = $"""
                UPDATE catalog.product_tr
                SET {string.Join(", ", assignments)}, updated_at = now()
                WHERE product_id = @productId AND locale_code = @locale
                """;
        }
        else
        {
            var columns = fields.Select(f => TranslationRules.ColumnFor(f.Field)).ToList();
            var placeholders = fields
                .Select((_, i) => $"@v{i.ToString(CultureInfo.InvariantCulture)}")
                .ToList();

            sql = $"""
                INSERT INTO catalog.product_tr
                    (product_id, locale_code, {string.Join(", ", columns)}, translation_status, source)
                VALUES (@productId, @locale, {string.Join(", ", placeholders)}, 'pending', 'human')
                """;
        }

        await using var command2 = new NpgsqlCommand(sql, scope.Connection, scope.Transaction);
        command2.Parameters.AddWithValue("productId", scope.ProductId);
        command2.Parameters.AddWithValue("locale", command.LocaleCode);
        for (var i = 0; i < fields.Count; i++)
        {
            var value = string.IsNullOrWhiteSpace(fields[i].Value) ? null : fields[i].Value!.Trim();
            command2.Parameters.AddWithValue(
                $"v{i.ToString(CultureInfo.InvariantCulture)}", (object?)value ?? DBNull.Value);
        }

        await command2.ExecuteNonQueryAsync(cancellationToken);
        return null;
    }

    private static async Task<Error?> ApplyAttributeValueAsync(
        ProductWriteScope.Scope scope,
        string localeCode,
        TranslationValueInput input,
        CancellationToken cancellationToken)
    {
        const string lookupSql = """
            SELECT a.id, a.is_localizable
            FROM catalog.attribute a
            JOIN catalog.attribute_set_attribute sa ON sa.attribute_id = a.id
            JOIN catalog.product p ON p.attribute_set_id = sa.set_id
            WHERE p.id = @productId AND a.code = @code
            """;

        var attribute = await DapperGateway.QuerySingleOrDefaultOnAsync<AttributeLookupRow>(
            scope.Connection, lookupSql,
            new { productId = scope.ProductId, code = input.Field },
            cancellationToken, scope.Transaction);

        if (attribute is null)
        {
            return LocalizationErrors.UnknownField(input.Field);
        }

        var attributeId = attribute.Id;

        if (!attribute.IsLocalizable)
        {
            return LocalizationErrors.NotLocalizable(input.Field);
        }

        var value = string.IsNullOrWhiteSpace(input.Value) ? null : input.Value!.Trim();

        // Clearing removes the row rather than storing an empty string, so the
        // read model reports the field as untranslated instead of translated
        // to nothing.
        if (value is null)
        {
            const string deleteSql = """
                DELETE FROM catalog.attribute_value
                WHERE product_id = @productId AND attribute_id = @attributeId
                  AND locale_code = @locale
                """;

            await using var delete = new NpgsqlCommand(deleteSql, scope.Connection, scope.Transaction);
            delete.Parameters.AddWithValue("productId", scope.ProductId);
            delete.Parameters.AddWithValue("attributeId", attributeId);
            delete.Parameters.AddWithValue("locale", localeCode);
            await delete.ExecuteNonQueryAsync(cancellationToken);
            return null;
        }

        // ux_av keys on coalesce(locale_code,''), so a locale-scoped value and
        // the language-neutral one never collide.
        const string upsertSql = """
            INSERT INTO catalog.attribute_value
                (product_id, attribute_id, locale_code, value_text, data_state, source, updated_at)
            VALUES (@productId, @attributeId, @locale, @value, 'approved', 'manual', now())
            ON CONFLICT (coalesce(product_id,0), coalesce(sku_id,0), attribute_id,
                         coalesce(locale_code,''), coalesce(value_option_id,0))
            DO UPDATE SET value_text = EXCLUDED.value_text, updated_at = now()
            """;

        await using var upsert = new NpgsqlCommand(upsertSql, scope.Connection, scope.Transaction);
        upsert.Parameters.AddWithValue("productId", scope.ProductId);
        upsert.Parameters.AddWithValue("attributeId", attributeId);
        upsert.Parameters.AddWithValue("locale", localeCode);
        upsert.Parameters.AddWithValue("value", value);
        await upsert.ExecuteNonQueryAsync(cancellationToken);

        return null;
    }

    private static async Task SetStatusAsync(
        ProductWriteScope.Scope scope,
        UpdateProductLocalizationCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE catalog.product_tr
            SET translation_status = @status, updated_at = now()
            WHERE product_id = @productId AND locale_code = @locale
            """;

        await using var update = new NpgsqlCommand(sql, scope.Connection, scope.Transaction);
        update.Parameters.AddWithValue("productId", scope.ProductId);
        update.Parameters.AddWithValue("locale", command.LocaleCode);
        update.Parameters.AddWithValue("status", command.TranslationStatus!);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> ReadBaseLocaleAsync(
        ProductWriteScope.Scope scope, CancellationToken cancellationToken)
    {
        const string sql = "SELECT base_locale FROM catalog.product WHERE id = @productId";

        await using var command = new NpgsqlCommand(sql, scope.Connection, scope.Transaction);
        command.Parameters.AddWithValue("productId", scope.ProductId);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> LocaleExistsAsync(
        ProductWriteScope.Scope scope, string localeCode, CancellationToken cancellationToken)
    {
        const string sql = "SELECT 1 FROM catalog.locale WHERE code = @code";

        await using var command = new NpgsqlCommand(sql, scope.Connection, scope.Transaction);
        command.Parameters.AddWithValue("code", localeCode);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> TranslationRowExistsAsync(
        ProductWriteScope.Scope scope, string localeCode, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1 FROM catalog.product_tr WHERE product_id = @productId AND locale_code = @locale
            """;

        await using var command = new NpgsqlCommand(sql, scope.Connection, scope.Transaction);
        command.Parameters.AddWithValue("productId", scope.ProductId);
        command.Parameters.AddWithValue("locale", localeCode);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private sealed record LocaleQueryRow(
        string Code,
        string LocaleName,
        string? ProductName,
        string? ShortDesc,
        string? LongDesc,
        string? Marketing,
        string? SeoTitle,
        string? SeoDesc,
        string Status,
        string? Source,
        DateTimeOffset? UpdatedAt);

    private sealed record LocalizedValueRow(string LocaleCode, string Code, string? ValueText);

    private sealed record AttributeLookupRow(long Id, bool IsLocalizable);
}

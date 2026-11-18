using Npgsql;
using NpgsqlTypes;
using Pim.Catalog.Application.Products.Compliance;
using Pim.Catalog.Domain.Compliance;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Infrastructure.Persistence;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Reads a product's compliance evidence and writes certificates back.
///
/// Expiry is classified in C# against a single reference date captured per
/// request, not in SQL against now(): a query that straddles midnight would
/// otherwise be able to classify two rows against different days, and the
/// banner count would disagree with the rows it counts.
/// </summary>
public sealed class ProductComplianceStore(CatalogDbContext dbContext, TimeProvider timeProvider)
    : IProductComplianceStore
{
    public Task<ProductComplianceDto?> GetAsync(
        string productCode,
        CancellationToken cancellationToken) =>
        ProductWriteScope.ReadAsync(
            dbContext,
            connection => ReadAsync(connection, productCode, cancellationToken),
            cancellationToken);

    public Task<Result<ProductComplianceDto>> AddAsync(
        AddComplianceDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return InTransactionAsync(
            command.Code,
            command.RowVersion,
            async (connection, transaction, productId) =>
            {
                if (!string.IsNullOrWhiteSpace(command.Reference) &&
                    await ReferenceExistsAsync(
                        connection, transaction, productId, command.Reference, null, cancellationToken))
                {
                    return ComplianceErrors.DuplicateReference(command.Reference);
                }

                const string sql = """
                    INSERT INTO catalog.compliance_document
                        (product_id, doc_type, reference, standard, authority, valid_from, valid_to, status)
                    VALUES (@productId, @docType, @reference, @standard, @authority, @validFrom, @validTo, 'valid')
                    """;

                await using var insert = new NpgsqlCommand(sql, connection, transaction);
                insert.Parameters.AddWithValue("productId", productId);
                insert.Parameters.AddWithValue("docType", command.DocType);
                AddNullableText(insert, "reference", command.Reference);
                AddNullableText(insert, "standard", command.Standard);
                AddNullableText(insert, "authority", command.Authority);
                AddNullableDate(insert, "validFrom", command.ValidFrom);
                AddNullableDate(insert, "validTo", command.ValidTo);
                await insert.ExecuteNonQueryAsync(cancellationToken);

                return null;
            },
            cancellationToken);
    }

    public Task<Result<ProductComplianceDto>> UpdateAsync(
        UpdateComplianceDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return InTransactionAsync(
            command.Code,
            command.RowVersion,
            async (connection, transaction, productId) =>
            {
                if (!string.IsNullOrWhiteSpace(command.Reference) &&
                    await ReferenceExistsAsync(
                        connection, transaction, productId, command.Reference,
                        command.DocumentId, cancellationToken))
                {
                    return ComplianceErrors.DuplicateReference(command.Reference);
                }

                const string sql = """
                    UPDATE catalog.compliance_document
                    SET doc_type = @docType, reference = @reference, standard = @standard,
                        authority = @authority, valid_from = @validFrom, valid_to = @validTo,
                        status = @status
                    WHERE id = @id AND product_id = @productId
                    """;

                await using var update = new NpgsqlCommand(sql, connection, transaction);
                update.Parameters.AddWithValue("id", command.DocumentId);
                update.Parameters.AddWithValue("productId", productId);
                update.Parameters.AddWithValue("docType", command.DocType);
                update.Parameters.AddWithValue("status", command.Status);
                AddNullableText(update, "reference", command.Reference);
                AddNullableText(update, "standard", command.Standard);
                AddNullableText(update, "authority", command.Authority);
                AddNullableDate(update, "validFrom", command.ValidFrom);
                AddNullableDate(update, "validTo", command.ValidTo);

                // product_id is in the predicate on purpose: an id belonging to a
                // different product must 404, not silently edit that product.
                var affected = await update.ExecuteNonQueryAsync(cancellationToken);
                return affected == 0 ? ComplianceErrors.NotFound(command.DocumentId) : null;
            },
            cancellationToken);
    }

    public Task<Result<ProductComplianceDto>> RemoveAsync(
        RemoveComplianceDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return InTransactionAsync(
            command.Code,
            command.RowVersion,
            async (connection, transaction, productId) =>
            {
                const string sql = """
                    DELETE FROM catalog.compliance_document
                    WHERE id = @id AND product_id = @productId
                    """;

                await using var delete = new NpgsqlCommand(sql, connection, transaction);
                delete.Parameters.AddWithValue("id", command.DocumentId);
                delete.Parameters.AddWithValue("productId", productId);

                var affected = await delete.ExecuteNonQueryAsync(cancellationToken);
                return affected == 0 ? ComplianceErrors.NotFound(command.DocumentId) : null;
            },
            cancellationToken);
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(
            name, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim());

    private static void AddNullableDate(NpgsqlCommand command, string name, DateOnly? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Date)
        {
            Value = value.HasValue ? value.Value : DBNull.Value,
        });

    private static async Task<bool> ReferenceExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long productId,
        string reference,
        long? excludingId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1 FROM catalog.compliance_document
            WHERE product_id = @productId AND reference = @reference
              AND (@excludingId::bigint IS NULL OR id <> @excludingId)
            LIMIT 1
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("productId", productId);
        command.Parameters.AddWithValue("reference", reference.Trim());
        command.Parameters.AddWithValue("excludingId", (object?)excludingId ?? DBNull.Value);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    /// <summary>
    /// Delegates the lock/compare/bump protocol to the shared scope so all five
    /// product-scoped stores cannot drift apart on the part that matters most.
    /// </summary>
    private Task<Result<ProductComplianceDto>> InTransactionAsync(
        string productCode,
        int expectedRowVersion,
        Func<NpgsqlConnection, NpgsqlTransaction, long, Task<Error?>> action,
        CancellationToken cancellationToken) =>
        ProductWriteScope.ExecuteAsync(
            dbContext,
            productCode,
            expectedRowVersion,
            scope => action(scope.Connection, scope.Transaction, scope.ProductId),
            connection => ReadAsync(connection, productCode, cancellationToken),
            cancellationToken);

    private async Task<ProductComplianceDto?> ReadAsync(
        NpgsqlConnection connection,
        string productCode,
        CancellationToken cancellationToken)
    {
        const string productSql = """
            SELECT id, row_version FROM catalog.product WHERE product_code = @code
            """;

        var header = await DapperGateway.QuerySingleOrDefaultOnAsync<ProductVersionRow>(
            connection, productSql, new { code = productCode }, cancellationToken);

        if (header is null)
        {
            return null;
        }

        var (productId, rowVersion) = (header.Id, header.RowVersion);

        // One reference date for the whole payload.
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var documents = await LoadDocumentsAsync(connection, productId, today, cancellationToken);
        var indicators = await LoadIndicatorsAsync(connection, productId, cancellationToken);
        var materials = await LoadMaterialsAsync(connection, productId, cancellationToken);
        var substances = await LoadSubstancesAsync(connection, productId, cancellationToken);
        var greenBuilding = await LoadGreenBuildingAsync(connection, productId, cancellationToken);

        // Withdrawn and superseded certificates are excluded from the counts:
        // a lapsed date on a document nobody relies on is not a renewal task.
        var live = documents
            .Where(d => string.Equals(d.Status, ComplianceStatus.Valid, StringComparison.Ordinal))
            .ToList();

        return new ProductComplianceDto(
            productCode,
            rowVersion,
            live.Count(d => string.Equals(d.ExpiryState, nameof(ExpiryState.Expiring), StringComparison.Ordinal)),
            live.Count(d => string.Equals(d.ExpiryState, nameof(ExpiryState.Expired), StringComparison.Ordinal)),
            documents,
            indicators,
            materials,
            substances,
            greenBuilding);
    }

    private static async Task<List<ComplianceDocumentDto>> LoadDocumentsAsync(
        NpgsqlConnection connection,
        long productId,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, doc_type, reference, standard, authority, valid_from, valid_to, status
            FROM catalog.compliance_document
            WHERE product_id = @productId
            ORDER BY doc_type, valid_to NULLS LAST, id
            """;

        var rows = await DapperGateway.QueryOnAsync<DocumentRow>(
            connection, sql, new { productId }, cancellationToken);

        // ExpiryState is DERIVED, never stored: one reference date for every
        // row, so the banner above the table can never disagree with it.
        return rows
            .Select(r => new ComplianceDocumentDto(
                r.Id, r.DocType, r.Reference, r.Standard, r.Authority,
                r.ValidFrom, r.ValidTo, r.Status,
                ComplianceExpiry.Classify(r.ValidTo, today).ToString()))
            .ToList();
    }

    private static async Task<List<SustainabilityIndicatorDto>> LoadIndicatorsAsync(
        NpgsqlConnection connection,
        long productId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT indicator, value, unit, method, stage
            FROM catalog.sustainability_indicator
            WHERE product_id = @productId
            ORDER BY indicator
            """;

        // Column-for-column with the DTO, so Dapper maps it directly:
        // no intermediate row type earns its keep here.
        var rows = await DapperGateway.QueryOnAsync<SustainabilityIndicatorDto>(
            connection, sql, new { productId }, cancellationToken);

        return rows.ToList();
    }

    private static async Task<List<MaterialCompositionDto>> LoadMaterialsAsync(
        NpgsqlConnection connection,
        long productId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT material, pct, is_recyclable, is_hazardous
            FROM catalog.material_composition
            WHERE product_id = @productId
            ORDER BY sort_order, material
            """;

        // Column-for-column with the DTO, so Dapper maps it directly:
        // no intermediate row type earns its keep here.
        var rows = await DapperGateway.QueryOnAsync<MaterialCompositionDto>(
            connection, sql, new { productId }, cancellationToken);

        return rows.ToList();
    }

    private static async Task<List<SubstanceComplianceDto>> LoadSubstancesAsync(
        NpgsqlConnection connection,
        long productId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT regime, result, threshold, notes
            FROM catalog.substance_compliance
            WHERE product_id = @productId
            ORDER BY regime
            """;

        // Column-for-column with the DTO, so Dapper maps it directly:
        // no intermediate row type earns its keep here.
        var rows = await DapperGateway.QueryOnAsync<SubstanceComplianceDto>(
            connection, sql, new { productId }, cancellationToken);

        return rows.ToList();
    }

    private static async Task<List<GreenBuildingContributionDto>> LoadGreenBuildingAsync(
        NpgsqlConnection connection,
        long productId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT scheme, credit, note
            FROM catalog.green_building_contribution
            WHERE product_id = @productId
            ORDER BY scheme, credit
            """;

        // Column-for-column with the DTO, so Dapper maps it directly:
        // no intermediate row type earns its keep here.
        var rows = await DapperGateway.QueryOnAsync<GreenBuildingContributionDto>(
            connection, sql, new { productId }, cancellationToken);

        return rows.ToList();
    }

    private sealed record ProductVersionRow(long Id, int RowVersion);

    /// <summary>
    /// A certificate as stored. ExpiryState is absent on purpose: it is
    /// computed from ValidTo against one reference date, and a stored copy
    /// would go stale the moment the date passed.
    /// </summary>
    private sealed record DocumentRow(
        long Id,
        string DocType,
        string? Reference,
        string? Standard,
        string? Authority,
        DateOnly? ValidFrom,
        DateOnly? ValidTo,
        string Status);
}

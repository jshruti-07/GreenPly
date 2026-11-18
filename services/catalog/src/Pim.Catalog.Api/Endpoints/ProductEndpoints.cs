using MediatR;
using Pim.Catalog.Application.Attributes;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Application.Products.Assets;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Products;
using Pim.Catalog.Application.Products.ChangeLifecycleState;
using Pim.Catalog.Application.Products.ChangeWorkflowState;
using Pim.Catalog.Application.Products.Classifications;
using Pim.Catalog.Application.Products.Compliance;
using Pim.Catalog.Application.Products.CreateProduct;
using Pim.Catalog.Application.Products.ExportProducts;
using Pim.Catalog.Application.Products.GetProductByCode;
using Pim.Catalog.Application.Products.ListProducts;
using Pim.Catalog.Application.Products.Localization;
using Pim.Catalog.Application.Products.Logistics;
using Pim.Catalog.Application.Products.Planning;
using Pim.Catalog.Application.Products.UpdateProductGeneral;
using Pim.Catalog.Application.Products.Variants;

namespace Pim.Api.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products").WithTags("Products");

        // "" (not "/") so the pattern is exactly /api/v1/products.
        group.MapGet("", async (
            ISender sender,
            CancellationToken ct,
            int page = 1,
            int pageSize = 25,
            string sortBy = ListProductsQuery.DefaultSortBy,
            string sortDir = ListProductsQuery.DefaultSortDir,
            string? status = null,
            long? categoryId = null,
            string? search = null,
            string? locale = null) =>
        {
            var query = new ListProductsQuery(page, pageSize, sortBy, sortDir, status, categoryId, search, locale);
            var result = await sender.Send(query, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<PagedResult<ProductListItemDto>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("Paged product list with sorting, workflow-status / category / free-text filters. " +
                     "`locale` selects the display language; omitted it defaults to English. " +
                     "Rows carry nameLocale/categoryLocale so a fallback to the base locale is visible.");

        group.MapPost("/", async (CreateProductCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/products/{result.Value.Code}", result.Value)
                : result.ToProblem();
        })
        .Produces<ProductDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/export.csv", async (
            ISender sender,
            CancellationToken ct,
            string sortBy = ListProductsQuery.DefaultSortBy,
            string sortDir = ListProductsQuery.DefaultSortDir,
            string? status = null,
            long? categoryId = null,
            string? search = null,
            string? codes = null,
            string? locale = null) =>
        {
            // Selection arrives as a comma-separated code list; product codes
            // never contain commas (ProductCode format rule).
            var codeList = string.IsNullOrWhiteSpace(codes)
                ? null
                : codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var query = new ExportProductsQuery(sortBy, sortDir, status, categoryId, search, codeList, locale);
            var result = await sender.Send(query, ct);

            if (result.IsFailure)
            {
                return result.ToProblem();
            }

            var fileName = $"products-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(result.Value),
                ProductCsv.ContentType,
                fileName);
        })
        .Produces<string>(StatusCodes.Status200OK, ProductCsv.ContentType)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("CSV export of the grid: current filters, or an explicit selection via ?codes=A,B,C.");

        group.MapGet("/{code}", async (string code, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductByCodeQuery(code), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{code}", async (
            string code,
            UpdateProductGeneralRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            var command = new UpdateProductGeneralCommand(
                code, request.Name, request.OwnerDisplayName, request.Gtin,
                request.SapMaterialNo, request.RowVersion);

            var result = await sender.Send(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Updates the General tab fields. Send the rowVersion you loaded; a mismatch returns 409.");

        group.MapPost("/{code}/workflow", async (
            string code,
            ChangeWorkflowRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ChangeWorkflowStateCommand(code, request.TargetState, request.RowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Advances or rejects the product in the editorial workflow.");

        // `locale` is bound here for the same reason the grid binds it: the
        // query has carried the parameter since ADR-009 and the handler already
        // resolves it, but this endpoint never passed one — so the tab was
        // pinned to the default language whatever the shell was set to. A
        // half-wired parameter reads as a deliberate decision from the outside;
        // it was an omission (Sprint 17).
        group.MapGet("/{code}/attributes", async (
            string code,
            string? locale,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductAttributesQuery(code, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductAttributesDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Attribute template for the product's attribute set, with current values.");

        group.MapPut("/{code}/attributes", async (
            string code,
            string? locale,
            UpdateAttributesRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            // The refreshed payload comes back in the locale the form was being
            // edited in — a save must not silently change the language of the
            // screen the user is looking at.
            var command = new UpdateProductAttributesCommand(
                code, request.Values, request.RowVersion, locale);
            var result = await sender.Send(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductAttributesDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Saves attribute values. All-or-nothing: one invalid value rejects the whole save.");

        group.MapGet("/{code}/variants", async (string code, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductVariantsQuery(code), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductVariantsDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Variants (SKUs) of the product, in display order.");

        group.MapPost("/{code}/variants", async (
            string code, AddSkuRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new AddSkuCommand(code, request.Code, request.Gtin, request.ErpMaterialNo, request.RowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductVariantsDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Adds a variant. Returns the refreshed variant list.");

        group.MapPut("/{code}/variants/{skuCode}", async (
            string code, string skuCode, UpdateSkuRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateSkuCommand(code, skuCode, request.Gtin, request.ErpMaterialNo, request.Status, request.RowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductVariantsDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Updates a variant's commercial fields. The SKU code is immutable.");

        group.MapDelete("/{code}/variants/{skuCode}", async (
            string code, string skuCode, ISender sender, CancellationToken ct, int rowVersion = 0) =>
        {
            var result = await sender.Send(new RemoveSkuCommand(code, skuCode, rowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductVariantsDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Removes a variant. Retiring a distributed SKU should instead set status=EndOfLife.");

        group.MapGet("/{code}/classifications", async (string code, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductClassificationsQuery(code), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductClassificationsDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Every configured classification scheme, with this product's mapping and feature completeness.");

        group.MapPut("/{code}/classifications/{schemeCode}", async (
            string code, string schemeCode, AssignClassRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new AssignClassificationCommand(code, schemeCode, request.ClassCode, request.RowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductClassificationsDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Assigns the product's class in one scheme. A null classCode clears the mapping. Feature values the new class does not declare are discarded.");

        group.MapPut("/{code}/classifications/{schemeCode}/features", async (
            string code, string schemeCode, UpdateFeaturesRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateClassificationFeaturesCommand(code, schemeCode, request.Values, request.RowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductClassificationsDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Saves feature values for the assigned class. All-or-nothing: one invalid value rejects the whole save.");

        group.MapGet("/{code}/compliance", async (string code, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductComplianceQuery(code), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductComplianceDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Certifications with server-derived expiry state, plus sustainability evidence.");

        group.MapPost("/{code}/compliance", async (
            string code, AddComplianceRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AddComplianceDocumentCommand(
                code, request.DocType, request.Reference, request.Standard, request.Authority,
                request.ValidFrom, request.ValidTo, request.RowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductComplianceDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Records a certificate against the product.");

        group.MapPut("/{code}/compliance/{documentId:long}", async (
            string code, long documentId, UpdateComplianceRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdateComplianceDocumentCommand(
                code, documentId, request.DocType, request.Reference, request.Standard, request.Authority,
                request.ValidFrom, request.ValidTo, request.Status, request.RowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductComplianceDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Updates a certificate. Withdraw or supersede rather than delete when the document was ever relied on.");

        group.MapDelete("/{code}/compliance/{documentId:long}", async (
            string code, long documentId, ISender sender, CancellationToken ct, int rowVersion = 0) =>
        {
            var result = await sender.Send(
                new RemoveComplianceDocumentCommand(code, documentId, rowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductComplianceDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Deletes a certificate recorded in error.");

        group.MapGet("/{code}/packaging", async (string code, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductPackagingQuery(code), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductPackagingDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Packaging hierarchy per SKU, innermost level first.");

        group.MapPost("/{code}/packaging", async (
            string code, AddPackagingRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AddPackagingUnitCommand(
                code, request.SkuCode, request.Level, request.ParentLevel, request.ContainsQty,
                request.Gtin, request.Sscc, request.GrossWeight, request.NetWeight,
                request.LengthMm, request.WidthMm, request.HeightMm,
                request.IsBaseUnit, request.DangerousGoods, request.UnClass, request.RowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductPackagingDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Adds a packaging level to a SKU. A level may only be contained by a larger one.");

        group.MapPut("/{code}/packaging/{unitId:long}", async (
            string code, long unitId, UpdatePackagingRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdatePackagingUnitCommand(
                code, unitId, request.ContainsQty, request.Gtin, request.Sscc,
                request.GrossWeight, request.NetWeight, request.LengthMm, request.WidthMm,
                request.HeightMm, request.IsBaseUnit, request.DangerousGoods,
                request.UnClass, request.RowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductPackagingDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Updates a packaging level. The level itself is immutable; remove and re-add to change it.");

        group.MapDelete("/{code}/packaging/{unitId:long}", async (
            string code, long unitId, ISender sender, CancellationToken ct, int rowVersion = 0) =>
        {
            var result = await sender.Send(new RemovePackagingUnitCommand(code, unitId, rowVersion), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductPackagingDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Removes a packaging level and everything nested inside it.");

        group.MapGet("/{code}/relationships", async (
            string code, string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductRelationshipsQuery(code, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductRelationshipsDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Links to other products, grouped by type. Every type is returned, empty ones included.");

        group.MapPost("/{code}/relationships", async (
            string code, string? locale, AddRelationshipRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AddRelationshipCommand(
                code, request.RelationType, request.TargetCode, request.Priority,
                request.RowVersion, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductRelationshipsDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Links another product. Links are one-directional and are not mirrored automatically.");

        group.MapDelete("/{code}/relationships/{relationType}/{targetCode}", async (
            string code, string relationType, string targetCode, string? locale,
            ISender sender, CancellationToken ct, int rowVersion = 0) =>
        {
            var result = await sender.Send(
                new RemoveRelationshipCommand(code, relationType, targetCode, rowVersion, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductRelationshipsDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Removes one link.");

        group.MapGet("/{code}/applications", async (
            string code, string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductApplicationsQuery(code, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductApplicationsDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Every application option grouped by kind, each flagged as assigned or not.");

        group.MapPut("/{code}/applications", async (
            string code, string? locale, SetApplicationsRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SetProductApplicationsCommand(
                    code, request.ApplicationCodes, request.RowVersion, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductApplicationsDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Replaces the whole assignment set. Sending an empty list clears it.");

        group.MapGet("/{code}/activity", async (
            string code,
            ISender sender,
            CancellationToken ct,
            int page = 1,
            int pageSize = 25,
            string? action = null,
            string? search = null) =>
        {
            var result = await sender.Send(
                new GetProductActivityQuery(code, page, pageSize, action, search), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductActivityDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Audit history for the product, newest first. Actor is null until authentication ships.");

        // `locale` here is not only presentation: on the write endpoints below
        // it is the locale asset_tr rows are written to (R-18). Read and write
        // deliberately take it from the same place.
        group.MapGet("/{code}/assets", async (
            string code, string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductAssetsQuery(code, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductAssetsDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Images and documents linked to the product, split by whether they can be rendered.");

        group.MapPost("/{code}/assets", async (
            string code, string? locale, AddAssetRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AddProductAssetCommand(
                code, request.StorageKey, request.Role, request.AssetType,
                request.Title, request.AltText, request.RowVersion, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductAssetsDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Registers a file and links it in one call. A key already registered is reused, not duplicated.");

        group.MapPut("/{code}/assets/{linkId:long}", async (
            string code, long linkId, string? locale, UpdateAssetRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdateProductAssetCommand(
                code, linkId, request.Role, request.Title, request.AltText,
                request.SortOrder, request.RowVersion, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductAssetsDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Updates an asset link's role, order and captions.");

        group.MapPost("/{code}/assets/{linkId:long}/primary", async (
            string code, long linkId, string? locale, SetPrimaryRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SetPrimaryAssetCommand(code, linkId, request.RowVersion, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductAssetsDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Promotes an asset to primary for its role, demoting the previous one.");

        group.MapDelete("/{code}/assets/{linkId:long}", async (
            string code, long linkId, string? locale,
            ISender sender, CancellationToken ct, int rowVersion = 0) =>
        {
            var result = await sender.Send(
                new RemoveProductAssetCommand(code, linkId, rowVersion, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductAssetsDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Unlinks the asset from this product. The asset itself survives; it may be shared.");

        group.MapGet("/{code}/planning", async (
            string code, string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductPlanningQuery(code, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductPlanningDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary(
            "Planning and BIM view: planning parameters, BIM classification references and geometry files. " +
            "Read-only — each part is written through Attributes, Classifications or Media & Documents.");

        group.MapGet("/{code}/localization", async (
            string code, string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductLocalizationQuery(code, locale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductLocalizationDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Every configured locale for the product, base locale first, with per-field translations.");

        // NOTE the parameter name. Everywhere else the display locale arrives as
        // ?locale=, but here {locale} is already the route segment naming the
        // language being EDITED. Two different locales in one request need two
        // different names, so the display one is ?displayLocale=.
        group.MapPut("/{code}/localization/{locale}", async (
            string code, string locale, string? displayLocale, UpdateLocalizationRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdateProductLocalizationCommand(
                code, locale, request.Values, request.TranslationStatus,
                request.RowVersion, displayLocale), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductLocalizationDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary(
            "Saves one locale's translations. The base locale is rejected: its text is edited " +
            "on General and Attributes.");

        group.MapPost("/{code}/lifecycle", async (
            string code,
            ChangeLifecycleRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new ChangeLifecycleStateCommand(code, request.TargetState), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
        })
        .Produces<ProductDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }

    public sealed record ChangeLifecycleRequest(string TargetState);

    public sealed record UpdateLocalizationRequest(
        IReadOnlyList<TranslationValueInput> Values,
        string? TranslationStatus,
        int RowVersion);

    public sealed record ChangeWorkflowRequest(string TargetState, int RowVersion);

    public sealed record AddSkuRequest(string Code, string? Gtin, string? ErpMaterialNo, int RowVersion);

    public sealed record UpdateSkuRequest(string? Gtin, string? ErpMaterialNo, string Status, int RowVersion);

    public sealed record AddAssetRequest(
        string StorageKey,
        string Role,
        string? AssetType,
        string? Title,
        string? AltText,
        int RowVersion);

    public sealed record UpdateAssetRequest(
        string Role,
        string? Title,
        string? AltText,
        int SortOrder,
        int RowVersion);

    public sealed record SetPrimaryRequest(int RowVersion);

    public sealed record AddPackagingRequest(
        string SkuCode,
        string Level,
        string? ParentLevel,
        decimal ContainsQty,
        string? Gtin,
        string? Sscc,
        decimal? GrossWeight,
        decimal? NetWeight,
        decimal? LengthMm,
        decimal? WidthMm,
        decimal? HeightMm,
        bool IsBaseUnit,
        bool DangerousGoods,
        string? UnClass,
        int RowVersion);

    public sealed record UpdatePackagingRequest(
        decimal ContainsQty,
        string? Gtin,
        string? Sscc,
        decimal? GrossWeight,
        decimal? NetWeight,
        decimal? LengthMm,
        decimal? WidthMm,
        decimal? HeightMm,
        bool IsBaseUnit,
        bool DangerousGoods,
        string? UnClass,
        int RowVersion);

    public sealed record AddRelationshipRequest(
        string RelationType,
        string TargetCode,
        int Priority,
        int RowVersion);

    public sealed record SetApplicationsRequest(
        IReadOnlyList<string> ApplicationCodes,
        int RowVersion);

    public sealed record AssignClassRequest(string? ClassCode, int RowVersion);

    public sealed record UpdateFeaturesRequest(
        IReadOnlyList<ClassificationFeatureInput> Values,
        int RowVersion);

    public sealed record AddComplianceRequest(
        string DocType,
        string? Reference,
        string? Standard,
        string? Authority,
        DateOnly? ValidFrom,
        DateOnly? ValidTo,
        int RowVersion);

    public sealed record UpdateComplianceRequest(
        string DocType,
        string? Reference,
        string? Standard,
        string? Authority,
        DateOnly? ValidFrom,
        DateOnly? ValidTo,
        string Status,
        int RowVersion);

    public sealed record UpdateAttributesRequest(
        IReadOnlyList<AttributeValueInput> Values,
        int RowVersion);

    public sealed record UpdateProductGeneralRequest(
        string Name,
        string? OwnerDisplayName,
        string? Gtin,
        string? SapMaterialNo,
        int RowVersion);
}

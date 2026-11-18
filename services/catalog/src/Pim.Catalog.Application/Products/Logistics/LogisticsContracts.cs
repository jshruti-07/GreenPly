using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Packaging;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Logistics;

// ---------------------------------------------------------------------------
// Packaging
// ---------------------------------------------------------------------------

/// <summary>
/// One packaging level of one SKU. <paramref name="ParentLevel"/> is the level
/// that contains it, resolved to a name so the client never has to join ids.
/// </summary>
public sealed record PackagingUnitDto(
    long Id,
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
    string? UnClass);

/// <summary>Packaging is per SKU, so the payload is grouped by SKU.</summary>
public sealed record SkuPackagingDto(string SkuCode, IReadOnlyList<PackagingUnitDto> Units);

public sealed record ProductPackagingDto(
    string ProductCode,
    int RowVersion,
    IReadOnlyList<SkuPackagingDto> Skus);

public sealed record GetProductPackagingQuery(string Code) : IRequest<Result<ProductPackagingDto>>;

public sealed record AddPackagingUnitCommand(
    string Code,
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
    int RowVersion) : IRequest<Result<ProductPackagingDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "created";

    string IAuditableCommand.AuditDetail => $"{Level} packaging added to {SkuCode}";
}

public sealed record UpdatePackagingUnitCommand(
    string Code,
    long UnitId,
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
    int RowVersion) : IRequest<Result<ProductPackagingDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "updated";

    string IAuditableCommand.AuditDetail => $"Packaging unit {UnitId}";
}

public sealed record RemovePackagingUnitCommand(string Code, long UnitId, int RowVersion)
    : IRequest<Result<ProductPackagingDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "deleted";

    string IAuditableCommand.AuditDetail => $"Packaging unit {UnitId} removed";
}

public interface IProductPackagingStore
{
    Task<ProductPackagingDto?> GetAsync(string productCode, CancellationToken cancellationToken);

    Task<Result<ProductPackagingDto>> AddAsync(
        AddPackagingUnitCommand command, CancellationToken cancellationToken);

    Task<Result<ProductPackagingDto>> UpdateAsync(
        UpdatePackagingUnitCommand command, CancellationToken cancellationToken);

    Task<Result<ProductPackagingDto>> RemoveAsync(
        RemovePackagingUnitCommand command, CancellationToken cancellationToken);
}

// ---------------------------------------------------------------------------
// Relationships
// ---------------------------------------------------------------------------

/// <summary>
/// A link to another product. The target's name and workflow state come along
/// so the tab can render a meaningful card without a request per row.
/// </summary>
public sealed record ProductRelationshipDto(
    string RelationType,
    string TargetCode,
    string? TargetName,
    string TargetWorkflowState,
    int Priority);

public sealed record RelationshipGroupDto(
    string RelationType,
    IReadOnlyList<ProductRelationshipDto> Items);

public sealed record ProductRelationshipsDto(
    string ProductCode,
    int RowVersion,
    IReadOnlyList<RelationshipGroupDto> Groups);

public sealed record GetProductRelationshipsQuery(string Code, string? Locale = null)
    : IRequest<Result<ProductRelationshipsDto>>;

public sealed record AddRelationshipCommand(
    string Code,
    string RelationType,
    string TargetCode,
    int Priority,
    int RowVersion,
    /// <summary>Locale rendered in the refreshed payload; a save must not change the
    /// language of the screen the user is looking at.</summary>
    string? Locale = null) : IRequest<Result<ProductRelationshipsDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "linked";

    string IAuditableCommand.AuditDetail => $"{RelationType} link to {TargetCode} added";
}

public sealed record RemoveRelationshipCommand(
    string Code,
    string RelationType,
    string TargetCode,
    int RowVersion,
    /// <summary>Locale rendered in the refreshed payload; a save must not change the
    /// language of the screen the user is looking at.</summary>
    string? Locale = null) : IRequest<Result<ProductRelationshipsDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "unlinked";

    string IAuditableCommand.AuditDetail => $"{RelationType} link to {TargetCode} removed";
}

public interface IProductRelationshipStore
{
    Task<ProductRelationshipsDto?> GetAsync(
        string productCode, DisplayLocale locale, CancellationToken cancellationToken);

    Task<Result<ProductRelationshipsDto>> AddAsync(
        AddRelationshipCommand command, DisplayLocale locale, CancellationToken cancellationToken);

    Task<Result<ProductRelationshipsDto>> RemoveAsync(
        RemoveRelationshipCommand command, DisplayLocale locale, CancellationToken cancellationToken);
}

// ---------------------------------------------------------------------------
// Applications
// ---------------------------------------------------------------------------

public sealed record ApplicationOptionDto(string Code, string Label, bool IsAssigned);

public sealed record ApplicationGroupDto(
    string GroupCode,
    string GroupLabel,
    IReadOnlyList<ApplicationOptionDto> Options);

public sealed record ProductApplicationsDto(
    string ProductCode,
    int RowVersion,
    IReadOnlyList<ApplicationGroupDto> Groups);

public sealed record GetProductApplicationsQuery(string Code, string? Locale = null)
    : IRequest<Result<ProductApplicationsDto>>;

/// <summary>
/// Replaces the whole assignment set. A set-valued field is far less
/// error-prone to save wholesale than as a stream of add/remove deltas, which
/// can interleave and leave the set in a state neither client intended.
/// </summary>
public sealed record SetProductApplicationsCommand(
    string Code,
    IReadOnlyList<string> ApplicationCodes,
    int RowVersion,
    /// <summary>Locale rendered in the refreshed payload; a save must not change the
    /// language of the screen the user is looking at.</summary>
    string? Locale = null) : IRequest<Result<ProductApplicationsDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "updated";

    string IAuditableCommand.AuditDetail => $"Applications set ({ApplicationCodes.Count})";
}

public interface IProductApplicationStore
{
    Task<ProductApplicationsDto?> GetAsync(
        string productCode, DisplayLocale locale, CancellationToken cancellationToken);

    Task<Result<ProductApplicationsDto>> SetAsync(
        SetProductApplicationsCommand command, DisplayLocale locale, CancellationToken cancellationToken);
}

// ---------------------------------------------------------------------------
// Validators
// ---------------------------------------------------------------------------

public sealed class AddPackagingUnitCommandValidator : AbstractValidator<AddPackagingUnitCommand>
{
    public AddPackagingUnitCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.SkuCode).NotEmpty();
        RuleFor(x => x.RowVersion).GreaterThan(0);
        RuleFor(x => x.ContainsQty).GreaterThan(0).WithMessage("Contained quantity must be greater than zero.");
        RuleFor(x => x.UnClass).MaximumLength(32);

        RuleFor(x => x.Level)
            .NotEmpty()
            .Must(PackagingLevel.IsKnown)
            .WithMessage($"Level must be one of: {string.Join(", ", PackagingLevel.Ordered)}.");
    }
}

public sealed class UpdatePackagingUnitCommandValidator : AbstractValidator<UpdatePackagingUnitCommand>
{
    public UpdatePackagingUnitCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.UnitId).GreaterThan(0);
        RuleFor(x => x.RowVersion).GreaterThan(0);
        RuleFor(x => x.ContainsQty).GreaterThan(0).WithMessage("Contained quantity must be greater than zero.");
        RuleFor(x => x.UnClass).MaximumLength(32);
    }
}

public sealed class RemovePackagingUnitCommandValidator : AbstractValidator<RemovePackagingUnitCommand>
{
    public RemovePackagingUnitCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.UnitId).GreaterThan(0);
        RuleFor(x => x.RowVersion).GreaterThan(0);
    }
}

public sealed class AddRelationshipCommandValidator : AbstractValidator<AddRelationshipCommand>
{
    public AddRelationshipCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.TargetCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.RowVersion).GreaterThan(0);
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);

        RuleFor(x => x.RelationType)
            .NotEmpty()
            .Must(RelationType.IsKnown)
            .WithMessage($"Relationship type must be one of: {string.Join(", ", RelationType.All)}.");
    }
}

public sealed class RemoveRelationshipCommandValidator : AbstractValidator<RemoveRelationshipCommand>
{
    public RemoveRelationshipCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.TargetCode).NotEmpty();
        RuleFor(x => x.RowVersion).GreaterThan(0);
        RuleFor(x => x.RelationType).NotEmpty().Must(RelationType.IsKnown);
    }
}

public sealed class SetProductApplicationsCommandValidator
    : AbstractValidator<SetProductApplicationsCommand>
{
    /// <summary>Bounds one request; the reference set is tens of entries, not thousands.</summary>
    public const int MaxApplications = 200;

    public SetProductApplicationsCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.RowVersion).GreaterThan(0);

        RuleFor(x => x.ApplicationCodes)
            .NotNull()
            .Must(c => c.Count <= MaxApplications)
            .WithMessage($"At most {MaxApplications} applications can be assigned.");
    }
}

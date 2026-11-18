using FluentValidation;
using Pim.Catalog.Application.Common;
using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Domain.Assets;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Assets;

/// <summary>
/// One asset as linked to this product.
///
/// <paramref name="Url"/> is what the client renders. It is the web rendition
/// when one exists, otherwise the storage key — resolved server-side so the UI
/// never has to know the difference between a hosted file and an external one.
/// </summary>
public sealed record ProductAssetDto(
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
    int SortOrder,
    bool IsExternal);

/// <summary>
/// Assets split the way the tab presents them: pictures in a gallery, files in
/// a list. The split is by asset TYPE, not by role, because that is what
/// decides whether a thumbnail can be rendered at all.
/// </summary>
public sealed record ProductAssetsDto(
    string ProductCode,
    int RowVersion,
    IReadOnlyList<ProductAssetDto> Media,
    IReadOnlyList<ProductAssetDto> Documents);

public sealed record GetProductAssetsQuery(string Code, string? Locale = null)
    : IRequest<Result<ProductAssetsDto>>;

/// <summary>
/// Registers a file and links it in one call.
///
/// Deliberately combined: a two-step "create asset, then link it" API lets a
/// steward create an orphan asset that nothing references and nobody can find.
/// Re-registering a key that already exists reuses that asset rather than
/// duplicating the row.
/// </summary>
public sealed record AddProductAssetCommand(
    string Code,
    string StorageKey,
    string Role,
    string? AssetType,
    string? Title,
    string? AltText,
    int RowVersion,
    /// <summary>
    /// Locale of the refreshed payload and — for title and alt text — the
    /// locale they are WRITTEN to. Read and write use the same one by design
    /// (R-18); splitting them is what made an English edit land on the German
    /// row and disappear.
    /// </summary>
    string? Locale = null) : IRequest<Result<ProductAssetsDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "linked";

    string IAuditableCommand.AuditDetail =>
        $"{Role} asset {AssetRules.FileNameFrom(StorageKey)} linked";
}

public sealed record UpdateProductAssetCommand(
    string Code,
    long LinkId,
    string Role,
    string? Title,
    string? AltText,
    int SortOrder,
    int RowVersion,
    /// <summary>
    /// Locale of the refreshed payload and — for title and alt text — the
    /// locale they are WRITTEN to. Read and write use the same one by design
    /// (R-18); splitting them is what made an English edit land on the German
    /// row and disappear.
    /// </summary>
    string? Locale = null) : IRequest<Result<ProductAssetsDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "updated";

    string IAuditableCommand.AuditDetail => $"Asset link {LinkId} ({Role})";
}

/// <summary>Promotes one asset to primary for its role, demoting any other.</summary>
public sealed record SetPrimaryAssetCommand(
    string Code, long LinkId, int RowVersion, string? Locale = null)
    : IRequest<Result<ProductAssetsDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "updated";

    string IAuditableCommand.AuditDetail => $"Asset link {LinkId} set as primary";
}

/// <summary>
/// Unlinks from this product. The asset row itself survives: the same file is
/// routinely shared across a series, and deleting it here would silently strip
/// it from other products.
/// </summary>
public sealed record RemoveProductAssetCommand(
    string Code, long LinkId, int RowVersion, string? Locale = null)
    : IRequest<Result<ProductAssetsDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "unlinked";

    string IAuditableCommand.AuditDetail => $"Asset link {LinkId} removed";
}

public interface IProductAssetStore
{
    Task<ProductAssetsDto?> GetAsync(
        string productCode, DisplayLocale locale, CancellationToken cancellationToken);

    Task<Result<ProductAssetsDto>> AddAsync(
        AddProductAssetCommand command, DisplayLocale locale, CancellationToken cancellationToken);

    Task<Result<ProductAssetsDto>> UpdateAsync(
        UpdateProductAssetCommand command, DisplayLocale locale, CancellationToken cancellationToken);

    Task<Result<ProductAssetsDto>> SetPrimaryAsync(
        SetPrimaryAssetCommand command, DisplayLocale locale, CancellationToken cancellationToken);

    Task<Result<ProductAssetsDto>> RemoveAsync(
        RemoveProductAssetCommand command, DisplayLocale locale, CancellationToken cancellationToken);
}

public sealed class AddProductAssetCommandValidator : AbstractValidator<AddProductAssetCommand>
{
    public const int MaxTitleLength = 250;

    public AddProductAssetCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.RowVersion).GreaterThan(0);

        RuleFor(x => x.StorageKey)
            .NotEmpty()
            .MaximumLength(AssetRules.MaxStorageKeyLength);

        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(AssetRole.IsKnown)
            .WithMessage($"Role must be one of: {string.Join(", ", AssetRole.All)}.");

        RuleFor(x => x.AssetType)
            .Must(t => t is null || AssetType.IsKnown(t))
            .WithMessage($"Asset type must be one of: {string.Join(", ", AssetType.All)}.");

        RuleFor(x => x.Title).MaximumLength(MaxTitleLength);
        RuleFor(x => x.AltText).MaximumLength(MaxTitleLength);
    }
}

public sealed class UpdateProductAssetCommandValidator : AbstractValidator<UpdateProductAssetCommand>
{
    public UpdateProductAssetCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.LinkId).GreaterThan(0);
        RuleFor(x => x.RowVersion).GreaterThan(0);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);

        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(AssetRole.IsKnown)
            .WithMessage($"Role must be one of: {string.Join(", ", AssetRole.All)}.");

        RuleFor(x => x.Title).MaximumLength(AddProductAssetCommandValidator.MaxTitleLength);
        RuleFor(x => x.AltText).MaximumLength(AddProductAssetCommandValidator.MaxTitleLength);
    }
}

public sealed class SetPrimaryAssetCommandValidator : AbstractValidator<SetPrimaryAssetCommand>
{
    public SetPrimaryAssetCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.LinkId).GreaterThan(0);
        RuleFor(x => x.RowVersion).GreaterThan(0);
    }
}

public sealed class RemoveProductAssetCommandValidator : AbstractValidator<RemoveProductAssetCommand>
{
    public RemoveProductAssetCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.LinkId).GreaterThan(0);
        RuleFor(x => x.RowVersion).GreaterThan(0);
    }
}

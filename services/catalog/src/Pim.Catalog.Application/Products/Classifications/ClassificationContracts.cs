using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Classifications;

/// <summary>A class a product can be mapped into, within one scheme.</summary>
public sealed record ClassificationClassDto(string Code, string? Label);

/// <summary>
/// One feature of the assigned class, with the product's value rendered to its
/// canonical string form. <paramref name="Value"/> is null when unfilled — the
/// UI shows those as missing and they are what FeatureFilled excludes.
/// </summary>
public sealed record ClassificationFeatureDto(
    string Code,
    string? Name,
    string DataType,
    string? Unit,
    bool IsRequired,
    string? Value);

/// <summary>
/// A product's mapping into one scheme.
///
/// FeatureCount/FeatureFilled are computed from real rows rather than stored,
/// so the "12 / 14 features" figure cannot drift away from the values beneath
/// it. Schemes whose class declares no features report 0/0 and the UI shows a
/// dash — that is the DataNorm/UNSPSC case, where the class assignment *is*
/// the whole mapping.
/// </summary>
public sealed record ProductClassificationDto(
    string SchemeCode,
    string SchemeName,
    string SchemeVersion,
    string? Purpose,
    bool IsPrimary,
    string MappedStatus,
    string? ClassCode,
    string? ClassLabel,
    int FeatureCount,
    int FeatureFilled,
    IReadOnlyList<ClassificationClassDto> AvailableClasses,
    IReadOnlyList<ClassificationFeatureDto> Features);

/// <summary>The full Classifications tab payload.</summary>
public sealed record ProductClassificationsDto(
    string ProductCode,
    int RowVersion,
    IReadOnlyList<ProductClassificationDto> Schemes);

public sealed record GetProductClassificationsQuery(string Code)
    : IRequest<Result<ProductClassificationsDto>>;

/// <summary>
/// Assigns (or clears, with a null ClassCode) the product's class in one
/// scheme. Changing the class discards feature values that the new class does
/// not declare — they are meaningless outside their class.
/// </summary>
public sealed record AssignClassificationCommand(
    string Code,
    string SchemeCode,
    string? ClassCode,
    int RowVersion) : IRequest<Result<ProductClassificationsDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "classified";

    string IAuditableCommand.AuditDetail => ClassCode is null
        ? $"{SchemeCode} mapping cleared"
        : $"{SchemeCode} mapped to {ClassCode}";
}

/// <summary>A single feature edit. Null <paramref name="Value"/> clears it.</summary>
public sealed record ClassificationFeatureInput(string FeatureCode, string? Value);

public sealed record UpdateClassificationFeaturesCommand(
    string Code,
    string SchemeCode,
    IReadOnlyList<ClassificationFeatureInput> Values,
    int RowVersion) : IRequest<Result<ProductClassificationsDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "updated";

    string IAuditableCommand.AuditDetail => $"{SchemeCode} feature values ({Values.Count})";
}

/// <summary>Read/write port for classification mappings; SQL lives in Infrastructure.</summary>
public interface IProductClassificationStore
{
    Task<ProductClassificationsDto?> GetAsync(string productCode, CancellationToken cancellationToken);

    Task<Result<ProductClassificationsDto>> AssignClassAsync(
        string productCode,
        string schemeCode,
        string? classCode,
        int expectedRowVersion,
        CancellationToken cancellationToken);

    Task<Result<ProductClassificationsDto>> SaveFeaturesAsync(
        string productCode,
        string schemeCode,
        IReadOnlyList<ClassificationFeatureInput> values,
        int expectedRowVersion,
        CancellationToken cancellationToken);
}

public sealed class AssignClassificationCommandValidator
    : AbstractValidator<AssignClassificationCommand>
{
    public AssignClassificationCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.SchemeCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ClassCode).MaximumLength(64);
        RuleFor(x => x.RowVersion).GreaterThan(0);
    }
}

public sealed class UpdateClassificationFeaturesCommandValidator
    : AbstractValidator<UpdateClassificationFeaturesCommand>
{
    /// <summary>Bounds one request; a class declares tens of features, not thousands.</summary>
    public const int MaxValuesPerRequest = 500;

    public UpdateClassificationFeaturesCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.SchemeCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.RowVersion).GreaterThan(0);

        RuleFor(x => x.Values)
            .NotNull()
            .Must(v => v.Count <= MaxValuesPerRequest)
            .WithMessage($"At most {MaxValuesPerRequest} feature values can be saved at once.");

        RuleForEach(x => x.Values).ChildRules(value =>
            value.RuleFor(v => v.FeatureCode).NotEmpty());
    }
}

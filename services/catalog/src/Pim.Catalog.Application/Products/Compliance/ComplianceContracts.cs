using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Domain.Compliance;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Compliance;

/// <summary>
/// One certificate. <paramref name="ExpiryState"/> is derived server-side so
/// every client agrees on what "expiring" means; clients must not recompute it
/// from ValidTo, because the warning window is a business rule, not a UI one.
/// </summary>
public sealed record ComplianceDocumentDto(
    long Id,
    string DocType,
    string? Reference,
    string? Standard,
    string? Authority,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string Status,
    string ExpiryState);

/// <summary>An EPD-backed environmental figure, e.g. GWP 142 kg CO2-eq (A1-A3).</summary>
public sealed record SustainabilityIndicatorDto(
    string Indicator,
    decimal? Value,
    string? Unit,
    string? Method,
    string? Stage);

public sealed record MaterialCompositionDto(
    string Material,
    decimal? Pct,
    bool? IsRecyclable,
    bool IsHazardous);

public sealed record SubstanceComplianceDto(
    string Regime,
    string Result,
    string? Threshold,
    string? Notes);

public sealed record GreenBuildingContributionDto(string Scheme, string Credit, string? Note);

/// <summary>
/// The full Compliance tab payload.
///
/// ExpiringCount drives the banner and is counted server-side against the same
/// reference date used for every row, so the banner can never disagree with the
/// table beneath it.
/// </summary>
public sealed record ProductComplianceDto(
    string ProductCode,
    int RowVersion,
    int ExpiringCount,
    int ExpiredCount,
    IReadOnlyList<ComplianceDocumentDto> Documents,
    IReadOnlyList<SustainabilityIndicatorDto> Indicators,
    IReadOnlyList<MaterialCompositionDto> Materials,
    IReadOnlyList<SubstanceComplianceDto> Substances,
    IReadOnlyList<GreenBuildingContributionDto> GreenBuilding);

public sealed record GetProductComplianceQuery(string Code) : IRequest<Result<ProductComplianceDto>>;

public sealed record AddComplianceDocumentCommand(
    string Code,
    string DocType,
    string? Reference,
    string? Standard,
    string? Authority,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    int RowVersion) : IRequest<Result<ProductComplianceDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "created";

    string IAuditableCommand.AuditDetail => $"Certificate {Reference ?? DocType} added";
}

public sealed record UpdateComplianceDocumentCommand(
    string Code,
    long DocumentId,
    string DocType,
    string? Reference,
    string? Standard,
    string? Authority,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string Status,
    int RowVersion) : IRequest<Result<ProductComplianceDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "updated";

    string IAuditableCommand.AuditDetail => $"Certificate {Reference ?? DocType} ({Status})";
}

public sealed record RemoveComplianceDocumentCommand(
    string Code,
    long DocumentId,
    int RowVersion) : IRequest<Result<ProductComplianceDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "deleted";

    string IAuditableCommand.AuditDetail => $"Certificate {DocumentId} removed";
}

/// <summary>Read/write port for compliance evidence; SQL lives in Infrastructure.</summary>
public interface IProductComplianceStore
{
    Task<ProductComplianceDto?> GetAsync(string productCode, CancellationToken cancellationToken);

    Task<Result<ProductComplianceDto>> AddAsync(
        AddComplianceDocumentCommand command, CancellationToken cancellationToken);

    Task<Result<ProductComplianceDto>> UpdateAsync(
        UpdateComplianceDocumentCommand command, CancellationToken cancellationToken);

    Task<Result<ProductComplianceDto>> RemoveAsync(
        RemoveComplianceDocumentCommand command, CancellationToken cancellationToken);
}

/// <summary>Field limits shared by add and update so the two cannot drift apart.</summary>
internal static class ComplianceDocumentLimits
{
    public const int MaxReferenceLength = 128;
    public const int MaxStandardLength = 128;
    public const int MaxAuthorityLength = 128;

    public static string DocTypeMessage =>
        $"Document type must be one of: {string.Join(", ", ComplianceDocType.All)}.";

    public static string StatusMessage =>
        $"Status must be one of: {string.Join(", ", ComplianceStatus.All)}.";
}

// The two validators repeat their field rules rather than sharing a helper.
// FluentValidation's RuleFor takes an expression tree, so a shared helper has
// to accept Expression<Func<T,...>> for the per-field rules AND plain Funcs for
// the cross-field date check — the resulting signature was longer and harder to
// read than the rules themselves.

public sealed class AddComplianceDocumentCommandValidator
    : AbstractValidator<AddComplianceDocumentCommand>
{
    public AddComplianceDocumentCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.RowVersion).GreaterThan(0);

        RuleFor(x => x.DocType)
            .NotEmpty()
            .Must(ComplianceDocType.IsKnown)
            .WithMessage(ComplianceDocumentLimits.DocTypeMessage);

        RuleFor(x => x.Reference).MaximumLength(ComplianceDocumentLimits.MaxReferenceLength);
        RuleFor(x => x.Standard).MaximumLength(ComplianceDocumentLimits.MaxStandardLength);
        RuleFor(x => x.Authority).MaximumLength(ComplianceDocumentLimits.MaxAuthorityLength);

        RuleFor(x => x.ValidTo)
            .Must((command, validTo) =>
                validTo is null || command.ValidFrom is null || validTo >= command.ValidFrom)
            .WithMessage("Valid-to cannot be earlier than valid-from.");
    }
}

public sealed class UpdateComplianceDocumentCommandValidator
    : AbstractValidator<UpdateComplianceDocumentCommand>
{
    public UpdateComplianceDocumentCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.DocumentId).GreaterThan(0);
        RuleFor(x => x.RowVersion).GreaterThan(0);

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(ComplianceStatus.IsKnown)
            .WithMessage(ComplianceDocumentLimits.StatusMessage);

        RuleFor(x => x.DocType)
            .NotEmpty()
            .Must(ComplianceDocType.IsKnown)
            .WithMessage(ComplianceDocumentLimits.DocTypeMessage);

        RuleFor(x => x.Reference).MaximumLength(ComplianceDocumentLimits.MaxReferenceLength);
        RuleFor(x => x.Standard).MaximumLength(ComplianceDocumentLimits.MaxStandardLength);
        RuleFor(x => x.Authority).MaximumLength(ComplianceDocumentLimits.MaxAuthorityLength);

        RuleFor(x => x.ValidTo)
            .Must((command, validTo) =>
                validTo is null || command.ValidFrom is null || validTo >= command.ValidFrom)
            .WithMessage("Valid-to cannot be earlier than valid-from.");
    }
}

public sealed class RemoveComplianceDocumentCommandValidator
    : AbstractValidator<RemoveComplianceDocumentCommand>
{
    public RemoveComplianceDocumentCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.DocumentId).GreaterThan(0);
        RuleFor(x => x.RowVersion).GreaterThan(0);
    }
}

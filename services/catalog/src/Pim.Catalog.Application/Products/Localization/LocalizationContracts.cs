using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Auditing;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Domain.Localization;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.Localization;

/// <summary>
/// One translatable field, shown side by side with the base locale.
///
/// <paramref name="BaseValue"/> is read-only context — the master text the
/// translator works from. Only <paramref name="Value"/> is editable, and only
/// for a non-base locale.
/// </summary>
public sealed record TranslationFieldDto(
    string Field,
    string Label,
    string? BaseValue,
    string? Value,
    bool IsRequired,
    bool IsMultiline,
    /// <summary>True for a localizable attribute, false for a product_tr column.</summary>
    bool IsAttribute);

/// <summary>
/// One locale's state for this product.
///
/// <paramref name="Completeness"/> counts filled translatable fields, not
/// required ones: a translator's job is finished when nothing is left blank,
/// which is a different question from whether the product is commercially
/// complete. The two numbers are deliberately not the same metric.
/// </summary>
public sealed record ProductLocaleDto(
    string LocaleCode,
    string LocaleName,
    bool IsBase,
    string TranslationStatus,
    string? Source,
    DateTimeOffset? UpdatedAtUtc,
    int FieldCount,
    int FilledCount,
    int Completeness,
    IReadOnlyList<TranslationFieldDto> Fields);

/// <summary>
/// The Localization tab: every configured locale, with the base locale first.
///
/// The base locale is included so the tab can show what is being translated
/// from, but it is never editable here — editing German copy is the General
/// and Attributes tabs' job. Presenting the master as an editable column would
/// invite a translator to "fix" the source.
/// </summary>
public sealed record ProductLocalizationDto(
    string ProductCode,
    string BaseLocale,
    int RowVersion,
    int PendingLocaleCount,
    IReadOnlyList<ProductLocaleDto> Locales);

public sealed record GetProductLocalizationQuery(string Code, string? Locale = null)
    : IRequest<Result<ProductLocalizationDto>>;

/// <summary>A single field edit. A null or blank value clears the translation.</summary>
public sealed record TranslationValueInput(string Field, string? Value);

/// <summary>
/// Saves one locale's translations in one transaction.
///
/// Scoped to a single locale on purpose: a translator works in one language,
/// and a batch spanning locales would make a partial failure ambiguous about
/// which language was left half-saved.
/// </summary>
public sealed record UpdateProductLocalizationCommand(
    string Code,
    string LocaleCode,
    IReadOnlyList<TranslationValueInput> Values,
    string? TranslationStatus,
    int RowVersion,
    /// <summary>
    /// The language the SCREEN is in — not the one being edited. That is
    /// <paramref name="LocaleCode"/>, and the two are independent: a steward
    /// working in an English UI can perfectly well be translating into French.
    /// Named apart from Locale on purpose, because one record carrying both a
    /// "locale" and a "locale code" would be a defect waiting to be written.
    /// </summary>
    string? DisplayLocale = null) : IRequest<Result<ProductLocalizationDto>>, IAuditableCommand
{
    string IAuditableCommand.ProductCode => Code;

    string IAuditableCommand.AuditAction => "translated";

    string IAuditableCommand.AuditDetail =>
        $"{LocaleCode}: {Values.Count} field{(Values.Count == 1 ? "" : "s")}" +
        (TranslationStatus is null ? "" : $", status {TranslationStatus}");
}

public interface IProductLocalizationStore
{
    Task<ProductLocalizationDto?> GetAsync(
        string productCode, DisplayLocale locale, CancellationToken cancellationToken);

    Task<Result<ProductLocalizationDto>> SaveAsync(
        UpdateProductLocalizationCommand command, DisplayLocale locale, CancellationToken cancellationToken);
}

public sealed class UpdateProductLocalizationCommandValidator
    : AbstractValidator<UpdateProductLocalizationCommand>
{
    public UpdateProductLocalizationCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.RowVersion).GreaterThan(0);

        RuleFor(x => x.LocaleCode)
            .NotEmpty()
            .MaximumLength(TranslationRules.MaxLocaleLength);

        RuleFor(x => x.TranslationStatus)
            .Must(s => s is null || TranslationStatus.IsSettable(s))
            .WithMessage(
                $"Translation status must be one of: {string.Join(", ", TranslationStatus.Settable)}.");

        RuleFor(x => x.Values)
            .NotNull()
            .Must(v => v.Count > 0)
            .WithMessage("At least one field must be supplied.")
            .When(x => x.TranslationStatus is null);

        RuleForEach(x => x.Values).ChildRules(field =>
        {
            field.RuleFor(v => v.Field)
                .NotEmpty()
                .Must(TranslationRules.IsKnownField)
                .WithMessage(f =>
                    $"'{f.Field}' is not a translatable field. Translatable fields are: " +
                    $"{string.Join(", ", TranslationRules.ProductFields)}, plus localizable attributes.");

            field.RuleFor(v => v.Value)
                .MaximumLength(TranslationRules.MaxValueLength);
        });
    }
}

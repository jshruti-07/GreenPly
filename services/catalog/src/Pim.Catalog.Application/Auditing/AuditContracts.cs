using FluentValidation;
using MediatR;
using Pim.Catalog.Application.Common;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Auditing;

/// <summary>
/// Marks a command that must leave an audit entry.
///
/// This is an explicit opt-in rather than an inference from the type name.
/// Name-based detection ("anything ending in Command") silently starts and
/// stops auditing as classes are renamed, and the failure mode is a missing
/// history nobody notices until it is needed. A missing marker is instead
/// caught by <c>AuditCoverageTests</c>, which asserts that every command type
/// in the assembly is either auditable or explicitly exempt.
/// </summary>
public interface IAuditableCommand
{
    /// <summary>The product this change belongs to. The Activity tab reads by product.</summary>
    string ProductCode { get; }

    /// <summary>Past-tense verb shown in the timeline, e.g. "updated", "published".</summary>
    string AuditAction { get; }

    /// <summary>
    /// Human-readable summary of what changed, e.g. "SKU FD-90-L added".
    /// Written for the person reading the timeline, not for machines.
    /// </summary>
    string AuditDetail { get; }
}

/// <summary>One entry in a product's history.</summary>
public sealed record ActivityEntryDto(
    long Id,
    DateTimeOffset OccurredAt,
    string? Actor,
    string Action,
    string? Detail,
    string? Source);

public sealed record ProductActivityDto(
    string ProductCode,
    IReadOnlyList<string> AvailableActions,
    PagedResult<ActivityEntryDto> Entries);

/// <summary>
/// Activity is paged because it grows without bound — unlike every other tab,
/// this one cannot return the whole set.
/// </summary>
public sealed record GetProductActivityQuery(
    string Code,
    int Page = 1,
    int PageSize = 25,
    string? Action = null,
    string? Search = null) : IRequest<Result<ProductActivityDto>>;

public interface IProductActivityReader
{
    Task<ProductActivityDto?> GetAsync(GetProductActivityQuery query, CancellationToken cancellationToken);
}

/// <summary>Append-only writer for audit entries.</summary>
public interface IAuditWriter
{
    Task WriteAsync(
        string productCode,
        string action,
        string detail,
        string source,
        CancellationToken cancellationToken);
}

public sealed class GetProductActivityQueryValidator : AbstractValidator<GetProductActivityQuery>
{
    public const int MaxPageSize = 100;

    public GetProductActivityQueryValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Action).MaximumLength(64);
    }
}

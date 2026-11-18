namespace Pim.Catalog.Application.Common;

/// <summary>Standard list envelope per .claude/rules/api-conventions.md.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

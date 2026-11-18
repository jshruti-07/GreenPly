using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Categories;

/// <summary>
/// Errors the Categories read side can return.
///
/// In Application rather than Domain because there is no category aggregate:
/// the taxonomy is written by the seed and by future admin tooling, and this
/// sprint reads it. Putting these in Domain would imply an invariant that
/// nothing in Domain enforces.
/// </summary>
public static class CategoryErrors
{
    public static readonly Error CodeRequired =
        Error.Validation("Category.Code.Required", "A category code is required.");

    public static Error NotFound(string code) =>
        Error.NotFound("Category.NotFound", $"Category '{code}' was not found.");
}

using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.AttributeLibrary;

public static class AttributeLibraryErrors
{
    public static readonly Error CodeRequired =
        Error.Validation("Attribute.Code.Required", "An attribute code is required.");

    public static Error NotFound(string code) =>
        Error.NotFound("Attribute.NotFound", $"Attribute '{code}' was not found.");
}

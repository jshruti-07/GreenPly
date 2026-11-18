using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Products;

public static class ProductErrors
{
    public static readonly Error CodeEmpty =
        Error.Validation("Product.Code.Empty", "Product code is required.");

    public static readonly Error CodeTooLong =
        Error.Validation("Product.Code.TooLong", $"Product code must not exceed {ProductCode.MaxLength} characters.");

    public static readonly Error CodeInvalidFormat =
        Error.Validation("Product.Code.InvalidFormat",
            "Product code must be uppercase alphanumeric segments separated by hyphens (e.g. FD-90-GS-1000).");

    public static readonly Error DuplicateCode =
        Error.Conflict("Product.Code.Duplicate", "A product with this code already exists.");

    public static readonly Error NameRequired =
        Error.Validation("Product.Name.Required", "A product name in the base locale is required.");

    public static readonly Error NameTooLong =
        Error.Validation("Product.Name.TooLong",
            $"Product name must not exceed {ProductTranslation.MaxNameLength} characters.");

    public static Error NotFound(string code) =>
        Error.NotFound("Product.NotFound", $"Product '{code}' was not found.");

    public static readonly Error ConcurrentModification =
        Error.Conflict("Product.ConcurrentModification",
            "This product was changed by someone else after you opened it. Reload and reapply your changes.");

    public static Error InvalidWorkflowTransition(WorkflowState from, WorkflowState to) =>
        Error.Conflict("Product.Workflow.InvalidTransition",
            $"Workflow transition {from} -> {to} is not allowed.");

    public static Error InvalidLifecycleTransition(LifecycleState from, LifecycleState to) =>
        Error.Conflict("Product.Lifecycle.InvalidTransition",
            $"Lifecycle transition {from} -> {to} is not allowed.");
}

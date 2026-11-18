using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Translations;

public static class TranslationErrors
{
    public static readonly Error TargetRequired =
        Error.Validation("Translation.Target.Required",
            "A target locale is required — the language being translated into.");

    public static Error TargetIsBase(string code) =>
        Error.Validation("Translation.Target.IsBase",
            $"'{code}' is the catalogue's base locale, which is the source of every translation rather than a target for one.");

    public static Error UnknownState(string state) =>
        Error.Validation("Translation.State.Unknown",
            $"'{state}' is not a queue state. Use attention, missing, outdated, review or all.");

    public static readonly Error PageOutOfRange =
        Error.Validation("Translation.Page.OutOfRange", "Page must be 1 or greater.");

    public static Error PageSizeOutOfRange(int max) =>
        Error.Validation("Translation.PageSize.OutOfRange",
            $"Page size must be between 1 and {max}.");
}
